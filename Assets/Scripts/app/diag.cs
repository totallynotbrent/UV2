using System.Collections.Generic;
using UnityEngine;

namespace UV2.App
{
    // bench harness: per-subsystem timing, an fps readout, and a buffered trace; a no-op unless config.diagnostics is on.
    public static class diag
    {
        private class sample
        {
            public double ms;
        }

        private static readonly Dictionary<string, double> _accum = new();
        private static readonly List<KeyValuePair<string, double>> _sorted = new();
        private static string _current;
        private static double _current_ms;

        // rolling fps: frames + time over the last report window.
        private static int _frames;
        private static float _window_start;
        private static float _last_report = -10f;

        // buffered lines: flushed once per report window.
        private static readonly List<string> _lines = new();

        // extra verbose state the bench wants in the per-second line.
        public static string extra_state = "";

        public static bool enabled => config.diagnostics;

        // call at the top of Update to keep the frame counters honest.
        public static void frame_begin()
        {
            if (!enabled) return;
            _frames++;
            if (_window_start <= 0f) _window_start = Time.unscaledTime;
        }

        public static void begin(string name)
        {
            if (!enabled) return;
            flush_current();
            _current = name;
            _current_ms = Time.realtimeSinceStartupAsDouble;
        }

        // ends the current subsystem pass and accumulates its ms.
        public static void end(string name)
        {
            if (!enabled || _current != name) return;
            double ms = (Time.realtimeSinceStartupAsDouble - _current_ms) * 1000.0;
            _accum[name] = (_accum.TryGetValue(name, out var acc) ? acc : 0.0) + ms;
            _current = null;
        }

        private static void flush_current()
        {
            _current = null;
        }

        // once per report window (1s): fps + per-subsystem ms share.
        public static void report(float now)
        {
            if (!enabled) return;
            if (now - _last_report < 1f) return;
            _last_report = now;

            float window = Time.unscaledTime - _window_start;
            float fps = window > 0f ? _frames / window : 0f;
            float ms = window > 0f ? window * 1000f / Mathf.Max(1, _frames) : 0f;

            _sorted.Clear();
            foreach (var kv in _accum) _sorted.Add(kv);
            _sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            double total_logged = 0.0;
            foreach (var kv in _sorted) total_logged += kv.Value;
            double frames = Mathf.Max(1, _frames);

            var parts = new List<string>();
            foreach (var kv in _sorted)
            {
                double avg = kv.Value / frames;
                double share = total_logged > 0 ? kv.Value / total_logged * 100.0 : 0.0;
                parts.Add($"{kv.Key} {avg:0.00}ms({share:0}%)");
            }
            string breakdown = string.Join(" ", parts);
            string line = $"perf fps {fps:0.0} frame {ms:0.00}ms logged {total_logged / frames:0.00}ms";
            if (parts.Count > 0) line += $" | {breakdown}";
            if (!string.IsNullOrEmpty(extra_state)) line += $" | {extra_state}";
            _lines.Add(line);

            trace_log.write(_lines);
            _lines.Clear();
            _accum.Clear();
            _frames = 0;
            _window_start = Time.unscaledTime;
        }

        // appends to the buffered verbose trace (flushed with the report).
        public static void log(string message)
        {
            if (!enabled) return;
            _lines.Add(message);
        }

        // writes a verbose line straight to the trace, bypassing the diagnostics gate so boot lines always land.
        public static void event_line(string message)
        {
            trace_log.write(message);
        }
    }
}
