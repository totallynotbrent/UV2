using System;
using System.IO;
using UnityEngine;

namespace UV2.App
{
    // runtime configuration: game data root path and the selection file location.
    public static class config
    {
        private static string _main_path;
        private static bool _loaded;

        [Serializable]
        private class config_file
        {
            public string main_path;
            public int target_fps = 120;
            public bool diagnostics = true;
        }

        // verbose trace + per-frame subsystem timing; on for experimental
        // benches, off for the main push. the file field wins when present.
        public static bool diagnostics
        {
            get
            {
                load_or_generate();
                return _diagnostics;
            }
        }

        private static bool _diagnostics = true;

        // the frame cap the concert aims for; 120 by default, v-sync off.
        public static int target_fps
        {
            get
            {
                load_or_generate();
                return _target_fps;
            }
        }

        private static int _target_fps = 120;

        private static string config_path
        {
            get
            {
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exe_dir, "Config.json");
            }
        }

        // first run writes Config.json with the default path so the user has a file to edit.
        private static void load_or_generate()
        {
            if (_loaded) return;
            _loaded = true;

            // env override beats the config file; used by tooling and container runs.
            string env_path = System.Environment.GetEnvironmentVariable("UV2_MAIN_PATH");
            if (!string.IsNullOrEmpty(env_path) && System.IO.Directory.Exists(env_path))
            {
                _main_path = env_path;
                return;
            }

            string default_path = default_main_path();
            string main_path = default_path;
            _diagnostics = true;
            _target_fps = 120;

            if (File.Exists(config_path))
            {
                try
                {
                    var cfg = JsonUtility.FromJson<config_file>(File.ReadAllText(config_path));
                    if (!string.IsNullOrEmpty(cfg.main_path))
                        main_path = cfg.main_path;
                    if (cfg.target_fps > 0) _target_fps = cfg.target_fps;
                    if (cfg.diagnostics) _diagnostics = true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[config] failed to read {config_path}: {e.Message}; using default");
                }
            }
            else
            {
                var fresh = new config_file { main_path = default_path, target_fps = 120, diagnostics = true };
                try
                {
                    File.WriteAllText(config_path, JsonUtility.ToJson(fresh, true));
                    Debug.Log($"[config] generated {config_path} with main_path {default_path}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[config] could not write {config_path}: {e.Message}");
                }
            }

            // the diagnostics gate reads as the inverse when the file carries
            // diagnostics=false explicitly; json has no tri-state so the
            // generated default writes diagnostics=true and a false in the
            // file must turn it off.
            try
            {
                if (File.Exists(config_path) && File.ReadAllText(config_path).Contains("\"diagnostics\": false"))
                    _diagnostics = false;
            }
            catch { }

            _main_path = main_path;
        }

        // the game's default install layout under the user profile, same default as viewer v1.
        private static string default_main_path()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Umamusume", "umamusume_Data", "Persistent");
        }

        public static string data_root
        {
            get
            {
                load_or_generate();
                return _main_path;
            }
        }

        public static string master_db_path
        {
            get
            {
                load_or_generate();
                return Path.Combine(_main_path, "master", "master.mdb");
            }
        }

        public static string meta_db_path
        {
            get
            {
                load_or_generate();
                return Path.Combine(_main_path, "meta");
            }
        }

        public static string datapack_path
        {
            get
            {
                // streaming assets first (in-build); sidecar 'data' folder next to the exe (release zip).
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                string streaming = Path.Combine(exe_dir, "UV2_Data", "StreamingAssets", "data");
                if (Directory.Exists(streaming)) return streaming;
                return Path.Combine(exe_dir, "data");
            }
        }
    }
}
