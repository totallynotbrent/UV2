using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.Live
{
    // reads a small key=value file for calibration runs, so the bench can
    // vary one term per render without rebuilding the player.
    //
    // the file is read from the working directory, one level up, or the
    // rig's mapped drive, and the first location that exists wins. the
    // values load once, at startup.
    public static class uv2_ab_flags
    {
        private static readonly Dictionary<string, string> values =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool loaded;

        public static float get_float(string key, float fallback)
        {
            ensure_loaded();
            if (values.TryGetValue(key, out var raw) &&
                float.TryParse(raw, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out var parsed))
                return parsed;
            return fallback;
        }

        public static string get_string(string key, string fallback)
        {
            ensure_loaded();
            if (values.TryGetValue(key, out var raw) && raw.Length > 0) return raw;
            return fallback;
        }

        public static bool get_bool(string key, bool fallback)
        {
            ensure_loaded();
            if (!values.TryGetValue(key, out var raw)) return fallback;
            raw = raw.Trim().ToLowerInvariant();
            if (raw == "1" || raw == "true" || raw == "on" || raw == "yes") return true;
            if (raw == "0" || raw == "false" || raw == "off" || raw == "no") return false;
            return fallback;
        }

        private static void ensure_loaded()
        {
            if (loaded) return;
            loaded = true;
            string[] candidates =
            {
                Path.Combine(Directory.GetCurrentDirectory(), "flags.txt"),
                Path.Combine(Application.dataPath, "../flags.txt"),
                @"Z:\uv2\flags.txt",
            };
            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                        int eq = trimmed.IndexOf('=');
                        if (eq <= 0) continue;
                        values[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim();
                    }
                    // logs the file that won and what it carried, so a sweep
                    // that quietly ran with the defaults is visible.
                    var pairs = new List<string>();
                    foreach (var kv in values) pairs.Add($"{kv.Key}={kv.Value}");
                    UnityEngine.Debug.Log($"[uv2flags] loaded {path}: {string.Join(", ", pairs.ToArray())}");
                    return;
                }
                catch { /* unreadable candidate, try the next */ }
            }
            UnityEngine.Debug.Log("[uv2flags] no flags file found, using defaults");
        }
    }
}
