using UnityEngine;

namespace UV2.UI
{
    // live load-progress channel: the stage loader reports each phase and
    // the concert window renders the latest line every frame.
    public static class load_progress
    {
        public static string current = "";
        public static float started_at = -1f;
        public static float last_update = -1f;
        private static int _seq;
        private static string _last_key;

        // publishes a phase line; repeated identical keys don't advance the
        // sequence so per-chara progress still ticks via the count.
        public static void report(string key)
        {
            float now = Time.realtimeSinceStartup;
            if (started_at < 0) started_at = now;
            if (_last_key != key) { _seq++; _last_key = key; }
            last_update = now;
            current = key;
        }

        public static string describe()
        {
            if (string.IsNullOrEmpty(current)) return "starting";
            float now = Time.realtimeSinceStartup;
            float elapsed = started_at < 0 ? 0f : now - started_at;
            return $"{current}  ({elapsed:0.0}s)";
        }
    }
}
