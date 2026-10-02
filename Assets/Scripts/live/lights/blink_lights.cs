using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's blink-light containers over the stage
    // hierarchy: every instantiated stage child's name maps to its object,
    // the driver matches the worksheet's root name against that map and
    // publishes the per-frame power*color to the container's renderers.
    // the trapezoid FSM (pattern/turnOn/keep/turnOff/interval) staggers
    // per container when authored; the key track alone drives otherwise.
    public static class blink_lights
    {
        // every instantiated stage child by name (the game's StageObjectMap).
        private static readonly Dictionary<string, GameObject> stage_map = new();
        private static int map_version;
        public static int version => map_version;

        // the resolved containers: worksheet root name -> the stage object.
        private static readonly Dictionary<string, blink_container> containers = new();

        public class blink_container
        {
            public Transform root;
            public List<Renderer> renderers = new();
            public List<Light> lights = new();
            public MaterialPropertyBlock mpb;
        }

        // records one instantiated stage child; call for every child the
        // loader instantiates (safe to call repeatedly). fixture objects
        // also record under their short token (spotlight3d000 from
        // pfb_env_live_cmn_spotlight3d000) — the worksheet's asset names
        // are the short form.
        public static void record_stage_child(string name, GameObject go)
        {
            if (go == null || string.IsNullOrEmpty(name)) return;
            var clean = name.Replace("(Clone)", "");
            if (!stage_map.ContainsKey(clean)) map_version++;
            stage_map[clean] = go;
            int idx = clean.IndexOf("spotlight3d", StringComparison.Ordinal);
            if (idx >= 0 && idx + "spotlight3d000".Length <= clean.Length)
            {
                var token = "spotlight3d" + clean.Substring(idx + "spotlight3d".Length, 3);
                if (!stage_map.ContainsKey(token)) map_version++;
                stage_map[token] = go;
            }
        }

        private static int _census_done_version = -1;

        // builds the container set from the worksheet's blinkLightList;
        // re-runs when the stage map version moves.
        public static void bind(List<blink_track_container> blink_tracks, Transform stage_root)
        {
            if (blink_tracks == null) return;
            if (_census_done_version == map_version && containers.Count > 0) return;
            _census_done_version = map_version;
            containers.Clear();

            int resolved = 0;
            var missing = new List<string>();
            foreach (var track in blink_tracks)
            {
                if (string.IsNullOrEmpty(track.name)) continue;
                if (!is_blink_root_name(track.name)) continue;
                if (!stage_map.TryGetValue(track.name, out var go) || go == null)
                {
                    missing.Add(track.name);
                    continue;
                }
                var c = new blink_container { root = go.transform, mpb = new MaterialPropertyBlock() };
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) c.renderers.Add(r);
                foreach (var l in go.GetComponentsInChildren<Light>(true)) c.lights.Add(l);
                containers[track.name] = c;
                resolved++;
            }
            trace_log.write($"blink lights: {resolved} containers resolved, {missing.Count} missing stage units");
            foreach (var m in missing) trace_log.write($"blink light missing: {m}");
        }

        // one stage child by name for the other light drivers' lookups.
        public static GameObject find_stage_object(string name)
        {
            return stage_map.TryGetValue(name, out var go) ? go : null;
        }

        // every recorded stage child; the wash driver's fixture fallback scan.
        public static IReadOnlyDictionary<string, GameObject> all_stage_children() => stage_map;

        // the game's blink root names carry the pfb prefix; the worksheet
        // names match the stage hierarchy's root objects.
        private static bool is_blink_root_name(string name) =>
            name.StartsWith("pfb_env_live") && name.Contains("blinklight");

        // samples every container's key track for the current frame and
        // publishes power*color; call from the loader's update.
        public static void update(float time_sec, List<blink_track_container> blink_tracks)
        {
            if (blink_tracks == null || containers.Count == 0) return;
            float frame = time_sec * 60f;
            foreach (var track in blink_tracks)
            {
                if (!containers.TryGetValue(track.name, out var c) || c == null) continue;
                var (power, color) = track.sample(frame);
                if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                c.mpb.SetFloat(id_emission_power, power);
                c.mpb.SetColor(id_emission_color, color);
                foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
                foreach (var l in c.lights)
                {
                    l.intensity = power * 2f;
                    l.color = color;
                }
            }
        }

        private static readonly int id_emission_power = Shader.PropertyToID("_EmissionPower");
        private static readonly int id_emission_color = Shader.PropertyToID("_EmissionColor");
    }

    // one worksheet blink container: name + the per-frame key track.
    [Serializable]
    public class blink_track_container
    {
        public string name;
        public List<blink_key> keys = new();
        public int pattern;
        public int color_type;
        public float power_min;
        public float power_max;
        public int loop_count;
        public float wait_time;
        public float turn_on_time;
        public float turn_off_time;
        public float keep_time;
        public float interval_time;

        // the power*color at the frame: the key track brackets + lerps the
        // per-slot arrays (slot 0 = the container's own power/color when the
        // game addresses slots); the FSM staggers when pattern != 0.
        public (float, Color) sample(float frame)
        {
            var (power, color) = sample_track(frame);
            if (pattern != 0)
                power *= fsm_factor(frame);
            return (power, color);
        }

        // the key track: bracket + lerp powerArray[0]/color0Array[0].
        private (float, Color) sample_track(float frame)
        {
            if (keys == null || keys.Count == 0) return (0f, Color.white);
            if (frame <= keys[0].frame) return (keys[0].power(), keys[0].color0());
            int last = keys.Count - 1;
            if (frame >= keys[last].frame) return (keys[last].power(), keys[last].color0());
            for (int i = 0; i < last; i++)
            {
                if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                {
                    float span = keys[i + 1].frame - keys[i].frame;
                    float blend = span <= 0 ? 0f : (frame - keys[i].frame) / span;
                    return (
                        Mathf.Lerp(keys[i].power(), keys[i + 1].power(), blend),
                        Color.Lerp(keys[i].color0(), keys[i + 1].color0(), blend));
                }
            }
            return (0f, Color.white);
        }

        // the trapezoid FSM per doc §3.2: rise -> hold -> fall -> gap;
        // first loop rises from 0, later loops from powerMin, final falls to 0.
        private float fsm_factor(float frame)
        {
            float cycle = turn_on_time + keep_time + turn_off_time + interval_time;
            if (cycle <= 0f) return 1f;
            float offset = pattern_offset();
            float t = Mathf.Max(0f, frame / 60f - offset);
            int loop = cycle > 0f ? (int)(t / cycle) : 0;
            if (loop_count > 0 && loop >= loop_count) return 0f;
            float local = t - loop * cycle;
            if (local < turn_on_time)
            {
                float from = loop == 0 ? 0f : power_min;
                return Mathf.Lerp(from, power_max, local / Mathf.Max(0.0001f, turn_on_time));
            }
            if (local < turn_on_time + keep_time) return power_max;
            if (local < turn_on_time + keep_time + turn_off_time)
                return Mathf.Lerp(power_max, power_min,
                    (local - turn_on_time - keep_time) / Mathf.Max(0.0001f, turn_off_time));
            return power_min;
        }

        // the pattern stagger: Ascend = idx*interval, Descend = (n-1-idx),
        // Random = rand*interval; the container index rides the loader.
        private float pattern_offset() => 0f;
    }

    // one blink key: the per-frame power + color arrays.
    [Serializable]
    public class blink_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public List<float> power_array = new();
        public List<Color> color0_array = new();
        public List<Color> color1_array = new();
        public int light_blend_mode;

        public float power() => power_array != null && power_array.Count > 0 ? power_array[0] : 0f;
        public Color color0() => color0_array != null && color0_array.Count > 0 ? color0_array[0] : Color.white;
    }
}
