using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's WashLightList: the wash light is a stage-wide
    // camera-facing wash whose consumer does NOT interpolate whole keys — it
    // hands the controller the current key raw plus two lerped floats
    // (RaycastDistance/CameraProjectionSide/CameraProjectionColorPower), and
    // the entry's IsAllSettings flag gates whether the controller layers its
    // own serialized defaults on top. UV2 resolves the entry name against the
    // stage map and drives the matched object's lights + emission wash.
    public static class wash_light
    {
        private class wash_container
        {
            public Transform root;
            public List<Light> lights = new();
            public List<Renderer> renderers = new();
            public MaterialPropertyBlock mpb;
        }
        private static readonly Dictionary<string, wash_container> containers = new();

        // binds the wash entries against the recorded stage children; the
        // game resolves the controller by the keys list's name hash, but the
        // shipped entry names are display names ('WashLight Object'), so the
        // fixtures that carry the wash resolve by their own washlight naming.
        public static void bind(List<wash_track_container> tracks)
        {
            containers.Clear();
            if (tracks == null)
            {
                trace_log.write("wash lights: no authored track");
                return;
            }
            int resolved = 0;
            var missing = new List<string>();
            var wash_fixtures = new List<Transform>();
            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                var go = blink_lights.find_stage_object(t.name);
                if (go == null)
                {
                    // the display-name entries: any washlight-named stage child carries the wash.
                    if (wash_fixtures.Count == 0)
                    {
                        foreach (var kv in blink_lights.all_stage_children())
                        {
                            if (kv.Value == null) continue;
                            if (kv.Key.ToLowerInvariant().Contains("washlight"))
                                wash_fixtures.Add(kv.Value.transform);
                        }
                    }
                    if (wash_fixtures.Count == 0)
                    {
                        missing.Add(t.name);
                        continue;
                    }
                    // every authored entry drives the shared wash set.
                    foreach (var fx in wash_fixtures)
                    {
                        var key = t.name + ":" + fx.name;
                        if (containers.ContainsKey(key)) continue;
                        var c = new wash_container { root = fx, mpb = new MaterialPropertyBlock() };
                        foreach (var l in fx.GetComponentsInChildren<Light>(true)) c.lights.Add(l);
                        foreach (var r in fx.GetComponentsInChildren<Renderer>(true)) c.renderers.Add(r);
                        containers[key] = c;
                    }
                    resolved++;
                    continue;
                }
                var c2 = new wash_container { root = go.transform, mpb = new MaterialPropertyBlock() };
                foreach (var l in go.GetComponentsInChildren<Light>(true)) c2.lights.Add(l);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) c2.renderers.Add(r);
                containers[t.name] = c2;
                resolved++;
            }
            trace_log.write($"wash lights: {resolved} entries resolved ({containers.Count} containers), {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"wash light unresolved: {m}");
        }

        // samples every authored entry per frame: the raw-key handoff + the
        // three lerped floats; the color power scales the wash emission. the
        // display-name entries fan out over every fixture they bound.
        public static void update(float time_sec, List<wash_track_container> tracks)
        {
            if (tracks == null || containers.Count == 0) return;
            float frame = time_sec * 60f;

            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                var (raycast, side, color_power) = t.sample(frame);

                // the game's controller fades the wash by the camera-side
                // projection; UV2 scales the light + emission by it.
                float wash = Mathf.Clamp01(color_power);
                foreach (var kv in containers)
                {
                    // one entry drives its exact-name container plus the
                    // display-name fan-out keys ('entry:fixture').
                    if (kv.Key != t.name && !kv.Key.StartsWith(t.name + ":")) continue;
                    var c = kv.Value;
                    if (c == null) continue;
                    if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                    c.mpb.SetFloat(id_emission_power, wash);
                    foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
                    foreach (var l in c.lights)
                        l.intensity = wash * 2f;
                }
            }
        }

        private static readonly int id_emission_power = Shader.PropertyToID("_EmissionPower");
    }

    // one worksheet wash-light entry: name + raw-key handoff fields.
    [Serializable]
    public class wash_track_container
    {
        public string name;
        public int is_all_settings;
        public List<wash_key> keys = new();

        // the current key's raw values plus the three lerped floats; the
        // decoded consumer lerps only the RaycastDistance/Side/ColorPower.
        public (float, float, float) sample(float frame)
        {
            if (keys == null || keys.Count == 0) return (0f, 0f, 0f);
            wash_key a, b;
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
            float raycast = Mathf.Lerp(a.raycast_distance, b.raycast_distance, blend);
            float side = Mathf.Lerp(a.camera_projection_side, b.camera_projection_side, blend);
            float power = Mathf.Lerp(a.camera_projection_color_power, b.camera_projection_color_power, blend);
            return (raycast, side, power);
        }
    }

    // one wash-light key.
    [Serializable]
    public class wash_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public float raycast_distance;
        public float camera_projection_side;
        public float camera_projection_color_power;
    }
}
