using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // reads the slot->sequence map (datapack/slot_sequence_map.json): per-song
    // motionSequenceIndices straight from the game's decoded config matrix.
    public static class slot_sequences
    {
        private static Dictionary<string, object> songs;

        public static List<int> for_song(int music_id)
        {
            if (songs == null) load();
            if (songs == null || !songs.TryGetValue(music_id.ToString(), out var raw)) return null;
            if (raw is not Dictionary<string, object> entry) return null;
            if (entry.TryGetValue("motion_sequence_indices", out var arr) && arr is List<object> list)
                return list.Select(v => (int)(long)v).ToList();
            return null;
        }

        private static void load()
        {
            string path = Path.Combine(config.datapack_path, "slot_sequence_map.json");
            if (!File.Exists(path))
                path = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "data", "slot_sequence_map.json");
            if (!File.Exists(path)) { Debug.LogWarning("[slot_sequences] no slot_sequence_map.json"); return; }
            try
            {
                songs = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
                Debug.Log($"[slot_sequences] loaded {songs?.Count ?? 0} songs");
            }
            catch (System.Exception e) { Debug.LogWarning($"[slot_sequences] parse failed: {e.Message}"); }
        }
    }

    // reads the concert manifest (datapack/concert_manifest.json): per-song
    // bundle names + per-(chara,dress) body prefab names.
    public static class manifest_reader
    {
        private static Dictionary<string, object> root;

        private static void ensure()
        {
            if (root != null) return;
            string path = Path.Combine(config.datapack_path, "concert_manifest.json");
            if (!File.Exists(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "concert_manifest.json");
            if (!File.Exists(path))
            {
                Debug.LogError("[manifest_reader] concert_manifest.json not found");
                root = new Dictionary<string, object>();
                return;
            }
            try
            {
                root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
            }
            catch (Exception e)
            {
                Debug.LogError($"[manifest_reader] parse failed: {e.Message}");
                root = new Dictionary<string, object>();
            }
        }

        // one song's manifest entry; null when the song is absent.
        public static song_manifest song(int music_id)
        {
            ensure();
            if (!root.TryGetValue("songs", out var songs_obj) || songs_obj is not Dictionary<string, object> songs) return null;
            if (!songs.TryGetValue(music_id.ToString(), out var s_obj) || s_obj is not Dictionary<string, object> s) return null;

            var m = new song_manifest();
            if (s.TryGetValue("stage_id", out var sid)) m.stage_id = sid as string;
            if (s.TryGetValue("stage_controller", out var sc) && sc is List<object> sc_l)
                m.stage_controller = sc_l.OfType<string>().ToList();
            if (s.TryGetValue("stage_materials", out var sm) && sm is List<object> sm_l)
                m.stage_materials = sm_l.OfType<string>().ToList();
            if (s.TryGetValue("motion_clips", out var mc) && mc is Dictionary<string, object> mc_d)
                m.motion_clips = mc_d.Where(kv => kv.Value is string).ToDictionary(kv => kv.Key, kv => (string)kv.Value);
            return m;
        }

        // body bundle + prefab names for a (chara, dress) pair; null when absent.
        public static (string bundle, string prefab)? chara_body(int chara_id, int dress_id)
        {
            ensure();
            if (!root.TryGetValue("chara_bodies", out var cb_obj) || cb_obj is not Dictionary<string, object> cb) return null;
            if (!cb.TryGetValue($"{chara_id}:{dress_id}", out var e_obj) || e_obj is not Dictionary<string, object> e) return null;
            string bundle = e.TryGetValue("bundle", out var b) ? b as string : null;
            string prefab = e.TryGetValue("prefab", out var p) ? p as string : null;
            // meta_name is the full path the game's manifest keys the bundle by.
            if (e.TryGetValue("meta_name", out var mn) && mn is string meta && meta.Length > 0)
                return (meta, prefab);
            if (string.IsNullOrEmpty(bundle) || string.IsNullOrEmpty(prefab)) return null;
            return (bundle, prefab);
        }
    }
}
