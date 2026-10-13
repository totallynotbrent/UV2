using UnityEngine;

namespace UV2.Live
{
    // the per-frame solver, transcribed from the shipped solver's native
    // path with every constant register-verified (UpdateNativeClothInternalMono
    // 0x7ff8e49a8c70, UpdateForce 0x7ff8e49a8a80, CollisionDetect
    // 0x7ff8e49a7630, CySpringController's update order).
    //
    // per-bone order (timescale 1.0, one step per rendered frame):
    //   base   = parent_now * init_local            (the animated rest frame)
    //   aim    = base * bone_axis                   (rest line direction)
    //   diff   = prev_target - target               (halved when !is60fps)
    //   force  = aim * (stiffRate*stiffness/1000/30)
    //   force += diff * (dragRate*drag/100/30)
    //   force.y -= gravityRate*gravity/10000/30
    //   force += wind lerped by per-bone slow/fast slot pairs
    //   force += connected/30
    //   new    = target - diff*ts + force*pool*ts   (pool 1.5@60fps, 3.0 else)
    //   hip blend: ((1-msa)*clamp(hip)+msa)*springRate, sqrt@60fps;
    //             blend<1 lerps new toward the rest line (anti-fling)
    //   snap to self at bone_distance; collisions; rotation write:
    //   final  = FromToRotation(aim, dir) * base
    //   limit: euler delta vs base, wrap, clamp, base*Euler(clamped)
    //   dynamic_ratio<1: init_local lerps toward the animated pose
    //   target re-projected through final for the next bone's anchor
    public static class spring_solver
    {
        // the binary's force-term divisors (.rdata)
        const float STIFFNESS_DIV = 1000f;
        const float DRAG_DIV = 100f;
        const float GRAVITY_DIV = 10000f;
        // the constant scale arg InternalMono passes to UpdateForce
        const float FORCE_SCALE = 30f;
        // the force pool: 1.5 in 60fps mode, doubled otherwise
        const float POOL_60 = 1.5f;
        const float POOL_OTHER = 3.0f;
        // non-60fps mode halves the diff (velocity) term
        const float DIFF_SCALE_NON_60 = 0.5f;

        // solves one chain for one frame. rates are the caller's globals
        // (stiffness/drag/gravity multipliers), the per-part group rate,
        // and the frame's hip-move rate (< 0 disables the damper). the root
        // record seeds from its own animated transform (GatherRootSpring).
        public static void solve(spring_chain chain,
            Vector3 root_anim_pos, Quaternion parent_work_rot,
            float stiffness_rate, float drag_rate, float gravity_rate,
            Vector3 wind, float wind_power, float timescale,
            bool is_60fps, bool collision_switch, spring_collider[] colliders,
            float spring_rate, float hip_rate, float add_hip_rate)
        {
            var rs = chain.records;
            if (rs == null || rs.Length == 0) return;
            if (rs[0].skip != 0) return;

            // root record feeds from its own animated transform.
            rs[0].self_position = root_anim_pos;
            rs[0].parent_rot = parent_work_rot;

            for (int i = 0; i < chain.solve_count; i++)
            {
                if (i > 0)
                {
                    // the driver: this bone's base rides the previous bone's
                    // solved position; its parent frame = the previous
                    // bone's solved world rotation.
                    rs[i].self_position = rs[i - 1].target_position;
                    rs[i].parent_rot = rs[i - 1].final_rot;
                }

                step(ref rs[i], stiffness_rate, drag_rate, gravity_rate, wind,
                    wind_power, timescale, is_60fps, collision_switch, colliders,
                    spring_rate, hip_rate, add_hip_rate);
            }
        }

