using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UV2.Live
{
    // camera drive level: full = authored motion, partial = each cut holds its
    // static pose (no blending, shake, or cinematic clips) but still flips to
    // the next cut, free = manual fly.
    public enum camera_mode { full = 0, partial, free }

    // drives the authored camera along the worksheet tracks: position, look-at, fov, roll, and the cinematic motion override.
    public class camera_director : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private List<Transform> chara_roots = new();

        private Camera cam;
        private Vector3? trace_prev_pos;
        // the live look-at target this frame (the game's dof focal type-0
        // transform target; read by the postfx chain's dof eval).
        public Vector3 current_look_point;
        // the clock frame of the previous update, for the camera-delay window.
        private float prev_frame;

        private camera_mode _mode = camera_mode.full;
        public camera_mode mode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; free_seeded = false; } }
        }

        // free-fly state, seeded from the live rotation on first use.
        private float free_yaw, free_pitch;
        private bool free_seeded;

        // manual fly: right-drag looks, wasd/qe moves along the view, shift
        // sprints, the scroll wheel zooms the field of view.

        private void free_fly()
        {
            if (!free_seeded)
            {
                var e = cam.transform.eulerAngles;
                free_yaw = e.y;
                free_pitch = e.x > 180f ? e.x - 360f : e.x;
                free_seeded = true;
            }
            if (Input.GetMouseButton(1))
            {
                free_yaw += Input.GetAxis("Mouse X") * 2.4f;
                free_pitch -= Input.GetAxis("Mouse Y") * 2.4f;
                free_pitch = Mathf.Clamp(free_pitch, -89f, 89f);
                cam.transform.rotation = Quaternion.Euler(free_pitch, free_yaw, 0f);
            }
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += Vector3.back;
            if (Input.GetKey(KeyCode.A)) move += Vector3.left;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 12f : 3f;
            if (move != Vector3.zero)
                cam.transform.position += cam.transform.rotation * move * (speed * Time.deltaTime);
            // scroll adjusts fov in freecam (w/s already dollies, so the
            // wheel is free for zoom); clamped to unity's sane band.
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(wheel, 0f))
                cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - wheel * 12f, 5f, 120f);
        }

        // handshake state: the authored key params + the running noise time.
        private Vector2 n0, n1, n2;
        private float time_pos;
        private Vector3 shake_offset_pos;
        private float shake_offset_roll;

        // the centered 2-octave perlin fbm the game's noise engine uses.
        private static float fbm_centered(float x, float y)
        {
            float sum = (Mathf.PerlinNoise(x, y) - 0.5f);
            sum += 0.5f * (Mathf.PerlinNoise(x * 2f, y * 2f) - 0.5f);
            return sum;
        }

        // seeds the noise direction vectors once per camera, like the game's AlterAwake.
        private void seed_noise()
        {
            var r = new System.Random(GetInstanceID());
            Vector2 rand_dir() => new Vector2(Mathf.Cos((float)r.NextDouble() * Mathf.PI * 2f),
                                               Mathf.Sin((float)r.NextDouble() * Mathf.PI * 2f));
            n0 = rand_dir(); n1 = rand_dir(); n2 = rand_dir();
            time_pos = (float)r.NextDouble() * 10f;
        }

        // height-band constants: rate = (avg_height - 130) / 60 over the flagged characters.
        private const float LAYER_HEIGHT_MIN = 130f;
        private const float LAYER_HEIGHT_DIFF = 60f;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> characters, Camera target)
        {
            ws = worksheet;
            clock = timeline;
            chara_roots = characters;
            cam = target;
            seed_noise();
        }

        // the game applies the handshake as a render-time view-matrix shake, so
        // the authored transform is never polluted by the noise.
        private void OnPreCull()
        {
            if (shake_offset_pos == Vector3.zero && shake_offset_roll == 0f) return;
            var rot = Quaternion.Euler(0f, 0f, shake_offset_roll);
            cam.worldToCameraMatrix = Matrix4x4.TRS(shake_offset_pos, rot, new Vector3(1f, 1f, -1f))
                                    * cam.transform.worldToLocalMatrix;
        }

        private void OnPreRender()
        {
            // reset the matrix so the next frame's eval starts from the true transform.
            cam.ResetWorldToCameraMatrix();
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

            if (_mode == camera_mode.free)
            {
                free_fly();
                return;
            }

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
                    // the type only picks how each endpoint resolves. partial mode
                    // holds each cut static: no cross-key blending, no trace chase.
                    bool partial = _mode == camera_mode.partial;
                    if (!partial && next != null && next.interpolate_type != 0)
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
                    if (!partial && delay && trace_prev_pos.HasValue)
                        pos = Vector3.Slerp(trace_prev_pos.Value, pos, Mathf.Clamp01(cur.trace_speed * Time.deltaTime * 60f));

                    cam.transform.position = pos;
                    trace_prev_pos = pos;

                    if (cur.near_clip > 0f) cam.nearClipPlane = cur.near_clip;
                    if (cur.far_clip > 0f) cam.farClipPlane = cur.far_clip;
                }
            }

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
                    if (_mode != camera_mode.partial && next != null && next.interpolate_type != 0)
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
                    // partial mode holds the cut's static framing: no chase.
                    if ((cur.attribute & 2) != 0
                        && _mode != camera_mode.partial
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
                    // partial holds the cut's static fov: no cross-key lerp.
                    if (_mode != camera_mode.partial && next != null)
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
                    // partial holds the cut's static roll.
                    if (_mode != camera_mode.partial && next != null)
                    {
                        float k = key_eval.interp(cur, next, t);
                        deg = key_eval.lerp_f(cur.degree, next.degree, k);
                    }
                    cam.transform.Rotate(Vector3.forward, deg, Space.Self);
                }
            }

            // the authored handshake rides on top: the key params interp between
            // keys (zeroed when no key is current, unlike pos which holds), and
            // the game applies the noise as a render-time view-matrix offset.
            // partial mode runs static: no shake.
            if (ws.handshake.Count > 0 && _mode == camera_mode.full)
            {
                int i = key_eval.bracket(ws.handshake, t);
                float power = 0f, frequency = 0f, rate = 0f;
                if (i >= 0)
                {
                    var cur = ws.handshake[i];
                    var next = i + 1 < ws.handshake.Count ? ws.handshake[i + 1] : null;
                    power = cur.power;
                    frequency = cur.frequency;
                    rate = cur.rate;
                    if (next != null && next.interpolate_type != 0)
                    {
                        float k = key_eval.interp(cur, next, t);
                        power = key_eval.lerp_f(cur.power, next.power, k);
                        frequency = key_eval.lerp_f(cur.frequency, next.frequency, k);
                        rate = key_eval.lerp_f(cur.rate, next.rate, k);
                    }
                }
                // the noise engine: 2-octave centered perlin fbm, time advancing
                // at the game's fixed 0.2/sec, applied along the camera axes.
                if (power > 0f)
                {
                    time_pos += Time.deltaTime * 0.2f * Mathf.Max(0.01f, rate);
                    Vector3 noise = new Vector3(
                        fbm_centered(n0.x * time_pos, n0.y * time_pos),
                        fbm_centered(n1.x * time_pos, n1.y * time_pos),
                        fbm_centered(n2.x * time_pos, n2.y * time_pos));
                    shake_offset_pos = noise * power * 2f;
                    shake_offset_roll = noise.x * power * 2f;
                }
                else
                {
                    shake_offset_pos = Vector3.zero;
                    shake_offset_roll = 0f;
                }
            }

            // the cinematic clip overrides the camera last. partial mode runs
            // static: the authored cut pose stands without the motion clip.
            if (_mode == camera_mode.full)
                apply_camera_motion(t);
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
