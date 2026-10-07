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
        private Dictionary<int, Gallop.FaceDrivenKeyTarget> face_targets;

        // per-slot runtime face state (the game's LiveFaceController mirror).
        private class slot_state
        {
            public Transform root;
            public Transform eye_locator_l, eye_locator_r;
            // memo (the game's store78): last applied values; skip when unchanged.
            public int memo_mouth_id = int.MinValue;
            public float memo_mouth_w = -1f;
            public int memo_mouth_mix = int.MinValue;
            public Vector2 memo_gaze = new(float.MinValue, float.MinValue);
            // the morph surface (the game's FaceDrivenKeyTarget mirror).
            public Gallop.FaceDrivenKeyTarget face_target;
            public Dictionary<string, Transform> path_map;
            // rest pose captured before the first apply so every frame resets.
            public Dictionary<Transform, Vector3> rest_pos, rest_scale, rest_rot;
        }

        private readonly List<slot_state> slots = new();
        private int trace_tick;
        private const int TRACE_EVERY = 240;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> characters,
            Dictionary<int, Gallop.FaceDrivenKeyTarget> targets_by_chara, List<int> chara_ids)
        {
            ws = worksheet;
            clock = timeline;
            chara_roots = characters;
            face_targets = targets_by_chara;

            int n = Mathf.Min(ws.facial_slots.Count, chara_roots.Count);
            for (int i = 0; i < n; i++)
            {
                var st = new slot_state { root = chara_roots[i] };
                st.eye_locator_l = find_deep(chara_roots[i], "Eye_locator_L");
                st.eye_locator_r = find_deep(chara_roots[i], "Eye_locator_R");
                int cid = i < chara_ids.Count ? chara_ids[i] : 0;
                if (cid > 0) face_targets.TryGetValue(cid, out st.face_target);
                bind_face_paths(st, chara_roots[i]);
                slots.Add(st);
            }
            int eye_hits = slots.Count(s => s.eye_locator_l && s.eye_locator_r);
            int morph_hits = slots.Count(s => s.face_target != null);
            trace_log.write($"facial: open {slots.Count} slots, eye locators on {eye_hits}, " +
                            $"face targets on {morph_hits}, " +
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

        // resolves the head's own FaceDrivenKeyTarget (the morph database the
        // game serializes into every pfb_chrXXXX_00 head prefab) and maps its
        // trs paths to the runtime rig, capturing the rest pose once.
        private void bind_face_paths(slot_state st, Transform root)
        {
            if (st.face_target == null) return;

            var by_name = new Dictionary<string, Transform>();
            if (root != null) collect_unique_names(root, by_name);
            st.path_map = new Dictionary<string, Transform>();
            st.rest_pos = new Dictionary<Transform, Vector3>();
            st.rest_scale = new Dictionary<Transform, Vector3>();
            st.rest_rot = new Dictionary<Transform, Vector3>();

            // the game's rest pose per category is the BASE entry (index 0)'s
            // own trs values: FacialReset writes them verbatim before the
            // active morphs add on top (census checklist item + fork
            // FacialResetAll). collect them first.
            var base_trs = new Dictionary<string, Gallop.TrsArray>();
            foreach (var list in new List<IEnumerable<Gallop.FacialPartsTarget>>
                     { st.face_target._eyeTarget, st.face_target._eyebrowTarget, st.face_target._mouthTarget })
                foreach (var entry in list.Take(1))
                    foreach (var group in entry._faceGroupInfo)
                        foreach (var trs in group._trsArray)
                            base_trs[trs._path] = trs;

            int paths = 0;
            foreach (var list in new List<IEnumerable<Gallop.FacialPartsTarget>>
                     { st.face_target._eyeTarget, st.face_target._eyebrowTarget, st.face_target._mouthTarget })
                foreach (var entry in list)
                    foreach (var group in entry._faceGroupInfo)
                        foreach (var trs in group._trsArray)
                        {
                            paths++;
                            if (string.IsNullOrEmpty(trs._path) || st.path_map.ContainsKey(trs._path)) continue;
                            by_name.TryGetValue(trs._path, out var t);
                            st.path_map[trs._path] = t;
                            if (t == null || st.rest_pos.ContainsKey(t)) continue;
                            if (base_trs.TryGetValue(trs._path, out var b))
                            {
                                st.rest_pos[t] = b._position;
                                st.rest_scale[t] = b._scale;
                                st.rest_rot[t] = b._rotation;
                            }
                            else
                            {
                                st.rest_pos[t] = t.localPosition;
                                st.rest_scale[t] = t.localScale;
                                st.rest_rot[t] = t.localEulerAngles;
                            }
                        }
            int resolved = st.path_map.Values.Count(t => t != null);
            trace_log.write($"facial: slot face target {st.face_target._mouthTarget.Count} mouth / " +
                            $"{st.face_target._eyeTarget.Count} eye / {st.face_target._eyebrowTarget.Count} eyebrow " +
                            $"targets, {paths} paths, {resolved} resolved");
        }

        // the game's morph accumulation: reset the touched transforms to the
        // rest pose, add each active morph's deltas scaled by its weight, and
        // apply rotation via the maya-axis compose (z*y*x).
        private static Quaternion from_maya_euler(Vector3 e) =>
            Quaternion.Euler(0f, 0f, e.z) * Quaternion.Euler(0f, e.y, 0f) * Quaternion.Euler(e.x, 0f, 0f);

        private static void collect_unique_names(Transform root, Dictionary<string, Transform> into)
        {
            if (root == null) return;
            into.TryAdd(root.name, root);
            for (int i = 0; i < root.childCount; i++) collect_unique_names(root.GetChild(i), into);
        }

        // the game's CalcMorphWeight: speed picks a preset ramp length (1->3,
        // 10->2, 20->6, 21->9 frames, else the key's own time), the ramp eases
        // by the key's interpolate type from the key time.
        private static float morph_ramp(float t, int speed, int time_frames, int interpolate_type,
            int key_frame, int during_frames)
        {
            int inter = speed switch { 1 => 3, 10 => 2, 20 => 6, 21 => 9, _ => time_frames };
            float ease = Mathf.Min(during_frames, inter) / 60f;
            if (ease <= 0f) return 1f;
            float pass = t - key_frame / 60f;
            if (pass >= ease) return 1f;
            float ratio = Mathf.Clamp01(pass / ease);
            return interpolate_type switch
            {
                2 => ease_in(ratio),
                3 => ease_out(ratio),
                4 => ease_in(ease_out(ratio)),
                _ => ratio,
            };
        }

        private static float ease_in(float x) => x * x;
        private static float ease_out(float x) => 1f - (1f - x) * (1f - x);

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

                // one accumulation pass per chara: the game resets the touched
                // transforms once, then all categories contribute into the same
                // morph table before the apply.
                if (st.face_target != null)
                {
                    var weights = new Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)>();
                    update_mouth(st, tracks, t, i, weights);
                    update_eyes(st, tracks, t, weights);
                    update_eyebrows(st, tracks, t, weights);
                    update_ears(st, tracks, t, weights);
                    apply_morph(st, weights);
                }
                update_gaze(st, tracks, t, i);
            }
        }

        // mouth keys: the game's AlterUpdateFacialNew - the key's parts array
        // is the morph mix (1-based indices), blended with the previous key's
        // mix over the CalcMorphWeight ramp, then accumulated onto the rig.
        private void update_mouth(slot_state st, facial_track_set tracks, float t, int slot,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights)
        {
            if (tracks.mouth.Count == 0) return;
            int bi = key_eval.bracket(tracks.mouth, t);
            if (bi < 0) return;
            var cur = tracks.mouth[bi];
            var prev = bi > 0 ? tracks.mouth[bi - 1] : null;
            var next = bi + 1 < tracks.mouth.Count ? tracks.mouth[bi + 1] : null;
            int dur = next != null ? next.frame - cur.frame : cur.time_frames;
            float ratio = morph_ramp(t, cur.speed, cur.time_frames, cur.interpolate_type, cur.frame, dur);

            // reset the category, then add prev*(1-ratio) + cur*ratio. the
            // game's mouth ride scales by the parts' own weights only (the
            // key weight is not a term in the mouth path, unlike the eye,
            // eyebrow and ear rides).
            if (prev != null && ratio < 1f)
            {
                foreach (var part in prev.parts)
                {
                    if (part.parts_id == 0) continue;
                    float w = part.weight_per * 0.01f * (1f - ratio);
                    add_morph(st, weights, st.face_target._mouthTarget, part.parts_id, w);
                }
            }
            foreach (var part in cur.parts)
            {
                if (part.parts_id == 0) continue;
                float w = part.weight_per * 0.01f * ratio;
                add_morph(st, weights, st.face_target._mouthTarget, part.parts_id, w);
            }

            int mix = 17;
            foreach (var part in cur.parts) mix = mix * 31 + (part.parts_id * 131 + part.weight_per);
            if (cur.facial_id != st.memo_mouth_id || !Mathf.Approximately(ratio, st.memo_mouth_w) || mix != st.memo_mouth_mix)
            {
                trace_log.write($"facial: slot{slot} mouth id {cur.facial_id} parts " +
                                string.Join("+", cur.parts.Select(p => $"#{p.parts_id}:{p.weight_per}")) +
                                $" r {ratio:F2} t {t:F1}");
                st.memo_mouth_id = cur.facial_id;
                st.memo_mouth_w = ratio;
                st.memo_mouth_mix = mix;
            }
        }

        // eye/eyebrow keys: the same parts blend as the mouth, per eye side.
        private void update_eyes(slot_state st, facial_track_set tracks, float t,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights)
        {
            ride_eye_eyebrow(st, tracks.eye, st.face_target._eyeTarget, t, weights);
        }

        private void update_eyebrows(slot_state st, facial_track_set tracks, float t,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights)
        {
            ride_eye_eyebrow(st, tracks.eyebrow, st.face_target._eyebrowTarget, t, weights);
        }

        // the per-side eye/eyebrow ride: prev*(1-ratio) + cur*ratio per side.
        private void ride_eye_eyebrow<T>(slot_state st, List<T> keys, List<Gallop.FacialPartsTarget> targets, float t,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights) where T : facial_eye_key
        {
            if (keys.Count == 0) return;
            int bi = key_eval.bracket(keys, t);
            if (bi < 0) return;
            var cur = keys[bi];
            var prev = bi > 0 ? keys[bi - 1] : null;
            var next = bi + 1 < keys.Count ? keys[bi + 1] : null;
            int dur = next != null ? next.frame - cur.frame : cur.time_frames;
            float ratio = morph_ramp(t, cur.speed, cur.time_frames, cur.interpolate_type, cur.frame, dur);
            float key_w = Mathf.Clamp(cur.weight * 0.01f, 0f, 1f);
            if (prev != null && ratio < 1f)
            {
                float pw = Mathf.Clamp(prev.weight * 0.01f, 0f, 1f);
                foreach (var part in prev.parts_l)
                    if (part.parts_id != 0)
                        add_morph_side(st, weights, targets, part.parts_id, part.weight_per * 0.01f * (1f - ratio) * pw, 1);
                foreach (var part in prev.parts_r)
                    if (part.parts_id != 0)
                        add_morph_side(st, weights, targets, part.parts_id, part.weight_per * 0.01f * (1f - ratio) * pw, 0);
            }
            foreach (var part in cur.parts_l)
                if (part.parts_id != 0)
                    add_morph_side(st, weights, targets, part.parts_id, part.weight_per * 0.01f * ratio * key_w, 1);
            foreach (var part in cur.parts_r)
                if (part.parts_id != 0)
                    add_morph_side(st, weights, targets, part.parts_id, part.weight_per * 0.01f * ratio * key_w, 0);
        }

        // ear keys: id-driven, NOT trs-driven (census §4) - the face target
        // has no _earTarget; the game drives ears via SetEar/PlayEarDrivenKey
        // on the motion system (EarType/2 ids + procedural twitch). not part
        // of the trs morph pass; tracked as the named ear gap.
        private void update_ears(slot_state st, facial_track_set tracks, float t,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights)
        {
        }

        // accumulates one morph entry's trs deltas into the weight table. the
        // group index is the side (0=right, 1=left per the game's direction
        // flag); single-group categories ignore it.
        private void add_morph(slot_state st,
            Dictionary<Transform, (Vector3, Vector3, Vector3)> weights,
            List<Gallop.FacialPartsTarget> targets, int parts_id, float w) =>
            add_morph_side(st, weights, targets, parts_id, w, -1);

        // parts_id is the 0-based entry index (0 = Base; census-proven
        // across all 61 songs - the fork's -1 shifted against a list that
        // had already dropped the base entry; ours keeps it).
        private void add_morph_side<T>(slot_state st,
            Dictionary<Transform, (Vector3, Vector3, Vector3)> weights,
            List<T> targets, int parts_id, float w, int side) where T : Gallop.FacialPartsTarget
        {
            if (parts_id < 0 || parts_id >= targets.Count) return;
            var groups = targets[parts_id]._faceGroupInfo;
            if (groups.Count == 0) return;
            int g = side < 0 || groups.Count == 1 ? 0 : Mathf.Clamp(side, 0, groups.Count - 1);
            foreach (var trs in groups[g]._trsArray)
            {
                if (!st.path_map.TryGetValue(trs._path, out var t) || t == null) continue;
                var d = weights.TryGetValue(t, out var acc) ? acc : (Vector3.zero, Vector3.zero, Vector3.zero);
                weights[t] = (d.Item1 + trs._position * w, d.Item2 + trs._scale * w, d.Item3 + trs._rotation * w);
            }
        }

        // writes the accumulated deltas over the rest pose (the game's reset +
        // ProcessMorph + FromMayaEuler apply).
        private static void apply_morph(slot_state st,
            Dictionary<Transform, (Vector3 p, Vector3 s, Vector3 r)> weights)
        {
            foreach (var kv in weights)
            {
                var t = kv.Key;
                if (!st.rest_pos.TryGetValue(t, out var rp)) continue;
                t.localPosition = rp + kv.Value.p;
                t.localScale = st.rest_scale[t] + kv.Value.s;
                t.localRotation = from_maya_euler(st.rest_rot[t] + kv.Value.r);
            }
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
