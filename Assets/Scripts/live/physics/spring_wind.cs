using UnityEngine;

namespace UV2.Live
{
    // wind composition from the decoded CreateWindControlData +
    // UpdateSpringHasWind: four direction cones built at load from the
    // container's angle/width pairs, two oscillator phases (vertical,
    // horizontal) advanced per solver call by 2pi/cycle.
    //
    // verified composition (per wind-enabled root chain):
    //   t_v = clamp01(sin(v_phase + root_phase_shift) * 0.5 + 0.5)
    //   t_h = clamp01(sin(h_phase + root_phase_shift) * 0.5 + 0.5)
    //   raw = lerp(upper, downer, t_v) + lerp(right, left, t_h)
    //   n = normalize(raw)
    //   horiz = normalize(n.x, 0, n.z)
    //   wind = (horiz.x * power * 0.005,        <- HORIZONTAL rate on x/z
    //           n.y * power * 0.015,            <- VERTICAL rate on y
    //           horiz.z * power * 0.005)
    //   wind rotated by the root parent's world rotation; per-bone wind rates
    //   gate the add inside the solver.
    public class spring_wind
    {
        public Vector3 dir_upper, dir_downer;      // vertical cone pair
        public Vector3 dir_right, dir_left;        // horizontal cone pair
        public float cycle_vertical;               // seconds per vertical cycle
        public float cycle_horizontal;
        public float power_scale = 1f;             // per-part windPowerScale
        public bool enable_vertical = true;
        public bool enable_horizontal = true;

        // phase state (one shared accumulator + per-root shift)
        float v_phase, h_phase;

        public void set_phases(float vertical, float horizontal)
        {
            v_phase = vertical;
            h_phase = horizontal;
        }

        // advances both phases for one solver call (omega = 2pi/cycle).
        public void advance(float dt)
        {
            v_phase += dt * (2f * Mathf.PI) / Mathf.Max(cycle_vertical, 1e-6f);
            h_phase += dt * (2f * Mathf.PI) / Mathf.Max(cycle_horizontal, 1e-6f);
        }

        // composes the pre-rotation wind vector for ONE root chain (the caller
        // applies the root-parent rotation and passes it to the solver).
        // the game's CalculateWind returns a WORLD vector: the direction cones
        // bake the root's local rotation at build time (is_local_direction);
        // no per-frame rotation rides the character's spin.
        public Vector3 compose(float root_phase_shift, Quaternion parent_rot)
        {
            // the game sums two independently-normalized cone directions,
            // each with its own constant: the VERTICAL cone rides 0.005 and
            // the HORIZONTAL cone 0.015 (CalculateWind, doc Q5). the old
            // normalize-the-sum-then-split version collapsed both cones
            // into one vector and misattached the constants to axes.
            Vector3 w = Vector3.zero;
            if (enable_vertical)
            {
                float t = Mathf.Clamp01(Mathf.Sin(v_phase + root_phase_shift) * 0.5f + 0.5f);
                Vector3 dv = Vector3.Lerp(dir_upper, dir_downer, t);
                if (dv.sqrMagnitude > 1e-10f)
                    w += dv.normalized * (power_scale * 0.005f);
            }
            if (enable_horizontal)
            {
                float t = Mathf.Clamp01(Mathf.Sin(h_phase + root_phase_shift) * 0.5f + 0.5f);
                Vector3 dh = Vector3.Lerp(dir_right, dir_left, t);
                if (dh.sqrMagnitude > 1e-10f)
                    w += dh.normalized * (power_scale * 0.015f);
            }
            return parent_rot * w;
        }
    }
}
