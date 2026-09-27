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
                ui_theme.make_text(root, "empty", "no selection file found.\nlaunch a concert from the desktop launcher.", 18, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(40, -40);
                return;
            }

            var title_rect = ui_theme.make_text(root, "title", sel.title, 30, ui_theme.text_main).GetComponent<RectTransform>();
            title_rect.anchorMin = title_rect.anchorMax = new Vector2(0.5f, 1f);
            title_rect.pivot = new Vector2(0.5f, 1f);
            title_rect.anchoredPosition = new Vector2(0, -40);

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

            var meta_rect = ui_theme.make_text(root, "meta", $"music {sel.music_id}   stage {sel.stage_id}   {sel.member_count} members", 17, ui_theme.text_dim).GetComponent<RectTransform>();
            meta_rect.anchorMin = meta_rect.anchorMax = new Vector2(0.5f, 1f);
            meta_rect.pivot = new Vector2(0.5f, 1f);
            meta_rect.anchoredPosition = new Vector2(0, -78);

            var db = master_db.reader.open(config.master_db_path);
            var names = new System.Collections.Generic.Dictionary<int, string>();
            if (db != null)
            {
                foreach (var r in db.query("SELECT `index`, text FROM text_data WHERE category=6"))
                    names[(int)r.get_int(0)] = r.get_text(1);
                db.Dispose();
            }

            // responsive cast list: explicit top-left anchors so columns are absolute, not screen-center relative.
            var canvas_rect = canvas.GetComponent<RectTransform>().rect;
            float canvas_w = canvas_rect.width > 0 ? canvas_rect.width : 1600f;
            float canvas_h = canvas_rect.height > 0 ? canvas_rect.height : 900f;

            var ordered = sel.slots.OrderBy(s => s.position).ToList();
            int count = ordered.Count;
            int columns = Mathf.Max(1, Mathf.CeilToInt(count / 9f));
            if (canvas_w < 900f) columns = 1;
            int rows = Mathf.CeilToInt(count / (float)columns);
            float left_margin = 48f;
            float top_margin = 120f;
            float column_step = (canvas_w - left_margin * 2f) / columns;
            float row_step = Mathf.Min(26f, (canvas_h - top_margin - 70f) / Mathf.Max(1, rows));

            for (int i = 0; i < count; i++)
            {
                var slot = ordered[i];
                string name = names.TryGetValue(slot.chara_id, out var n) ? n : slot.chara_id > 0 ? $"chara {slot.chara_id}" : "empty";
                string line = $"pos {slot.position}: {name}" + (slot.dress_id > 0 ? $"   (dress {slot.dress_id})" : "");
                int column = i / rows;
                int row = i % rows;
                var slot_rect = ui_theme.make_text(root, $"slot_{slot.position}", line, 17, ui_theme.text_main).GetComponent<RectTransform>();
                slot_rect.anchorMin = slot_rect.anchorMax = slot_rect.pivot = new Vector2(0f, 1f);
                slot_rect.anchoredPosition = new Vector2(left_margin + column * column_step, -(top_margin + row * row_step));
            }

            var note = ui_theme.make_text(root, "note", "stage, audio and timeline land in the next phases.", 14, ui_theme.text_dim);
            var note_rect = note.GetComponent<RectTransform>();
            note_rect.anchorMin = note_rect.anchorMax = note_rect.pivot = new Vector2(0, 0);
            note_rect.anchoredPosition = new Vector2(40, 28);
        }
    }
}
