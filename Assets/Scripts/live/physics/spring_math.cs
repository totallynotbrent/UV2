using UnityEngine;

namespace UV2.Live
{
    // math helpers for the spring solver, transcribed from the decoded
    // contract (umadump decode doc, builder-facing section): angle wrap,
    // the x30 degree time constant, and parent-local euler clamping.
    public static class spring_math
    {
        // wraps degrees to (-180, 180] like the solver's RoundAngle.
        public static float round_angle(float deg)
        {
            if (deg > 180f) deg -= 360f * Mathf.Ceil((deg - 180f) / 360f);
            else if (deg <= -180f) deg += 360f * Mathf.Ceil((-180f - deg) / 360f);
            return deg;
        }

        public static Vector3 round_angle(Vector3 e)
        {
            return new Vector3(round_angle(e.x), round_angle(e.y), round_angle(e.z));
        }

        // clamps each euler axis to [-min, max] then wraps; min/max are the
        // serialized limit magnitudes applied symmetrically about rest.
        public static Vector3 clamp_euler(Vector3 e, Vector3 min, Vector3 max)
        {
            return round_angle(new Vector3(
                Mathf.Clamp(e.x, -min.x, max.x),
                Mathf.Clamp(e.y, -min.y, max.y),
                Mathf.Clamp(e.z, -min.z, max.z)));
        }

        // the world rotation a bone gets when its solved direction is d:
        // FromToRotation(aim, d) composed over the parent's world rotation.
        public static Quaternion solve_rotation(Quaternion parent_rot, Vector3 aim, Vector3 d)
        {
            if (d.sqrMagnitude < 1e-12f) return parent_rot;
            return Quaternion.FromToRotation(aim, d.normalized) * parent_rot;
        }

        // parent-local euler of a solved world rotation (the clamp input).
        public static Vector3 local_euler(Quaternion parent_rot, Quaternion world)
        {
            return (Quaternion.Inverse(parent_rot) * world).eulerAngles;
        }

        // rebuilds the final world rotation from clamped parent-local euler:
        // FromEuler(clamped * 30 * deg2rad) applied over the parent frame.
        public static Quaternion apply_limit(Quaternion parent_rot, Quaternion init_local, Vector3 clamped_euler)
        {
            Vector3 rad = clamped_euler * (30f * Mathf.Deg2Rad);
            return parent_rot * (init_local * Quaternion.Euler(rad));
        }
    }
}
