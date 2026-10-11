using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // per-character simulation: chains grouped by part, warm-up, suppression,
    // and the begin/end frame the contract describes.
    public class spring_sim
    {
        public enum part { head = 0, body = 1, tail = 2 }

        public class part_state
        {
            public part id;
            public List<spring_chain> chains = new List<spring_chain>();
            public bool has_wind;
            public float spring_rate = 1f;        // CySpringRate from the formation keys
            public float wind_power_rate = 1f;
            public bool apply_transform = true;   // IsApplyTailTransform is false for tail-less rigs
            public Quaternion[][] prev_final;     // per-chain rotation history for blending
        }

        public List<part_state> parts = new List<part_state>();
        public spring_collider[] colliders = new spring_collider[0];
        public spring_wind wind = new spring_wind();

        // globals the controller feeds (contract defaults)
        public float stiffness_rate = 1f;
        public float drag_rate = 1f;
        public float gravity_rate = 1f;
        public float step_scale = 1f;
        public float suppression_length = 1f;     // serialized SuppressionLength (gap threshold, squared compare)
        public float suppression_step = 0.01f;    // 1cm/frame settle

        Transform owner;
        bool simulating;

        public spring_sim(Transform character) { owner = character; }

        // BeginSimulation: reset force accumulators, advance wind phases,
        // seed each root's parent work from the live rig.
        public void begin(float dt)
        {
            simulating = true;
            if (wind != null && dt > 0f) wind.advance();
        }

        // solves every part in order (head, body, tail).
        public void update(float dt, bool collision_switch)
        {
            if (!simulating) return;
            Vector3 wind_vec = wind != null && dt > 0f ? wind.compose() : Vector3.zero;
            foreach (var p in parts)
            {
                if (p == null || p.chains.Count == 0) continue;
                foreach (var chain in p.chains)
                {
                    if (chain == null || chain.records == null) continue;
                    var root = chain.records[0];
                    Transform rt = chain.root_transform;
                    if (rt == null || rt.parent == null) continue;
                    Vector3 parent_pos = rt.parent.position;
                    Quaternion parent_rot = rt.parent.rotation;

                    // suppression first: catch the root toward the animation pose
                    Vector3 anim_world = rt.position;
                    spring_solver.suppress(ref root, anim_world,
                        suppression_length * suppression_length, suppression_step);

                    spring_solver.solve(chain, 0, parent_pos, parent_rot,
                        stiffness_rate, drag_rate, gravity_rate,
                        wind_vec, p.has_wind ? p.wind_power_rate : 0f,
                        step_scale, collision_switch, colliders,
                        root.move_rate, 0f, p.spring_rate);
                }
            }
        }

        // EndSimulation: write final rotations back to the transforms.
        public void end()
        {
            simulating = false;
            foreach (var p in parts)
            {
                if (p == null || !p.apply_transform) continue;
                foreach (var chain in p.chains)
                {
                    if (chain == null) continue;
                    if (p.prev_final == null || p.prev_final.Length < p.chains.Count)
                        p.prev_final = new Quaternion[p.chains.Count][];
                    chain.write_back(1f, p.prev_final[p.chains.IndexOf(chain)]);
                }
            }
        }

        // warm-up: n = ceil(60 * t) full begin/update/end passes at dt=1/60
        // with history reset between steps (the contract's warm-up loop).
        public void warm_up(float warm_time)
        {
            if (warm_time <= 0f) warm_time = 1f;
            int n = Mathf.CeilToInt(60f * warm_time);
            for (int i = 0; i < n; i++)
            {
                begin(1f / 60f);
                update(1f / 60f, collision_switch: false);
                end();
                // history reset: prev = target after each warm-up step
                foreach (var p in parts)
                    foreach (var chain in p.chains)
                    {
                        if (chain?.records == null) continue;
                        for (int bi = 0; bi < chain.records.Length; bi++)
                        {
                            var r = chain.records[bi];
                            r.prev_target = r.target_position;
                            chain.records[bi] = r;
                        }
                    }
            }
        }

        // resolves collider transforms against the live skeleton each frame
        // (bones move); called from begin by the director.
        public void refresh_colliders(Transform character, float height_scale, Vector3 parent_scale)
        {
            for (int i = 0; i < colliders.Length; i++)
                colliders[i] = spring_collider.from_param(collider_params[i], character,
                    height_scale, parent_scale);
        }

        public List<collision_param> collider_params = new List<collision_param>();
    }
}