        // one bone, one step.
        private static void step(ref spring_record r, float stiffness_rate, float drag_rate,
            float gravity_rate, Vector3 wind, float wind_power, float timescale,
            bool is_60fps, bool collision_switch, spring_collider[] colliders,
            float spring_rate, float hip_rate, float add_hip_rate)
        {
            // the animated rest frame: the owner bone's bind local pose under
            // the parent's current world rotation, carrying the child's bind
            // offset into the current frame (the binary's aim quaternion).
            Quaternion base_rot = r.parent_rot * r.init_local;
            Vector3 aim = base_rot * r.bone_axis;

            // velocity proxy + history shift; non-60fps mode halves it.
            Vector3 diff = r.prev_target - r.target_position;
            if (!is_60fps) diff *= DIFF_SCALE_NON_60;
            r.prev_target = r.target_position;

            // UpdateForce: rebuilt fresh every step, all terms carry the
            // serialized divisors and the constant scale of 30.
            float stiff_f = stiffness_rate * r.stiffness / (STIFFNESS_DIV * FORCE_SCALE);
            Vector3 force = aim * stiff_f;
            float drag_f = drag_rate * r.drag / (DRAG_DIV * FORCE_SCALE);
            force += diff * drag_f;
            force.y -= gravity_rate * r.gravity / (GRAVITY_DIV * FORCE_SCALE);

            // wind: per-bone slow/fast slot pairs lerped by the clamped
            // power; the vertical pair drives y, the horizontal pair x/z.
            float wp = Mathf.Clamp01(wind_power);
            float v_rate = Mathf.Lerp(r.wind_rates.x, r.wind_rates.y, wp);
            float h_rate = Mathf.Lerp(r.wind_rates.z, r.wind_rates.w, wp);
            force.x += h_rate * wind.x;
            force.y += v_rate * wind.y;
            force.z += h_rate * wind.z;

            // connected-bone force accumulated between steps
            force += r.connected_force / FORCE_SCALE;
            r.force = force;

            // the verlet rebuild (timescale 1.0 in the game's live path)
            float pool = is_60fps ? POOL_60 : POOL_OTHER;
            Vector3 new_target = r.target_position - diff * timescale
                + force * (pool * timescale);

            // hip-move damper: the frame's hip rate (add-spring chains take
            // the add rate) blends through the move-spring apply rate into
            // the group rate; 60fps mode takes the square root; a result
            // under 1 pulls the target toward the rest line (anti-fling).
            float sel = r.is_add_spring != 0 ? add_hip_rate : hip_rate;
            if (sel >= 0f)
            {
                float clamped = Mathf.Clamp01(sel);
                float blend = (1f - r.move_rate) * clamped + r.move_rate;
                blend *= spring_rate;
                if (is_60fps) blend = blend >= 0f ? Mathf.Sqrt(blend) : 0f;
                if (blend < 1f)
                {
                    float t = Mathf.Clamp(blend, 0f, 1f);
                    Vector3 rest_line = r.self_position + aim * r.bone_distance;
                    new_target.x = (new_target.x - rest_line.x) * t + rest_line.x;
                    new_target.y = (new_target.y - rest_line.y) * t + rest_line.y;
                    new_target.z = (new_target.z - rest_line.z) * t + rest_line.z;
                }
            }

            // hard length snap (register-proven step 10): the verlet target is
            // re-anchored to the parent's solved position at exactly
            // InitBoneDistance along the integrated direction. this is how
            // root motion transports the chain — without it the targets stay
            // world-anchored while the character dances away, and the
            // (target - self) direction the rotation reads swings wildly.
            Vector3 to_target = new_target - r.self_position;
            if (to_target.sqrMagnitude > 1e-12f)
                new_target = to_target.normalized * r.bone_distance + r.self_position;
            r.target_position = new_target;

            // collision push-out + re-snap, in index order
            if (collision_switch && r.check_chara_collision != 0 &&
                r.active_collisions > 0 && colliders != null)
                resolve_collisions(ref r, colliders);

            // the rotation write: FromToRotation maps the rest direction
            // onto the solved direction, applied over the base frame.
            Vector3 solved_dir = (r.target_position - r.self_position).normalized;
            Quaternion final = Quaternion.FromToRotation(aim, solved_dir) * base_rot;

            // limit clamp: the euler delta from the base pose, wrapped to
            // (-180,180], clamped per axis against min/max, rebuilt as the
            // clamped delta applied OVER the base frame (FromEuler(e) *
            // ParentRotation, the binary's order — not base * euler).
            if (r.is_limit != 0)
            {
                Quaternion relative = final * Quaternion.Inverse(base_rot);
                Vector3 e = wrap_euler(relative.eulerAngles);
                e.x = Mathf.Clamp(e.x, -r.limit_min.x, r.limit_max.x);
                e.y = Mathf.Clamp(e.y, -r.limit_min.y, r.limit_max.y);
                e.z = Mathf.Clamp(e.z, -r.limit_min.z, r.limit_max.z);
                final = Quaternion.Euler(e) * base_rot;
            }
            r.final_rot = final;

            // the dynamic-ratio relax: a bone whose ratio sits under 1 lerps
            // its rest local rotation toward the animation's own pose by the
            // ratio, so the rest line chases the animation with lag (the
            // tail/bust follow-through; the binary mutates InitLocalRotation).
            if (r.dynamic_ratio < 1f && r.dynamic_ratio != 1f)
                r.init_local = Quaternion.Lerp(r.init_local,
                    r.anim_local, r.dynamic_ratio);
            // the binary leaves TargetPosition untouched here — the verlet
            // state (with the step-10 snap) is the carried position state,
            // so the next bone anchors on it as-is.
        }

        // to-euler in [0,360) wrapped to (-180,180] (MakePositive + the
        // binary's `if (180 < v) v -= 360` step)
        static Vector3 wrap_euler(Vector3 e)
        {
            if (e.x > 180f) e.x -= 360f;
            if (e.y > 180f) e.y -= 360f;
            if (e.z > 180f) e.z -= 360f;
            return e;
        }

        // sphere/capsule push-out to (radius + collider radius), then a
        // re-snap of the segment length. type 1 (plane) is compiled out in
        // the shipped build: skipped here.
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
                    if (dist <= reach && dist > 1e-8f)
                        pos = col.position + away / dist * reach;
                }
                else if (col.type == spring_collider_type.capsule)
                {
                    // closest point on the segment, then the same push-out;
                    // end caps fall out of the clamped projection.
                    Vector3 seg = col.position2 - col.position;
                    float len2 = seg.sqrMagnitude;
                    float t = len2 > 1e-10f
                        ? Mathf.Clamp01(Vector3.Dot(pos - col.position, seg) / len2)
                        : 0f;
                    Vector3 closest = col.position + seg * t;
                    Vector3 away = pos - closest;
                    float dist = away.magnitude;
                    if (dist <= reach && dist > 1e-8f)
                        pos = closest + away / dist * reach;
                }

                r.target_position = pos;
                // re-snap the segment length after every collision
                Vector3 d2 = pos - r.self_position;
                if (d2.sqrMagnitude > 1e-12f)
                    r.target_position = d2.normalized * r.bone_distance + r.self_position;
            }
        }

        // seeds both position history slots after a cut/teleport so the chain
        // carries no velocity impulse (the contract's suppression write-back).
        // step is 1cm toward the animation pose; both verlet slots are seeded.
        public static void suppress(ref spring_record r, Vector3 anim_world, float step_len)
        {
            Vector3 diff = anim_world - r.target_position;
            float sqr = diff.sqrMagnitude;
            if (sqr < 1e-4f)
            {
                // degenerate guard: use the raw diff, skip the normalize
                r.target_position += diff;
            }
            else
            {
                r.target_position += diff.normalized * step_len;
            }
            r.prev_target = r.target_position;
        }
    }
}
