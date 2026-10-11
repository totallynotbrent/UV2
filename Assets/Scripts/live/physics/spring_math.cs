using UnityEngine;

namespace UV2.Live
{
    // math helpers for the spring solver, transcribed from the decoded
    // contract: angle wrap, the x30 degree time constant, and
    // parent-local euler clamping.
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

        // clamps each euler axis to [-min, max] then wraps; the limits are the
        // serialized _limitAngleMin/Max magnitudes applied about rest.
        public static Vector3 clamp_euler(Vector3 e, Vector3 min, Vector3 max)
        {
            return round_angle(new Vector3(
                Mathf.Clamp(e.x, -min.x, max.x),
                Mathf.Clamp(e.y, -min.y, max.y),
                Mathf.Clamp(e.z, -min.z, max.z)));
        }
    }
}
