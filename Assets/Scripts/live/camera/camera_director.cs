using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.Live;

namespace UV2.Live
{
    // drives the authored camera along the worksheet tracks: position, look-at,
    // fov, roll, and the cut chain. per out/camera_motion_questions_decoded.md.
    public class camera_director : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private List<Transform> chara_roots = new();

        private Camera cam;
        private Vector3? trace_prev_pos;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> characters, Camera target)
        {
            ws = worksheet;
            clock = timeline;
            chara_roots = characters;
            cam = target;
        }

        // samples the authored cinematic clip onto a proxy transform and
        // drives the camera with it, exactly v1's OnUpdateCameraMotion.
        private Transform motion_proxy;
        private string motion_proxy_clip;
        private string loaded_clip_name;
        private AnimationClip motion_proxy_clip_data;

        // loads a camera-motion clip bundle straight from the install by its
        // authored name; null when the install doesn't carry it.
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
            float clip_time = (t - key.time) * key.play_speed + key.motion_head_time;
            motion_proxy_clip_data.SampleAnimation(motion_proxy.gameObject, clip_time);
            cam.transform.SetPositionAndRotation(motion_proxy.position + key.offset, motion_proxy.rotation);
            return true;
        }

        private void LateUpdate()
        {
            if (ws == null || clock == null || cam == null) return;
            float t = clock.time;

            // switcher: with one camera, indices other than 0 leave the base
            // camera running (v1 only touches cameras it actually has; the
            // multi-camera composite is later scope).

            // the authored cinematic clip drives the camera while it runs.
            if (apply_camera_motion(t)) return;

            // position
            if (ws.camera_pos.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_pos, t);
                if (i >= 0)
                {
                    var cur = ws.camera_pos[i];
                    var next = i + 1 < ws.camera_pos.Count ? ws.camera_pos[i + 1] : null;
                    float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, t));

                    // character keys: the flagged group's part anchor plus
                    // the key's position; chara_pos only rides the const-height
                    // parts (the game adds it inside those cases only).
                    bool const_anchor = cur.chara_relative_parts >= 11 && cur.chara_relative_parts <= 14;
                    Vector3 pos = cur.set_type == 1
                        ? chara_parts.group_world(chara_roots, cur.chara_relative_base, cur.chara_relative_parts) + cur.position + (const_anchor ? cur.chara_pos : Vector3.zero)
                        : cur.position + cur.pos_direct;
                    pos += cur.offset;
                    if (next != null && cur.set_type == next.set_type)
                    {
                        bool next_const = next.chara_relative_parts >= 11 && next.chara_relative_parts <= 14;
                        Vector3 pos_next = next.set_type == 1
                            ? chara_parts.group_world(chara_roots, next.chara_relative_base, next.chara_relative_parts) + next.position + (next_const ? next.chara_pos : Vector3.zero)
                            : next.position + next.pos_direct;
                        pos_next += next.offset;
                        // authored bezier control points shape the segment.
                        if (next.bezier_points != null && next.bezier_points.Count > 0)
                            pos = key_eval.bezier_v3(pos, pos_next, next.bezier_points, k);
                        else
                            pos = key_eval.lerp_v3(pos, pos_next, k);
                    }

                    // camera-delay trace: the flag on the current key asks the
                    // camera to slerp toward the target instead of snapping.
                    if ((cur.attribute & 4) != 0 && trace_prev_pos.HasValue)
                        pos = Vector3.Slerp(trace_prev_pos.Value, pos, Mathf.Clamp01(cur.trace_speed * Time.deltaTime * 60f));

                    cam.transform.position = pos;
                    trace_prev_pos = pos;

                    if (cur.near_clip > 0f) cam.nearClipPlane = cur.near_clip;
                    if (cur.far_clip > 0f) cam.farClipPlane = cur.far_clip;
                }
            }

            // camera-layer band: the authored track recenters the chara-relative
            // framing; the game feeds it through the per-anchor offset term.
            Vector3 layer_offset = Vector3.zero;
            if (ws.camera_layer.Count > 0)
            {
                int li = key_eval.bracket(ws.camera_layer, t);
                if (li >= 0)
                {
                    var cur_l = ws.camera_layer[li];
                    var next_l = li + 1 < ws.camera_layer.Count ? ws.camera_layer[li + 1] : null;
                    float k_l = key_eval.interp(cur_l, next_l, key_eval.span_t(cur_l, next_l, t));
                    Vector3 mid = (cur_l.offset_min_position + cur_l.offset_max_position) * 0.5f;
                    if (next_l != null)
                    {
                        Vector3 mid_next = (next_l.offset_min_position + next_l.offset_max_position) * 0.5f;
                        mid = key_eval.lerp_v3(mid, mid_next, k_l);
                    }
                    layer_offset = mid;
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
                    float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, t));

                    // character keys mirror the position track; the offset
                    // only rides the const-height parts.
                    bool look_const = cur.look_at_chara_parts >= 11 && cur.look_at_chara_parts <= 14;
                    Vector3 look = cur.look_at_type == 1
                        ? chara_parts.group_world(chara_roots, cur.look_at_chara_pos, cur.look_at_chara_parts) + layer_offset + cur.position + (look_const ? cur.look_at_chara_pos_offset : Vector3.zero)
                        : cur.position;
                    if (next != null && cur.look_at_type == next.look_at_type)
                    {
                        bool next_look_const = next.look_at_chara_parts >= 11 && next.look_at_chara_parts <= 14;
                        Vector3 look_next = next.look_at_type == 1
                            ? chara_parts.group_world(chara_roots, next.look_at_chara_pos, next.look_at_chara_parts) + layer_offset + next.position + (next_look_const ? next.look_at_chara_pos_offset : Vector3.zero)
                            : next.position;
                        if (next.bezier_points != null && next.bezier_points.Count > 0)
                            look = key_eval.bezier_v3(look, look_next, next.bezier_points, k);
                        else
                            look = key_eval.lerp_v3(look, look_next, k);
                    }
                    cam.transform.LookAt(look, Vector3.up);
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
                        float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, t));
                        fov = key_eval.lerp_f(cur.fov, next.fov, k);
                    }
                    if (fov > 0f)
                    {
                        // v1's width limit: wide aspects narrow the vertical fov.
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
                        float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, t));
                        deg = key_eval.lerp_f(cur.degree, next.degree, k);
                    }
                    cam.transform.Rotate(Vector3.forward, deg, Space.Self);
                }
            }
        }
    }
}
