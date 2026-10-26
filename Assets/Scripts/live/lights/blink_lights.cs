using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's blink-light containers over the stage hierarchy,
    // per-slot like the game: each light<N>_ child under the root is its own
    // slot with its own envelope phase, color picked by colorType, hue blended
    // through FNV-derived ratios per slot and loop.
    public static class blink_lights
    {
        private const int k_max_slots = 10;

        // every instantiated stage child by name.
        private static readonly Dictionary<string, GameObject> stage_map = new();
        private static int map_version;
        public static int version => map_version;

        // the resolved containers: worksheet root name -> the stage object.
        private static readonly Dictionary<string, blink_container> containers = new();

        // film keys can couple a post-film layer to a named blink container:
        // container name -> brightness power multiplier.
        private static readonly Dictionary<string, float> film_coupling = new();

        public class blink_container
        {
            public Transform root;
            public List<Renderer> renderers = new();
            public List<Light> lights = new();
            public MaterialPropertyBlock mpb;
            public List<Material> materials = new();

            // per-slot renderers/lights resolved from light<N>_ child names.
            public Dictionary<int, slot_group> slot_renderers = new();
        }

        // one light<N_> slot's renderers and lights under a blink root.
        public class slot_group
        {
            public List<Renderer> renderers = new();
            public List<Light> lights = new();
            public MaterialPropertyBlock mpb = new();
        }

        // records a stage child by name and aliases spotlight3d fixtures under their short token.
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

        // builds the container set from the blink tracks and re-runs when the stage map changes.
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

                // per-slot addressing: children named light<N>_ (or an ancestor
                // with that name) own one slot each, like the game's fixture
                // addressing; unindexed children fall back to the root slot.
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == go.transform) continue;
                    int slot = parse_light_index(t.name);
                    if (slot < 0)
                    {
                        // walk up: the owning fixture name may sit above the mesh
                        var walk = t.parent;
                        while (walk != null && walk != go.transform)
                        {
                            slot = parse_light_index(walk.name);
                            if (slot >= 0) break;
                            walk = walk.parent;
                        }
                    }
                    if (slot < 0 || slot >= k_max_slots) continue;
                    if (!c.slot_renderers.TryGetValue(slot, out var g))
                        c.slot_renderers[slot] = g = new slot_group();
                    var rend = t.GetComponent<Renderer>();
                    if (rend != null) g.renderers.Add(rend);
                    var lig = t.GetComponent<Light>();
                    if (lig != null) g.lights.Add(lig);
                }

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

        // parses light<N>_ child names; the game's per-slot fixture addressing.
        private static int parse_light_index(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 6) return -1;
            if ((name[0] != 'l' && name[0] != 'L') ||
                (name[1] != 'i' && name[1] != 'I') ||
                (name[2] != 'g' && name[2] != 'G') ||
                (name[3] != 'h' && name[3] != 'H') ||
                (name[4] != 't' && name[4] != 'T')) return -1;
            int i = 5, value = 0;
            bool any = false;
            while (i < name.Length && name[i] >= '0' && name[i] <= '9')
            {
                value = value * 10 + (name[i] - '0');
                any = true;
                i++;
                if (value > 100000) return -1;
            }
            if (!any) return -1;
            if (i < name.Length && name[i] != '_') return -1;
            return value;
        }

        // blink root names start with pfb_env_live and contain blinklight or blinkbeamlight.
        public static bool is_blink_root_name(string name) =>
            name.StartsWith("pfb_env_live") &&
            (name.Contains("blinklight") || name.Contains("blinkbeamlight"));

        // film-coupling: the post-film layer publishes the container it wants
        // pulsed; keyed per container name, cleared per film key like the game.
        public static void set_film_coupling(string container_name, float brightness_power)
        {
            if (string.IsNullOrEmpty(container_name)) return;
            film_coupling[container_name] = brightness_power;
        }

        public static void clear_film_coupling() => film_coupling.Clear();

        private static readonly List<string> _census_reported = new();
        private static readonly Dictionary<string, int> _last_traced_key = new();

        // samples every container's key track and publishes per-slot color*power.
        public static void update(float time_sec, List<blink_track_container> blink_tracks)
        {
            if (blink_tracks == null || containers.Count == 0) return;

            foreach (var track in blink_tracks)
            {
                if (!containers.TryGetValue(track.name, out var c) || c == null) continue;
                apply_render_queues(c);

                // find the current key: the last key whose frame <= now,
                // blended toward the next when the game interpolates.
                var cur = track.current_key_blended(time_sec);
                if (cur == null) continue;

                // key-relative clock, like the game's progressTime. the
                // anchor stays the unblended key's start; the blend shapes
                // the values, not the clock.
                var cur_anchor = track.current_key(time_sec);
                float local = Mathf.Max(0f, time_sec - cur_anchor.time);
                int slot_count = track.slot_count;
                if (slot_count <= 0) continue;
                // the game's container count is the FIXTURE count (the
                // highest resolvable light<N>_ index plus one), not the
                // authored array length: the worksheets ship ten array
                // slots per key while a rig has three fixtures, and the
                // chase envelopes step over the fixtures that exist.
                if (c.slot_renderers.Count > 0) slot_count = c.slot_renderers.Count;

                float power_scale = 1f;
                if (film_coupling.TryGetValue(track.name, out var film_power) && film_power > 0f)
                    power_scale *= film_power;

                if (c.slot_renderers.Count > 0)
                {
                    // per-slot publish: each light<N>_ slot group gets its own
                    // color/power through its own MPB, the game's model.
                    foreach (var kv in c.slot_renderers)
                    {
                        int slot = kv.Key;
                        if (slot >= slot_count) continue;
                        var (power, color) = sample_slot(track, cur, local, slot, slot_count);
                        float p = Mathf.Max(0f, power) * power_scale;
                        var g = kv.Value;
                        publish_slot_color(g.mpb, color, p);
                        foreach (var r in g.renderers) r.SetPropertyBlock(g.mpb);
                        foreach (var l in g.lights)
                        {
                            l.intensity = p;
                            l.color = color;
                        }
                    }

                    // one-shot census per root: log the slot model once.
                    if (!_census_reported.Contains(track.name))
                    {
                        _census_reported.Add(track.name);
                        trace_log.write($"blink slots: '{track.name}' {c.slot_renderers.Count} slot groups, {slot_count} worksheet slots, key at f{cur.frame}");
                    }
                    // trace key changes (family + applied color/power) so a
                    // cue that fails to show has a paper trail.
                    if (!_last_traced_key.TryGetValue(track.name, out var last_k) || last_k != cur.frame)
                    {
                        _last_traced_key[track.name] = cur.frame;
                        // every slot, with its published state and how many
                        // renderers carry it: a rig that publishes but never
                        // shows up is distinguishable from one that never
                        // publishes.
                        var parts = new System.Text.StringBuilder();
                        var cam = Camera.main;
                        for (int s = 0; s < slot_count; s++)
                        {
                            var st = sample_slot(track, cur, local, s, slot_count);
                            c.slot_renderers.TryGetValue(s, out var sg);
                            int rc = sg != null ? sg.renderers.Count : 0;
                            int lc = sg != null ? sg.lights.Count : 0;
                            string view = "";
                            if (sg != null && sg.renderers.Count > 0)
                            {
                                var r0 = sg.renderers[0];
                                if (r0 != null)
                                {
                                    bool in_view = false;
                                    if (cam != null)
                                        in_view = GeometryUtility.TestPlanesAABB(
                                            GeometryUtility.CalculateFrustumPlanes(cam), r0.bounds);
                                    view = $"[{r0.gameObject.name} en{(r0.enabled ? 1 : 0)} ac{(r0.gameObject.activeInHierarchy ? 1 : 0)} fw{(in_view ? 1 : 0)}]";
                                }
                            }
                            parts.Append($" [{s}]p{st.Item1:0.00}r{st.Item2.r:0.00}g{st.Item2.g:0.00}b{st.Item2.b:0.00}m{rc}l{lc}{view}");
                        }
                        trace_log.write($"blink key: '{track.name}' f{cur.frame}{parts}");
                    }
                    continue;
                }

                // fallback container-level publish (slot 0's state) for roots
                // whose stage assets carry no light<N>_ children.
                var (cpower, ccolor) = sample_slot(track, cur, local, 0, slot_count);
                float cp = Mathf.Max(0f, cpower) * power_scale;
                if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                publish_slot_color(c.mpb, ccolor, cp);
                foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
                foreach (var l in c.lights)
                {
                    l.intensity = cp;
                    l.color = ccolor;
                }
            }
        }

        // the per-slot state: envelope FSM + color build, the fork's math.
        private static (float, Color) sample_slot(blink_track_container track, blink_key cur,
            float local, int slot, int slot_count)
        {
            float power;
            Color color;

            // envelope FSM over the key's authored times.
            float wait = 0f;
            float interval = Mathf.Max(0f, cur.interval_time);
            switch (cur.pattern)
            {
                case 1: // random: FNV-derived per-slot offset
                    {
                        float r = ratio01(slot, cur.key_index);
                        wait = r * interval;
                        break;
                    }
                case 2: // ascend: container i waits i*intervalTime (0x7ff8e4d7e344)
                    wait = slot * interval;
                    break;
                case 3: // descend: container i waits (n-1-i)*intervalTime
                    wait = (slot_count - slot - 1) * interval;
                    break;
            }

            float cycle = Mathf.Max(0.05f, cur.turn_on_time) + cur.keep_time
                + Mathf.Max(0.05f, cur.turn_off_time) + interval;
            float t = local - wait;
            if (cur.pattern >= 1 && cur.pattern <= 3 && t < 0f)
            {
                power = 0f;
                color = sample_track_color(track, cur, slot, 0, slot_count);
                return (power, color);
            }

            int loop = 0;
            bool last_loop = false;
            bool force_off = false;
            if (cur.pattern >= 1 && cur.pattern <= 3)
            {
                if (cycle > 0f) loop = Mathf.FloorToInt(t / cycle);
                if (cur.loop_count > 0)
                {
                    if (loop < cur.loop_count) last_loop = loop == cur.loop_count - 1;
                    else { force_off = true; loop = cur.loop_count - 1; }
                }
            }

            // HSV ratios per slot+loop, the fork's FNV chain.
            float h_ratio = ratio01(slot, cur.key_index + 3 * loop + 0);
            float s_ratio = ratio01(slot, cur.key_index + 3 * loop + 1);
            float v_ratio = ratio01(slot, cur.key_index + 3 * loop + 2);

            if (cur.pattern == 0)
            {
                power = Mathf.Max(0f, pick(cur.power_array, slot, 1f));
                color = sample_track_color(track, cur, slot, 0, slot_count);
                return (power, color);
            }

            if (force_off)
            {
                power = 0f;
                color = sample_track_color(track, cur, slot, loop, slot_count);
                return (power, color);
            }

            float phase = t - loop * cycle;
            float pmin = Mathf.Max(0f, cur.power_min);
            float pmax = Mathf.Max(0f, cur.power_max);
            float pdiff = Mathf.Max(0f, pmax - pmin);

            if (phase < cur.turn_on_time)
            {
                float u = phase / Mathf.Max(0.0001f, cur.turn_on_time);
                power = loop > 0 ? u * pdiff + pmin : u * pmax;
            }
            else if (phase < cur.turn_on_time + cur.keep_time)
            {
                power = pmax;
            }
            else if (phase >= cur.turn_on_time + cur.keep_time + cur.turn_off_time)
            {
                power = last_loop ? 0f : pmin;
            }
            else
            {
                float u2 = 1f - (phase - (cur.turn_on_time + cur.keep_time)) / Mathf.Max(0.0001f, cur.turn_off_time);
                power = last_loop ? u2 * pmax : u2 * pdiff + pmin;
            }

            color = sample_track_color(track, cur, slot, loop, slot_count);
            return (power, color);
        }

        // colorType model: 0 blend (HSV lerped color0->color1), 1 ascend
        // rotation, 2 descend rotation, 3 switch (parity), per slot.
        private static Color sample_track_color(blink_track_container track, blink_key cur,
            int slot, int loop, int slot_count)
        {
            Color c0 = pick_color(cur.color0_array, slot, Color.black);
            Color c1 = pick_color(cur.color1_array, slot, c0);

            switch (cur.color_type)
            {
                case 1: // ascend: rotate color0 forward by loopCount
                    {
                        int step = positive_mod(loop, slot_count);
                        int src = (slot + slot_count - step) % slot_count;
                        return pick_color(cur.color0_array, src, Color.black);
                    }
                case 2: // descend
                    {
                        int step = positive_mod(loop, slot_count);
                        int src = (slot + step) % slot_count;
                        return pick_color(cur.color0_array, src, Color.black);
                    }
                case 3: // switch: color0/color1 by loop parity
                    return (loop & 1) != 0 ? c1 : c0;
                default: // blend
                    {
                        if (cur.pattern == 0) return c0;
                        bool rev = cur.is_reverse_hue != null && cur.is_reverse_hue.Count > slot
                            && cur.is_reverse_hue[slot] != 0;
                        float h_ratio = ratio01(slot, cur.key_index + 3 * loop);
                        float s_ratio = ratio01(slot, cur.key_index + 3 * loop + 1);
                        float v_ratio = ratio01(slot, cur.key_index + 3 * loop + 2);
                        return blend_hsv(c0, c1, h_ratio, s_ratio, v_ratio, rev);
                    }
            }
        }

        // the fork's FNV-derived deterministic ratio.
        private static float ratio01(int a, int b)
        {
            unchecked
            {
                uint x = 2166136261u;
                x = (x ^ (uint)(a + 1)) * 16777619u;
                x = (x ^ (uint)(b + 1)) * 16777619u;
                x ^= x >> 13;
                x *= 1274126177u;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777215.0f;
            }
        }

        // hue path walk c0->c1 the game's way: forward lerp, or reverse wrap
        // when the key flags isReverseHue on that slot.
        private static Color blend_hsv(Color c0, Color c1, float hr, float sr, float vr, bool reverse)
        {
            Color.RGBToHSV(c0, out float h0, out float s0, out float v0);
            Color.RGBToHSV(c1, out float h1, out float s1, out float v1);
            float h;
            if (reverse)
            {
                float delta = h1 - h0;
                if (Mathf.Abs(delta) < 1e-6f) h = h0;
                else
                {
                    if (delta > 0f) delta -= 1f; else delta += 1f;
                    h = Mathf.Repeat(h0 + delta * hr, 1f);
                }
            }
            else
            {
                h = Mathf.LerpAngle(h0 * 360f, h1 * 360f, hr) / 360f;
                h = Mathf.Repeat(h, 1f);
            }
            float s = Mathf.Lerp(s0, s1, sr);
            float v = Mathf.Lerp(v0, v1, vr);
            return Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
        }

        private static int positive_mod(int a, int b)
        {
            if (b <= 0) return 0;
            int m = a % b;
            return m < 0 ? m + b : m;
        }

        private static float pick(List<float> arr, int i, float def)
        {
            if (arr == null || arr.Count == 0) return def;
            return i < arr.Count ? arr[i] : arr[arr.Count - 1];
        }

        private static Color pick_color(List<Color> arr, int i, Color def)
        {
            if (arr == null || arr.Count == 0) return def;
            return i < arr.Count ? arr[i] : arr[arr.Count - 1];
        }

        private static readonly int id_blink_light_color = Shader.PropertyToID("_BlinkLightColor");
        private static readonly int id_mul_color0 = Shader.PropertyToID("_MulColor0");

        // the game's BlinkLightController sets each fixture material's render
        // queue from the shader's last path segment: base 3100 plus the table
        // value (decode: uv2_live10112_blink_rig_decoded.md B.6).
        private static readonly Dictionary<string, int> render_queue_table = new()
        {
            { "LightBlend", 0 },
            { "LightBlink", 10 },
            { "LightBlinkSimple", 11 },
            { "LightBlinkBlend", 12 },
            { "StageLightBlinkCutoff", 13 },
            { "StageLightBlinkFadeout", 14 },
            { "StageBeamLight", 20 },
            { "StageBeamLightCutoff", 21 },
            { "StageBeamLightFadeout", 22 },
        };

        private static void apply_render_queues(blink_container c)
        {
            if (c == null) return;
            if (c.materials.Count == 0)
            {
                foreach (var r in c.renderers)
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m != null && !c.materials.Contains(m)) c.materials.Add(m);
                    }
                }
            }

            foreach (var m in c.materials)
            {
                if (m == null) continue;
                var sh = m.shader;
                if (sh == null) continue;
                string full = sh.name;
                if (string.IsNullOrEmpty(full)) continue;
                int slash = full.LastIndexOf('/');
                string tail = slash >= 0 ? full.Substring(slash + 1) : full;
                if (!render_queue_table.TryGetValue(tail, out int add)) continue;
                int queue = 3100 + add;
                if (m.renderQueue != queue) m.renderQueue = queue;
            }
        }

        // the game's BlinkLightController publishes the authored colour times
        // the slot's power as one full RGBA into _BlinkLightColor, alpha
        // included, and never writes _ColorPower - the material keeps its
        // serialized 1.0. _MulColor0 carries the same value on the game's
        // flag path and is inert on the blink shader.
        private static void publish_slot_color(MaterialPropertyBlock mpb, Color color, float power)
        {
            var rgba = new Color(color.r * power, color.g * power, color.b * power, color.a * power);
            mpb.SetColor(id_blink_light_color, rgba);
            mpb.SetColor(id_mul_color0, rgba);
        }
    }
    // one worksheet blink container: name + the per-key model.
    [Serializable]
    public class blink_track_container
    {
        public string name;
        public List<blink_key> keys = new();

        // how many slots the authored arrays drive; resolved at parse time
        // from the longest authored array, like the game's fixture sizing.
        public int slot_count { get; internal set; }

        // the current key at a time: the last key whose frame <= t*60.
        public blink_key current_key(float time_sec)
        {
            if (keys == null || keys.Count == 0) return null;
            float frame = time_sec * 60f;
            blink_key cur = keys[0];
            foreach (var k in keys)
                if (k.frame <= frame) cur = k;
                else break;
            return cur;
        }

        // the index of the current key (for the next-key lookup).
        public int current_key_index(float time_sec)
        {
            if (keys == null || keys.Count == 0) return -1;
            float frame = time_sec * 60f;
            int i = 0;
            for (int j = 0; j < keys.Count; j++)
                if (keys[j].frame <= frame) i = j;
                else break;
            return i;
        }

        // the game blends EVERY numeric + color field from the current
        // key toward the next by the interpolation factor (the blink
        // consumer 0x7ff8e4dc3527-396a: powerMin/powerMax/loopCount/
        // waitTime/turnOn/turnOff/keepTime/intervalTime and the color
        // arrays all lerp by CalculateInterpolationValue; the power
        // array lerps elementwise). discrete fields (pattern, colorType,
        // blend mode) come from the current key raw. with no blend the
        // returned key equals the current key.
        public blink_key current_key_blended(float time_sec)
        {
            blink_key cur = current_key(time_sec);
            if (cur == null) return null;
            int i = current_key_index(time_sec);
            var next = (i >= 0 && i + 1 < keys.Count) ? keys[i + 1] : null;
            if (next == null || next.interpolate_type == 0) return cur;
            float span = next.time - cur.time;
            float raw = span <= 0f ? 0f : Mathf.Clamp01((time_sec - cur.time) / span);
            float k;
            switch (next.interpolate_type)
            {
                case 1: k = raw; break;
                case 2: k = key_eval.evaluate_curve(next.curve, raw); break;
                case 3: k = Mathf.Clamp01(live_easing.evaluate(next.easing_type,
                            Mathf.Max(0f, time_sec - cur.time), 0f, 1f,
                            Mathf.Max(0.0001f, span))); break;
                default: return cur;
            }
            var b = new blink_key
            {
                frame = cur.frame,
                attribute = cur.attribute,
                interpolate_type = cur.interpolate_type,
                key_index = cur.key_index,
                pattern = cur.pattern,
                color_type = cur.color_type,
                light_blend_mode = cur.light_blend_mode,
                loop_count = Mathf.RoundToInt(Mathf.Lerp(cur.loop_count, next.loop_count, k)),
                is_reverse_hue = cur.is_reverse_hue,
            };
            b.power_array = lerp_array(cur.power_array, next.power_array, k, 1f);
            b.color0_array = lerp_colors(cur.color0_array, next.color0_array, k);
            b.color1_array = lerp_colors(cur.color1_array, next.color1_array, k);
            b.power_min = Mathf.Lerp(cur.power_min, next.power_min, k);
            b.power_max = Mathf.Lerp(cur.power_max, next.power_max, k);
            b.wait_time = Mathf.Lerp(cur.wait_time, next.wait_time, k);
            b.turn_on_time = Mathf.Lerp(cur.turn_on_time, next.turn_on_time, k);
            b.turn_off_time = Mathf.Lerp(cur.turn_off_time, next.turn_off_time, k);
            b.keep_time = Mathf.Lerp(cur.keep_time, next.keep_time, k);
            b.interval_time = Mathf.Lerp(cur.interval_time, next.interval_time, k);
            return b;
        }

        private static List<float> lerp_array(List<float> a, List<float> b, float k, float def)
        {
            int n = Mathf.Max(a?.Count ?? 0, b?.Count ?? 0);
            var out_l = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                float av = a != null && i < a.Count ? a[i] : def;
                float bv = b != null && i < b.Count ? b[i] : def;
                out_l.Add(Mathf.Lerp(av, bv, k));
            }
            return out_l;
        }

        private static List<Color> lerp_colors(List<Color> a, List<Color> b, float k)
        {
            int n = Mathf.Max(a?.Count ?? 0, b?.Count ?? 0);
            var out_l = new List<Color>(n);
            for (int i = 0; i < n; i++)
            {
                Color ac = a != null && i < a.Count ? a[i] : Color.black;
                Color bc = b != null && i < b.Count ? b[i] : Color.black;
                out_l.Add(Color.Lerp(ac, bc, k));
            }
            return out_l;
        }
    }

    // one blink key: the per-slot power/color arrays + the envelope timing,
    // all authored per key (pattern/colorType/times change key to key).
    [Serializable]
    public class blink_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public List<curve_key> curve = new();
        public List<float> power_array = new();
        public List<Color> color0_array = new();
        public List<Color> color1_array = new();
        public List<byte> is_reverse_hue = new();
        public int light_blend_mode;
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

        // the key's index in the container: the FNV ratio chain salts with it.
        public int key_index;

        // seconds position of this key on the clock.
        public float time => frame / 60f;
    }

}
