using UnityEngine;

namespace UV2.Live
{
    // the per-frame solver, transcribed from the decoded contract: verlet
    // integrate, hard length snap, collision push-out + re-snap, parent-local
    // euler limit clamp, final rotation write. one pass, no iteration.
    public static class spring_solver
    {
        // solves one chain for one frame. rates are the caller's globals
        // (stiffness/drag/gravity multipliers), step_scale normalizes them,
        // wind is the composed world-space wind force, wind_strength gates it.
        public static void solve(spring_chain chain, int parent_work,
            Vector3 parent_work_pos, Quaternion parent_work_rot,
            float stiffness_rate, float drag_rate, float gravity_rate,
            Vector3 wind, float wind_strength, float step_scale,
            bool collision_switch, spring_collider[] colliders,
            float move_rate, float add_move_rate, float spring_rate)
        {
            var rs = chain.records;
            if (rs == null || rs.Length == 0) return;
            if (rs[0].skip != 0) return;

            // root record feeds from its parent work (the character rig).
            rs[0].self_position = parent_work_pos + parent_work_rot * (Vector3.zero) ;
            rs[0].parent_rot = parent_work_rot;

            float step_div = 1f / Mathf.Max(step_scale, 1e-6f);
            Quaternion prev_parent = parent_work_rot;
            float step_scale_local = step_scale;

            for (int i = 0; i < chain.solve_count; i++)
            {
                if (i > 0)
                {
                    // the driver: this bone's base rides the previous bone's solve.
                    rs[i].self_position = rs[i - 1].target_position;
                    rs[i].parent_rot = rs[i - 1].init_local * rs[i - 1].final_rot;
                }

                step(ref rs[i], stiffness_rate, drag_rate, gravity_rate, wind,
                    wind_strength, step_div, collision_switch, colliders, spring_rate, step_scale);
            }
        }

        // one bone, one step (the contract's 13-step order).
        private static void step(ref spring_record r, float stiffness_rate, float drag_rate,
            float gravity_rate, Vector3 wind, float wind_strength, float step_div,
            bool collision_switch, spring_collider[] colliders, float spring_rate, float step_scale)
        {
            // 1-2. velocity proxy + history shift (diff is the NEGATED displacement)
            Vector3 diff = r.prev_target - r.target_position;
            r.prev_target = r.target_position;

            // 3. rest direction in the current parent frame
            Vector3 aim = r.parent_rot * r.bone_axis;

            // 4-8. force accumulation
            Vector3 force = aim * (r.stiffness * stiffness_rate * spring_rate);
            bool dynamic = r.dynamic_ratio < 1f;
            float drag_scale = dynamic ? r.dynamic_ratio : 1f;
            force += diff * (0.5f * drag_scale) * (r.drag * drag_rate * spring_rate);
            // lag term: pulls the target toward (lag x) the previous position
            float lag = dynamic ? 0.9999f : 3.0f;
            Vector3 lag_pos = r.prev_target * lag;
            force -= 2f * (lag_pos * r.move_rate - r.target_position);
            // gravity: plain -Y in the shipped path, normalized by 10000 and the step scale
            force.y -= r.gravity * gravity_rate * spring_rate / (10000f * Mathf.Max(step_div * step_scale, 1e-6f));
            // wind: per-bone wind bit gates the add (rates are 0 when the bone is windless)
            if (wind_strength > 0f)
            {
                float vr = Mathf.Lerp(r.wind_rates.x, r.wind_rates.y, 0f);
                float hr = Mathf.Lerp(r.wind_rates.z, r.wind_rates.w, 0f);
                force += new Vector3(wind.x * hr, wind.y * vr, wind.z * hr) * wind_strength;
            }
            r.force = force;

            // 9. verlet integrate
            r.target_position = r.target_position - diff + force;

            // 10. hard length snap to the parent's solved position
            Vector3 d = r.target_position - r.self_position;
            if (d.sqrMagnitude > 1e-12f)
                r.target_position = d.normalized * r.bone_distance + r.self_position;

            // 11. collision push-out + re-snap, in index order
            if (collision_switch && r.active_collisions > 0 && colliders != null)
                resolve_collisions(ref r, colliders);

            // 12-13. rotation solve + parent-local limit clamp
            Vector3 solved_dir = r.target_position - r.self_position;
            Quaternion world = spring_math.solve_rotation(r.parent_rot, aim, solved_dir);
            if (r.is_limit != 0)
            {
                Vector3 e = spring_math.round_angle(spring_math.local_euler(r.parent_rot, world));
                e = spring_math.clamp_euler(e, r.limit_min, r.limit_max);
                world = spring_math.apply_limit(r.parent_rot, r.init_local, e);
            }
            r.final_rot = world;
        }

        // sphere/capsule push-out to (radius + collider radius), then re-snap.
        private static void resolve_collisions(ref spring_record r, spring_collider[] colliders)
        {
            for (int c = 0; c < r.active_collisions; c++)
            {
                if (c >= colliders.Length) break;
                var col = colliders[c];
                float reach = r.collision_radius + col.radius;
                Vector3 pos = r.target_position;

                if (col.type == spring_collider_type.sphere)
                {
                    Vector3 away = pos - col.position;
                    float dist = away.magnitude;
                    if (dist < reach && dist > 1e-8f)
                        pos = col.position + away / dist * reach;
                }
                else if (col.type == spring_collider_type.capsule)
                {
                    // closest point on the capsule segment, then the same push-out
                    Vector3 seg = col.position2 - col.position;
                    float len2 = seg.sqrMagnitude;
                    float t = len2 > 1e-10f
                        ? Mathf.Clamp01(Vector3.Dot(pos - col.position, seg) / len2)
                        : 0f;
                    Vector3 closest = col.position + seg * t;
                    Vector3 away = pos - closest;
                    float dist = away.magnitude;
                    if (dist < reach && dist > 1e-8f)
                        pos = closest + away / dist * reach;
                }
                // plane colliders are compiled out in the shipped build: skip.

                r.target_position = pos;
                // re-snap the segment length after every collision
                Vector3 d = pos - r.self_position;
                if (d.sqrMagnitude > 1e-12f)
                    r.target_position = d.normalized * r.bone_distance + r.self_position;
            }
        }

        // seeds both position history slots after a cut/teleport so the chain
        // carries no velocity impulse (the contract's suppression write-back).
        public static void suppress(ref spring_record r, Vector3 anim_world, float threshold_sqr, float step_len)
        {
            Vector3 diff = anim_world - r.target_position;
            float sqr = diff.sqrMagnitude;
            if (sqr < threshold_sqr) return;
            Vector3 step_pos = sqr < 1e-4f ? diff : diff.normalized * step_len;
            Vector3 seeded = r.target_position + step_pos;
            r.target_position = seeded;
            r.prev_target = seeded;
            r.final_rot = r.parent_rot * r.init_local;
        }
    }
}
