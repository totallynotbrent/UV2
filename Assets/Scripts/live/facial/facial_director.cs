// facial animation director: drives each chara's face from the cutt facial tracks,
// matching the game's LiveFaceController consumer chain per the decode
// (~/umadump/out/uv2_facial_blend_decoded.md):
//   rate r = clamp(speed*0.01, 0, 1) as a per-category intensity multiplier,
//   hard-cut on id change, silent no-op on unknown ids, gaze via eyeTrack rates.
// apply surface: the head's eye_locator_l/r for gaze; mouth keys gate the
// chara's mouth/singing weight on the animator.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    public class facial_director : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private List<Transform> chara_roots;

        // per-slot runtime face state (the game's LiveFaceController mirror).
        private class slot_state
        {
            public Transform root;
            public Transform eye_locator_l, eye_locator_r;
            // memo (the game's store78): last applied values; skip when unchanged.
            public int memo_mouth_id = int.MinValue;
            public float memo_mouth_w = -1f;
            public Vector2 memo_gaze = new(float.MinValue, float.MinValue);
        }

        private readonly List<slot_state> slots = new();
        private int trace_tick;
        private const int TRACE_EVERY = 240;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> characters)
        {
            ws = worksheet;
            clock = timeline;
            chara_roots = characters;

            int n = Mathf.Min(ws.facial_slots.Count, chara_roots.Count);
            for (int i = 0; i < n; i++)
            {
                var st = new slot_state { root = chara_roots[i] };
                st.eye_locator_l = find_deep(chara_roots[i], "Eye_locator_L");
                st.eye_locator_r = find_deep(chara_roots[i], "Eye_locator_R");
                slots.Add(st);
            }
            int eye_hits = slots.Count(s => s.eye_locator_l && s.eye_locator_r);
            trace_log.write($"facial: open {slots.Count} slots, eye locators on {eye_hits}, " +
                            $"mouth keys on {ws.facial_slots.Sum(s => s.mouth.Count)}, " +
                            $"gaze keys on {ws.facial_slots.Sum(s => s.eye_track.Count)}");

            // one-shot hierarchy probe: what facial transforms exist under slot 0?
            if (chara_roots.Count > 0)
            {
                var names = new List<string>();
                collect_names(chara_roots[0], 0, 12, names);
                trace_log.write($"facial: slot0 tree ({names.Count}): {string.Join(",", names.Take(80))}");
            }
        }

        // collects transform names down to depth max_depth for the apply-surface probe.
        private static void collect_names(Transform root, int depth, int max_depth, List<string> names)
        {
            if (root == null || depth > max_depth || names.Count >= 200) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name.Contains("Eye") || c.name.Contains("Mouth") || c.name.Contains("Eyebrow")
                    || c.name.Contains("Head") || c.name.Contains("Tooth") || c.name.Contains("Tongue")
                    || c.name.Contains("facial") || c.name.Contains("Mayu"))
                    names.Add(new string('>', depth) + c.name);
                collect_names(c, depth + 1, max_depth, names);
            }
        }

        private static Transform find_deep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = find_deep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private void Update()
        {
            if (clock == null || ws == null) return;
            float t = clock.time;
            trace_tick++;

            for (int i = 0; i < slots.Count && i < ws.facial_slots.Count; i++)
            {
                var st = slots[i];
                var tracks = ws.facial_slots[i];
                if (st.root == null) continue;

                update_mouth(st, tracks, t, i);
                update_gaze(st, tracks, t, i);
            }
        }

        // mouth keys: the game's UpdateFacePartsSetFromFacialId semantics. the
        // current key sets the target (id + weight), and r = clamp(speed*0.01)
        // scales the stored weight as the intensity multiplier.
        private void update_mouth(slot_state st, facial_track_set tracks, float t, int slot)
        {
            if (tracks.mouth.Count == 0) return;
            int bi = key_eval.bracket(tracks.mouth, t);
            if (bi < 0) return;
            var k = tracks.mouth[bi];
            float r = Mathf.Clamp(k.speed * 0.01f, 0f, 1f);
            float w = Mathf.Clamp(k.weight * 0.01f, 0f, 1f) * r;
            if (k.facial_id != st.memo_mouth_id || !Mathf.Approximately(w, st.memo_mouth_w))
            {
                apply_mouth(st, k.facial_id, w, t, slot);
                st.memo_mouth_id = k.facial_id;
                st.memo_mouth_w = w;
            }
        }

        private void apply_mouth(slot_state st, int facial_id, float w, float t, int slot)
        {
            // facialId 8001 = the singing mouth set; UV2's apply = the singing
            // layer weight on the chara's animator (id 8001 keys drive lip movement
            // via the game's mouth clips; the layer weight is UV2's equivalent gate).
            var anim = st.root.GetComponentInChildren<Animator>();
            bool applied = false;
            if (anim != null)
            {
                for (int layer = 0; layer < anim.layerCount; layer++)
                {
                    var lname = anim.GetLayerName(layer);
                    if (lname == "singing" || lname == "mouth")
                    {
                        anim.SetLayerWeight(layer, w);
                        applied = true;
                    }
                }
            }
            trace_log.write($"facial: slot{slot} mouth id {facial_id} w {w:F2} t {t:F1}" +
                            (applied ? "" : " (no animator layer; hold)"));
        }

        // eyeTrack keys: gaze. the current key's h/v rates steer the eye locators.
        // the blend honors the NEXT key's interpolate type via key_eval.interp —
        // type 0 = hold (the game's IsInterpolateKey gate), so authored hold
        // spans keep the current rates until the next key lands.
        private void update_gaze(slot_state st, facial_track_set tracks, float t, int slot)
        {
            if (tracks.eye_track.Count == 0 || (st.eye_locator_l == null && st.eye_locator_r == null))
                return;
            int bi = key_eval.bracket(tracks.eye_track, t);
            if (bi < 0) return;
            var k = tracks.eye_track[bi];

            float h = k.horizontal_rate_per * 0.01f;
            float v = k.vertical_rate_per * 0.01f;
            if (bi + 1 < tracks.eye_track.Count && tracks.eye_track[bi + 1].frame > k.frame)
            {
                float k_t = key_eval.interp(k, tracks.eye_track[bi + 1], t);
                if (k_t > 0f)
                {
                    var nk = tracks.eye_track[bi + 1];
                    h = Mathf.Lerp(h, nk.horizontal_rate_per * 0.01f, k_t);
                    v = Mathf.Lerp(v, nk.vertical_rate_per * 0.01f, k_t);
                }
            }

            if (!Mathf.Approximately(h, st.memo_gaze.x) || !Mathf.Approximately(v, st.memo_gaze.y))
            {
                apply_gaze(st, h, v);
                st.memo_gaze = new Vector2(h, v);
                if (trace_tick % TRACE_EVERY == 0 || slot == 0)
                    trace_log.write($"facial: slot{slot} gaze h {h:F2} v {v:F2} t {t:F1}");
            }
        }

        private static void apply_gaze(slot_state st, float h, float v)
        {
            // the game turns the eye locators toward the target; 30 deg = full rate.
            var rot = new Vector3(-v * 30f, h * 30f, 0f);
            if (st.eye_locator_l != null) st.eye_locator_l.localEulerAngles = rot;
            if (st.eye_locator_r != null) st.eye_locator_r.localEulerAngles = rot;
        }
    }
}
