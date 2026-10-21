using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's lightShafts tracks. minimum parity with the
    // fork: the first enabled entry publishes a bloom lift through the
    // _VolumeLightBloomLift global the same way StageLensFlareStageDriver
    // does (lift = clamp01(alpha.x) * 0.05, capped 1.2). the fork runs its
    // shafts walker BEFORE the volume walker, so volume writes last and
    // wins when both author the lift in one frame; UV2 runs this update
    // before volume_uv_scroll.update_volume in the same diag order.
    public static class shafts_lights
    {
        private static bool _census_done;
        private static float _last_traced_lift = -1f;

        // the last lift this walker published (the bloom feature reads it
        // to fold the shaft lift into the downsample intensity).
        public static float last_lift;

        // shaft state published to the render pass: enabled by the key,
        // intensity from alpha, pivot = the stage Sunshafts marker projected
        // to screen (the game's SunShaftsComposite pivots there, §760). the
        // volume walker sets the tint from its color1; the lightShafts
        // family leaves it white.
        public static bool pass_enabled;
        public static float pass_intensity;
        public static Vector2 pass_pivot;
        public static Color pass_tint = Color.white;

        // the stage Sunshafts marker, resolved lazily from the loaded stage.
        private static Transform _shafts_marker;

        public static void update(float time_sec, List<shafts_track_container> tracks)
        {
            // this walker owns its lift per frame; zero it before any early
            // return so a volume-driven frame never bleeds a stale lift.
            last_lift = 0f;

            // the volume SunShafts entry is the authoritative shaft pass
            // when it runs (the game's SunShafts controller: radial
            // god-rays at the sun position, power as the weight). the
            // lightShafts family is the secondary lift-only track.
            volume_frame(time_sec);
            if (pass_enabled && _volume_drove) return;

            if (tracks == null || tracks.Count == 0) return;

            if (!_census_done)
            {
                _census_done = true;
                trace_log.write($"light shafts: {tracks.Count} entries authored");
            }

            float frame = time_sec * 60f;
            float lift = 0f;
            bool any_enabled = false;
            float intensity = 0f;

            foreach (var track in tracks)
            {
                if (track.keys == null || track.keys.Count == 0) continue;

                // current key: last key whose frame <= now; publish verbatim.
                int idx = -1;
                for (int i = 0; i < track.keys.Count; i++)
                    if (track.keys[i].frame <= frame) idx = i;
                if (idx < 0) continue;
                var cur = track.keys[idx];

                if (cur.enabled == 0) continue;
                any_enabled = true;
                float a = Mathf.Clamp01(cur.alpha.x);
                lift = Mathf.Max(lift, Mathf.Min(a * 0.05f, 1.2f));
                intensity = Mathf.Max(intensity, a);
            }

            if (any_enabled)
            {
                if (lift != _last_traced_lift)
                {
                    _last_traced_lift = lift;
                    trace_log.write($"light shafts: lift {lift:0.000}");
                }
                Shader.SetGlobalFloat(id_volume_light_bloom_lift, lift);
                last_lift = lift;

                pass_enabled = true;
                pass_intensity = intensity;
                if (_shafts_marker == null)
                {
                    var marker = blink_lights.find_stage_object("Sunshafts");
                    _shafts_marker = marker != null ? marker.transform : null;
                }
                if (_shafts_marker != null && Camera.main != null)
                {
                    var vp = Camera.main.WorldToViewportPoint(_shafts_marker.position);
                    pass_pivot = new Vector2(vp.x, vp.y);
                }
                else pass_pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                last_lift = 0f;
                pass_enabled = false;
                pass_intensity = 0f;
            }
        }

        public static void reset() => _census_done = false;

        // the volume walker's state this frame: when the volume
        // SunShafts entry is enabled it drives the pass alone.
        private static bool _volume_drove;
        private static bool _volume_census_done;

        // the volumeLight SunShafts entry: radial god-rays keyed by
        // power (percent units, x0.01 like the other authored scales)
        // with color1 as the tint and the sunPosition as the pivot.
        private static void volume_frame(float time_sec)
        {
            _volume_drove = false;
            var tracks = stage_loader.volume_tracks_view;
            if (tracks == null || tracks.Count == 0) return;

            if (!_volume_census_done)
            {
                _volume_census_done = true;
                trace_log.write($"volume sunshafts: {tracks.Count} entries");
            }

            float frame = time_sec * 60f;
            foreach (var t in tracks)
            {
                if (t.keys == null || t.keys.Count == 0) continue;
                var k = t.current_key(frame);
                if (k == null || k.enable == 0) continue;

                _volume_drove = true;
                pass_enabled = true;
                pass_intensity = Mathf.Clamp01(k.power * 0.01f);
                pass_tint = k.color1;
                // the sun position is world-space on the stage; pivot =
                // its viewport projection (the game's composite radializes
                // around the projected sun).
                if (Camera.main != null)
                {
                    var pos = new Vector3(k.sun_position.x, k.sun_position.y, k.sun_position.z);
                    var vp = Camera.main.WorldToViewportPoint(pos);
                    pass_pivot = new Vector2(vp.x, vp.y);
                }
                return;
            }
        }

        private static readonly int id_volume_light_bloom_lift =
            Shader.PropertyToID("_VolumeLightBloomLift");
    }
}
