using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Concert;
using UV2.Data;

namespace UV2.UI
{
    // window 2 shell: reads the selection json the explorer wrote and shows what it loaded.
    public class concert_window : MonoBehaviour
    {
        public void open()
        {
            var canvas = ui_theme.build_canvas("concert_canvas");
            var root = canvas.transform;
            ui_theme.panel(root, "backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.07f, 1f), raycast: true);

            var sel = selection_store.load();
            if (sel == null)
            {
                ui_theme.make_text(root, "empty", "no selection file found.\nlaunch a concert from the explorer's unit setup window.", 18, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(40, -40);
                return;
            }

            ui_theme.make_text(root, "title", sel.title, 30, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(40, -40);

            // the launcher only knows the song and cast; the stage resolves from the install here.
            if (sel.stage_id < 0)
            {
                using var sdb = master_db.reader.open(config.master_db_path);
                using var smeta = meta_reader.reader.open(config.meta_db_path);
                var entry = song_catalog.load(sdb, smeta, config.data_root)
                    .FirstOrDefault(s => s.music_id == sel.music_id);
                if (entry != null && entry.stage_id > 0)
                {
                    sel.stage_id = entry.stage_id;
                    selection_store.save(sel);
                }
            }

            ui_theme.make_text(root, "meta", $"music {sel.music_id}   stage {sel.stage_id}   {sel.member_count} members", 17, ui_theme.text_dim).GetComponent<RectTransform>().anchoredPosition = new Vector2(40, -76);

            var db = master_db.reader.open(config.master_db_path);
            var names = new System.Collections.Generic.Dictionary<int, string>();
            if (db != null)
            {
                foreach (var r in db.query("SELECT `index`, text FROM text_data WHERE category=6"))
                    names[(int)r.get_int(0)] = r.get_text(1);
                db.Dispose();
            }

            // two columns so any cast size fits the window; 9 rows per column.
            int per_column = 9;
            int column = 0;
            float y = -120;
            foreach (var slot in sel.slots)
            {
                string name = names.TryGetValue(slot.chara_id, out var n) ? n : slot.chara_id > 0 ? $"chara {slot.chara_id}" : "empty";
                string line = $"pos {slot.position}: {name}" + (slot.dress_id > 0 ? $"   (dress {slot.dress_id})" : "");
                float x = 48 + column * 300;
                ui_theme.make_text(root, $"slot_{slot.position}", line, 17, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
                y -= 26;
                if (slot.position % per_column == 0)
                {
                    column++;
                    y = -120;
                }
            }

            var note = ui_theme.make_text(root, "note", "stage, audio and timeline land in the next phases.", 14, ui_theme.text_dim);
            var note_rect = note.GetComponent<RectTransform>();
            note_rect.anchorMin = note_rect.anchorMax = note_rect.pivot = new Vector2(0, 0);
            note_rect.anchoredPosition = new Vector2(40, 28);
        }
    }
}
