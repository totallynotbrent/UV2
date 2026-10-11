using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // one root chain: records[0] = the root bone, [1..n] = children bound by
    // transform-name matching against the container's child elements.
    public class spring_chain
    {
        public Transform root_transform;
        public spring_record[] records;
        public Transform[] bones;          // bones[i] drives records[i]
        public int solve_count;            // TrueNativesCount: n or n-1 when the end bone is not simulated
        public float wind_phase_shift;     // (index / count) * pi/2 from the container order

        // the deserialized params for this chain (bone name -> element values)
        public spring_element_params root_params;
        public Dictionary<string, spring_element_params> child_params;

        // collision name list the chain's bones reference (indices resolved per character)
        public List<string> collision_names;

        public bool bind(Transform root_bone, spring_element_params root_p,
            Dictionary<string, spring_element_params> children, float phase_shift)
        {
            root_transform = root_bone;
            root_params = root_p;
            child_params = children ?? new Dictionary<string, spring_element_params>();
            wind_phase_shift = phase_shift;

            // walk the transform hierarchy depth-first collecting bones that
            // carry child-element params (collision transforms never carry any).
            var chain = new List<Transform>();
            collect(root_bone, chain);
            if (chain.Count == 0) return false;

            int n = chain.Count;
            bones = new Transform[n];
            records = new spring_record[n];
            for (int i = 0; i < n; i++)
            {
                bones[i] = chain[i];
                Transform parent_bone = i == 0 ? null : bones[i - 1];
                records[i] = spring_record.bind(chain[i], parent_bone);
                if (i > 0 && records[i].bone_distance < 1e-4f) return false; // degenerate segment
            }
            // a leaf inherits its parent's axis + distance (the contract's end-bone rule)
            if (n >= 2)
            {
                records[n - 1].bone_axis = records[n - 2].bone_axis;
                records[n - 1].bone_distance = records[n - 2].bone_distance;
            }
            apply_params();
            solve_count = root_p != null && root_p.need_simulate_end == 0 ? n - 1 : n;
            return true;
        }

        private void collect(Transform t, List<Transform> chain)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name.StartsWith("Col_") || c.name.StartsWith("col_")) continue;
                if (!child_params.ContainsKey(c.name)) continue;
                chain.Add(c);
                collect(c, chain);
            }
        }

        private void apply_params()
        {
            if (root_params != null) root_params.apply(ref records[0]);
            for (int i = 1; i < records.Length; i++)
            {
                if (child_params.TryGetValue(bones[i].name, out var p)) p.apply(ref records[i]);
            }
        }

        // writes the solved final rotation back to the transforms (PostSpring).
        public void write_back(float blend_rate, Quaternion[] prev_final)
        {
            for (int i = 0; i < solve_count; i++)
            {
                if (bones[i] == null) continue;
                Quaternion q = records[i].final_rot;
                if (blend_rate < 1f && prev_final != null)
                    q = Quaternion.Slerp(prev_final[i], q, blend_rate);
                bones[i].rotation = q;
                if (prev_final != null) prev_final[i] = q;
            }
        }
    }

    // one deserialized per-bone param set from one container element.
    public class spring_element_params
    {
        public string bone_name;           // the transform this element drives
        public float stiffness;
        public float drag;
        public float gravity;
        public float collision_radius;
        public float move_rate = 1f;
        public byte need_simulate_end = 1;
        public byte is_limit;
        public Vector3 limit_min;
        public Vector3 limit_max;
        public Vector4 wind_rates = Vector4.one;
        public List<string> collision_names = new List<string>();
        public byte need_env_collision;

        public void apply(ref spring_record r)
        {
            r.stiffness = stiffness;
            r.drag = drag;
            r.gravity = gravity;
            r.collision_radius = collision_radius;
            r.move_rate = move_rate;
            r.is_limit = is_limit;
            r.limit_min = limit_min;
            r.limit_max = limit_max;
            r.wind_rates = wind_rates;
        }
    }
}
