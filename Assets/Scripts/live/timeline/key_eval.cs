using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // key evaluation: finds the bracketing keys on a track and blends between them
    // with the worksheet's standard interpolation (linear per-component lerp).
    public static class key_eval
    {
        // the blend the game's CalculateInterpolationValue applies between keys.
        public static float interp(live_key cur, live_key next, float t)
        {
            if (cur == null) return 0f;
            if (next == null) return 1f;
            if (cur.easing_type != 0) return t; // easing types decoded later; linear for now
            return Mathf.Clamp01(t);
        }

        // index of the last key at or before time t; -1 when t precedes the first key.
        public static int bracket<T>(List<T> keys, float t) where T : live_key
        {
            if (keys == null || keys.Count == 0) return -1;
            int lo = 0, hi = keys.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (keys[mid].time <= t) lo = mid;
                else hi = mid - 1;
            }
            return keys[lo].time <= t ? lo : -1;
        }

        // the normalized position of t inside the bracketing pair.
        public static float span_t(live_key cur, live_key next, float t)
        {
            if (cur == null) return 0f;
            if (next == null) return 1f;
            float len = next.time - cur.time;
            if (len <= 0f) return 1f;
            return Mathf.Clamp01((t - cur.time) / len);
        }

        // per-component lerp helper for vector tracks.
        public static Vector3 lerp_v3(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t);

        public static float lerp_f(float a, float b, float t) => Mathf.Lerp(a, b, t);
    }
}
