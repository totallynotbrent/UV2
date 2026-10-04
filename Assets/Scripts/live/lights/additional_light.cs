using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's additional-light entries, one Unity light per entry.
    public static class additional_light
    {
        // one light per worksheet entry index.
        private static readonly Dictionary<int, Light> lights = new();

        // the bind report: authored entries + their enable census.
        public static void bind(List<additional_track_container> tracks)
        {
            foreach (var kv in lights)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value.gameObject);
            lights.Clear();
            if (tracks == null)
            {
                trace_log.write("additional lights: no authored track");
                return;
            }
            int enabled = 0;
            foreach (var t in tracks)
                if (t.keys != null)
                    foreach (var k in t.keys)
                        if (k.is_enable != 0) { enabled++; break; }
            trace_log.write($"additional lights: {tracks.Count} entries, {enabled} with an enabled key");
        }

        // samples every entry per frame and publishes to its light.
        public static void update(float time_sec, List<additional_track_container> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;
            float frame = time_sec * 60f;

            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t.keys == null || t.keys.Count == 0) continue;
                var info = t.sample(frame);

                var light = ensure_light(i);
                light.transform.SetPositionAndRotation(info.position, Quaternion.Euler(info.rotate));
                light.type = (LightType)info.type;
                light.range = info.range;
                light.spotAngle = info.spot_angle;
                light.bounceIntensity = info.indirect_multiplier;
                light.intensity = info.strength;
                // remap the stored shadow type: 0 -> soft, 1 -> hard, else soft.
                light.shadows = info.shadow_type switch
                {
                    0 => LightShadows.Soft,
                    1 => LightShadows.Hard,
                    _ => LightShadows.Soft,
                };
                light.enabled = info.is_enable != 0;
            }
        }

        // creates (or returns) the light for an entry index.
        private static Light ensure_light(int index)
        {
            if (lights.TryGetValue(index, out var existing) && existing != null)
                return existing;
            var host = new GameObject($"additional_light_{index}");
            var light = host.AddComponent<Light>();
            light.shadows = LightShadows.None;
            lights[index] = light;
            return light;
        }

        // the assembled per-frame light state.
        public struct additional_state
        {
            public Vector3 position;
            public Vector3 rotate;
            public int is_enable;
            public int type;
            public float range;
            public float spot_angle;
            public float indirect_multiplier;
            public float strength;
            public int shadow_type;
        }
    }

    // one worksheet additional-light entry: the key list.
    [Serializable]
    public class additional_track_container
    {
        public string name;
        public List<additional_key> keys = new();

        // the lerped light state for the frame (IsEnable/Type/ShadowType raw).
        public additional_light.additional_state sample(float frame)
        {
            if (keys == null || keys.Count == 0)
                return default;
            additional_key a, b;
            float blend;
            if (frame <= keys[0].frame) { a = b = keys[0]; blend = 0f; }
            else if (frame >= keys[keys.Count - 1].frame) { a = b = keys[keys.Count - 1]; blend = 0f; }
            else
            {
                a = b = keys[keys.Count - 1]; blend = 0f;
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                    {
                        a = keys[i]; b = keys[i + 1];
                        float span = b.frame - a.frame;
                        blend = span <= 0 ? 0f : (frame - a.frame) / span;
                        break;
                    }
                }
            }
            var s = new additional_light.additional_state
            {
                position = Vector3.Lerp(a.position, b.position, blend),
                rotate = Vector3.Lerp(a.rotate, b.rotate, blend),
                is_enable = a.is_enable,
                type = a.type,
                range = Mathf.Lerp(a.range, b.range, blend),
                spot_angle = Mathf.Lerp(a.spot_angle, b.spot_angle, blend),
                indirect_multiplier = Mathf.Lerp(a.indirect_multiplier, b.indirect_multiplier, blend),
                strength = Mathf.Lerp(a.strength, b.strength, blend),
                shadow_type = a.shadow_type,
            };
            return s;
        }
    }

    // one additional-light key.
    [Serializable]
    public class additional_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public Vector3 position;
        public Vector3 rotate;
        public int is_enable;
        public int type;
        public float range;
        public float spot_angle;
        public float indirect_multiplier;
        public int shadow_type;
        public float strength;
        public float bias;
        public float normal_bias;
        public float near_plane;
    }
}
