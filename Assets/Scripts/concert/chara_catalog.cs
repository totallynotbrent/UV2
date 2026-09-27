using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UV2.Data;

namespace UV2.Concert
{
    // one character with their live-usable outfits.
    public class chara_entry
    {
        public int chara_id;
        public string name;
        public int height;
        public int bust;
        public int scale;
        public int skin;
        public int shape;
        public int socks;
        public int tail_model_id;
        public int personal_dress;
        public List<int> live_dress_ids = new List<int>();
        public Dictionary<int, string> dress_names = new Dictionary<int, string>();
    }

    public static class chara_catalog
    {
        // loads all 172 characters plus per-character live dress lists.
        public static List<chara_entry> load(master_db.reader db)
        {
            var names = new Dictionary<int, string>();
            foreach (var r in db.query("SELECT `index`, text FROM text_data WHERE category=6"))
                names[(int)r.get_int(0)] = r.get_text(1);

            var dress_names = new Dictionary<int, string>();
            foreach (var r in db.query("SELECT `index`, text FROM text_data WHERE category=14"))
                dress_names[(int)r.get_int(0)] = r.get_text(1);

            var restricted = new HashSet<int>();
            foreach (var r in db.query("SELECT music_id, dress_id FROM live_dress_restrict_data"))
                restricted.Add((int)r.get_int(1));

            var live_dresses = new Dictionary<int, List<int>>();
            foreach (var r in db.query("SELECT id, chara_id FROM dress_data WHERE use_live=1 OR use_live_theater=1"))
            {
                int cid = (int)r.get_int(1);
                if (!live_dresses.TryGetValue(cid, out var lst)) live_dresses[cid] = lst = new List<int>();
                lst.Add((int)r.get_int(0));
            }

            var out_list = new List<chara_entry>();
            foreach (var r in db.query("SELECT id, height, bust, scale, skin, shape, socks, tail_model_id, personal_dress FROM chara_data"))
            {
                var c = new chara_entry();
                c.chara_id = (int)r.get_int(0);
                c.height = (int)r.get_int(1);
                c.bust = (int)r.get_int(2);
                c.scale = (int)r.get_int(3);
                c.skin = (int)r.get_int(4);
                c.shape = (int)r.get_int(5);
                c.socks = (int)r.get_int(6);
                c.tail_model_id = (int)r.get_int(7);
                c.personal_dress = (int)r.get_int(8);
                c.name = names.GetValueOrDefault(c.chara_id, $"chara {c.chara_id}");

                if (live_dresses.TryGetValue(c.chara_id, out var own))
                    c.live_dress_ids.AddRange(own);
                if (live_dresses.TryGetValue(0, out var shared))
                    c.live_dress_ids.AddRange(shared);
                // dress restrictions from live_dress_restrict_data remove blocked outfits everywhere.
                for (int i = c.live_dress_ids.Count - 1; i >= 0; i--)
                {
                    if (restricted.Contains(c.live_dress_ids[i]))
                        c.live_dress_ids.RemoveAt(i);
                }

                foreach (int did in c.live_dress_ids)
                {
                    if (dress_names.TryGetValue(did, out string dn))
                        c.dress_names[did] = dn;
                    else
                        Debug.LogError($"[chara_catalog] no cat14 name for dress {did}");
                }

                out_list.Add(c);
            }
            return out_list;
        }
    }
}
