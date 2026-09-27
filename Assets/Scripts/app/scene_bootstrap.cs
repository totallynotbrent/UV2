using System.Linq;
using UnityEngine;

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
                var canvas = UV2.UI.ui_theme.build_canvas("error_canvas");
                UV2.UI.ui_theme.make_text(canvas.transform, "error", "master.mdb not found at:\n" + config.master_db_path + "\n\ncreate Config.json next to the exe with {\"main_path\": \"<your game Persistent folder>\"} and restart.", 18, UV2.UI.ui_theme.text_main).GetComponent<RectTransform>().anchoredPosition = new Vector2(40, -40);
                return;
            }
            using (db)
            {
                var songs = UV2.Concert.song_catalog.load(config.datapack_path, db);
                var charas = UV2.Concert.chara_catalog.load(db);
                Debug.Log($"[boot] catalogs loaded: {songs.Count} songs, {charas.Count} characters, {songs.Count(s => s.stage_ok)} stages resolved, {songs.Count(s => s.has_live)} with live flag");
                var stage_fail = songs.Where(s => !s.stage_ok).Select(s => s.music_id).ToList();
                if (stage_fail.Count > 0) Debug.LogError($"[boot] songs with unresolved stage: {string.Join(", ", stage_fail)}");
                var host = new GameObject("picker_host");
                var picker = host.AddComponent<UV2.UI.picker_form>();
                picker.open(songs, charas);
            }
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
