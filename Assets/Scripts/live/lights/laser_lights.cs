using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's laser entries with the game's full model: one
    // instanced fixture per entry, transform from the key track, formation ray
    // gating, per-ray pitch/position, the 6-mode blink step machine with the
    // 256-entry random table, per-ray camera billboarding, raycast length.
    public static class laser_lights
    {
        private const int k_max_rays = 5;
        private const int k_random_table = 256;
        private const float k_ray_distance = 90f;

        // attribute feature bits (LiveTimelineKeyLaserData ATTR_*).
        private const int attr_enable_render = 0x00010000;
        private const int attr_enable_raycast = 0x00020000;
        private const int attr_disable_root_light = 0x00040000;

        private class ray_state
        {
            public Transform transform;
            public Transform parent;
            public Renderer renderer;
            public bool enable;
            public Vector3 dir_forward;
            public Vector3 dir_right;
            public float dist_level = 1f;
            public float rotation_weight = 1f;
        }

        private class laser_container
        {
            public Transform root;
            public Transform controller;
            public List<ray_state> rays = new();
            public List<Renderer> light_renderers = new();
            public bool[] random_table = new bool[k_random_table];
            public int enable_ray_count;
            public int blink;
            public float blink_period;
        }

        private static readonly Dictionary<string, laser_container> containers = new();
        private static Camera _cam;

        // binds the laser fixtures by instanced entry name ('fixture - index').
        public static void bind(List<laser_track_container> tracks, Camera cam)
        {
            containers.Clear();
            _cam = cam;
            if (tracks == null) return;
            int resolved = 0;
            var missing = new List<string>();
            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                var go = blink_lights.find_stage_object(t.name);
                if (go == null)
                {
                    missing.Add(t.name);
                    continue;
                }
                var c = new laser_container { root = go.transform };
                var rng = new System.Random(go.GetInstanceID());
                for (int i = 0; i < k_random_table; i++)
                    c.random_table[i] = rng.Next(0, 2) == 0;

                // the controller: the fork's lasercontroller child or the root.
                var ctrl = find_child(go.transform, "lasercontroller");
                c.controller = ctrl != null ? ctrl : go.transform;

                // rays: one renderer named laser* per controller child, in
                // sibling order, max 5 (the fork's joint hierarchy rule).
                var seen = new HashSet<int>();
                for (int i = 0; i < c.controller.childCount && c.rays.Count < k_max_rays; i++)
                {
                    var joint = c.controller.GetChild(i);
                    foreach (var r in joint.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || !r.transform.name.StartsWith("laser", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (seen.Add(r.GetInstanceID()))
                            c.rays.Add(new ray_state
                            {
                                transform = r.transform,
                                parent = r.transform.parent,
                                renderer = r,
                            });
                        break;
                    }
                }
                // fallback: every renderer named laser* under the controller.
                if (c.rays.Count == 0)
                {
                    foreach (var r in c.controller.GetComponentsInChildren<Renderer>(true))
                    {
                        if (c.rays.Count >= k_max_rays) break;
                        if (r.transform.name.StartsWith("laser", StringComparison.OrdinalIgnoreCase))
                            c.rays.Add(new ray_state
                            {
                                transform = r.transform,
                                parent = r.transform.parent,
                                renderer = r,
                            });
                    }
                }

                // root light renderers: the light_000 child + siblings.
                var light_root = find_child(go.transform, "light_000");
                if (light_root != null)
                {
                    var lr = light_root.GetComponent<Renderer>();
                    if (lr != null) c.light_renderers.Add(lr);
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        if (r != null && r.transform.name.StartsWith("light_", StringComparison.OrdinalIgnoreCase)
                            && !c.light_renderers.Contains(r))
                            c.light_renderers.Add(r);
                }

                containers[t.name] = c;
                resolved++;
                trace_log.write($"laser '{t.name}': rays={c.rays.Count} lights={c.light_renderers.Count}");
            }
            trace_log.write($"laser lights: {resolved} fixtures resolved, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"laser light unresolved: {m}");
        }

        private static Transform find_child(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        // per-frame drive from the key track, the fork's UpdateInfo path.
        public static void update(float time_sec, List<laser_track_container> tracks, List<Transform> chara_roots)
        {
            if (tracks == null || containers.Count == 0) return;
            float frame = time_sec * 60f;
            Camera cam = _cam != null ? _cam : Camera.main;

            foreach (var t in tracks)
            {
                if (!containers.TryGetValue(t.name, out var c) || c == null || c.root == null) continue;
                var (obj_pos, obj_rot, obj_scale, rotate, deg_root_yaw, deg_laser_pitch,
                    formation, attribute, blink, blink_period, pos_interval, raycast_distance,
                    cur_frame) = t.sample(frame);
                if (cur_frame < 0) continue;

                bool is_enabled_render = (attribute & attr_enable_render) != 0;
                c.root.gameObject.SetActive(is_enabled_render);
                if (!is_enabled_render) continue;

                c.root.localPosition = obj_pos;
                c.root.localRotation = obj_rot;
                c.root.localScale = obj_scale;

                // formation ray gating (the game's table).
                c.enable_ray_count = formation_ray_count(formation);
                if (c.rays.Count > 0)
                    c.enable_ray_count = Mathf.Clamp(c.enable_ray_count, 0, c.rays.Count);

                for (int i = 0; i < c.rays.Count; i++)
                {
                    var ray = c.rays[i];
                    ray.enable = i < c.enable_ray_count;
                    if (ray.enable) update_formation(ray, formation, i);
                }

                // root light gate.
                bool root_light = (attribute & attr_disable_root_light) == 0 && formation != 0;
                foreach (var lr in c.light_renderers)
                    if (lr != null) lr.enabled = root_light;

                // controller aim: key rotation + local yaw.
                if (c.enable_ray_count > 0 && c.controller != null)
                {
                    c.controller.localRotation = rotate;
                    c.controller.Rotate(Vector3.up, deg_root_yaw, Space.Self);
                }

                for (int i = 0; i < c.enable_ray_count && i < c.rays.Count; i++)
                {
                    var ray = c.rays[i];
                    update_pitch(ray, deg_laser_pitch);
                    update_position_interval(ray, pos_interval);
                }

                // blink step machine from the key's local clock.
                float local_time = Mathf.Max(0f, time_sec - cur_frame / 60f);
                apply_blink(c, local_time, Mathf.Max(0, (int)frame - cur_frame));

                // render enable + camera billboard + raycast length.
                for (int i = 0; i < c.rays.Count; i++)
                {
                    var ray = c.rays[i];
                    if (ray.renderer != null) ray.renderer.enabled = ray.enable;
                }
                if (cam != null)
                {
                    for (int i = 0; i < c.enable_ray_count && i < c.rays.Count; i++)
                    {
                        var ray = c.rays[i];
                        if (!ray.enable || ray.transform == null) continue;
                        ray.transform.localRotation = Quaternion.identity;
                        Vector3 ray_axis = ray.transform.rotation * Vector3.up;
                        Vector3 cam_dir = cam.transform.position - ray.transform.position;
                        Vector3 projected = cam_dir - Vector3.Dot(cam_dir, ray_axis) * ray_axis;
                        if (projected.sqrMagnitude > 0.000001f)
                            ray.transform.rotation = Quaternion.LookRotation(projected, ray_axis);
                    }
                }
                bool is_enabled_raycast = (attribute & attr_enable_raycast) != 0;
                for (int i = 0; i < c.enable_ray_count && i < c.rays.Count; i++)
                {
                    var ray = c.rays[i];
                    if (ray.transform == null) continue;
                    ray.transform.localScale = Vector3.one;
                    if (!is_enabled_raycast || raycast_distance <= 0.000001f || k_ray_distance <= 0.000001f) continue;
                    if (ray.transform.parent != null)
                    {
                        // no character colliders in the bench; raycast against
                        // everything the stage offers, like the game's chara mask.
                        if (Physics.Raycast(ray.transform.position, ray.transform.up, out var hit, raycast_distance))
                        {
                            var scale = ray.transform.localScale;
                            scale.y = hit.distance / k_ray_distance;
                            ray.transform.localScale = scale;
                        }
                    }
                }
            }
        }

        // the game's formation ray-count table.
        private static int formation_ray_count(int formation)
        {
            switch (formation)
            {
                case 0: case 6: return 0;
                case 1: return 1;
                case 2: return 2;
                case 3: return 3;
                case 4: return 4;
                case 5: return 5;
                case 8: return 2;
                case 9: return 3;
                case 10: return 4;
                case 11: return 5;
                default: return 0;
            }
        }

        // the game's per-ray formation offsets.
        private static void update_formation(ray_state ray, int formation, int index)
        {
            ray.dir_forward = Vector3.zero;
            ray.dist_level = 1f;
            ray.rotation_weight = 1f;

            switch (formation)
            {
                case 1:
                    ray.dist_level = 0f;
                    break;
                case 2:
                    ray.dir_forward.x = index != 0 ? -1f : 1f;
                    ray.dist_level = 0.5f;
                    break;
                case 3:
                    ray.dir_forward.x = index - 1f;
                    break;
                case 4:
                    ray.dir_forward.x = (index & 1) != 0 ? -1f : 1f;
                    if (index > 1) ray.dist_level = 1.5f;
                    else { ray.dist_level = 0.5f; ray.rotation_weight = 0.33333334f; }
                    break;
                case 5:
                    if (index == 0) { ray.dir_forward.x = 0f; ray.dist_level = 0f; }
                    else
                    {
                        ray.dir_forward.x = index >= 3 ? 1f : -1f;
                        if ((index & 1) != 0) ray.rotation_weight = 0.5f;
                        else ray.dist_level = 2f;
                    }
                    break;
                case 6: case 7:
                    break;
                case 8:
                    ray.dir_forward.x = index != 0 ? -1f : 1f;
                    break;
                case 9:
                    ray.dir_forward.z = 1f;
                    ray.dir_forward = Quaternion.Euler(0f, index * 120f, 0f) * ray.dir_forward;
                    ray.dir_forward.Normalize();
                    break;
                case 10:
                    {
                        float direction = index <= 1 ? -1f : 1f;
                        if ((index & 1) != 0) ray.dir_forward.z = direction;
                        else ray.dir_forward.x = direction;
                        break;
                    }
                case 11:
                    ray.dir_forward.z = 1f;
                    ray.dir_forward = Quaternion.Euler(0f, index * 72f, 0f) * ray.dir_forward;
                    ray.dir_forward.Normalize();
                    break;
            }
            ray.dir_right = Vector3.Cross(Vector3.up, ray.dir_forward);
        }

        private static void update_pitch(ray_state ray, float deg_pitch)
        {
            if (ray.parent == null) return;
            ray.parent.localRotation = Quaternion.AngleAxis(deg_pitch * ray.rotation_weight, ray.dir_right);
        }

        private static void update_position_interval(ray_state ray, float position_interval)
        {
            if (ray.parent == null) return;
            ray.parent.localPosition = ray.dir_forward * (position_interval * ray.dist_level);
        }

        // the game's blink step machine: replays from zero each update.
        private static void apply_blink(laser_container c, float local_time, int update_count)
        {
            if (c.rays.Count == 0 || c.enable_ray_count <= 0) return;

            if (c.blink == 0)
            {
                for (int i = 0; i < c.enable_ray_count; i++)
                    c.rays[i].enable = true;
                return;
            }

            // unsigned compare: 1,2,5,6 start on; 3,4 start off.
            bool initial_enable = (uint)(c.blink - 3) > 1u;
            for (int i = 0; i < c.enable_ray_count; i++)
                c.rays[i].enable = initial_enable;

            int blink_step_count = c.blink_period > 0f
                ? (int)(local_time / c.blink_period)
                : update_count;

            int blink_count = 0;
            for (int s = 0; s < blink_step_count; s++)
                blink_count = update_blink(c, s, blink_count);
        }

        private static int update_blink(laser_container c, int update_count, int blink_count)
        {
            switch (c.blink)
            {
                case 1:
                    for (int i = 0; i < c.enable_ray_count; i++)
                        c.rays[i].enable = !c.rays[i].enable;
                    break;
                case 2:
                    for (int i = 0; i < c.enable_ray_count; i++)
                    {
                        if (c.rays[i].enable) c.rays[i].enable = false;
                        else
                        {
                            int random_index = (i + c.enable_ray_count * update_count) % k_random_table;
                            c.rays[i].enable = c.random_table[random_index];
                        }
                    }
                    break;
                case 3:
                    for (int i = 0; i < c.enable_ray_count; i++)
                        c.rays[i].enable = i == blink_count;
                    break;
                case 4:
                    for (int i = 0; i < c.enable_ray_count; i++)
                    {
                        int ray_index = c.enable_ray_count - i - 1;
                        c.rays[i].enable = false;
                        c.rays[ray_index].enable = i == blink_count;
                    }
                    break;
                case 5:
                    for (int i = 0; i < c.enable_ray_count; i++)
                        c.rays[i].enable = i != blink_count;
                    break;
                case 6:
                    for (int i = 0; i < c.enable_ray_count; i++)
                    {
                        int ray_index = c.enable_ray_count - i - 1;
                        c.rays[i].enable = true;
                        c.rays[ray_index].enable = i != blink_count;
                    }
                    break;
            }
            return c.enable_ray_count > 1 ? (blink_count + 1) % c.enable_ray_count : 0;
        }

        public static void reset()
        {
            containers.Clear();
            _cam = null;
        }
    }
    // one worksheet laser entry: name + key track.
    [Serializable]
    public class laser_track_container
    {
        public string name;
        public int object_index;
        public int material_index;
        public List<laser_key> keys = new();

        // brackets + lerps the key pair; returns the current key's frame for
        // the key-relative blink clock (-1 = before first key).
        public (Vector3, Quaternion, Vector3, Quaternion, float, float,
            int, int, int, float, float, float, int) sample(float frame)
        {
            if (keys == null || keys.Count == 0)
                return (Vector3.zero, Quaternion.identity, Vector3.one, Quaternion.identity,
                    0f, 0f, 0, 0, 0, 0f, 0f, 0f, -1);
            laser_key a, b;
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

            // the fork lerps only when the NEXT key IsInterpolateKey.
            bool lerp = b != a && b.interpolate_type != 0;
            float k = lerp ? blend : 0f;
            return (
                lerp ? Vector3.Lerp(a.object_position, b.object_position, k) : a.object_position,
                lerp ? Quaternion.Lerp(Quaternion.Euler(a.object_rotate), Quaternion.Euler(b.object_rotate), k)
                     : Quaternion.Euler(a.object_rotate),
                lerp ? Vector3.Lerp(a.object_scale, b.object_scale, k) : a.object_scale,
                lerp ? Quaternion.Lerp(Quaternion.Euler(a.rotate), Quaternion.Euler(b.rotate), k)
                     : Quaternion.Euler(a.rotate),
                lerp ? Mathf.Lerp(a.deg_root_yaw, b.deg_root_yaw, k) : a.deg_root_yaw,
                lerp ? Mathf.Lerp(a.deg_laser_pitch, b.deg_laser_pitch, k) : a.deg_laser_pitch,
                a.formation,
                a.attribute,
                lerp ? (int)Mathf.Lerp(a.blink, b.blink, k) : a.blink,
                lerp ? Mathf.Lerp(a.blink_period, b.blink_period, k) : a.blink_period,
                lerp ? Mathf.Lerp(a.pos_interval, b.pos_interval, k) : a.pos_interval,
                a.raycast_distance,
                a.frame);
        }
    }

    // one laser key.
    [Serializable]
    public class laser_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public Vector3 object_position;
        public Vector3 object_rotate;
        public Vector3 object_scale;
        public int formation;
        public Vector3 rotate;
        public float deg_root_yaw;
        public float deg_laser_pitch;
        public float pos_interval;
        public int blink;
        public float blink_period;
        public float raycast_distance;
    }

}
