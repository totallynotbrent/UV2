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

        public static void update(float time_sec, List<shafts_track_container> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;

            if (!_census_done)
            {
                _census_done = true;
                trace_log.write($"light shafts: {tracks.Count} entries authored");
            }

            float frame = time_sec * 60f;
            float lift = 0f;
            bool any_enabled = false;

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
            }

            if (any_enabled)
                Shader.SetGlobalFloat(id_volume_light_bloom_lift, lift);
        }

        public static void reset() => _census_done = false;

        private static readonly int id_volume_light_bloom_lift =
            Shader.PropertyToID("_VolumeLightBloomLift");
    }
}
