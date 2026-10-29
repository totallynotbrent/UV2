// facial toon shaping: the song's facialToonSet drives the per-character
// toon shaping on the head materials.
//
// the game side (~/umadump/out/uv2_chara_toon_light_decoded.md,
// ~/umadump/out/uv2_six_postfx_families_decoded.md section 55, and the
// shipped material defaults):
//   LiveTimelineFacialToonData holds TWENTY sub-lists per song, one per
//   cast slot (center, left1, right1, left2, right2, motion5..motion19 -
//   the same slot naming the formation groups use). every key carries
//     CheekPretenseThreshold  -> _CheekPretenseThreshold
//     NosePretenseThreshold   -> _NosePretenseThreshold
//     CylinderBlend           -> _CylinderBlend
//     HairNormalBlend         -> _HairNormalBlend
//     EyeToonStep             -> _ToonStep     (eye material)
//     EyeToonFeather          -> _ToonFeather  (eye material)
//     EyeSaturation           -> _Saturation   (eye material)
//
// the SLOT MAP comes from the sub-list NAMES, not from their position in
// the list (a song may leave a group empty, which would shift every
// later group):
//   center=slot 1, left1=2, right1=3, left2=4, right2=5, motion5=6 ...
//   motion19=20. motion5 is the sixth sub-list, so it maps to the sixth
//   cast slot (place06), NOT the fifth: counting one short collides
//   motion5 with right2 and shifts every later slot by one.
//
// the property ROUTES come from the shipped material defaults, not from
// guesswork. the three shipped head materials carry
//   face: _CylinderBlend _HairNormalBlend _CheekPretenseThreshold
//         _NosePretenseThreshold _ToonStep _ToonFeather
//   hair: _CylinderBlend _HairNormalBlend _ToonStep _ToonFeather
//   eye:  _CylinderBlend _ToonStep _ToonFeather _EyePupliScale
//         _Saturation
// so _CylinderBlend and _HairNormalBlend travel to face AND hair, the
// two thresholds to the face alone, and the eye's own _ToonStep and
// _ToonFeather carry the authored eye triple. the authored eye values
// are constant at 0.4 / 0.1 across the sampled songs and the shipped eye
// material defaults read _ToonStep 0.4 and _ToonFeather 0.1 - the eye
// triple addresses the eye's generic toon properties, not an
// eye-exclusive spelling.
//
// _Saturation is deliberately NOT written here: the decode shows the
// game publishing it through the rim-parameter applier onto the
// character's own property block (uv2_chara_toon_light_decoded.md,
// SetRimParameter family, _Saturation(175)), and the bg color1 tint
// publishes its saturation on the same block. seizing it from the toon
// would trade one authored source for another.
//
// the writers MERGE into the renderer block instead of replacing it:
// global light and bg color1 both publish onto the same head
// renderers, so the block is read back and extended. the toon block is
// written LAST, matching the game's dispatch order (global light ->
// bg color1 -> facial toon), so the toon values win the properties
// they share.
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    public class facial_toon : MonoBehaviour
    {
        private live_worksheet ws;
        private List<Transform> chara_roots;

        private static readonly int id_cheek = Shader.PropertyToID("_CheekPretenseThreshold");
        private static readonly int id_nose = Shader.PropertyToID("_NosePretenseThreshold");
        private static readonly int id_cylinder = Shader.PropertyToID("_CylinderBlend");
        private static readonly int id_hair_normal = Shader.PropertyToID("_HairNormalBlend");
        private static readonly int id_toon_step = Shader.PropertyToID("_ToonStep");
        private static readonly int id_toon_feather = Shader.PropertyToID("_ToonFeather");
        private static readonly int id_eye_pupil = Shader.PropertyToID("_EyePupliScale");
        private static readonly int id_use_orig_light = Shader.PropertyToID("_UseOriginalDirectionalLight");
        private static readonly int id_orig_light_dir = Shader.PropertyToID("_OriginalDirectionalLightDir");

        // which head materials take which part of the authored block.
        [System.Flags]
        private enum part_kind
        {
            none = 0,
            face = 1,
            hair = 2,
            eye = 4,
        }

        // one slot's live state: the head renderers and the last published
        // values (skip the write when nothing changed).
        private class slot_state
        {
            public List<Renderer> face_renderers = new();
            public List<Renderer> hair_renderers = new();
            public List<Renderer> eye_renderers = new();
            public List<Renderer> head_renderers = new();
            public float cheek = float.NaN;
            public float nose = float.NaN;
            public float cylinder = float.NaN;
            public float hair_normal = float.NaN;
            public float eye_step = float.NaN;
            public float eye_feather = float.NaN;
            public bool own_light;
            public Vector3 own_dir;
        }

        private readonly Dictionary<string, slot_state> slots = new();
        private int trace_tick;

        // places holds each character's standing place (the game's roster
        // index): the facial-toon groups are named for standing places
        // (center, left1, ... motion19), so the group's place picks the
        // root, not the root's index in the compacted cast list.
        public void open(live_worksheet worksheet, List<Transform> characters, IReadOnlyList<int> places = null)
        {
            ws = worksheet;
            chara_roots = characters;

            if (ws == null || ws.facial_toon.Count == 0) return;

            var root_for_place = new Dictionary<int, Transform>();
            for (int i = 0; i < chara_roots.Count; i++)
            {
                int place = places != null && i < places.Count ? places[i] : i;
                if (root_for_place.ContainsKey(place)) continue;
                root_for_place[place] = chara_roots[i];
            }

            foreach (var track in ws.facial_toon)
            {
                if (track == null || track.keys.Count == 0) continue;
                int place = place_of(track.name);
                if (place < 0) continue;
                if (!root_for_place.TryGetValue(place, out var root)) continue;
                if (root == null) continue;

                var st = new slot_state();
                Collect(root, st);
                if (st.face_renderers.Count == 0 && st.hair_renderers.Count == 0 &&
                    st.eye_renderers.Count == 0) continue;
                slots[track.name] = st;
            }

            int keys = 0;
            foreach (var track in ws.facial_toon) keys += track.keys.Count;
            var bound = new List<string>();
            foreach (var kv in slots)
            {
                var st = kv.Value;
                bound.Add($"{kv.Key}:f{st.face_renderers.Count}h{st.hair_renderers.Count}e{st.eye_renderers.Count}");
            }
            trace_log.write($"facial toon: {slots.Count}/{ws.facial_toon.Count} groups bound, {keys} keys [{string.Join(",", bound)}]");
        }

        // the standing place a worksheet group names: center=0, left1=1,
        // right1=2, left2=3, right2=4, motion5=5 ... motion19=19. the
        // group's place IS the roster slot the global-light mask bit
        // selects, so both systems agree on which character is meant.
        private static int place_of(string group)
        {
            switch (group)
            {
                case "center": return 0;
                case "left1": return 1;
                case "right1": return 2;
                case "left2": return 3;
                case "right2": return 4;
                default:
                    if (group.StartsWith("motion") && group.Length > 6 &&
                        int.TryParse(group.Substring(6), out int motion) && motion >= 5 && motion <= 19)
                        return motion;
                    return -1;
            }
        }

        // collects the head renderers and sorts them into face, hair and
        // eye by the properties the shipped materials declare: the face
        // materials carry the pretense thresholds, the hair materials
        // carry _HairNormalBlend without them, and the eye material
        // carries _EyePupliScale (nine shipped eye shaders declare it
        // and nothing else does).
        private static void Collect(Transform root, slot_state st)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                part_kind kind = part_kind.none;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    bool cheek_prop = m.HasProperty(id_cheek);
                    bool nose_prop = m.HasProperty(id_nose);
                    bool hair_prop = m.HasProperty(id_hair_normal);
                    bool eye_prop = m.HasProperty(id_eye_pupil);
                    bool toon_step = m.HasProperty(id_toon_step);

                    if (eye_prop) kind |= part_kind.eye;
                    else if (cheek_prop || nose_prop) kind |= part_kind.face;
                    else if (hair_prop && toon_step) kind |= part_kind.hair;
                }
                if (kind == part_kind.none) continue;
                st.head_renderers.Add(r);
                if ((kind & part_kind.face) != 0) st.face_renderers.Add(r);
                if ((kind & part_kind.hair) != 0) st.hair_renderers.Add(r);
                if ((kind & part_kind.eye) != 0) st.eye_renderers.Add(r);
            }
        }

        // one shading pass per frame; the stage loader calls this from its
        // shade pass so the ordering against global_shade and bg_color1 is
        // deterministic.
        public void update(float song_time)
        {
            if (ws == null || slots.Count == 0) return;
            apply(song_time);
            trace_state();
        }

        public void apply(float t)
        {
            if (ws == null || slots.Count == 0) return;

            foreach (var track in ws.facial_toon)
            {
                if (!slots.TryGetValue(track.name, out var st)) continue;
                if (track.keys.Count == 0) continue;

                int i = key_eval.bracket(track.keys, t);
                if (i < 0) continue;
                var cur = track.keys[i];

                // the game reads the CURRENT key's values alone: the fork's
                // AlterUpdate_FacialToon fills the update info from currentKey
                // with no blend toward the next key, so the facial shaping
                // HOLDS across its span.
                publish(st, cur);
            }
        }

        private static void publish(slot_state st, facial_toon_key key)
        {
            float cheek = key.cheek_threshold;
            float nose = key.nose_threshold;
            float cyl = key.cylinder_blend;
            float hair = key.hair_normal_blend;
            float estep = key.eye_toon_step;
            float efeather = key.eye_toon_feather;

            // the toon-light override rides the same key: when the key asks
            // for it, the head's light direction comes from this key's own
            // euler angle instead of the global-light track's. the game
            // reads the pair off the key and publishes it on the head
            // materials (fork Director.cs OnUpdateFacialToon).
            bool use_own_light = key.use_original_directional_light != 0;
            Vector3 own_dir = Vector3.zero;
            if (use_own_light)
            {
                // the key carries the direction as EULER DEGREES, the same
                // encoding the global-light track uses (cut-in decode
                // uv2_chara_toon_light_decoded.md section 4), so the euler
                // rotates into the travelling direction the shader wants.
                var rot = Quaternion.Euler(key.original_directional_light_dir);
                own_dir = -(rot * Vector3.forward).normalized;
            }

            if (st.cheek == cheek && st.nose == nose && st.cylinder == cyl && st.hair_normal == hair &&
                st.eye_step == estep && st.eye_feather == efeather && st.own_light == use_own_light &&
                st.own_dir == own_dir) return;
            st.cheek = cheek;
            st.nose = nose;
            st.cylinder = cyl;
            st.hair_normal = hair;
            st.eye_step = estep;
            st.eye_feather = efeather;
            st.own_light = use_own_light;
            st.own_dir = own_dir;

            var mpb = new MaterialPropertyBlock();

            // face and hair share the cylinder and hair-normal blends, so
            // both sets take them; the thresholds go to the face alone.
            foreach (var r in st.face_renderers)
            {
                if (r == null) continue;
                try
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(id_cheek, cheek);
                    mpb.SetFloat(id_nose, nose);
                    mpb.SetFloat(id_cylinder, cyl);
                    mpb.SetFloat(id_hair_normal, hair);
                    r.SetPropertyBlock(mpb);
                }
                catch { /* destroyed mid-teardown */ }
            }

            foreach (var r in st.hair_renderers)
            {
                if (r == null) continue;
                try
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(id_cylinder, cyl);
                    mpb.SetFloat(id_hair_normal, hair);
                    r.SetPropertyBlock(mpb);
                }
                catch { /* destroyed mid-teardown */ }
            }

            // the eye triple rides the eye's generic toon properties.
            foreach (var r in st.eye_renderers)
            {
                if (r == null) continue;
                try
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(id_toon_step, estep);
                    mpb.SetFloat(id_toon_feather, efeather);
                    r.SetPropertyBlock(mpb);
                }
                catch { /* destroyed mid-teardown */ }
            }

            // the head's own toon-light direction, published AFTER the global
            // light so it wins the shared property (the game's dispatch
            // order is global light, then bg colour, then facial toon).
            if (!use_own_light) return;
            foreach (var r in st.head_renderers)
            {
                if (r == null) continue;
                try
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(id_use_orig_light, 1f);
                    mpb.SetVector(id_orig_light_dir, own_dir);
                    r.SetPropertyBlock(mpb);
                }
                catch { /* destroyed mid-teardown */ }
            }
        }

        // periodic readout so the trace shows the authored values moving.
        public void trace_state()
        {
            if (trace_tick++ % 240 != 0) return;
            if (ws == null) return;
            foreach (var track in ws.facial_toon)
            {
                if (!slots.TryGetValue(track.name, out var st)) continue;
                if (track.keys.Count == 0) continue;
                trace_log.write($"facial toon '{track.name}': cheek {st.cheek:0.000} nose {st.nose:0.000} " +
                    $"cyl {st.cylinder:0.00} hair {st.hair_normal:0.00} eye {st.eye_step:0.00}/{st.eye_feather:0.00}");
            }
        }
    }
}
