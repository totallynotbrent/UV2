using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.Data
{
    // livesettings csv row model: id,type,param1..param5 per song file.
    [Serializable]
    public class livesettings_row
    {
        public int id;
        public int type;
        public string param1;
        public string param2;
        public string param3;
        public string param4;
        public string param5;
    }

    public static class livesettings
    {
        // row types used by phase 1: 0 = cutt bundle name, 1 = stage id.
        public const int TYPE_CUTT = 0;
        public const int TYPE_STAGE = 1;

        public static List<livesettings_row> load(int music_id, string datapack)
        {
            string path = Path.Combine(datapack, "livesettings", $"{music_id}.txt");
            var rows = new List<livesettings_row>();
            if (!File.Exists(path))
            {
                Debug.LogError($"[livesettings] missing {path}");
                return rows;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (i == 0 || line.Length == 0) continue;
                string[] cells = line.Split(',');
                if (cells.Length < 2) continue;

                var r = new livesettings_row();
                r.id = int.Parse(cells[0]);
                r.type = int.Parse(cells[1]);
                if (cells.Length > 2) r.param1 = cells[2];
                if (cells.Length > 3) r.param2 = cells[3];
                if (cells.Length > 4) r.param3 = cells[4];
                if (cells.Length > 5) r.param4 = cells[5];
                if (cells.Length > 6) r.param5 = cells[6];
                rows.Add(r);
            }
            return rows;
        }

        public static int stage_id(List<livesettings_row> rows)
        {
            foreach (var r in rows)
            {
                if (r.type == TYPE_STAGE)
                {
                    if (int.TryParse(r.param1, out int sid)) return sid;
                }
            }
            return -1;
        }

        public static string cutt_name(List<livesettings_row> rows)
        {
            foreach (var r in rows)
            {
                if (r.type == TYPE_CUTT) return r.param1;
            }
            return null;
        }
    }
}
