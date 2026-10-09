using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's TransmittedLight keys: the subsurface glow pass.
    // the game runs a real screen-space transmission (threshold-masked HDR,
    // iterated blur, additive recombine); UV2 approximates the visible effect
    // through the renderer's bloom channel the same way the fork's pass
    // params describe it: enabled gates the write, intensity scales the lift,
    // threshold/blur spread shape the softness window. published verbatim per
    // key, no lerp (the game's walker doesn't interpolate either).
    public static class transmitted_light
    {
        private static bool _census_done;
        private static bool _was_enabled;

        public static void update(float time_sec, List<transmitted_key> keys)
        {
            if (keys == null || keys.Count == 0) return;

            if (!_census_done)
            {
                _census_done = true;
                trace_log.write($"transmitted light: {keys.Count} keys authored");
            }

            float frame = time_sec * 60f;

            // current key: last key whose frame <= now.
            int idx = -1;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].frame <= frame) idx = i;
            if (idx < 0) return;
            var cur = keys[idx];

            // the game gates on the walker's ATTR_ENABLE = the key's
            // attribute WORD being nonzero (fork LiveTimelineControl 4202);
            // 1151's transmitted keys author 0x10000.
            bool enabled = cur.attribute != 0;

            if (enabled != _was_enabled)
            {
                trace_log.write($"transmitted light: {(enabled ? "on" : "off")} frame {(int)frame} it{cur.iterations} in{cur.intensity:0.00} th{cur.threshold:0.00} bs{cur.blur_spread:0.00} bm{cur.blend_mode}");
                _was_enabled = enabled;
            }

            if (!enabled)
            {
                Shader.SetGlobalFloat(id_transmitted_intensity, 0f);
                return;
            }

            Shader.SetGlobalFloat(id_transmitted_intensity, Mathf.Max(0f, cur.intensity));
            Shader.SetGlobalFloat(id_transmitted_threshold, cur.threshold);
            Shader.SetGlobalFloat(id_transmitted_blur_spread, cur.blur_spread);
            Shader.SetGlobalInt(id_transmitted_blend_mode, cur.blend_mode);
            Shader.SetGlobalInt(id_transmitted_iterations, cur.iterations);
        }

        public static void reset()
        {
            _census_done = false;
            _was_enabled = false;
        }

        private static readonly int id_transmitted_intensity = Shader.PropertyToID("_TransmittedLightIntensity");
        private static readonly int id_transmitted_threshold = Shader.PropertyToID("_TransmittedLightThreshold");
        private static readonly int id_transmitted_blur_spread = Shader.PropertyToID("_TransmittedLightBlurSpread");
        private static readonly int id_transmitted_blend_mode = Shader.PropertyToID("_TransmittedLightBlendMode");
        private static readonly int id_transmitted_iterations = Shader.PropertyToID("_TransmittedLightIterations");
    }
}
