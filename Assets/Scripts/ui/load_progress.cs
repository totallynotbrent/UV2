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
        // the mark the smooth bar is chasing: the loader's phases stall for
        // whole seconds (cast load ~2s/chara) and a bar that only repaints on
        // the marks looks frozen between them, so the rendered value creeps
        // toward the mark every frame.
        public static float render_fraction;
        // the last four phase lines, oldest first — the loading screen
        // shows the trail so slow steps are visible as they happen.
        static readonly System.Collections.Generic.List<string> trail = new();
        static string _last_key;

        // true while the summary/loading ui is on screen. the capture gate
        // waits for it to clear: the seek can fire a beat before the window
        // tears its canvas down, and a capture taken then photographs the
        // loading screen instead of the stage.
        public static bool ui_active;
        // realtime stamp of the teardown; captures wait a beat past it so
        // the canvas is really gone from the framebuffer.
        public static float ui_hidden_at = -1f;

        public static bool capture_clear(float now)
        {
            if (ui_active) return false;
            if (ui_hidden_at < 0f) return true;
            return now - ui_hidden_at > 0.3f;
        }

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

        // the value the bar renders: creeps toward the loader's mark on a
        // linear schedule that always lands within a few seconds, so the bar
        // is visibly moving through the multi-second stalls (cast load
        // ~2s/chara). proportional-only easing was exponential decay — 8% of
        // the remaining gap per second never got anywhere and the bar sat
        // visually frozen on slow machines.
        public static float bar_fraction()
        {
            if (render_fraction < 0f) render_fraction = 0f;
            float gap = fraction - render_fraction;
            if (gap > 0f)
            {
                // cover whatever gap exists in about 4 seconds, never
                // overshooting the mark; a floor keeps tiny gaps crawling
                // visibly instead of snapping.
                float rate = Mathf.Max(gap / 4f, 0.005f);
                render_fraction = Mathf.Min(fraction, render_fraction + rate * Time.deltaTime);
            }
            return render_fraction;
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
            render_fraction = 0f;
            trail.Clear();
            _last_key = null;
        }
    }
}

