using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // per-character simulation: chains grouped by part, the begin/update/end
    // frame the contract describes, warm-up, suppression arming, connected
    // bones, and the per-frame collider refresh.
    public class spring_sim
    {
        public enum part { head = 0, body = 1, tail = 2 }

        public class part_state
        {
            public part id;
            public List<spring_chain> chains = new List<spring_chain>();
            public bool has_wind;
            public float spring_rate = 1f;        // CySpringRate from the formation keys
            public float wind_power_rate = 1f;    // per-part windPowerRate (clamped 0..1)
            public bool apply_transform = true;   // IsApplyTailTransform gates the tail's write
            public int bench_tick;                 // per-part bench sample counter

            // hip-move damper: the part's main container params (IsEnableHipMoveParam).
            // negative influence distance = the damper is disabled for the part.
            public bool hip_enabled;
            public float hip_influence_distance = -1f;
            public float hip_influence_max_distance = -1f;
            public float hip_rate = 1f;           // last frame's rate (blend input)
        }

        public List<part_state> parts = new List<part_state>();
        public spring_wind wind;                  // null when no container enabled wind

        // hip-move damper source: the character's Position/Hip transform and
        // the previous-frame position it moved from.
        public Transform hip_transform;
        Vector3 hip_prev_pos;
        bool hip_prev_valid;

        // per-part collision tables (the game keeps one per part; the chain
        // rows reference rows by name within their part's table).
        Dictionary<part, List<collision_param>> collision_tables = new();

        // part lookup helper for the loader (parts arrive in bind order).
        public part_state part_or_create(part id)
        {
            foreach (var p in parts)
                if (p.id == id) return p;
            var np = new part_state { id = id };
            parts.Add(np);
            return np;
        }

        // the loader hands over one part's collision rows after binding.
        public void add_collision_table(part id, List<collision_param> rows)
        {
            collision_tables[id] = rows;
        }

        // resolves each chain's collision-name lists against its part's
        // table (index order preserved; missing names skipped with a count).
        public void resolve_chain_collisions()
        {
            foreach (var p in parts)
            {
                if (!collision_tables.TryGetValue(p.id, out var table)) continue;
                var index = new Dictionary<string, int>();
                for (int i = 0; i < table.Count; i++)
                    if (!index.ContainsKey(table[i].collision_name))
                        index[table[i].collision_name] = i;

                foreach (var chain in p.chains)
                {
                    if (chain?.element_of == null) continue;
                    for (int bi = 0; bi < chain.element_of.Length; bi++)
                    {
                        var el = chain.element_of[bi];
                        if (el == null || el.collision_names.Count == 0) continue;
                        var r = chain.records[bi];
                        var slots = new List<int>();
                        foreach (var name in el.collision_names)
                            if (index.TryGetValue(name, out int ci)) slots.Add(ci);
                        if (slots.Count > 0)
                        {
                            r.collision_indices = slots.ToArray();
                            r.active_collisions = slots.Count;
                            chain.records[bi] = r;
                        }
                    }
                }
            }
        }

        // connected-bone rows collected from the containers; resolved after
        // every chain is bound (a pair's bones may sit in different parts).
        public List<spring_loader.connected_row> pending_connected = new();

        public void resolve_connected(Transform character)
        {
            foreach (var row in pending_connected)
            {
                if (row?.bone_a == null || row.bone_b == null) continue;
                Transform a = find_bone(character, row.bone_a);
                Transform b = find_bone(character, row.bone_b);
                if (a == null || b == null) continue;
                spring_chain ca = owning_chain(a);
                spring_chain cb = owning_chain(b);
                if (ca == null || cb == null) continue;
                connected.Add(new connected_pair
                {
                    rec_a = ca.records, idx_a = index_of(ca, a),
                    rec_b = cb.records, idx_b = index_of(cb, b),
                    bone_a = a, bone_b = b,
                    default_distance = Vector3.Distance(a.position, b.position),
                    intensity = row.intensity,
                });
            }
            pending_connected.Clear();
        }

        // arms the one-shot suppression pass (spawn/cut/teleport).
        public void arm_suppression() => suppression_armed = true;

        spring_chain owning_chain(Transform t)
        {
            foreach (var p in parts)
                foreach (var c in p.chains)
                {
                    if (c?.bones == null) continue;
                    for (int i = 0; i < c.bones.Length; i++)
                        if (c.bones[i] == t) return c;
                }
            return null;
        }

        static int index_of(spring_chain c, Transform t)
        {
            for (int i = 0; i < c.bones.Length; i++)
                if (c.bones[i] == t) return i;
            return 0;
        }

        static Transform find_bone(Transform root, string name)
        {
            if (root == null || root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = find_bone(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // globals the controller feeds (contract defaults)
        public float stiffness_rate = 1f;
        public float drag_rate = 1f;
        public float gravity_rate = 1f;
        public float step_scale = 1f;
        public float suppression_length = 1f;     // serialized SuppressionLength (squared-compare gate)

        // connected-bone pairs (container ConnectedBoneList)
        public class connected_pair
        {
            public spring_record[] rec_a; public int idx_a;   // chain A record slot
            public spring_record[] rec_b; public int idx_b;
            public Transform bone_a, bone_b;
            public float default_distance;
            public float intensity;
        }
        public List<connected_pair> connected = new List<connected_pair>();

        Transform owner;
        bool simulating;
        // one-shot suppression arm (SetSuppression on spawn/cut/teleport)
        public bool suppression_armed;

        public spring_sim() { }

        public spring_sim(Transform character) { owner = character; }

        // BeginSimulation: advance wind phases, refresh colliders, apply the
        // armed suppression once, accumulate connected-bone forces.
        public void begin(float dt)
        {
            simulating = true;

            if (wind != null && dt > 0f) wind.advance(dt);

            refresh_colliders();

            // hip-move damper: measure the frame's hip displacement and
            // resolve each part's rate (CySpringController.UpdateSpring's
            // per-frame path). a missing hip or a first frame disables the
            // blend (rate stays 1).
            if (hip_transform != null)
            {
                Vector3 hip_pos = hip_transform.position;
                float hip_dist = hip_prev_valid
                    ? Vector3.Distance(hip_pos, hip_prev_pos) : 0f;
                hip_prev_pos = hip_pos;
                hip_prev_valid = true;
                foreach (var p in parts)
                {
                    if (p == null) continue;
                    p.hip_rate = get_hip_move_rate(
                        p.hip_enabled, p.hip_influence_distance,
                        p.hip_influence_max_distance, hip_dist);
                }
            }
            else
            {
                foreach (var p in parts)
                    if (p != null) p.hip_rate = -1f;
            }

            if (suppression_armed)
            {
                apply_suppression();
                suppression_armed = false;
            }

            accumulate_connected_forces();
        }

        // solves every part in order (head, body, tail).
        public void update(bool collision_switch)
        {
            if (!simulating) return;
            foreach (var p in parts)
            {
                if (p == null || p.chains.Count == 0) continue;
                float wind_power = p.has_wind && wind != null ? Mathf.Clamp01(p.wind_power_rate) : 0f;
                resolved.TryGetValue(p.id, out var part_colliders);
                foreach (var chain in p.chains)
                {
                    if (chain == null || chain.records == null) continue;
                    Transform rt = chain.root_transform;
                    if (rt == null) continue;
                    // the root's parent work: the live rig transform above it.
                    Quaternion parent_rot = rt.parent != null ? rt.parent.rotation : Quaternion.identity;

                    // the animation's current local pose, gathered fresh every
                    // frame (the dynamic-ratio blend target; GatherRootSpring's
                    // AnimationRotation slot).
                    for (int bi = 0; bi < chain.records.Length; bi++)
                        if (chain.bones[bi] != null)
                            chain.records[bi].anim_local = chain.bones[bi].localRotation;

                    // the root's animated world position: the solver seeds the
                    // root's SelfPosition from its own animated transform.
                    Vector3 root_anim_pos = rt.position;

                    Vector3 wind_vec = Vector3.zero;
                    if (wind != null && wind_power > 0f)
                        wind_vec = wind.compose(chain.wind_phase_shift, parent_rot);

                    spring_solver.solve(chain, root_anim_pos, parent_rot,
                        stiffness_rate, drag_rate, gravity_rate,
                        wind_vec, wind_power, step_scale, collision_switch,
                        part_colliders, p.spring_rate, p.hip_rate);

                    // suppression easing toward the animated pose (1cm/frame)
                    if (suppression_pending)
                    {
                        var r = chain.records[0];
                        spring_solver.suppress(ref r, root_anim_pos, 0.01f);
                        chain.records[0] = r;
                        chain_propagate_suppression(chain);
                    }
                }
            }
            suppression_pending = false;
        }

        // EndSimulation: write the solved rotations back to the bones.
        public void end()
        {
            simulating = false;
            foreach (var p in parts)
            {
                if (p == null || !p.apply_transform) continue;
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null || chain.records.Length == 0) continue;
                    // bench evidence: per-chain angular delta from the
                    // animation pose, sampled every ~2s (trace-scan friendly).
                    // the tick counter is per part: the gate shares samples
                    // across head/body/tail instead of letting the first
                    // parts consume every window.
                    int tick = p.bench_tick++;
                    if (bench_sample(tick))
                    {
                        float delta = 0f;
                        for (int i = 0; i < chain.solve_count; i++)
                            delta = Mathf.Max(delta,
                                Quaternion.Angle(chain.records[i].final_rot, chain.bones[i].rotation));
                        trace_log.write($"spring step: part {p.id} chain '{chain.root_transform.name}' bones {chain.solve_count} maxdelta {delta:0.00}deg");
                    }
                    chain.write_back(null);
                }
            }
        }

        // samples 5 consecutive steps at startup then every 120th step (~2s).
        static bool bench_sample(int n) => n <= 5 || n % 120 == 0;

        // hip-move rate, the shipped solver's anti-fling damper
        // (GetHipMoveRate @ 0x7ff8e496a2b0): returns -1 when the part carries
        // no hip params (damper off), 1 inside the influence distance, a
        // linear ramp toward 0 across the influence band, 0 beyond the max.
        static float get_hip_move_rate(bool enabled, float influence, float max_distance,
            float hip_dist)
        {
            if (!enabled) return -1f;
            if (hip_dist <= influence) return 1f;
            if (hip_dist >= max_distance && hip_dist != max_distance) return 0f;
            // the binary's equality edge: hip_dist == max_distance returns the
            // ramp result below (guard division by a zero band width).
            return 1f - (max_distance - hip_dist) / (max_distance - influence);
        }

        // warm-up: n = ceil(60 * t) full begin/update/end passes at dt=1/60
        // (the game always warms at 60; fork WarmUpCySpring), history reset
        // between steps, no collisions, no rendering.
        public void warm_up(float warm_time)
        {
            if (warm_time <= 0f) warm_time = 1f;
            int n = Mathf.Max(1, Mathf.CeilToInt(warm_time * 60f));
            float dt = 1f / 60f;
            for (int i = 0; i < n; i++)
            {
                begin(dt);
                update(collision_switch: false);
                end();
                reset_history();
            }
        }

        // re-zeroes the velocity history so consecutive warm-up steps do not
        // accumulate velocity from the reset jumps (contract: history reset).
        void reset_history()
        {
            foreach (var p in parts)
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null) continue;
                    for (int bi = 0; bi < chain.records.Length; bi++)
                    {
                        var r = chain.records[bi];
                        r.prev_target = r.target_position;
                        r.force = Vector3.zero;
                        r.connected_force = Vector3.zero;
                        chain.records[bi] = r;
                    }
                }
        }

        void apply_suppression()
        {
            // armed on spawn/cut: ease each root toward its animated position
            // when the gap exceeds the squared threshold (game default 1.0).
            foreach (var p in parts)
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null || chain.root_transform == null) continue;
                    Vector3 anim_world = chain.root_transform.position;
                    Vector3 diff = anim_world - chain.records[0].target_position;
                    float check = suppression_length > 0f ? suppression_length : 1f;
                    if (diff.sqrMagnitude >= check * check)
                        suppression_pending = true;
                }
        }

        void chain_propagate_suppression(spring_chain chain)
        {
            // the contract's propagation loop: each next record copies the
            // previous record's eased target/prev and resets its rotation.
            for (int i = 1; i < chain.records.Length; i++)
            {
                var r = chain.records[i];
                var prev = chain.records[i - 1];
                r.target_position = prev.target_position;
                r.prev_target = prev.prev_target;
                r.final_rot = prev.final_rot;
                chain.records[i] = r;
            }
        }

        // connected-bone forces: pair distance error pulls both records
        // toward each other; accumulated into the PREVIOUS record's slot.
        void accumulate_connected_forces()
        {
            foreach (var c in connected)
            {
                if (c.rec_a == null || c.rec_b == null) continue;
                if (c.bone_a == null || c.bone_b == null) continue;

                Vector3 d1 = c.bone_b.position - c.bone_a.position;
                float dist1 = d1.magnitude;
                var ra = c.rec_a[c.idx_a];
                ra.connected_force += d1.normalized * ((dist1 - c.default_distance) * c.intensity);
                c.rec_a[c.idx_a] = ra;

                Vector3 d2 = c.bone_a.position - c.bone_b.position;
                float dist2 = d2.magnitude;
                var rb = c.rec_b[c.idx_b];
                rb.connected_force += d2.normalized * ((dist2 - c.default_distance) * c.intensity);
                c.rec_b[c.idx_b] = rb;
            }
        }

        // resolves collider world placements from the rider transforms (bones
        // move every frame; the riders were parented once at load).
        void refresh_colliders()
        {
            foreach (var kv in collision_tables)
            {
                var rows = kv.Value;
                if (rows == null) continue;
                if (!resolved.TryGetValue(kv.Key, out var cols) || cols.Length != rows.Count)
                {
                    cols = new spring_collider[rows.Count];
                    resolved[kv.Key] = cols;
                }
                for (int i = 0; i < rows.Count; i++)
                    cols[i] = spring_collider.from_rider(rows[i]);
            }
        }

        Dictionary<part, spring_collider[]> resolved = new();
        bool suppression_pending;
    }
}
