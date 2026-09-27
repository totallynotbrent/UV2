using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.Data;

namespace UV2.Concert
{
    // member rules for one song: slot count, allowed characters, default fills.
    public class song_chara_rules
    {
        public int member_count;
        public bool restricted;
        public HashSet<int> allowed = new HashSet<int>();
        public Dictionary<int, int> default_chara = new Dictionary<int, int>();
        public Dictionary<int, int> default_dress = new Dictionary<int, int>();

        public bool is_open_pick => !restricted;
    }

    public static class song_rules_loader
    {
        // slot count comes from live_data; permission and default fills from the member tables.
        public static song_chara_rules load(int music_id, int member_count, master_db.reader db)
        {
            var rules = new song_chara_rules();
            rules.member_count = member_count;

            foreach (var r in db.query($"SELECT chara_id FROM live_permission_data WHERE music_id={music_id}"))
                rules.allowed.Add((int)r.get_int(0));
            rules.restricted = db.query($"SELECT COUNT(*) FROM live_permission_data WHERE music_id={music_id}")[0].get_int(0) > 0;

            foreach (var r in db.query($"SELECT position_id, chara_id, dress_id FROM live_recommend_formation WHERE music_id={music_id}"))
            {
                rules.default_chara[(int)r.get_int(0)] = (int)r.get_int(1);
                rules.default_dress[(int)r.get_int(0)] = (int)r.get_int(2);
            }

            foreach (var r in db.query($"SELECT `order`, chara_id, dress_id1 FROM live_fix_member_data WHERE music_id={music_id}"))
            {
                rules.default_chara[(int)r.get_int(0)] = (int)r.get_int(1);
                rules.default_dress[(int)r.get_int(0)] = (int)r.get_int(2);
            }

            return rules;
        }
    }
}
