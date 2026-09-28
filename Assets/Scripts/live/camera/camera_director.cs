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

                    Vector3 pos = cur.set_type == 1
                        ? chara_parts.group_world(chara_roots, cur.chara_relative_parts) + cur.position + cur.chara_pos
                        : cur.position;
                    if (next != null && cur.set_type == next.set_type)
                    {
                        Vector3 pos_next = next.set_type == 1
                            ? chara_parts.group_world(chara_roots, next.chara_relative_parts) + next.position + next.chara_pos
                            : next.position;
                        pos = key_eval.lerp_v3(pos, pos_next, k);
                    }
                    cam.transform.position = pos;

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

                    Vector3 look = cur.look_at_type == 1
                        ? chara_parts.group_world(chara_roots, cur.look_at_chara_parts) + cur.position
                        : cur.position;
                    if (next != null && cur.look_at_type == next.look_at_type)
                    {
                        Vector3 look_next = next.look_at_type == 1
                            ? chara_parts.group_world(chara_roots, next.look_at_chara_parts) + next.position
                            : next.position;
                        look = key_eval.lerp_v3(look, look_next, k);
                    }
                    cam.transform.LookAt(look);
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
                    if (fov > 0f) cam.fieldOfView = fov;
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
