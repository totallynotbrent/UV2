using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UV2.App;
using UV2.Concert;
using UV2.Data;

namespace UV2.UI
{
    // window 1: song list -> slot grid -> per-slot character picker.
    public class picker_form : MonoBehaviour
    {
        private List<song_entry> _songs;
        private Dictionary<int, chara_entry> _chara_by_id;
        private List<chara_entry> _chara_list;

        private TMP_InputField _search;
        private RectTransform _song_rows;
        private RectTransform _detail;

        private song_entry _selected_song;
        private song_chara_rules _rules;
        private readonly List<GameObject> _row_pool = new List<GameObject>();

        // selection override per slot position (1-based); falls back to rules defaults.
        private readonly Dictionary<int, int> _picked_chara = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _picked_dress = new Dictionary<int, int>();

        public void open(List<song_entry> songs, List<chara_entry> chara_list)
        {
            _songs = songs;
            _chara_list = chara_list;
            _chara_by_id = chara_list.ToDictionary(c => c.chara_id, c => c);
            build_ui();
            refresh_song_list("");

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-uv2demo") StartCoroutine(run_headless_demo());
            }
        }

        // headless e2e: clicks a song, fills slot 1 from the popup pool, saves, jumps to the concert window.
        private System.Collections.IEnumerator run_headless_demo()
        {
            yield return null;
            Debug.Log("[demo] selecting first song");
            select_song(_songs[0]);
            yield return null;
            Debug.Log($"[demo] slot grid built for {_selected_song.title}, slots={_rules.member_count}");
            var popup = open_chara_popup_public(0);
            yield return null;
            var pool = _rules.restricted
                ? _chara_list.Where(c => _rules.allowed.Contains(c.chara_id)).ToList()
                : _chara_list;
            var pick = pool[0];
            _picked_chara[1] = pick.chara_id;
            _picked_dress[1] = pick.live_dress_ids.Count > 0 ? pick.live_dress_ids[0] : -1;
            Debug.Log($"[demo] slot 1 <- chara {pick.chara_id} ({pick.name})");
            if (popup != null) Destroy(popup);
            yield return null;
            save_selection();
        }

        public GameObject open_chara_popup_public(int position)
        {
            open_chara_popup(position);
            var canvas = FindObjectOfType<Canvas>();
            foreach (Transform t in canvas.transform)
            {
                if (t.name == "chara_popup") return t.gameObject;
            }
            return null;
        }

