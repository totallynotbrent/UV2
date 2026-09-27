using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.Data;

namespace UV2.Concert
{
    // one playable concert row built from live_data + text_data + livesettings + song_gaps.
    public class song_entry
    {
        public int music_id;
        public string title;
        public int sort;
        public int member_count;
        public int default_main_dress;
        public int default_main_dress_color;
        public int default_mob_dress;
        public int backdancer_dress;
        public int backdancer_order;
        public bool has_live;
        public int stage_id = -1;
        public int seconds;
        public bool stage_ok;
        public bool livesettings_ok;
    }

    public static class song_catalog
    {
        // census ids from song_gaps.json: the 61 concerts the game data supports.
        public static List<int> census_ids(string datapack)
        {
            string path = Path.Combine(datapack, "song_seconds.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[song_catalog] missing {path}");
                return new List<int>();
            }
            var wrapper = JsonUtility.FromJson<seconds_wrapper>(File.ReadAllText(path));
            return wrapper.items.Select(it => it.id).ToList();
        }

        public static List<song_entry> load(string datapack, master_db.reader db)
        {
            var seconds = load_seconds(datapack);
            var titles = new Dictionary<int, string>();
            foreach (var r in db.query("SELECT `index`, text FROM text_data WHERE category=16"))
                titles[(int)r.get_int(0)] = r.get_text(1);

            var out_list = new List<song_entry>();
            foreach (var r in db.query("SELECT music_id, sort, live_member_number, default_main_dress, default_main_dress_color, default_mob_dress, backdancer_dress, backdancer_order, has_live FROM live_data"))
            {
                int mid = (int)r.get_int(0);
                if (!seconds.ContainsKey(mid)) continue;

                var s = new song_entry();
                s.music_id = mid;
                s.sort = (int)r.get_int(1);
                s.member_count = (int)r.get_int(2);
                s.default_main_dress = (int)r.get_int(3);
                s.default_main_dress_color = (int)r.get_int(4);
                s.default_mob_dress = (int)r.get_int(5);
                s.backdancer_dress = (int)r.get_int(6);
                s.backdancer_order = (int)r.get_int(7);
                s.has_live = r.get_int(8) == 1;
                s.seconds = seconds[mid];
                s.title = titles.GetValueOrDefault(mid, $"music {mid}");
                if (!titles.ContainsKey(mid))
                    Debug.LogError($"[song_catalog] no cat16 title for music {mid}");

                var ls = Data.livesettings.load(mid, datapack);
                s.livesettings_ok = ls.Count > 0;
                s.stage_id = Data.livesettings.stage_id(ls);
                s.stage_ok = s.stage_id > 0;
                if (!s.stage_ok)
                    Debug.LogError($"[song_catalog] no stage row for music {mid}");
                out_list.Add(s);
            }

            out_list.Sort((a, b) => a.sort != b.sort ? a.sort.CompareTo(b.sort) : a.music_id.CompareTo(b.music_id));
            return out_list;
        }

        private static Dictionary<int, int> load_seconds(string datapack)
        {
            var secs = new Dictionary<int, int>();
            string path = Path.Combine(datapack, "song_seconds.json");
            if (!File.Exists(path)) return secs;
            var wrapper = JsonUtility.FromJson<seconds_wrapper>(File.ReadAllText(path));
            foreach (var it in wrapper.items) secs[it.id] = it.seconds;
            return secs;
        }

        [Serializable]
        private class seconds_item
        {
            public int id;
            public int seconds;
        }

        [Serializable]
        private class seconds_wrapper
        {
            public List<seconds_item> items;
        }
    }
}
