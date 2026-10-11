using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // one root chain: records[0] = the root bone, [1..n] = children bound by
    // transform-name matching against the container's child elements.
    // binding rules from the decoded bridge:
    //   - walk the transform hierarchy depth-first; a child joins the chain
    //     only when a child element names it (collision transforms are kept
    //     out by the collision-name table, checked at container load);
    //   - a pair's segment axis/distance live on the PARENT record (the child
    //     writes them during bind); a segment shorter than 1e-4 kills the
    //     whole chain;
    //   - a leaf child inherits its parent's axis and distance.
    public class spring_chain
    {
        public Transform root_transform;
        public spring_record[] records;
        public Transform[] bones;          // bones[i] drives records[i]
        public int solve_count;            // TrueNativesCount: n or n-1 when the end bone is not simulated
        public float wind_phase_shift;     // (index / count) * pi/2 from the container order

        // the deserialized params for this chain
        public spring_element_params root_params;
        public Dictionary<string, spring_element_params> child_params;
        public spring_element_params[] element_of;   // per-record element lookup

        // collision names referenced along the chain (root's list first; bones
        // without their own element inherit the root's)
        public List<string> collision_names;

        public bool bind(Transform root_bone, spring_element_params root_p,
            Dictionary<string, spring_element_params> children, float phase_shift)
        {
            root_transform = root_bone;
            root_params = root_p;
            child_params = children ?? new Dictionary<string, spring_element_params>();
            wind_phase_shift = phase_shift;

            // chain[0] is the root transform itself; children join by name match.
            var chain = new List<Transform> { root_bone };
            var elems = new List<spring_element_params> { root_p };
            collect(root_bone, chain, elems);
            if (chain.Count == 0) return false;

            int n = chain.Count;
            bones = new Transform[n];
            records = new spring_record[n];
            element_of = new spring_element_params[n];
            for (int i = 0; i < n; i++)
            {
                bones[i] = chain[i];
                element_of[i] = elems[i];
                Transform parent_bone = i == 0 ? null : bones[i - 1];
                records[i] = spring_record.bind(chain[i], parent_bone);
            }

            // child-driven segment fill: each non-root record writes its
            // axis/distance onto its PARENT record; a degenerate segment
            // kills the chain.
            for (int i = 1; i < n; i++)
            {
                Vector3 delta = bones[i].position - bones[i - 1].position;
                float dist = delta.magnitude;
                if (dist < 1e-4f) return false;
                Quaternion inv = Quaternion.Inverse(bones[i - 1].rotation);
                records[i - 1].bone_axis = (inv * delta).normalized;
                records[i - 1].bone_distance = dist;
            }
            // a leaf inherits its parent's axis + distance (the contract's end-bone rule)
            if (n >= 2)
            {
                records[n - 1].bone_axis = records[n - 2].bone_axis;
                records[n - 1].bone_distance = records[n - 2].bone_distance;
            }

            apply_params();
            // the ROOT element's _needSimulateEndBone gates the END bone's write
            byte need_end = root_p != null ? root_p.need_simulate_end : (byte)1;
            solve_count = need_end == 0 ? n - 1 : n;
            return true;
        }

        private void collect(Transform t, List<Transform> chain, List<spring_element_params> elems)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (!child_params.TryGetValue(c.name, out var p)) continue;
                chain.Add(c);
                elems.Add(p);
                collect(c, chain, elems);
            }
        }

        private void apply_params()
        {
            for (int i = 0; i < records.Length; i++)
            {
                var p = element_of[i];
                if (p == null) continue;
                p.apply(ref records[i]);
                if (p.collision_names.Count > 0 && collision_names == null)
                    collision_names = p.collision_names;
            }
        }

        // writes the solved final rotation back to the transforms (PostSpring).
        // the end bone is skipped when the root's need_simulate_end == 0.
        public void write_back(Quaternion[] prev_final)
        {
            for (int i = 0; i < solve_count; i++)
            {
                if (bones[i] == null) continue;
                bones[i].rotation = records[i].final_rot;
                if (prev_final != null && i < prev_final.Length) prev_final[i] = records[i].final_rot;
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
