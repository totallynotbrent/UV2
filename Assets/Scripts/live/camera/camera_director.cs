using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UV2.Live
{
    // drives the authored camera along the worksheet tracks: position, look-at, fov, roll, and the cinematic motion override.
    public class camera_director : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private List<Transform> chara_roots = new();

        private Camera cam;
        private Vector3? trace_prev_pos;
        // the clock frame of the previous update, for the camera-delay window.
        private float prev_frame;

        // handshake state: the game's Work struct from
        // uv2_handshake_math_decoded.md - a 2d screen-space random walk with a
        // persistent orbit angle, screen point, chase target and reseed timer.
        private int live_seed;

        // rolls a chase target in the [-power, power]^2 square; the fixed
        // pattern swaps the perlin args between the components.
        private Vector2 roll_target(float power, byte fixed_pattern)
        {
            float x, y;
            if (fixed_pattern == 0)
            {
                x = UnityEngine.Random.Range(-1f, 1f);
                y = UnityEngine.Random.Range(-1f, 1f);
            }
            else
            {
                x = (Mathf.PerlinNoise(hs_prev_time, live_seed) - 0.5f) * 2f;
                y = (Mathf.PerlinNoise(live_seed, hs_prev_time) - 0.5f) * 2f;
            }
            return new Vector2(x * power, y * power);
        }

        // the game's angle helper: asin(y/|d|) in degrees, x<0 wraps to
        // 360-deg, nan guards to zero.
        private static float angle_deg(Vector2 d)
        {
            float mag = d.magnitude;
            if (mag <= 1e-5f) return 0f;
            float deg = Mathf.Asin(d.y / mag) * Mathf.Rad2Deg;
            if (float.IsNaN(deg)) return 0f;
            if (d.x < 0f) deg = 360f - deg;
            return deg;
        }

        // move-towards in degree space with the (−180, 180] wrap.
        private static float move_towards_angle(float current, float target, float max_delta)
        {
            float delta = target - current;
            while (delta < 0f) delta += 360f;
            while (delta >= 360f) delta -= 360f;
            if (delta > 180f) delta -= 360f;
            if (Mathf.Abs(delta) <= max_delta) return target;
            return current + Mathf.Sign(delta) * max_delta;
        }

        private float hs_current_angle;
        private Vector2 hs_screen_position;
        private Vector2 hs_next_target;
        private float hs_duration;
        private Vector3 hs_offset;
        private float hs_prev_time;
        private bool hs_active;

        // height-band constants: rate = (avg_height - 130) / 60 over the flagged characters.
        private const float LAYER_HEIGHT_MIN = 130f;
        private const float LAYER_HEIGHT_DIFF = 60f;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> characters, Camera target)
        {
            ws = worksheet;
            clock = timeline;
            chara_roots = characters;
            cam = target;
            // the deterministic per-live seed, like the game's LiveSettings.Id.
            var sel = UV2.App.selection_store.load();
            live_seed = sel != null ? sel.music_id : 0;
            hs_prev_time = 0f;
        }

        // applies the shake additively to the camera's localPosition in the
        // late pass, like the game's Director.AlterLateUpdate: the authored
        // transform is re-evaluated from the keys every frame, so the noise
        // never accumulates and never survives a key change.
        private void apply_shake()
        {
            if (!hs_active) return;
            cam.transform.localPosition += hs_offset;
        }

        // proxy transform that samples the cinematic clip; the camera rides it as a late override.
        private Transform motion_proxy;
        private string motion_proxy_clip;
        private string loaded_clip_name;
        private AnimationClip motion_proxy_clip_data;

        // loads a camera-motion clip by name from the install; null when absent.
        private AnimationClip load_camera_clip(string clip_name)
        {
            if (loaded_clip_name == clip_name) return motion_proxy_clip_data;
            using var meta = UV2.Data.meta_reader.reader.open(UV2.App.config.meta_db_path);
            if (meta == null) return null;
            var rows = meta.lookup(new System.Collections.Generic.HashSet<string> { clip_name });
            if (!rows.TryGetValue(clip_name, out var row)) return null;
            var bundle = UV2.Data.game_assets.open(row, UV2.App.config.data_root);
            if (bundle == null) return null;
            var clip = bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            if (clip == null) return null;
            loaded_clip_name = clip_name;
            motion_proxy_clip_data = clip;
            return clip;
        }

        // applies the enabled motion key's clip through the proxy as a late camera override.
        private bool apply_camera_motion(float t)
        {
            if (ws.camera_motion.Count == 0) return false;
            int i = key_eval.bracket(ws.camera_motion, t);
            if (i < 0) return false;
            var key = ws.camera_motion[i];
            if (!key.is_enable || string.IsNullOrEmpty(key.clip_name)) return false;

            if (motion_proxy == null)
                motion_proxy = new GameObject("TimelineCameraMotionProxy").transform;
            if (motion_proxy_clip != key.clip_name)
            {
                var clip = load_camera_clip(key.clip_name);
                if (clip == null) return false;
                motion_proxy_clip = key.clip_name;
            }
            // crossfade normalized time: elapsed since the key, scaled by play speed, offset by head time, over clip length.
            float length = motion_proxy_clip_data.length;
            if (length <= 0f) return false;
            float clip_time = (t - key.time) * key.play_speed + key.motion_head_time;
            clip_time = Mathf.Clamp01(clip_time / length) * length;
            motion_proxy_clip_data.SampleAnimation(motion_proxy.gameObject, clip_time);
            Vector3 offset = key.motion_type == 1
                ? chara_parts.group_world(chara_roots, key.chara_relative_base, key.chara_relative_parts) + key.offset + key.chara_pos
                : key.offset;
            cam.transform.SetPositionAndRotation(motion_proxy.position + offset, motion_proxy.rotation);
            return true;
        }

        // offsets the band between min and max by the flagged characters' average height; midpoint when none resolve.
        private Vector3 layer_offset_for(int flags, Vector3 min, Vector3 max)
        {
            float height = chara_parts.group_height(chara_roots, flags);
            if (height <= 0f) return (min + max) * 0.5f;
            float rate = Mathf.Clamp((height - LAYER_HEIGHT_MIN) / LAYER_HEIGHT_DIFF, 0f, 1f);
            return min + rate * (max - min);
        }

        private void LateUpdate()
        {
            if (ws == null || clock == null || cam == null) return;
            float t = clock.time;

            // the delay window compares the clock frame before this update.
            float clock_frame = t * 60f;
            float prev_frame_this = prev_frame;
            prev_frame = clock_frame;


            // position
            if (ws.camera_pos.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_pos, t);
                if (i >= 0)
                {
                    var cur = ws.camera_pos[i];
                    var next = i + 1 < ws.camera_pos.Count ? ws.camera_pos[i + 1] : null;
                    float k = key_eval.interp(cur, next, t);

                    // props-attach keys ride a prop transform: node.position +
                    // node.rotation * key.position, charaPos/locator/layer all
                    // skipped (game's IsAttachedToProps branch); when no prop
                    // resolves, the authored position stands alone.
                    if (cur.is_attached_to_props)
                    {
                        Transform node = null;
                        for (int slot = 0; slot < chara_roots.Count && slot < chara_parts.MAX; slot++)
                        {
                            if ((cur.chara_relative_base & (1 << slot)) == 0) continue;
                            node = props_system.camera_attach_node(slot + 1, cur.props_index, cur.props_attach_node_index);
                            if (node != null) break;
                        }
                        Vector3 pos_att = node != null
                            ? node.position + node.rotation * cur.position
                            : cur.position;
                        pos_att += cur.offset;
                        if (next != null && next.interpolate_type != 0)
                        {
                            Vector3 next_att = next.is_attached_to_props
                                ? pos_att // unresolved attach keys hold - matching the game's fallback
                                : pos_att; // attach keys don't blend out through authored geometry
                            pos_att = key_eval.lerp_v3(pos_att, next_att, 0f);
                        }
                        cam.transform.position = pos_att;
                        trace_prev_pos = pos_att;
                        if (cur.near_clip > 0f) cam.nearClipPlane = cur.near_clip;
                        if (cur.far_clip > 0f) cam.farClipPlane = cur.far_clip;
                        goto lookat;
                    }

                    // the layer band rides the pos key's own flags.
                    Vector3 pos_layer = Vector3.zero;
                    if (cur.set_type == 1)
                        pos_layer = layer_band(t, cur.chara_relative_base);

                    // chara_pos only applies to the const-height part anchors.
                    bool const_anchor = cur.chara_relative_parts >= 11 && cur.chara_relative_parts <= 14;
                    Vector3 pos = cur.set_type == 1
                        ? chara_parts.group_world(chara_roots, cur.chara_relative_base, cur.chara_relative_parts, pos_layer) + cur.position + (const_anchor ? cur.chara_pos : Vector3.zero)
                        : cur.position + cur.pos_direct;
                    pos += cur.offset;
                    // the game blends between consecutive pos keys regardless of set type;
                    // the type only picks how each endpoint resolves.
                    if (next != null && next.interpolate_type != 0)
                    {
                        bool next_const = next.chara_relative_parts >= 11 && next.chara_relative_parts <= 14;
                        Vector3 next_layer = next.set_type == 1 ? layer_band(t, next.chara_relative_base) : Vector3.zero;
                        Vector3 pos_next = next.set_type == 1
                            ? chara_parts.group_world(chara_roots, next.chara_relative_base, next.chara_relative_parts, next_layer) + next.position + (next_const ? next.chara_pos : Vector3.zero)
                            : next.position + next.pos_direct;
                        pos_next += next.offset;
                        // authored bezier control points shape the segment.
                        if (next.bezier_points != null && next.bezier_points.Count > 0)
                            pos = key_eval.bezier_v3(pos, pos_next, next.bezier_points, k);
                        else
                            pos = key_eval.lerp_v3(pos, pos_next, k);
                    }

                    // the trace flag slerps toward the target instead of snapping:
                    // bit 2 enables the chase, bit 4 inherits it across keys, and
                    // it also applies before the key's own frame (the approach-in).
                    bool delay = (cur.attribute & 2) != 0
                        && ((prev_frame_this >= cur.frame) || (t < cur.time) || ((cur.attribute & 4) != 0));
                    if (delay && trace_prev_pos.HasValue)
                        pos = Vector3.Slerp(trace_prev_pos.Value, pos, Mathf.Clamp01(cur.trace_speed * Time.deltaTime * 60f));

                    cam.transform.position = pos;
                    trace_prev_pos = pos;

                    if (cur.near_clip > 0f) cam.nearClipPlane = cur.near_clip;
                    if (cur.far_clip > 0f) cam.farClipPlane = cur.far_clip;
                }
            }

        lookat:;

            // look-at
            if (ws.camera_lookat.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_lookat, t);
                if (i >= 0)
                {
                    var cur = ws.camera_lookat[i];
                    var next = i + 1 < ws.camera_lookat.Count ? ws.camera_lookat[i + 1] : null;
                    float k = key_eval.interp(cur, next, t);

                    // the look-at band rides the look-at key's own flags.
                    Vector3 look_layer = Vector3.zero;
                    if (cur.look_at_type == 1)
                        look_layer = layer_band(t, cur.look_at_chara_pos);

                    // the chara offset only applies to the const-height parts.
                    bool look_const = cur.look_at_chara_parts >= 11 && cur.look_at_chara_parts <= 14;
                    Vector3 look = cur.look_at_type == 1
                        ? chara_parts.group_world(chara_roots, cur.look_at_chara_pos, cur.look_at_chara_parts, look_layer) + cur.position + (look_const ? cur.look_at_chara_pos_offset : Vector3.zero)
                        : cur.position;
                    if (next != null && next.interpolate_type != 0)
                    {
                        bool next_look_const = next.look_at_chara_parts >= 11 && next.look_at_chara_parts <= 14;
                        Vector3 next_layer = next.look_at_type == 1 ? layer_band(t, next.look_at_chara_pos) : Vector3.zero;
                        Vector3 look_next = next.look_at_type == 1
                            ? chara_parts.group_world(chara_roots, next.look_at_chara_pos, next.look_at_chara_parts, next_layer) + next.position + (next_look_const ? next.look_at_chara_pos_offset : Vector3.zero)
                            : next.position;
                        if (next.bezier_points != null && next.bezier_points.Count > 0)
                            look = key_eval.bezier_v3(look, look_next, next.bezier_points, k);
                        else
                            look = key_eval.lerp_v3(look, look_next, k);
                    }
                    cam.transform.LookAt(look, Vector3.up);

                    // the look-at chase slerps the forward vector toward the
                    // target direction, scaled back to the original distance.
                    if ((cur.attribute & 2) != 0
                        && ((prev_frame_this >= cur.frame) || (t < cur.time) || ((cur.attribute & 4) != 0)))
                    {
                        Vector3 to_target = look - cam.transform.position;
                        float mag = to_target.magnitude;
                        if (mag > float.Epsilon)
                        {
                            Vector3 dir = to_target / mag;
                            float chase = Mathf.Clamp01(cur.trace_speed * Time.deltaTime * 60f);
                            Vector3 fwd = Vector3.Slerp(cam.transform.forward, dir, chase);
                            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                        }
                    }
                }
            }

            // fov
            if (ws.camera_fov.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_fov, t);
                if (i >= 0)
                {
                    var cur = ws.camera_fov[i];
                    var next = i + 1 < ws.camera_fov.Count ? ws.camera_fov[i + 1] : null;
                    float fov = cur.fov;
                    if (next != null)
                    {
                        float k = key_eval.interp(cur, next, t);
                        fov = key_eval.lerp_f(cur.fov, next.fov, k);
                    }
                    if (fov > 0f)
                    {
                        // wide aspects narrow the vertical fov past 16:9.
                        float aspect = (float)cam.pixelWidth / Mathf.Max(1, cam.pixelHeight);
                        if (aspect > 16f / 9f) fov /= aspect / (16f / 9f);
                        cam.fieldOfView = fov;
                        // the game publishes a normalized fov shader global.
                        Shader.SetGlobalFloat("_GlobalCameraFov", Mathf.Min(fov / 30f, 1f));
                    }
                }
            }

            // roll (applied after look-at so it banks around the view axis)
            if (ws.camera_roll.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_roll, t);
                if (i >= 0)
                {
                    var cur = ws.camera_roll[i];
                    var next = i + 1 < ws.camera_roll.Count ? ws.camera_roll[i + 1] : null;
                    float deg = cur.degree;
                    if (next != null)
                    {
                        float k = key_eval.interp(cur, next, t);
                        deg = key_eval.lerp_f(cur.degree, next.degree, k);
                    }
                    cam.transform.Rotate(Vector3.forward, deg, Space.Self);
                }
            }

            // the authored handshake rides on top. the key params interp
            // between keys and zero when no key is current (unlike pos which
            // holds); the noise body is the game's Work random walk from
            // uv2_handshake_math_decoded.md section 3, register-exact.
            if (ws.handshake.Count > 0)
            {
                int i = key_eval.bracket(ws.handshake, t);
                float power = 0f, frequency = 0f, rate = 0f;
                byte fixed_pattern = 0;
                if (i >= 0)
                {
                    var cur = ws.handshake[i];
                    var next = i + 1 < ws.handshake.Count ? ws.handshake[i + 1] : null;
                    power = cur.power;
                    frequency = cur.frequency;
                    rate = cur.rate;
                    fixed_pattern = cur.use_fixed_shake_pattern;
                    if (next != null && next.interpolate_type != 0)
                    {
                        float k = key_eval.interp(cur, next, t);
                        power = key_eval.lerp_f(cur.power, next.power, k);
                        frequency = key_eval.lerp_f(cur.frequency, next.frequency, k);
                        rate = key_eval.lerp_f(cur.rate, next.rate, k);
                    }
                }

                const float HS_EPS = 1e-6f;
                const float HS_POWER_MIN = 0.01f;
                const float HS_DELTA_TIME_FACTOR = 30f;
                const float HS_TIME_RANGE = 0.1f;

                if (power < HS_EPS || frequency < HS_EPS || rate < HS_EPS)
                {
                    // the reset path: the walk state clears, no shake.
                    hs_current_angle = 0f;
                    hs_screen_position = Vector2.zero;
                    hs_next_target = Vector2.zero;
                    hs_duration = 0f;
                    hs_offset = Vector3.zero;
                    hs_active = false;
                }
                else
                {
                    // the game feeds DeltaTimePauseReset - the frame's own
                    // delta, never a song-time difference. a burst after a
                    // long gated gap must not integrate the whole gap in one
                    // step (that launched the camera meters off).
                    float dt = t - hs_prev_time;
                    if (dt < 0f || dt > 0.1f) dt = Time.deltaTime;
                    hs_prev_time = t;

                    float eff_power = Mathf.Max(power, HS_POWER_MIN) * HS_POWER_MIN;

                    // the chase target re-rolls while it sits within eff_power
                    // of the screen point (fixed pattern: one re-roll then a
                    // signed diagonal snap).
                    Vector2 d = hs_next_target - hs_screen_position;
                    if (fixed_pattern == 0)
                    {
                        int guard = 0;
                        while (d.magnitude <= eff_power && guard++ < 64)
                        {
                            hs_next_target = roll_target(eff_power, fixed_pattern);
                            d = hs_next_target - hs_screen_position;
                        }
                    }
                    else if (d.magnitude < eff_power)
                    {
                        hs_next_target = roll_target(eff_power, fixed_pattern);
                        d = hs_next_target - hs_screen_position;
                        if (d.magnitude < eff_power)
                        {
                            hs_next_target = hs_screen_position + new Vector2(
                                d.x >= 0f ? eff_power : -eff_power,
                                d.y >= 0f ? eff_power : -eff_power);
                            d = hs_next_target - hs_screen_position;
                        }
                    }

                    // the orbit angle chases the target direction at
                    // frequency*speed, wrapped to (-180,180].
                    float drift_angle = angle_deg(d);
                    float drift_speed = fixed_pattern != 0
                        ? Mathf.PerlinNoise(t, live_seed * 2f) * 9f + 1f
                        : UnityEngine.Random.Range(1f, 10f);
                    hs_current_angle = move_towards_angle(hs_current_angle, drift_angle,
                        frequency * drift_speed);

                    if (dt > HS_EPS)
                    {
                        // the screen point orbits at dt*30*eff_power, x on sin
                        // and y on cos.
                        float step = dt * HS_DELTA_TIME_FACTOR * eff_power;
                        float rad = hs_current_angle * Mathf.Deg2Rad;
                        hs_screen_position.x += Mathf.Sin(rad) * step;
                        hs_screen_position.y += Mathf.Cos(rad) * step;
                    }

                    hs_duration += dt * frequency;
                    if (hs_duration >= HS_TIME_RANGE)
                    {
                        hs_duration = 0f;
                        hs_next_target = roll_target(eff_power, fixed_pattern);
                    }

                    // the output projects the walk onto the camera: horizontal
                    // rides the right vector, vertical is plain world-y, rate
                    // scales everything.
                    var right = cam.transform.rotation * Vector3.right;
                    hs_offset = new Vector3(
                        right.x * hs_screen_position.x * rate,
                        hs_screen_position.y * rate,
                        right.z * hs_screen_position.x * rate);
                    hs_active = true;
                    if ((int)(t * 60f) % 60 == 0)
                        UV2.App.trace_log.write($"handshake: on t{t:0.0} p{power:0.00} f{frequency:0.00} r{rate:0.00} off{hs_offset.x:0.000},{hs_offset.y:0.000},{hs_offset.z:0.000}");
                }
            }

            // the cinematic clip overrides the camera last.
            apply_camera_motion(t);

            // the handshake offset lands on top of the final transform.
            apply_shake();
        }

        // evaluates the camera-layer band at t blended with the flagged characters' height rate.
        private Vector3 layer_band(float t, int flags)
        {
            if (ws.camera_layer.Count == 0) return Vector3.zero;
            int li = key_eval.bracket(ws.camera_layer, t);
            if (li < 0) return Vector3.zero;
            var cur_l = ws.camera_layer[li];
            var next_l = li + 1 < ws.camera_layer.Count ? ws.camera_layer[li + 1] : null;
            float k_l = key_eval.interp(cur_l, next_l, t);
            Vector3 min = cur_l.offset_min_position;
            Vector3 max = cur_l.offset_max_position;
            if (next_l != null)
            {
                min = key_eval.lerp_v3(min, next_l.offset_min_position, k_l);
                max = key_eval.lerp_v3(max, next_l.offset_max_position, k_l);
            }
            return layer_offset_for(flags, min, max);
        }
    }
}
