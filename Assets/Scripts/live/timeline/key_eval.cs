using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // key evaluation: finds the bracketing keys and blends with the game's
    // interpolation contract - the NEXT key's interpolate_type drives the
    // blend (0 = hold, 2 = linear, 4 = curve, 6 = ease), with the easing
    // table applied on top when authored.
    public static class key_eval
    {
        // the blend the game's CalculateInterpolationValue applies between keys.
        public static float interp(live_key cur, live_key next, float t)
        {
            if (cur == null) return 0f;
            if (next == null) return 0f;

            float span = next.time - cur.time;
            float raw = span <= 0f ? 0f : (t - cur.time) / span;

            // the next key's authored AnimationCurve drives the blend when it
            // carries keyframes (the game's CurveInterpolateKeyframes);
            // linear keys with an empty curve plain-lerp.
            if (next.curve != null && next.curve.Count > 0)
                return evaluate_curve(next.curve, Mathf.Clamp01(raw));

            switch (next.interpolate_type)
            {
                case 2: // linear
                    return Mathf.Clamp01(raw);
                case 4: // curve: a fixed smooth profile stands in when the
                    // authored curve carries no keyframes.
                    return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raw));
                case 6: // ease: the game's 41-entry table
                    return Mathf.Clamp01(live_easing.evaluate(next.easing_type,
                        Mathf.Max(0f, t - cur.time), 0f, 1f, Mathf.Max(0.0001f, span)));
                default:
                    return 0f; // hold
            }
        }

        // the authored curve evaluated at u in 0..1: piecewise-linear through
        // the keyframe values (the game's AnimationCurve.Evaluate shape).
        public static float evaluate_curve(List<curve_key> curve, float u)
        {
            if (curve == null || curve.Count == 0) return u;
            if (curve.Count == 1) return curve[0].value;
            for (int i = 0; i < curve.Count - 1; i++)
            {
                var a = curve[i];
                var b = curve[i + 1];
                if (u <= b.time)
                {
                    float span = b.time - a.time;
                    float local = span <= 0f ? 1f : (u - a.time) / span;
                    return Mathf.Lerp(a.value, b.value, local);
                }
            }
            return curve[curve.Count - 1].value;
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

        // bezier through authored control points: the segment runs cur -> ctrl
        // points -> next; de casteljau over the run.
        public static Vector3 bezier_v3(Vector3 a, Vector3 b, List<Vector3> ctrl, float t)
        {
            if (ctrl == null || ctrl.Count == 0) return Vector3.Lerp(a, b, t);
            var pts = new List<Vector3>(ctrl.Count + 2) { a };
            pts.AddRange(ctrl);
            pts.Add(b);
            int n = pts.Count - 1;
            for (int r = n; r > 0; r--)
                for (int i = 0; i < r; i++)
                    pts[i] = Vector3.Lerp(pts[i], pts[i + 1], t);
            return pts[0];
        }
    }
}
