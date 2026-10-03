using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.App
{
    // writes the whole concert boot trace to uv2_trace.log beside the exe so the
    // load chain is auditable on any machine, gpu or not.
    public static class trace_log
    {
        private static string _path;
        private static float _t0;

        // starts a fresh trace file for this player run.
        public static void open()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            _path = Path.Combine(dir, "uv2_trace.log");
            _t0 = Time.realtimeSinceStartup;
            try
            {
                File.WriteAllText(_path, $"uv2 trace {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                string build = Path.Combine(dir, "build.txt");
                if (File.Exists(build))
                    write($"commit {File.ReadAllText(build).Trim()}");
                write($"unity {Application.unityVersion}, gpu={SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize}mb");
                write($"diagnostics {config.diagnostics}, target fps {config.target_fps}");
                write($"main_path '{config.data_root}'");
            }
            catch { _path = null; }
        }

        // one trace line: to the file and the player log.
        public static void write(string message)
        {
            Debug.Log($"[trace] {message}");
            if (_path == null) return;
            try
            {
                File.AppendAllText(_path, $"[{Time.realtimeSinceStartup - _t0,7:0.00}s] {message}\n");
            }
            catch { }
        }

        // a batch of trace lines in one file append (the diagnostics
        // per-second flush path: one syscall per window, not per line).
        public static void write(List<string> messages)
        {
            if (messages == null || messages.Count == 0) return;
            if (_path == null)
            {
                foreach (var m in messages) Debug.Log($"[trace] {m}");
                return;
            }
            try
            {
                var sb = new System.Text.StringBuilder();
                float t = Time.realtimeSinceStartup - _t0;
                foreach (var m in messages)
                    sb.Append($"[{t,7:0.00}s] {m}\n");
                File.AppendAllText(_path, sb.ToString());
            }
            catch { }
        }
    }
}
