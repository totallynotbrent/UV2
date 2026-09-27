using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Concert;
using UV2.Data;

namespace UV2.App
{
    // scene bootstrappers: create the ui at runtime instead of authoring scenes by hand.
    public static class scene_bootstrap
    {
        public static void build_picker_scene()
        {
            var cam_go = new GameObject("main_camera", typeof(Camera));
            var cam = cam_go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
            cam.orthographic = true;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = -10f;
            cam.farClipPlane = 100f;

            Debug.Log($"[boot] picker scene starting, master db at {config.master_db_path}, datapack at {config.datapack_path}");
            var db = UV2.Data.master_db.reader.open(config.master_db_path);
            if (db == null)
            {
                Debug.LogError($"[boot] master.mdb not found at {config.master_db_path}");
                build_missing_db_screen(config.master_db_path, config_path_display());
                return;
            }

            var meta = meta_reader.reader.open(config.meta_db_path);
            using (db)
            using (meta)
            {
                var charas = UV2.Concert.chara_catalog.load(db);
                var songs = UV2.Concert.song_catalog.load(db, meta, config.data_root);
                Debug.Log($"[boot] catalogs loaded: {songs.Count} songs, {charas.Count} characters, {songs.Count(s => s.stage_ok)} stages resolved, {songs.Count(s => s.has_live)} with live flag, {songs.Count(s => s.jacket != null)} jackets");
                var stage_fail = songs.Where(s => !s.stage_ok).Select(s => s.music_id).ToList();
                if (stage_fail.Count > 0) Debug.LogError($"[boot] songs with unresolved stage: {string.Join(", ", stage_fail)}");

                var host = new GameObject("picker_host");
                var picker = host.AddComponent<UV2.UI.picker_form>();
                picker.open(songs, charas);
            }
        }

        // the config file's location for the error screen; exe dir, not the resolved data path.
        private static string config_path_display()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.json");
        }

        // centered full-screen notice when the game database is not found.
        private static void build_missing_db_screen(string db_path, string cfg_path)
        {
            var canvas = UV2.UI.ui_theme.build_canvas("error_canvas");
            var root = canvas.transform;
            UV2.UI.ui_theme.panel(root, "backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.07f, 1f), raycast: true);

            string message =
                "master.mdb not found.\n\n" +
                $"looked for:\n{db_path}\n\n" +
                $"edit main_path in the auto-generated config:\n{cfg_path}\n" +
                "point it at your game's umamusume_Data/Persistent folder, then restart.";
            var txt = UV2.UI.ui_theme.make_text(root, "error", message, 22, UV2.UI.ui_theme.text_main);
            var rect = txt.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760, 0);
            rect.anchoredPosition = Vector2.zero;
            txt.enableWordWrapping = true;
            txt.alignment = TextAlignmentOptions.Center;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        }

        public static void build_concert_scene()
        {
            var cam_go = new GameObject("main_camera", typeof(Camera));
            var cam = cam_go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
            cam.orthographic = true;
            cam.nearClipPlane = -10f;
            cam.farClipPlane = 100f;

            var host = new GameObject("concert_host");
            host.AddComponent<UV2.UI.concert_window>().open();
        }
    }
}
