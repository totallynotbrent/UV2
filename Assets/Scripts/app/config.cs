using System;
using System.IO;
using UnityEngine;

namespace UV2.App
{
    // runtime configuration: game data root path, frame cap, and diagnostics.
    public static class config
    {
        private static string _main_path;
        private static bool _loaded;

        [Serializable]
        private class config_file
        {
            public string main_path;
            public int target_fps = 60;
            // off by default: the trace and the bench dumps litter the
            // install folder and are only wanted while debugging.
            public bool diagnostics = false;
        }

        // enables the verbose trace and per-frame subsystem timing.
        public static bool diagnostics
        {
            get
            {
                load_or_generate();
                return _diagnostics;
            }
        }

        private static bool _diagnostics;

        // v-sync is off, so this cap sets the real frame rate.
        public static int target_fps
        {
            get
            {
                load_or_generate();
                return _target_fps;
            }
        }

        private static int _target_fps = 60;

        private static string config_path
        {
            get
            {
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exe_dir, "Config.json");
            }
        }

        // loads config.json, generating a default one on first run.
        private static void load_or_generate()
        {
            if (_loaded) return;
            _loaded = true;

            // the UV2_MAIN_PATH env var overrides the config file.
            string env_path = System.Environment.GetEnvironmentVariable("UV2_MAIN_PATH");
            bool env_path_set = !string.IsNullOrEmpty(env_path) && System.IO.Directory.Exists(env_path);

            // UV2_DIAGNOSTICS turns the trace and the bench dumps on without
            // touching the config file, which is how the benches run. it is
            // read after the config so it can still override.
            string env_diag = System.Environment.GetEnvironmentVariable("UV2_DIAGNOSTICS");
            bool env_diag_set = !string.IsNullOrEmpty(env_diag);
            bool env_diag_value = env_diag_set &&
                (env_diag == "1" || env_diag.Equals("true", StringComparison.OrdinalIgnoreCase));

            string default_path = default_main_path();
            string main_path = default_path;
            _diagnostics = false;
            _target_fps = 60;

            if (File.Exists(config_path))
            {
                try
                {
                    var cfg = JsonUtility.FromJson<config_file>(File.ReadAllText(config_path));
                    if (!string.IsNullOrEmpty(cfg.main_path))
                        main_path = cfg.main_path;
                    if (cfg.target_fps > 0) _target_fps = cfg.target_fps;
                    _diagnostics = cfg.diagnostics;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[config] failed to read {config_path}: {e.Message}; using default");
                }
            }
            else
            {
                var fresh = new config_file { main_path = default_path, target_fps = 60, diagnostics = false };
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

            // the json parse handles both states, but a config written by
            // another tool can have odd spacing, so the text is scanned too.
            try
            {
                if (File.Exists(config_path))
                {
                    string body = File.ReadAllText(config_path);
                    if (body.Contains("\"diagnostics\": true")) _diagnostics = true;
                }
            }
            catch { }

            // the environment wins over the file.
            if (env_diag_set) _diagnostics = env_diag_value;
            if (env_path_set) main_path = env_path;

            _main_path = main_path;
        }

        // default data install location under the user profile.
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
                // the data folder follows the product name, so both spellings are tried.
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string folder in new[] { "UmaLive_Data", "UV2_Data" })
                {
                    string streaming = Path.Combine(exe_dir, folder, "StreamingAssets", "data");
                    if (Directory.Exists(streaming)) return streaming;
                }
                return Path.Combine(exe_dir, "data");
            }
        }
    }
}
