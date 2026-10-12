using UnityEngine;

namespace UV2.Live
{
    // the per-frame solver, transcribed from the decoded contract: verlet
    // integrate, hard length snap, collision push-out + re-snap, parent-local
    // euler limit clamp, final rotation write. one pass, no iteration.
    //
    // the verified 13-step per-bone order:
    //   diff = prev - target (NEGATED velocity); prev = target
    //   aim  = parent_rot * bone_axis
    //   force  = aim * stiffness * stiffnessRate * groupRate
    //   force += diff * 0.5 * drag * dragRate * groupRate   (diff already halved)
    //   force += connected_force (accumulated between steps)
    //   force.y -= gravity * gravityRate / (10000 * stepScale)
    //   force += wind * per-bone wind bit
    //   target = target - diff + force                      (verlet)
    //   d = normalize(target - self); target = d * boneDistance + self  (hard snap)
    //   collisions: push-out + re-snap, index order, sphere|capsule only
    //   rot = FromToRotation(aim, d) * parent_rot
    //   euler = RoundAngle(ToEuler(Inverse(parent_rot) * rot)), clamp if isLimit
    //   final = FromEuler(clamped * 30 * deg2rad) * parent_rot
    //
    // the driver couples records: self[i] = target[i-1], parent_rot[i] =
    // init_local[i-1] * final[i-1]. force is rebuilt fresh every step.
    public static class spring_solver
    {
        // solves one chain for one frame. rates are the caller's globals
        // (stiffness/drag/gravity multipliers) and the per-part group rate;
        // step_scale normalizes the fixed-step constants. the root record
        // seeds from its own animated world position (GatherRootSpring).
        // hip_rate < 0 disables the hip-move damper for the part.
        public static void solve(spring_chain chain,
            Vector3 root_anim_pos, Quaternion parent_work_rot,
            float stiffness_rate, float drag_rate, float gravity_rate,
            Vector3 wind, float wind_strength, float step_scale,
            bool collision_switch, spring_collider[] colliders,
            float spring_rate, float hip_rate)
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
                    // solve; its parent frame = the previous bone's solved
                    // world rotation (the bone it actually hangs from).
                    rs[i].self_position = rs[i - 1].target_position;
                    rs[i].parent_rot = rs[i - 1].final_rot;
                }

                step(ref rs[i], stiffness_rate, drag_rate, gravity_rate, wind,
                    wind_strength, step_scale, collision_switch, colliders, spring_rate, hip_rate);
            }
        }

        // one bone, one step.
        private static void step(ref spring_record r, float stiffness_rate, float drag_rate,
            float gravity_rate, Vector3 wind, float wind_strength, float step_scale,
            bool collision_switch, spring_collider[] colliders, float spring_rate, float hip_rate)
        {
            // 1-2. velocity proxy + history shift (diff is the NEGATED displacement)
            Vector3 diff = r.prev_target - r.target_position;
            r.prev_target = r.target_position;

            // 3. rest direction in the current parent frame
            Vector3 aim = r.parent_rot * r.bone_axis;

            // 4-8. force accumulation (rebuilt fresh every step)
            Vector3 force = aim * (r.stiffness * stiffness_rate * spring_rate);
            bool dynamic = r.dynamic_ratio != 1f;
            // drag: the displacement fed into the force is halved when the bone
            // has no dynamic ratio; the dynamic-ratio path scales it instead.
            float drag_scale = dynamic ? r.dynamic_ratio : 0.5f;
            force += diff * (drag_scale * r.drag * drag_rate * spring_rate);
            // connected-bone force accumulated between steps
            force += r.connected_force;
            // lag term: pulls the integrated position toward (lag x) the previous
            // position; lag = 3.0 (normal) or 0.9999 x MoveSpringApplyRate (dynamic).
            float lag = dynamic ? 0.9999f * r.move_rate : 3.0f;
            // hip-move damper (InternalMono @ 0x7ff8e49a8c70): the frame's hip
            // rate blends the damping toward the rest line; hip_rate < 0 = the
            // part carries no hip params, the blend is skipped entirely.
            if (hip_rate >= 0f)
            {
                float hip_blend = (1f - r.move_rate) * Mathf.Clamp01(hip_rate) + r.move_rate;
                lag *= hip_blend;
            }
            force -= 2f * (r.prev_target * lag - r.target_position);
            // gravity: plain -Y in the shipped path, /10000 and /stepScale
            force.y -= r.gravity * gravity_rate * spring_rate / (10000f * Mathf.Max(step_scale, 1e-6f));
            // wind: the per-bone wind rate gates the add (rate 0 = windless bone)
            if (wind_strength > 0f)
            {
                float vr = r.wind_rates.x != 0f || r.wind_rates.y != 0f ? 1f : 0f;
                float hr = r.wind_rates.z != 0f || r.wind_rates.w != 0f ? 1f : 0f;
                force += wind * (vr + hr > 0f ? 1f : 0f) * wind_strength;
            }
            r.force = force;

            // 9. verlet integrate
            r.target_position = r.target_position - diff + force;

            // 10. hard length snap to the parent's solved position
            Vector3 d = r.target_position - r.self_position;
            float d_len = d.magnitude;
            if (d_len > 1e-8f)
            {
                d /= d_len;
                r.target_position = d * r.bone_distance + r.self_position;
            }
            else
            {
                d = aim;
            }

            // 11. collision push-out + re-snap, in index order
            if (collision_switch && r.check_chara_collision != 0 &&
                r.active_collisions > 0 && colliders != null)
                resolve_collisions(ref r, colliders, d);

            // 12-13. rotation solve + parent-local limit clamp
            Vector3 solved_dir = (r.target_position - r.self_position).normalized;
            Quaternion world = Quaternion.FromToRotation(aim, solved_dir) * r.parent_rot;
            // the euler round-trip applies to every bone: wrap to ±180, clamp
            // under isLimit, then rebuild with the x30 degree time constant
            // (a per-step rotation damper at the game's fixed 60fps).
            Vector3 e = spring_math.round_angle(
                (Quaternion.Inverse(r.parent_rot) * world).eulerAngles);
            if (r.is_limit != 0)
                e = spring_math.clamp_euler(e, r.limit_min, r.limit_max);
            world = Quaternion.Euler(e * (30f * Mathf.Deg2Rad)) * r.parent_rot;
            r.final_rot = world;

            // the dynamic-ratio blend (InternalMono tail @ 0x7ff8e49a8c70):
            // a bone whose DynamicRatio < 1 lerps its local rotation toward the
            // animation's own pose by the ratio — partial animation tracking on
            // top of the spring solve (tail chains, bust).
            if (r.dynamic_ratio < 1f && r.dynamic_ratio != 1f)
                r.final_rot = Quaternion.Lerp(r.final_rot,
                    r.anim_local * r.parent_rot, r.dynamic_ratio);
        }

        // sphere/capsule push-out to (radius + collider radius), then re-snap.
        // type 1 (plane) is compiled out in the shipped build: skipped here.
        private static void resolve_collisions(ref spring_record r, spring_collider[] colliders, Vector3 seg_dir)
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
                    // closest point on the segment (0 <= t < length), then the
                    // same push-out; end caps fall out of the clamped projection.
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
