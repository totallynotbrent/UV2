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

        private void LateUpdate()
        {
            if (ws == null || clock == null || cam == null) return;
            float t = clock.time;

            // switcher first: which camera's tracks run (index 0 = the base camera
            // sheet; the locators for multi-camera are phase-6 scope).
            int active_sheet = 0;
            if (ws.camera_switcher.Count > 0)
            {
                int i = key_eval.bracket(ws.camera_switcher, t);
                if (i >= 0) active_sheet = ws.camera_switcher[i].camera_index;
            }
            if (active_sheet != 0) return; // multi-camera composite is out of phase-2 scope

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
                        ? chara_parts.group_world(chara_roots, cur.look_at_chara_pos, cur.look_at_chara_parts) + cur.position + (look_const ? cur.look_at_chara_pos_offset : Vector3.zero)
                        : cur.position;
                    if (next != null && cur.look_at_type == next.look_at_type)
                    {
                        bool next_look_const = next.look_at_chara_parts >= 11 && next.look_at_chara_parts <= 14;
                        Vector3 look_next = next.look_at_type == 1
                            ? chara_parts.group_world(chara_roots, next.look_at_chara_pos, next.look_at_chara_parts) + next.position + (next_look_const ? next.look_at_chara_pos_offset : Vector3.zero)
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
