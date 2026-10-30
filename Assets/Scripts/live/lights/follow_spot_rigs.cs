using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the game's per-character follow-spot rigs.
    //
    // the game hangs a real spotlight assembly over each performer and
    // colours it from the FollowSpot* bgColor1 groups. gallop's own
    // UpdateCharaFollowSpotColor1 (stagecontroller, decoded at
    // 0x7ff8e4db4410) picks the standing places a group lights with a
    // LiveCharaPositionFlag mask and stores the group's colour and power on
    // that place's follow-spot entry; the entry's rig then renders the cone.
    //
    // the rig is the game's own prefab: gallop.live.followspotlightcontroller
    // carries _mulcolor0 and _colorpower, plus an addition and a multiply
    // material, and the common bundle ships the cone assemblies as
    // pfb_env_live_cmn_spotlight3d000, ...001 and ...000_001. the stage
    // loader already instantiates those three and parks them hidden, so the
    // rig here reactivates the prefab for each lit character and publishes
    // the authored colour and power onto its materials.
    public class follow_spot_rigs : MonoBehaviour
    {
        // the living place count the game walks (twenty standing places).
        private const int place_count = 20;

        // the game's cone prefabs, by the short names the stage loader
        // records them under. each is a billboarded beam assembly.
        private static readonly string[] fixture_names =
        {
            "spotlight3d000", "spotlight3d001", "spotlight3d000_001",
        };

        // one rig: the prefab and the materials that carry the tint.
        private class rig
        {
            public GameObject prefab;
            public List<Renderer> renderers = new();
            public MaterialPropertyBlock mpb;
        }

        // one rig per standing place.
        private readonly Dictionary<int, rig> rigs = new();

        private List<Transform> chara_roots;
        private List<int> chara_places;

        // the colour and power the game's handler stored for each place this
        // frame. a place with no entry is dark.
        private readonly Dictionary<int, Color> place_colors = new();
        private readonly Dictionary<int, float> place_powers = new();

        // the follow-spot controller's own properties: _MulColor0 carries the
        // authored colour and _ColorPower the authored power, and the
        // controller divides that colour by the power when it publishes.
        private static readonly int id_mul_color0 = Shader.PropertyToID("_MulColor0");
        private static readonly int id_color_power = Shader.PropertyToID("_ColorPower");

        // binds the rig to the loaded cast.
        public void open(List<Transform> roots, List<int> places)
        {
            chara_roots = roots;
            chara_places = places;
            clear_rigs();
        }

        // the game keeps one rig per character; a character leaving the stage
        // takes its rig with it.
        public void clear_rigs()
        {
            foreach (var r in rigs.Values)
            {
                if (r == null) continue;
                if (r.prefab != null) r.prefab.SetActive(false);
            }
            rigs.Clear();
        }

        private void OnDestroy() => clear_rigs();

        // builds the rigs once the cast is standing.
        private bool rigs_ready;
        public void build_rigs()
        {
            if (rigs_ready) return;
            if (chara_roots == null || chara_roots.Count == 0) return;

            // the game's three cone prefabs are pooled across the cast: each
            // lit character borrows one, so a crowded stage shares them.
            var pool = new List<GameObject>();
            foreach (var name in fixture_names)
            {
                var go = blink_lights.find_stage_object(name);
                if (go != null) pool.Add(go);
            }
            if (pool.Count == 0)
            {
                trace_log.write("follow spot: no spotlight3d prefab available");
                return;
            }
            rigs_ready = true;

            int built = 0;
            for (int ci = 0; ci < chara_roots.Count; ci++)
            {
                var root = chara_roots[ci];
                if (root == null) continue;
                int place = chara_places != null && ci < chara_places.Count ? chara_places[ci] : ci;
                if (place < 0 || place >= place_count) continue;
                var prefab = pool[built % pool.Count];
                built++;
                var r = new rig { prefab = prefab, mpb = new MaterialPropertyBlock() };
                foreach (var rr in prefab.GetComponentsInChildren<Renderer>(true)) r.renderers.Add(rr);
                rigs[place] = r;
            }
            trace_log.write($"follow spot: {built} rigs over {pool.Count} game cone prefabs");
        }

        // applies the game's per-place follow-spot colour and power.
        public void set_place_state(Dictionary<int, Color> colors, Dictionary<int, float> powers)
        {
            place_colors.Clear();
            place_powers.Clear();
            if (colors != null)
                foreach (var kv in colors) place_colors[kv.Key] = kv.Value;
            if (powers != null)
                foreach (var kv in powers) place_powers[kv.Key] = kv.Value;
        }

        // one shading pass per frame: hangs each rig over its character and
        // publishes the authored colour and power.
        public void update(float song_time)
        {
            if (chara_roots == null || chara_roots.Count == 0) return;
            build_rigs();

            // the game frees a follow spot when its power reaches zero, so a
            // group that stops driving a place puts the beam away.
            for (int ci = 0; ci < chara_roots.Count; ci++)
            {
                var root = chara_roots[ci];
                if (root == null) continue;
                int place = chara_places != null && ci < chara_places.Count ? chara_places[ci] : ci;
                if (!rigs.TryGetValue(place, out var r) || r == null) continue;

                bool lit = place_colors.TryGetValue(place, out var color);
                float power = lit && place_powers.TryGetValue(place, out var p) ? p : 0f;
                bool on = lit && power > 0.001f;

                if (r.prefab != null)
                {
                    if (r.prefab.activeSelf != on) r.prefab.SetActive(on);
                    if (!on) continue;
                    // the rig hangs over the character's head and shines down.
                    r.prefab.transform.position = root.position + Vector3.up * 4.0f;
                }

                // the authored colour and power are separate values on the
                // controller, so the power scales the colour rather than
                // replacing it.
                var tint = color * Mathf.Clamp01(power);
                r.mpb.SetColor(id_mul_color0, tint);
                r.mpb.SetFloat(id_color_power, power);
                foreach (var rr in r.renderers)
                {
                    if (rr == null) continue;
                    try { rr.SetPropertyBlock(r.mpb); }
                    catch { /* destroyed mid-shutdown */ }
                }
            }
        }
    }
}
