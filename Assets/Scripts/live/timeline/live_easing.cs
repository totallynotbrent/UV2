using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // the game's 41-entry easing table (LiveTimelineEasing), reconstructed
    // from the interpolation contract: value(t, begin, change, duration).
    public static class live_easing
    {
        // linear when no easing authored.
        public static float linear(float t, float b, float c, float d) => c * t / d + b;

        public static float expo_out(float t, float b, float c, float d) =>
            t >= d ? b + c : (1f - FastPow(2f, t * -10f / d)) * c + b;

        public static float expo_in(float t, float b, float c, float d) =>
            t <= 0f ? b : FastPow(2f, (t / d - 1f) * 10f) * c + b;

        public static float expo_in_out(float t, float b, float c, float d)
        {
            if (t <= 0f) return b;
            if (t >= d) return b + c;
            float t2 = t / (d * 0.5f);
            if (t2 < 1f) return (c * 0.5f) * FastPow(2f, (t2 - 1f) * 10f) + b;
            t2 -= 1f;
            return (c * 0.5f) * (2f - FastPow(2f, t2 * -10f)) + b;
        }

        public static float expo_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? expo_out(t * 2f, b, c / 2f, d) : expo_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float circ_out(float t, float b, float c, float d)
        {
            t = t / d - 1f;
            return c * Mathf.Sqrt(1f - t * t) + b;
        }

        public static float circ_in(float t, float b, float c, float d) =>
            -c * (Mathf.Sqrt(1f - (t /= d) * t) - 1f) + b;

        public static float circ_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return -c / 2f * (Mathf.Sqrt(1f - t * t) - 1f) + b;
            return c / 2f * (Mathf.Sqrt(1f - (t -= 2f) * t) + 1f) + b;
        }

        public static float circ_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? circ_out(t * 2f, b, c / 2f, d) : circ_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float quad_out(float t, float b, float c, float d) =>
            -c * (t /= d) * (t - 2f) + b;

        public static float quad_in(float t, float b, float c, float d) =>
            c * (t /= d) * t + b;

        public static float quad_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return c / 2f * t * t + b;
            return -c / 2f * ((t -= 1f) * (t - 2f) - 1f) + b;
        }

        public static float quad_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? quad_out(t * 2f, b, c / 2f, d) : quad_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float sine_out(float t, float b, float c, float d) =>
            c * Mathf.Sin(t / d * (Mathf.PI / 2f)) + b;

        public static float sine_in(float t, float b, float c, float d) =>
            -c * Mathf.Cos(t / d * (Mathf.PI / 2f)) + c + b;

        public static float sine_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return c / 2f * Mathf.Sin(Mathf.PI * t / 2f) + b;
            return -c / 2f * (Mathf.Cos(Mathf.PI * (t -= 1f) / 2f) - 2f) + b;
        }

        public static float sine_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? sine_out(t * 2f, b, c / 2f, d) : sine_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float cubic_out(float t, float b, float c, float d)
        {
            t = t / d - 1f;
            return c * (t * t * t + 1f) + b;
        }

        public static float cubic_in(float t, float b, float c, float d) =>
            c * (t /= d) * t * t + b;

        public static float cubic_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return c / 2f * t * t * t + b;
            return c / 2f * ((t -= 2f) * t * t + 2f) + b;
        }

        public static float cubic_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? cubic_out(t * 2f, b, c / 2f, d) : cubic_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float quart_out(float t, float b, float c, float d)
        {
            t = t / d - 1f;
            return -c * (t * t * t * t - 1f) + b;
        }

        public static float quart_in(float t, float b, float c, float d) =>
            c * (t /= d) * t * t * t + b;

        public static float quart_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return c / 2f * t * t * t * t + b;
            return -c / 2f * ((t -= 2f) * t * t * t - 2f) + b;
        }

        public static float quart_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? quart_out(t * 2f, b, c / 2f, d) : quart_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float quint_out(float t, float b, float c, float d)
        {
            t = t / d - 1f;
            return c * (t * t * t * t * t + 1f) + b;
        }

        public static float quint_in(float t, float b, float c, float d) =>
            c * (t /= d) * t * t * t * t + b;

        public static float quint_in_out(float t, float b, float c, float d)
        {
            if ((t /= d / 2f) < 1f) return c / 2f * t * t * t * t * t + b;
            return c / 2f * ((t -= 2f) * t * t * t * t + 2f) + b;
        }

        public static float quint_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? quint_out(t * 2f, b, c / 2f, d) : quint_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float elastic_out(float t, float b, float c, float d)
        {
            float t2 = t / d;
            if (t2 >= 1f) return b + c;
            float p = d * 0.3f;
            float s = p * 0.25f;
            return c * FastPow(2f, t2 * -10f) * Mathf.Sin((t2 * d - s) * 6.28318531f / p) + c + b;
        }

        public static float elastic_in(float t, float b, float c, float d)
        {
            float t2 = t / d;
            if (t2 >= 1f) return b + c;
            float p = d * 0.3f;
            float s = p * 0.25f;
            t2 -= 1f;
            return c * FastPow(2f, t2 * 10f) * Mathf.Sin((t2 * d - s) * -6.28318531f / p) + b;
        }

        public static float elastic_in_out(float t, float b, float c, float d)
        {
            float t2 = t / (d * 0.5f);
            if (t2 >= 2f) return b + c;
            float p = d * 0.45f;
            float s = p * 0.25f;
            float t3 = t2 - 1f;
            if (t2 < 1f) return (c * 0.5f) * FastPow(2f, t3 * 10f) * Mathf.Sin((t3 * d - s) * -6.28318531f / p) + b;
            return (c * 0.5f) * FastPow(2f, t3 * -10f) * Mathf.Sin((t3 * d - s) * 6.28318531f / p) + c + b;
        }

        public static float elastic_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? elastic_out(t * 2f, b, c / 2f, d) : elastic_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float bounce_out(float t, float b, float c, float d)
        {
            if ((t /= d) < 0.36363636f) return c * (7.5625f * t * t) + b;
            if (t < 0.72727272f) return c * (7.5625f * (t -= 0.54545454f) * t + 0.75f) + b;
            if (t < 0.9090909f) return c * (7.5625f * (t -= 0.81818181f) * t + 0.9375f) + b;
            return c * (7.5625f * (t -= 21f / 22f) * t + 63f / 64f) + b;
        }

        public static float bounce_in(float t, float b, float c, float d) =>
            c - bounce_out(d - t, 0f, c, d) + b;

        public static float bounce_in_out(float t, float b, float c, float d)
        {
            if (t < d / 2f) return bounce_in(t * 2f, 0f, c, d) * 0.5f + b;
            return bounce_out(t * 2f - d, 0f, c, d) * 0.5f + c * 0.5f + b;
        }

        public static float bounce_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? bounce_out(t * 2f, b, c / 2f, d) : bounce_in(t * 2f - d, b + c / 2f, c / 2f, d);

        public static float back_out(float t, float b, float c, float d)
        {
            t = t / d - 1f;
            return c * (t * t * (2.70158f * t + 1.70158f) + 1f) + b;
        }

        public static float back_in(float t, float b, float c, float d) =>
            c * (t /= d) * t * (2.70158f * t - 1.70158f) + b;

        public static float back_in_out(float t, float b, float c, float d)
        {
            float num = 1.70158f;
            if ((t /= d / 2f) < 1f) return c / 2f * (t * t * ((num * 1.525f + 1f) * t - num * 1.525f)) + b;
            return c / 2f * ((t -= 2f) * t * ((num * 1.525f + 1f) * t + num * 1.525f) + 2f) + b;
        }

        public static float back_out_in(float t, float b, float c, float d) =>
            t < d / 2f ? back_out(t * 2f, b, c / 2f, d) : back_in(t * 2f - d, b + c / 2f, c / 2f, d);

        // the game's easing dispatch: type index into the 41-entry table.
        public static float evaluate(int type, float t, float b, float c, float d)
        {
            switch (type)
            {
                case 0: return linear(t, b, c, d);
                case 1: return expo_out(t, b, c, d);
                case 2: return expo_in(t, b, c, d);
                case 3: return expo_in_out(t, b, c, d);
                case 4: return expo_out_in(t, b, c, d);
                case 5: return circ_out(t, b, c, d);
                case 6: return circ_in(t, b, c, d);
                case 7: return circ_in_out(t, b, c, d);
                case 8: return circ_out_in(t, b, c, d);
                case 9: return quad_out(t, b, c, d);
                case 10: return quad_in(t, b, c, d);
                case 11: return quad_in_out(t, b, c, d);
                case 12: return quad_out_in(t, b, c, d);
                case 13: return sine_out(t, b, c, d);
                case 14: return sine_in(t, b, c, d);
                case 15: return sine_in_out(t, b, c, d);
                case 16: return sine_out_in(t, b, c, d);
                case 17: return cubic_out(t, b, c, d);
                case 18: return cubic_in(t, b, c, d);
                case 19: return cubic_in_out(t, b, c, d);
                case 20: return cubic_out_in(t, b, c, d);
                case 21: return quart_out(t, b, c, d);
                case 22: return quart_in(t, b, c, d);
                case 23: return quart_in_out(t, b, c, d);
                case 24: return quart_out_in(t, b, c, d);
                case 25: return quint_out(t, b, c, d);
                case 26: return quint_in(t, b, c, d);
                case 27: return quint_in_out(t, b, c, d);
                case 28: return quint_out_in(t, b, c, d);
                case 29: return elastic_out(t, b, c, d);
                case 30: return elastic_in(t, b, c, d);
                case 31: return elastic_in_out(t, b, c, d);
                case 32: return elastic_out_in(t, b, c, d);
                case 33: return bounce_out(t, b, c, d);
                case 34: return bounce_in(t, b, c, d);
                case 35: return bounce_in_out(t, b, c, d);
                case 36: return bounce_out_in(t, b, c, d);
                case 37: return back_out(t, b, c, d);
                case 38: return back_in(t, b, c, d);
                case 39: return back_in_out(t, b, c, d);
                case 40: return back_out_in(t, b, c, d);
                default: return linear(t, b, c, d);
            }
        }

        private static float FastPow(float x, float y)
        {
            if (y == 0f) return 1f;
            if (y == 1f) return x;
            return Mathf.Pow(x, y);
        }
    }
}
