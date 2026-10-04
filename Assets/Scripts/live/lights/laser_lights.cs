using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's laser entries over the stage hierarchy, aiming each from its key track.
    public static class laser_lights
    {
        private class laser_container
        {
            public Transform root;
            public List<Renderer> renderers = new();
            public MaterialPropertyBlock mpb;
        }
        private static readonly Dictionary<string, laser_container> containers = new();

        // binds the laser containers by entry name, which carries a ' - ' object index suffix.
        public static void bind(List<laser_track_container> tracks)
        {
            containers.Clear();
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
                var c = new laser_container { root = go.transform, mpb = new MaterialPropertyBlock() };
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) c.renderers.Add(r);
                containers[t.name] = c;
                resolved++;
            }
            trace_log.write($"laser lights: {resolved} containers resolved, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"laser light unresolved: {m}");
        }

        // samples every container per frame; call from the loader's update.
        public static void update(float time_sec, List<laser_track_container> tracks, List<Transform> chara_roots)
        {
            if (tracks == null || containers.Count == 0) return;
            float frame = time_sec * 60f;
            foreach (var t in tracks)
            {
                if (!containers.TryGetValue(t.name, out var c) || c == null) continue;
                var (obj_pos, obj_rot, obj_scale, rotate, deg_root_yaw, deg_laser_pitch) = t.sample(frame);
                if (c.root == null) continue;
                c.root.position = obj_pos;
                c.root.localScale = obj_scale;
                // aim from the authored rotate and root yaw, sweeping the pitch by the blink cursor.
                c.root.rotation = Quaternion.Euler(rotate.x, deg_root_yaw, rotate.z);
                c.root.Rotate(Vector3.right, deg_laser_pitch * blink_sweep(t, frame), Space.Self);
                if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
            }
        }

        // blink cursor sweep: 0 = steady, the period advances the cursor in seconds.
        private static float blink_sweep(laser_track_container t, float frame)
        {
            if (t.blink == 0 || t.blink_period <= 0f) return 1f;
            float phase = (frame / 60f) % (t.blink_period * 2f);
            float u = phase / t.blink_period;
            if (u > 1f) u = 2f - u;
            return u;
        }
    }

    // one worksheet laser entry: name + key track + the blink machine.
    [Serializable]
    public class laser_track_container
    {
        public string name;
        public int object_index;
        public int material_index;
        public List<laser_key> keys = new();
        public int blink;
        public float blink_period;

        // brackets + lerps the key pair.
        public (Vector3, Vector3, Vector3, Vector3, float, float) sample(float frame)
        {
            if (keys == null || keys.Count == 0)
                return (Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, 0f, 0f);
            laser_key a, b;
            float blend;
            if (frame <= keys[0].frame) { a = b = keys[0]; blend = 0f; }
            else if (frame >= keys[keys.Count - 1].frame) { a = b = keys[keys.Count - 1]; blend = 0f; }
            else
            {
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                    {
                        a = keys[i]; b = keys[i + 1];
                        float span = b.frame - a.frame;
                        blend = span <= 0 ? 0f : (frame - a.frame) / span;
                        goto found;
                    }
                }
                a = b = keys[keys.Count - 1]; blend = 0f;
            }
            goto found;
        found:
            return (
                Vector3.Lerp(a.object_position, b.object_position, blend),
                Vector3.Lerp(a.object_rotate, b.object_rotate, blend),
                Vector3.Lerp(a.object_scale, b.object_scale, blend),
                Vector3.Lerp(a.rotate, b.rotate, blend),
                Mathf.Lerp(a.deg_root_yaw, b.deg_root_yaw, blend),
                Mathf.Lerp(a.deg_laser_pitch, b.deg_laser_pitch, blend));
        }
    }

    // one laser key.
    [Serializable]
    public class laser_key
    {
        public int frame;
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
    }
}
