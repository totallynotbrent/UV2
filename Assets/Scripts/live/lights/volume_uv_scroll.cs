using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's volume light (bloom lift + tint) and uv material scroll tracks.
    public static class volume_uv_scroll
    {
        // the volume side: the resolved entry count + whether any key is enabled.
        private static int _volume_entries;
        private static int _volume_enabled;

        // every stage material by asset name, for the uv scroll entries.
        private static readonly Dictionary<string, List<Material>> stage_materials = new();
        private static readonly Dictionary<string, uv_target> uv_targets = new();

        private class uv_target
        {
            public List<Material> materials = new();
        }

        // records one loaded stage hierarchy's materials, safe to call repeatedly.
        public static void record_stage_materials(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    var clean = m.name.Replace(" (Instance)", "");
                    if (!stage_materials.TryGetValue(clean, out var list))
                        stage_materials[clean] = list = new List<Material>();
                    if (!list.Contains(m)) list.Add(m);
                }
            }
        }

        // clears the recorded stage materials so a new song inherits nothing.
        public static void reset()
        {
            stage_materials.Clear();
            uv_targets.Clear();
            _volume_entries = 0;
            _volume_enabled = 0;
        }

        // counts the volume entries and resolves the uv scroll entries against the stage materials by name.
        public static void bind(List<volume_track_container> volume_tracks,
                                List<uv_scroll_track_container> uv_tracks)
        {
            uv_targets.Clear();

            if (volume_tracks != null)
            {
                foreach (var t in volume_tracks)
                {
                    if (t.keys == null || t.keys.Count == 0) continue;
                    _volume_entries++;
                    if (t.keys[0].enable != 0) _volume_enabled++;
                }
                trace_log.write($"volume lights: {_volume_entries} containers, {_volume_enabled} with an enabled first key");
            }
            else
                trace_log.write("volume lights: no authored track");

            if (uv_tracks == null || uv_tracks.Count == 0)
            {
                trace_log.write("uv scroll lights: no authored track");
                return;
            }

            int resolved = 0;
            var missing = new List<string>();
            foreach (var t in uv_tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                if (!stage_materials.TryGetValue(t.name, out var mats) || mats.Count == 0)
                {
                    missing.Add(t.name);
                    continue;
                }
                uv_targets[t.name] = new uv_target { materials = mats };
                resolved++;
            }
            trace_log.write($"uv scroll lights: {resolved} materials resolved, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"uv scroll light unresolved: {m}");
        }

        // publishes the first enabled entry's lerped power/color as shader-global bloom lift + tint.
        public static void update_volume(float time_sec, List<volume_track_container> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;
            float frame = time_sec * 60f;

            foreach (var t in tracks)
            {
                if (t.keys == null || t.keys.Count == 0) continue;
                var (power, color, ok) = t.sample(frame);
                if (!ok) continue;

                // the entry's brightness power multiplies into the tint color.
                color *= t.brightness_power;

                // lift = min(power * 0.02, 1.2); tint = color.
                Shader.SetGlobalFloat(id_volume_lift, Mathf.Min(power * 0.02f, 1.2f));
                Shader.SetGlobalColor(id_volume_tint, color);
                return;
            }
        }

        // publishes the lerped colors, power, and scroll offset onto every resolved material each frame.
        public static void update_uv_scroll(float time_sec, List<uv_scroll_track_container> tracks)
        {
            if (tracks == null || uv_targets.Count == 0) return;
            float frame = time_sec * 60f;

            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                if (!uv_targets.TryGetValue(t.name, out var target)) continue;
                var (color0, color1, power, offset, speed) = t.sample(frame);

                // the scroll offset advances by speed * seconds since the current key.
                float elapsed = t.elapsed_seconds(frame);
                Vector2 scrolled = offset + speed * elapsed;

                foreach (var m in target.materials)
                {
                    if (m == null) continue;
                    if (m.HasProperty(id_mul_color0)) m.SetColor(id_mul_color0, color0);
                    if (m.HasProperty(id_mul_color1)) m.SetColor(id_mul_color1, color1);
                    if (m.HasProperty(id_color_power)) m.SetFloat(id_color_power, power);
                    if (m.HasProperty(id_main_tex)) m.SetTextureOffset(id_main_tex, scrolled);
                }
            }
        }

        private static readonly int id_volume_lift = Shader.PropertyToID("_VolumeLightBloomLift");
        private static readonly int id_volume_tint = Shader.PropertyToID("_VolumeLightTint");
        private static readonly int id_mul_color0 = Shader.PropertyToID("_MulColor0");
        private static readonly int id_mul_color1 = Shader.PropertyToID("_MulColor1");
        private static readonly int id_color_power = Shader.PropertyToID("_ColorPower");
        private static readonly int id_main_tex = Shader.PropertyToID("_MainTex");
    }

    // one worksheet volumeLight entry: the sun-shaft keys.
    [Serializable]
    public class volume_track_container
    {
        public string name;
        public List<volume_key> keys = new();
        public float brightness_power = 1f;

        // the lerped sun-shaft state; ok=false when the current key is off.
        public (float, Color, bool) sample(float frame)
        {
            if (keys == null || keys.Count == 0) return (0f, Color.white, false);
            volume_key a, b;
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
            if (a.enable == 0) return (0f, Color.white, false);
            float power = Mathf.Lerp(a.power, b.power, blend);
            Color color = Color.Lerp(a.color1, b.color1, blend);
            return (power, color, true);
        }
    }

    // one volumeLight key.
    [Serializable]
    public class volume_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public Vector3 sun_position;
        public Color color1;
        public float power;
        public float komorebi;
        public float blur_radius;
        public float color_rate;
        public int enable;
        public int is_enabled_border_clear;
        public float brightness_power;
    }

    // one worksheet uvScrollLight entry: material name + the scroll keys.
    [Serializable]
    public class uv_scroll_track_container
    {
        public string name;
        public List<uv_scroll_key> keys = new();

        // the lerped colors/power/offset/speed for the frame.
        public (Color, Color, float, Vector2, Vector2) sample(float frame)
        {
            if (keys == null || keys.Count == 0)
                return (Color.white, Color.white, 0f, Vector2.zero, Vector2.zero);
            uv_scroll_key a, b;
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
            Color c0 = Color.Lerp(a.mul_color0, b.mul_color0, blend);
            Color c1 = Color.Lerp(a.mul_color1, b.mul_color1, blend);
            float power = Mathf.Lerp(a.color_power, b.color_power, blend);
            Vector2 offset = Vector2.Lerp(
                new Vector2(a.scroll_offset_x, a.scroll_offset_y),
                new Vector2(b.scroll_offset_x, b.scroll_offset_y), blend);
            Vector2 speed = Vector2.Lerp(
                new Vector2(a.scroll_speed_x, a.scroll_speed_y),
                new Vector2(b.scroll_speed_x, b.scroll_speed_y), blend);
            return (c0, c1, power, offset, speed);
        }

        // seconds elapsed since the current key.
        public float elapsed_seconds(float frame)
        {
            if (keys == null || keys.Count == 0) return 0f;
            foreach (var k in keys)
                if (frame >= k.frame) return (frame - k.frame) / 60f;
            return 0f;
        }
    }

    // one uvScrollLight key.
    [Serializable]
    public class uv_scroll_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public Color mul_color0;
        public Color mul_color1;
        public float color_power;
        public float scroll_offset_x;
        public float scroll_offset_y;
        public float scroll_speed_x;
        public float scroll_speed_y;
    }
}
