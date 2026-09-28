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
    // scene bootstrappers: the concert window is the whole app, built at runtime.
    public static class scene_bootstrap
    {
        public static void build_concert_scene()
        {
            try
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
            catch (Exception e)
            {
                Debug.LogError($"[boot] concert scene failed: {e}");
                build_error_screen(e);
            }
        }

        // writes song jackets and character icons beside the exe as pngs, for the desktop launcher.
        public static void dump_icons()
        {
            try
            {
                string base_dir = AppDomain.CurrentDomain.BaseDirectory;
                string jacket_dir = System.IO.Path.Combine(base_dir, "jackets");
                string icon_dir = System.IO.Path.Combine(base_dir, "charicons");
                System.IO.Directory.CreateDirectory(jacket_dir);
                System.IO.Directory.CreateDirectory(icon_dir);

                using var db = UV2.Data.master_db.reader.open(config.master_db_path);
                using var meta = UV2.Data.meta_reader.reader.open(config.meta_db_path);
                if (db == null || meta == null)
                {
                    Debug.LogError($"[icons] data not found: master={db == null}, meta={meta == null}");
                    return;
                }

                int jackets = 0;
                var songs = UV2.Concert.song_catalog.load(db, meta, config.data_root);
                foreach (var s in songs)
                {
                    if (s.jacket == null) continue;
                    string path = System.IO.Path.Combine(jacket_dir, s.music_id + ".png");
                    System.IO.File.WriteAllBytes(path, encode_png(s.jacket));
                    jackets++;
                }

                // base portrait per character: bundle chara/chr{id}/chr_icon_{id}, texture chr_icon_{id}.
                var chara_ids = new List<int>();
                foreach (var r in db.query("SELECT id FROM chara_data"))
                    chara_ids.Add((int)r.get_int(0));

                var icon_names = new HashSet<string>();
                foreach (int id in chara_ids)
                    icon_names.Add($"chara/chr{id}/chr_icon_{id}");
                var icon_rows = meta.lookup(icon_names);

                int icons = 0;
                foreach (int id in chara_ids)
                {
                    var row = icon_rows.GetValueOrDefault($"chara/chr{id}/chr_icon_{id}");
                    if (row == null) continue;
                    var tex = UV2.Data.game_assets.load_texture(row, config.data_root, $"chr_icon_{id}");
                    if (tex == null) continue;
                    string path = System.IO.Path.Combine(icon_dir, id + ".png");
                    System.IO.File.WriteAllBytes(path, encode_png(tex));
                    icons++;
                }

                Debug.Log($"[icons] wrote {jackets} jackets and {icons} character icons to {base_dir}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[icons] dump failed: {e}");
            }
        }

        // writes the shader bundle's pathID -> name map beside the exe, for offline decoding.
        public static void dump_shader_map()
        {
            try
            {
                string base_dir = AppDomain.CurrentDomain.BaseDirectory;
                using var meta = UV2.Data.meta_reader.reader.open(config.meta_db_path);
                if (meta == null) { Debug.LogError("[shaders] meta db not found"); return; }
                var rows = meta.lookup(new HashSet<string> { "shader" });
                var row = rows.GetValueOrDefault("shader");
                if (row == null) { Debug.LogError("[shaders] no meta row for bundle 'shader'"); return; }

                var bundle = UV2.Data.game_assets.open(row, config.data_root);
                if (bundle == null) { Debug.LogError("[shaders] bundle open failed"); return; }

                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (var path in bundle.GetAllAssetNames())
                {
                    var sh = bundle.LoadAsset<Shader>(path);
                    if (sh == null) continue;
                    sb.Append(path).Append('\t').Append(sh.name).Append('\n');
                    n++;
                }
                string out_path = System.IO.Path.Combine(base_dir, "shader_name_map.tsv");
                System.IO.File.WriteAllText(out_path, sb.ToString());
                Debug.Log($"[shaders] wrote {n} shader entries to {out_path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[shaders] dump failed: {e}");
            }
        }

        // encodes a gpu-resident texture to png by blitting through a render texture.
        private static byte[] encode_png(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = readable.EncodeToPNG();
            UnityEngine.Object.Destroy(readable);
            return png;
        }

        // any unexpected boot failure shows itself instead of a blank window.
        private static void build_error_screen(Exception e)
        {
            build_centered_screen($"failed to start:\n{e.GetType().Name}: {e.Message}");
        }

        private static void build_centered_screen(string message)
        {
            var canvas = UV2.UI.ui_theme.build_canvas("error_canvas");
            var root = canvas.transform;
            UV2.UI.ui_theme.panel(root, "backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.07f, 1f), raycast: true);

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
    }
}
