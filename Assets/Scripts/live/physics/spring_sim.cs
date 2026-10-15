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

            // clip-driven tail motion (PlayTailAnimation): while the motion
            // clip animates this part's bones the dynamics ratio tweens to 0
            // (rest line chases the animation, pure follow-through); when the
            // clip stops, RemoveClipDrivenTailMotion tweens back to 1 over
            // 0.5s. ratio_target holds the tween endpoint, ratio the value.
            public float ratio_target = 1f;
            public float ratio = 1f;

            // hip-move damper: the part's main container params (IsEnableHipMoveParam).
            // negative influence distance = the damper is disabled for the part.
            public bool hip_enabled;
            public float hip_influence_distance = -1f;
            public float hip_influence_max_distance = -1f;
            public float hip_rate = 1f;           // last frame's rate (blend input)
        }

        public List<part_state> parts = new List<part_state>();
        public spring_wind wind;                  // null when no container enabled wind

        // the timeline's charaWind keys own the on/off; a song without an
        // enabled key never blows (1004's container params stay dormant).
        public bool wind_enabled = false;

        public void set_wind_enabled(bool on) => wind_enabled = on;

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

        // arms the one-shot suppression pass (spawn/cut/teleport): the root
        // anchor's first solve after the arm runs v1's UpdateSuppression —
        // a 1cm clamp toward the animated pose when the gap exceeds 1m, so
        // the chain glides over instead of whipping through the teleport.
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
        public void update(bool collision_switch, float step_dt = 0f)
        {
            if (!simulating) return;
            foreach (var p in parts)
            {
                if (p == null || p.chains.Count == 0) continue;
                float wind_power = wind_enabled && p.has_wind && wind != null ? Mathf.Clamp01(p.wind_power_rate) : 0f;
                resolved.TryGetValue(p.id, out var part_colliders);

                // clip-driven dynamics ratio: the game tweens this ONLY from
                // PlayTailAnimation / RemoveClipDrivenTailMotion events — never
                // from pose inspection — and 1004 fires none. the ratio stays
                // pinned at 1 (pure physics) until an event source exists.
                p.ratio = 1f;
                p.ratio_target = 1f;
                foreach (var chain in p.chains)
                {
                    if (chain == null || chain.records == null) continue;
                    for (int bi = 0; bi < chain.records.Length; bi++)
                        chain.records[bi].dynamic_ratio = 1f;
                }
                foreach (var chain in p.chains)
                {
                    if (chain == null || chain.records == null) continue;
                    Transform rt = chain.root_transform;
                    if (rt == null) continue;
                    // the root's parent work: the live rig transform above it.
                    Quaternion parent_rot = rt.parent != null ? rt.parent.rotation : Quaternion.identity;

                    // the root's animated world position: the solver seeds the
                    // root's SelfPosition from its own animated transform.
                    Vector3 root_anim_pos = rt.position;

                    Vector3 wind_vec = Vector3.zero;
                    if (wind != null && wind_power > 0f)
                        wind_vec = wind.compose(chain.wind_phase_shift, parent_rot);

                    spring_solver.solve(chain, root_anim_pos, parent_rot,
                        stiffness_rate, drag_rate, gravity_rate,
                        wind_vec, wind_power, 1f, true, collision_switch,
                        part_colliders, p.spring_rate, p.hip_rate, p.hip_rate);
                }
            }
            suppression_pending = false;
        }

        // EndSimulation: write the solved rotations back to the bones.
        // non-step frame: write the last solved pose without solving. the
        // write path must run every rendered frame (the game's PostSpring
        // does the same on frames the native solve is skipped).
        public void hold()
        {
            simulating = false;
            foreach (var p in parts)
            {
                if (p == null || !p.apply_transform) continue;
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null || chain.records.Length == 0) continue;
                    chain.write_back(null);
                }
            }
        }

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
                    // parts consume every window. sampling only runs under
                    // the explicit bench flags — an 18-member cast at
                    // default cadence wrote 55k lines (20mb trace) in one
                    // normal listening run.
                    int tick = p.bench_tick++;
                    bool want_trace = bench_dense || tail_probe_enabled;
                    if (want_trace && (bench_sample(tick) || (bench_dense && (p.id == spring_sim.part.body || p.id == spring_sim.part.head))))
                    {
                        float delta = 0f;
                        for (int i = 0; i < chain.solve_count; i++)
                            delta = Mathf.Max(delta,
                                Quaternion.Angle(chain.records[i].final_rot, chain.bones[i].rotation));
                        // world position of the tip record + its height
                        // relative to the chain root: the direction check
                        // (hair must hang below its anchor, tail behind).
                        Vector3 tip = chain.records[chain.solve_count - 1].target_position;
                        Vector3 root_pos = chain.root_transform.position;
                        Vector3 rel = tip - root_pos;
                        trace_log.write($"spring step: part {p.id} chain '{chain.root_transform.name}' bones {chain.solve_count} maxdelta {delta:0.00}deg tip_rel ({rel.x:0.000},{rel.y:0.000},{rel.z:0.000}) len {rel.magnitude:0.000} owner {(owner != null ? owner.name : "?")}");
                        // tail probe: the tail pinned near 180deg needs the
                        // per-bone breakdown to localize the flip. aim is
                        // the rest direction, dir the solved direction.
                        if (p.id == spring_sim.part.tail && tail_probe_enabled)
                        {
                            for (int i = 0; i < chain.solve_count; i++)
                            {
                                var r = chain.records[i];
                                Quaternion base_rot = r.parent_rot * r.init_local;
                                Vector3 aim = base_rot * r.bone_axis;
                                Vector3 dir = (r.target_position - r.self_position).normalized;
                                float aim_dir = Vector3.Angle(aim, dir);
                                float aim_anim = Vector3.Angle(aim, (chain.bones[i].rotation * r.bone_axis));
                                trace_log.write($"tail probe: '{chain.root_transform.name}'[{i}] base {base_rot.x:0.000},{base_rot.y:0.000},{base_rot.z:0.000} axis {r.bone_axis.x:0.000},{r.bone_axis.y:0.000},{r.bone_axis.z:0.000} aim ({aim.x:0.000},{aim.y:0.000},{aim.z:0.000}) dir ({dir.x:0.000},{dir.y:0.000},{dir.z:0.000}) self ({r.self_position.x:0.000},{r.self_position.y:0.000},{r.self_position.z:0.000}) tgt ({r.target_position.x:0.000},{r.target_position.y:0.000},{r.target_position.z:0.000}) aim2dir {aim_dir:0.0} aim2anim {aim_anim:0.0} final2anim {Quaternion.Angle(r.final_rot, chain.bones[i].rotation):0.0} ratio {r.dynamic_ratio:0.000} lim {(int)r.is_limit} st {r.stiffness:0.000} drag {r.drag:0.000} g {r.gravity:0.000}");
                            }
                            if (resolved.TryGetValue(p.id, out var tcols) && tcols != null)
                            {
                                var r0 = chain.records[0];
                                for (int ci = 0; ci < tcols.Length; ci++)
                                {
                                    var tc = tcols[ci];
                                    float dist = Vector3.Distance(r0.target_position, tc.position);
                                    float reach = r0.collision_radius + tc.radius;
                                    trace_log.write($"tail col: '{chain.root_transform.name}' vs [{ci}] type {(int)tc.type} r {tc.radius:0.000} dist {dist:0.000} reach {reach:0.000} colpos {tc.position.x:0.000},{tc.position.y:0.000},{tc.position.z:0.000} tip {r0.target_position.x:0.000},{r0.target_position.y:0.000},{r0.target_position.z:0.000} self {r0.self_position.x:0.000},{r0.self_position.y:0.000},{r0.self_position.z:0.000} root {chain.root_transform.position.x:0.000},{chain.root_transform.position.y:0.000},{chain.root_transform.position.z:0.000}");
                                }
                            }
                        }
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
            // the warm-up burst must not advance the bench sampling gate:
            // record the tick before the burst and roll the counters back
            // after, so cadence evidence only counts live solves.
            int[] ticks = new int[parts.Count];
            for (int i = 0; i < parts.Count; i++) ticks[i] = parts[i].bench_tick;
            for (int i = 0; i < n; i++)
            {
                // the game's WarmUpCySpring runs the full BeginSimulation/
                // EndSimulation path, then the history-reset helper re-zeroes
                // Diff/PrevTargetPosition/Force (register-proven @
                // 0x7ff8e4961d70) so the burst settles the chain instead of
                // accumulating snap velocity from the bind jump.
                begin(dt);
                update(collision_switch: true, dt);
                end();
                reset_history();
            }
            for (int i = 0; i < parts.Count; i++) parts[i].bench_tick = ticks[i];
        }

        // the game binds AFTER placement: CySpringBoneBase.Initialize seeds
        // Target/PrevTarget from the placed pose and freezes InitLocalRotation
        // from it (v1's viewer binds post-placement, so its rest frame IS the
        // start pose). UV2 binds at load, so the start-pose cut must REPLAY
        // the seed: targets re-seeded to the placed pose (zero velocity) and
        // the rest frame re-captured relative to each bone's parent — hair
        // aims down from the tilted start-pose head, the tail behind, no
        // crawl from the stale load-pose state.
        public void reset_to_pose()
        {
            foreach (var p in parts)
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null || chain.bones == null) continue;
                    for (int i = 0; i < chain.records.Length && i < chain.bones.Length; i++)
                    {
                        var bone = chain.bones[i];
                        if (bone == null) continue;
                        var r = chain.records[i];
                        r.target_position = bone.position;
                        r.prev_target = bone.position;
                        r.self_position = bone.position;
                        r.force = Vector3.zero;
                        r.connected_force = Vector3.zero;
                        r.final_rot = bone.rotation;
                        // rest frame re-captured in the chain-parent frame
                        // (bind's algebra): Inverse(parent world) * bone world.
                        // the chain parent is the previous bone's solved
                        // frame, the hierarchy parent for the root — never
                        // raw localRotation (scaffold chains carry the
                        // character-root frame in it).
                        Quaternion parent_world = i > 0 && chain.bones[i - 1] != null
                            ? chain.bones[i - 1].rotation
                            : (bone.parent != null ? bone.parent.rotation : Quaternion.identity);
                        r.anim_local = bone.localRotation;
                        r.init_local = Quaternion.Inverse(parent_world) * bone.rotation;
                        chain.records[i] = r;
                    }

                    // full Initialize replay: the segment axis/distance are
                    // also bind-pose captures (the child writes its parent's
                    // rest direction; the leaf inherits). re-derived from the
                    // placed pose, the aim stops pointing where the stale
                    // load-pose pointed.
                    for (int i = 1; i < chain.records.Length; i++)
                    {
                        if (chain.bones[i] == null || chain.bones[i - 1] == null) continue;
                        Vector3 delta = chain.bones[i].position - chain.bones[i - 1].position;
                        float dist = delta.magnitude;
                        if (dist < 1e-4f) continue;
                        var pr = chain.records[i - 1];
                        Quaternion inv = Quaternion.Inverse(chain.bones[i - 1].rotation);
                        pr.bone_axis = (inv * delta).normalized;
                        pr.bone_distance = dist;
                        chain.records[i - 1] = pr;
                    }
                    int n = chain.records.Length;
                    if (n >= 2)
                    {
                        var leaf = chain.records[n - 1];
                        leaf.bone_axis = chain.records[n - 2].bone_axis;
                        leaf.bone_distance = chain.records[n - 2].bone_distance;
                        chain.records[n - 1] = leaf;
                    }
                    var root_rec = chain.records[0];
                    root_rec.bone_axis = Vector3.up;
                    root_rec.bone_distance = 1f;
                    chain.records[0] = root_rec;
                }
        }

        // tail flip diagnosis: dumps per-bone tail frames during bench runs.
        public static bool tail_probe_enabled;
        public static bool bench_dense;

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

        // armed on spawn/cut: the game's suppression clamps ONLY the root's
        // anchor toward its animated position for one frame (the child verlet
        // state is never rewritten); a one-frame clamp on the anchor then
        // lets the verlet drag the chain over. copying the eased position
        // onto every record collapses the chain to a point that re-extends
        // as a spike — the tail forward-phasing and the hair splayed out.
        void apply_suppression()
        {
            foreach (var p in parts)
                foreach (var chain in p.chains)
                {
                    if (chain?.records == null || chain.root_transform == null) continue;
                    Vector3 anim_world = chain.root_transform.position;
                    Vector3 diff = anim_world - chain.records[0].target_position;
                    float check = suppression_length > 0f ? suppression_length : 1f;
                    if (diff.sqrMagnitude >= check * check)
                    {
                        // v1's UpdateSuppression: one 1cm step toward the
                        // animated pose, root anchor only, both history
                        // slots seeded so no velocity impulse survives.
                        var r = chain.records[0];
                        spring_solver.suppress(ref r, anim_world, 0.01f);
                        chain.records[0] = r;
                    }
                }
            suppression_pending = false;
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
