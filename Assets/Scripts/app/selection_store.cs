using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UV2.App
{
    // per-slot selection state and its json handoff between the two windows.
    [Serializable]
    public class slot_pick
    {
        public int position;
        public int chara_id;
        public int dress_id;
    }

    [Serializable]
    public class selection_state
    {
        public int music_id;
        public string title = "";
        public int member_count;
        public int stage_id = -1;
        public List<slot_pick> slots = new List<slot_pick>();
    }

    public static class selection_store
    {
        public static string path
        {
            get
            {
                string exe_dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exe_dir, "selection.json");
            }
        }

        public static void save(selection_state sel)
        {
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(sel, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[selection_store] save failed: {e.Message}");
            }
        }

        public static selection_state load()
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<selection_state>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[selection_store] load failed: {e.Message}");
                return null;
            }
        }
    }
}
