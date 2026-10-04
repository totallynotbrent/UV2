using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the mob/cyalume group tracks into the shader group matrix arrays.
    public static class mob_control
    {
        // the mob and cyalume controllers both carry 11 group slots.
        public const int group_slots = 11;

        private static readonly Matrix4x4[] mob_matrix = new Matrix4x4[group_slots];
        private static readonly Matrix4x4[] cyalume_matrix = new Matrix4x4[group_slots];

        // rig roots tracked so CPU-side culling and raycasts can follow the shader-driven meshes.
        private static readonly List<Transform> mob_roots = new();
        private static readonly List<Transform> cyalume_roots = new();
        private static bool bound;

        private static readonly int id_mob_matrix = Shader.PropertyToID("_MobGroupMatrix");
        private static readonly int id_cyalume_matrix = Shader.PropertyToID("_CyalumeGroupMatrix");

        // records the mob-wall and pen-light roots found under a stage root.
        public static void record_rig(Transform stage_root)
        {
            if (stage_root == null) return;
            if (_recorded_roots.Add(stage_root)) { }
            int mob_before = mob_roots.Count, cyalume_before = cyalume_roots.Count;
            foreach (var child in stage_root.GetComponentsInChildren<Transform>(true))
            {
                var n = child.name.Replace("(Clone)", "");
                if (n.StartsWith("pfb_env_live_cmn_mob") || n.StartsWith("mob") && n.Contains("front") || n.StartsWith("mob") && n.Contains("back"))
                    mob_roots.Add(child);
                else if (n.StartsWith("pfb_env_live_cmn_cyalume_r") || n.StartsWith("pfb_env_live_cmn_cyalume_d") ||
                         n.StartsWith("cyalume_r") || n.StartsWith("cyalume_d"))
                    cyalume_roots.Add(child);
            }
            if (mob_roots.Count != mob_before || cyalume_roots.Count != cyalume_before || _recorded_roots.Count == 1)
                trace_log.write($"crowd mob: {mob_roots.Count} mob roots, {cyalume_roots.Count} cyalume roots recorded");
            bound = false;
        }

        public static void reset()
        {
            mob_roots.Clear();
            cyalume_roots.Clear();
            _recorded_roots.Clear();
            bound = false;
        }

        private static readonly HashSet<Transform> _recorded_roots = new();

        // samples both group tracks and publishes the matrix arrays.
        public static void update(float time_sec, List<mob_cyalume_group> mob_tracks,
            List<mob_cyalume_group> cyalume_tracks)
        {
            if ((mob_tracks == null || mob_tracks.Count == 0) &&
                (cyalume_tracks == null || cyalume_tracks.Count == 0)) return;

            if (!bound)
            {
                bound = true;
                for (int i = 0; i < group_slots; i++)
                {
                    mob_matrix[i] = Matrix4x4.identity;
                    cyalume_matrix[i] = Matrix4x4.identity;
                }
            }

            int mob_driven = 0, cyalume_driven = 0;
            if (mob_tracks != null)
                mob_driven = drive_track(mob_tracks, time_sec, mob_matrix);
            if (cyalume_tracks != null)
                cyalume_driven = drive_track(cyalume_tracks, time_sec, cyalume_matrix);

            if (mob_driven > 0) Shader.SetGlobalMatrixArray(id_mob_matrix, mob_matrix);
            if (cyalume_driven > 0) Shader.SetGlobalMatrixArray(id_cyalume_matrix, cyalume_matrix);
        }

        // lerps each group's keys into its matrix slot; returns how many groups published.
        private static int drive_track(List<mob_cyalume_group> tracks, float time_sec, Matrix4x4[] matrix)
        {
            float frame = time_sec * 60f;
            int driven = 0;
            foreach (var group in tracks)
            {
                var keys = group.keys;
                if (keys == null || keys.Count == 0) continue;
                int slot = group.group_index;
                if ((uint)slot >= (uint)group_slots) continue;

                mob_cyalume_key a, b;
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
                            blend = key_eval.interp(a, b, time_sec);
                            break;
                        }
                    }
                }

                var pos = Vector3.Lerp(a.position, b.position, blend);
                var rot = Quaternion.Euler(Vector3.Lerp(a.angle, b.angle, blend));
                var scl = Vector3.Lerp(a.scale, b.scale, blend);
                matrix[slot].SetTRS(pos, rot, scl);
                driven++;
            }
            return driven;
        }
    }
}
