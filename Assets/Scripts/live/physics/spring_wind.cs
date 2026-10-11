using UnityEngine;

namespace UV2.Live
{
    // wind composition from the decoded CreateWindControlData: four direction
    // cones (slow/fast pairs, vertical + horizontal), phase-driven weights.
    // phases advance per solver call (not wall clock), by 2pi / cycle.
    public class spring_wind
    {
        public float cycle_slow;           // seconds per slow cycle
        public float cycle_fast;
        public Vector3 dir_v_slow, dir_v_fast;   // vertical cones
        public Vector3 dir_h_slow, dir_h_fast;   // horizontal cones
        public float amplitude_v = 0.015f;       // decoded default amplitudes
        public float amplitude_h = 0.005f;
        public float power_rate = 1f;            // per-part windPowerRate (0..1)

        // phase state, per character
        float phase_v_slow, phase_v_fast, phase_h_slow, phase_h_fast;

        public void set_phases(float v_slow, float v_fast, float h_slow, float h_fast)
        {
            phase_v_slow = v_slow; phase_v_fast = v_fast;
            phase_h_slow = h_slow; phase_h_fast = h_fast;
        }

        // advances every phase by 2pi/cycle for one solver call.
        public void advance()
        {
            phase_v_slow += 2f * Mathf.PI / Mathf.Max(cycle_slow, 1e-4f);
            phase_v_fast += 2f * Mathf.PI / Mathf.Max(cycle_fast, 1e-4f);
            phase_h_slow += 2f * Mathf.PI / Mathf.Max(cycle_slow * 1.13f, 1e-4f);
            phase_h_fast += 2f * Mathf.PI / Mathf.Max(cycle_fast * 1.13f, 1e-4f);
        }

        // composes the world-space wind vector for this call; the caller passes
        // it to every chain. weights: sin(phase)*0.5+0.5 lerped slow->fast.
        public Vector3 compose()
        {
            float wv = Mathf.Lerp(weight(phase_v_slow), weight(phase_v_fast), 0.5f);
            float wh = Mathf.Lerp(weight(phase_h_slow), weight(phase_h_fast), 0.5f);
            Vector3 v = Vector3.Slerp(dir_v_slow, dir_v_fast, wv) * (amplitude_v * wv);
            Vector3 h = Vector3.Slerp(dir_h_slow, dir_h_fast, wh) * (amplitude_h * wh);
            return (v + h) * Mathf.Clamp01(power_rate);
        }

        private static float weight(float phase)
        {
            return Mathf.Sin(phase) * 0.5f + 0.5f;
        }
    }
}
