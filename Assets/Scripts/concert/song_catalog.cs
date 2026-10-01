using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Concert
{
    // one playable concert row built from live_data + text_data + the game's own bundles.
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
        public Texture2D jacket;
    }

    public static class song_catalog
    {
        // the phase-1 asset names this catalog needs from the meta db.
        public static HashSet<string> asset_names(IEnumerable<int> music_ids)
        {
            var names = new HashSet<string> { "livesettings" };
            foreach (int mid in music_ids)
                names.Add($"live/jacket/jacket_icon_l_{mid}");
            return names;
        }

        // loads the song list: master.mdb rows joined with livesettings bundles and jacket art.
        public static List<song_entry> load(master_db.reader db, meta_reader.reader meta, string data_root)
        {
            var seconds = load_seconds(db);
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
                out_list.Add(s);
            }

            out_list.Sort((a, b) => a.sort != b.sort ? a.sort.CompareTo(b.sort) : a.music_id.CompareTo(b.music_id));

            // livesettings + jackets come from the user's own install via the meta db.
            var meta_rows = meta.lookup(asset_names(out_list.Select(s => s.music_id)));
            var ls_row = meta_rows.GetValueOrDefault("livesettings");
            AssetBundle ls_bundle = null;
            if (ls_row != null)
                ls_bundle = game_assets.open(ls_row, data_root);
            if (ls_bundle == null && ls_row != null)
                Debug.LogError("[song_catalog] livesettings bundle failed to open");

            foreach (var s in out_list)
            {
                if (ls_bundle != null && ls_bundle.Contains(s.music_id.ToString()))
                {
                    var text = ls_bundle.LoadAsset<TextAsset>(s.music_id.ToString());
                    var ls = Data.livesettings.parse_csv(text != null ? text.text : null);
                    s.livesettings_ok = ls.Count > 0;
                    s.stage_id = Data.livesettings.stage_id(ls);
                }
                s.stage_ok = s.stage_id > 0;
                if (!s.stage_ok)
                    Debug.LogError($"[song_catalog] no stage row for music {s.music_id}");

                var jacket_row = meta_rows.GetValueOrDefault($"live/jacket/jacket_icon_l_{s.music_id}");
                if (jacket_row != null)
                    s.jacket = game_assets.load_texture(jacket_row, data_root, $"jacket_icon_l_{s.music_id}");
                if (s.jacket == null)
                    Debug.LogWarning($"[song_catalog] no jacket art for music {s.music_id}");
            }

            if (ls_bundle != null) ls_bundle.Unload(true);

            return out_list;
        }

        // song lengths come from the cutt census json shipped in streaming assets.
        private static Dictionary<int, int> load_seconds(master_db.reader db)
        {
            var secs = new Dictionary<int, int>();
            string path = Path.Combine(config.datapack_path, "song_seconds.json");
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