        private void build_ui()
        {
            var canvas = ui_theme.build_canvas("picker_canvas");
            var root = canvas.transform;

            ui_theme.panel(root, "backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.07f, 1f), raycast: true);

            var header = ui_theme.panel(root, "header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, 0), ui_theme.panel_bg_alt);
            var title_rect = ui_theme.make_text(header, "title", "UV2 — live concert picker", 24, ui_theme.text_main, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            title_rect.anchoredPosition = new Vector2(20, -30);
            _search = ui_theme.make_input(header, "search", new Vector2(360, 36), "search songs...");
            var search_rect = _search.GetComponent<RectTransform>();
            search_rect.anchorMin = new Vector2(1, 1);
            search_rect.anchorMax = new Vector2(1, 1);
            search_rect.anchoredPosition = new Vector2(-200, -30);
            _search.onValueChanged.AddListener(_ => refresh_song_list(_search.text));

            var (_, rows_root) = ui_theme.make_scroll(root, "song_list", new Vector2(0, 0), new Vector2(0.42f, 1), new Vector2(10, 10), new Vector2(-5, -70));
            _song_rows = rows_root;

            _detail = ui_theme.panel(root, "detail", new Vector2(0.42f, 0), new Vector2(1, 1), new Vector2(5, 10), new Vector2(-10, -70), ui_theme.panel_bg_alt);
        }

        private void refresh_song_list(string filter)
        {
            foreach (var go in _row_pool) UnityEngine.Object.Destroy(go);
            _row_pool.Clear();

            var visible = _songs.Where(s => string.IsNullOrEmpty(filter)
                || s.title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || s.music_id.ToString().Contains(filter)).ToList();

            float row_h = 64f;
            _song_rows.sizeDelta = new Vector2(0, visible.Count * row_h);
            for (int i = 0; i < visible.Count; i++)
            {
                var s = visible[i];
                var row = ui_theme.panel(_song_rows, $"song_{s.music_id}", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -(i + 1) * row_h), new Vector2(0, -i * row_h), ui_theme.panel_bg);
                row.GetComponent<Image>().raycastTarget = true;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => select_song(s));
                if (_selected_song != null && _selected_song.music_id == s.music_id)
                    row.GetComponent<Image>().color = ui_theme.row_selected;

                string jacket_path = Path.Combine(config.datapack_path, "jacket", $"{s.music_id}.png");
                var sprite = ui_theme.load_sprite(jacket_path);
                if (sprite != null)
                {
                    var jacket_rect = new GameObject("jacket", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                    jacket_rect.SetParent(row, false);
                    jacket_rect.anchorMin = new Vector2(0, 0.5f);
                    jacket_rect.anchorMax = new Vector2(0, 0.5f);
                    jacket_rect.anchoredPosition = new Vector2(36, 0);
                    jacket_rect.sizeDelta = new Vector2(52, 52);
                    var jimg = jacket_rect.GetComponent<Image>();
                    jimg.sprite = sprite;
                    jimg.preserveAspect = true;
                    jimg.raycastTarget = false;
                }

                var title = ui_theme.make_text(row, "title", $"{s.title}", 20, ui_theme.text_main);
                title.GetComponent<RectTransform>().anchoredPosition = new Vector2(74, -14);
                var meta = ui_theme.make_text(row, "meta", $"id {s.music_id}   {s.member_count} members   {s.seconds}s{(s.has_live ? "" : "   (no live flag)")}", 14, ui_theme.text_dim);
                meta.GetComponent<RectTransform>().anchoredPosition = new Vector2(74, -40);
            }
        }

        private void select_song(song_entry s)
        {
            _selected_song = s;
            _picked_chara.Clear();
            _picked_dress.Clear();
            var db = master_db.reader.open(config.master_db_path);
            if (db == null)
            {
                ui_theme.make_text(_detail, "db_error", "master.mdb not found:\n" + config.master_db_path, 16, ui_theme.text_main);
                return;
            }
            using (db)
            {
                _rules = song_rules_loader.load(s.music_id, s.member_count, db);
            }
            refresh_song_list(_search.text);
            build_slot_grid();
            Debug.Log($"[picker] song {s.music_id} ({s.title}): {_rules.member_count} slots, restricted={_rules.restricted}, defaults={_rules.default_chara.Count}");
        }

        private void build_slot_grid()
        {
            var s = _selected_song;
            ui_theme.make_text(_detail, "song_title", s.title, 26, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(20, -24);
            ui_theme.make_text(_detail, "song_meta", $"id {s.music_id}   stage {s.stage_id}   {s.member_count} members   backdancer_order {s.backdancer_order}", 15, ui_theme.text_dim).GetComponent<RectTransform>().anchoredPosition = new Vector2(20, -52);

            int count = s.member_count;
            int pivot = count / 2;
            int cols = Math.Min(count, 7);

            var grid = new GameObject("slots", typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<RectTransform>();
            grid.SetParent(_detail, false);
            grid.anchorMin = new Vector2(0, 1);
            grid.anchorMax = new Vector2(1, 1);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.anchoredPosition = new Vector2(0, -80);
            var layout = grid.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(120, 130);
            layout.spacing = new Vector2(8, 8);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = cols;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;

            for (int position = 0; position < count; position++)
            {
                var slot = ui_theme.panel(grid, $"slot_{position}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, ui_theme.panel_bg);
                slot.GetComponent<Image>().raycastTarget = true;
                slot.gameObject.AddComponent<Button>().onClick.AddListener(() => open_chara_popup(position));

                int chara = slot_chara(position + 1);
                int dress = slot_dress(position + 1);
                string label = chara > 0 && _chara_by_id.TryGetValue(chara, out var ch) ? ch.name : "pick a member";
                string dress_label = dress > 0 ? $"dress {dress}" : "";
                var name_txt = ui_theme.make_text_stretch(slot, "name", label, 15, ui_theme.text_main, TextAnchor.MiddleCenter);
                name_txt.GetComponent<RectTransform>().offsetMin = new Vector2(0, 30);
                name_txt.GetComponent<RectTransform>().offsetMax = new Vector2(0, -40);
                ui_theme.make_text_stretch(slot, "pos", $"pos {position + 1}", 12, ui_theme.text_dim, TextAnchor.LowerCenter).GetComponent<RectTransform>().offsetMax = new Vector2(0, -18);
                if (dress_label.Length > 0)
                    ui_theme.make_text_stretch(slot, "dress", dress_label, 11, ui_theme.text_dim, TextAnchor.LowerCenter).GetComponent<RectTransform>().offsetMax = new Vector2(0, -2);
            }

            var launch = ui_theme.make_button(_detail, "launch", "open concert window", new Vector2(220, 44), save_selection);
            var launch_rect = launch.GetComponent<RectTransform>();
            launch_rect.anchorMin = new Vector2(1, 0);
            launch_rect.anchorMax = new Vector2(1, 0);
            launch_rect.anchoredPosition = new Vector2(-20, 30);
        }

        private int slot_chara(int position)
        {
            if (_picked_chara.TryGetValue(position, out int picked)) return picked;
            return _rules.default_chara.GetValueOrDefault(position, -1);
        }

        private int slot_dress(int position)
        {
            if (_picked_dress.TryGetValue(position, out int picked)) return picked;
            return _rules.default_dress.GetValueOrDefault(position, -1);
        }

        private void save_selection()
        {
            if (_selected_song == null) return;
            var sel = new selection_state
            {
                music_id = _selected_song.music_id,
                title = _selected_song.title,
                member_count = _selected_song.member_count,
                stage_id = _selected_song.stage_id
            };
            for (int position = 1; position <= _selected_song.member_count; position++)
            {
                sel.slots.Add(new slot_pick
                {
                    position = position,
                    chara_id = slot_chara(position),
                    dress_id = slot_dress(position)
                });
            }
            selection_store.save(sel);
            Debug.Log($"[picker] selection saved: music {sel.music_id}, {sel.slots.Count} slots -> {selection_store.path}");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Concert");
        }

        private void open_chara_popup(int position)
        {
            var canvas = FindObjectOfType<Canvas>();
            var popup = new GameObject("chara_popup", typeof(RectTransform));
            popup.GetComponent<RectTransform>().SetParent(canvas.transform, false);
            var popup_rect = popup.GetComponent<RectTransform>();
            popup_rect.anchorMin = popup_rect.anchorMax = new Vector2(0.5f, 0.5f);
            popup_rect.sizeDelta = new Vector2(900, 640);

            ui_theme.panel(popup_rect, "bg", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.07f, 0.07f, 0.09f, 0.99f), raycast: true);

            var (_, content) = ui_theme.make_scroll(popup_rect, "list", new Vector2(0, 0), new Vector2(1, 0.88f), new Vector2(12, 12), new Vector2(-12, -48));
            var filter_input = ui_theme.make_input(popup_rect, "filter", new Vector2(300, 36), "filter characters...");
            var fr = filter_input.GetComponent<RectTransform>();
            fr.anchorMin = fr.anchorMax = new Vector2(0, 1);
            fr.anchoredPosition = new Vector2(170, -28);

            var close = ui_theme.make_button(popup_rect, "close", "close", new Vector2(100, 34), () => Destroy(popup));
            var cr = close.GetComponent<RectTransform>();
            cr.anchorMin = cr.anchorMax = new Vector2(1, 1);
            cr.anchoredPosition = new Vector2(-70, -28);

            System.Action<string> fill = null;
            fill = filter =>
            {
                for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
                var pool = _rules.restricted
                    ? _chara_list.Where(c => _rules.allowed.Contains(c.chara_id)).ToList()
                    : _chara_list;
                if (!string.IsNullOrEmpty(filter))
                    pool = pool.Where(c => c.name.Contains(filter) || c.chara_id.ToString().Contains(filter)).ToList();

                float row_h = 56f;
                content.sizeDelta = new Vector2(0, pool.Count * row_h);
                for (int i = 0; i < pool.Count; i++)
                {
                    var ch = pool[i];
                    var row = ui_theme.panel(content, $"chara_{ch.chara_id}", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -(i + 1) * row_h), new Vector2(0, -i * row_h), i % 2 == 0 ? ui_theme.panel_bg : ui_theme.panel_bg_alt);
                    row.GetComponent<Image>().raycastTarget = true;
                    int captured_position = position;
                    row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                    {
                        _picked_chara[captured_position + 1] = ch.chara_id;
                        int dress = ch.live_dress_ids.Count > 0 ? ch.live_dress_ids[0] : -1;
                        _picked_dress[captured_position + 1] = dress;
                        Debug.Log($"[picker] slot {captured_position + 1} <- chara {ch.chara_id} ({ch.name}) dress {dress}");
                        Destroy(popup);
                        build_slot_grid();
                    });
                    ui_theme.make_text(row, "name", ch.name, 18, ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(16, -14);
                    ui_theme.make_text(row, "meta", $"id {ch.chara_id}   {ch.live_dress_ids.Count} live outfits", 13, ui_theme.text_dim).GetComponent<RectTransform>().anchoredPosition = new Vector2(16, -36);
                }
            };
            filter_input.onValueChanged.AddListener(_ => fill(filter_input.text));
            fill("");
        }
    }
}
