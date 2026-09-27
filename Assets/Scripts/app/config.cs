using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.App
{
    // runtime configuration: game data root path and the selection file location.
    public static class config
    {
        private static string _main_path;

        public static string data_root
        {
            get
            {
                if (_main_path != null) return _main_path;
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                string cfg_path = Path.Combine(exe_dir, "Config.json");
                if (File.Exists(cfg_path))
                {
                    try
                    {
                        var cfg = JsonUtility.FromJson<config_file>(File.ReadAllText(cfg_path));
                        if (!string.IsNullOrEmpty(cfg.main_path))
                        {
                            _main_path = cfg.main_path;
                            return _main_path;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[config] failed to read {cfg_path}: {e.Message}");
                    }
                }
                _main_path = Path.Combine(exe_dir, "umamusume_Data", "Persistent");
                return _main_path;
            }
        }

        public static string master_db_path => Path.Combine(data_root, "master", "master.mdb");

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

        [Serializable]
        private class config_file
        {
            public string main_path;
        }
    }
}
