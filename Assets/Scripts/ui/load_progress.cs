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

        // fraction of the open sequence complete (0..1), driven by the
        // loader's phase marks; the bar renders this.
        public static float fraction;
        // the last four phase lines, oldest first — the loading screen
        // shows the trail so slow steps are visible as they happen.
        static readonly System.Collections.Generic.List<string> trail = new();
        static string _last_key;

        // phase marks: each report carries an optional fraction estimate.
        public static void report(string key, float at = -1f)
        {
            float now = Time.realtimeSinceStartup;
            if (started_at < 0) started_at = now;
            if (_last_key != key)
            {
                trail.Add(key);
                if (trail.Count > 4) trail.RemoveAt(0);
                _last_key = key;
            }
            if (at >= 0f) fraction = at;
            else fraction = Mathf.Min(1f, fraction + 0.05f);
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

        // the phase trail joined for the loading screen's detail lines.
        public static string describe_trail()
        {
            if (trail.Count == 0) return "";
            return string.Join("\n", trail);
        }

        public static void reset()
        {
            current = "";
            started_at = -1f;
            last_update = -1f;
            fraction = 0f;
            trail.Clear();
            _last_key = null;
        }
    }
}

