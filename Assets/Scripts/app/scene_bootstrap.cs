using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UV2.App;
using UV2.Concert;
using UV2.Data;
using UV2.Live;

namespace UV2.App
{
    // scene bootstrappers: the concert window is the whole app, built at runtime.
    public static class scene_bootstrap
    {
        public static void build_concert_scene(bool free_clock = false)
        {
            try
            {
                stage_loader.force_free_clock = free_clock;
                // the batch editor reverts asset hdr during bakes, so the
                // pipeline settings are pinned at boot instead: hdr for bloom,
                // the camera generates the depth texture for dof.
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (urp != null)
                    urp.supportsHDR = true;
                var cam_go = new GameObject("main_camera", typeof(Camera));
                var cam = cam_go.GetComponent<Camera>();
                cam.depthTextureMode = UnityEngine.DepthTextureMode.Depth;
                // urp skips the whole post chain unless the camera opts in.
                var cam_data = cam.GetUniversalAdditionalCameraData();
                cam_data.renderPostProcessing = true;
                // live camera defaults; the fov/position come from the worksheet camera keys.
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.orthographic = false;
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 1f;
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

        // writes song jackets and character icons beside the exe as pngs.
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

        // writes the shader bundle's pathID -> name map beside the exe.
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

        // diagnostic: loads a cutt camera bundle and reports how the worksheet binds against the stub types.
        public static void probe_cutt_binding()
        {
            try
            {
                using var meta = UV2.Data.meta_reader.reader.open(config.meta_db_path);
                var rows = meta.lookup(new HashSet<string> { "cutt/cutt_son1004/son1004_camera" });
                var row = rows.GetValueOrDefault("cutt/cutt_son1004/son1004_camera");
                if (row == null) { Debug.Log("[probecutt] no meta row"); return; }
                var bundle = UV2.Data.game_assets.open(row, config.data_root);
                if (bundle == null) { Debug.Log("[probecutt] bundle open failed"); return; }
                Debug.Log($"[probecutt] bundle loaded: {bundle.GetAllAssetNames().Length} named assets");
                foreach (var n in bundle.GetAllAssetNames())
                    Debug.Log($"[probecutt] container: {n}");

                var typed = bundle.LoadAllAssets<Gallop.Live.Cutt.LiveTimelineWorkSheet>();
                Debug.Log($"[probecutt] LoadAllAssets<LiveTimelineWorkSheet>: {typed.Length}");
                foreach (var t in typed)
                {
                    if (t == null) { Debug.Log("[probecutt]   got: NULL"); continue; }
                    Debug.Log($"[probecutt]   got: {t.name} bound");
                    Debug.Log($"[probecutt]   version={t.version} sheetType={t.SheetType} totalLen={t.TotalTimeLength} variation={t.IsVariationSheet}");
                    Debug.Log($"[probecutt]   camPosKeys={t.cameraPosKeys?.thisList?.Count ?? -1} camFov={t.cameraFovKeys?.thisList?.Count ?? -1} motSeqs={t.charaMotSeqList?.Count ?? -1}");
                    if (t.charaMotSeqList != null && t.charaMotSeqList.Count > 0 &&
                        t.charaMotSeqList[0]?.keys?.thisList != null && t.charaMotSeqList[0].keys.thisList.Count > 0)
                        Debug.Log($"[probecutt]   first motion: {t.charaMotSeqList[0].keys.thisList[0].motionName} frame={t.charaMotSeqList[0].keys.thisList[0].frame}");
                    Debug.Log($"[probecutt]   formation groups: center={t.formationOffsetSet?.centerKeys?.thisList?.Count ?? -1}");
                }

                var mbs = bundle.LoadAllAssets<MonoBehaviour>();
                Debug.Log($"[probecutt] LoadAllAssets<MonoBehaviour>: {mbs.Length}");
                foreach (var mb in mbs)
                    Debug.Log($"[probecutt]   mb: {(mb == null ? "NULL" : mb.name + " type=" + mb.GetType().Name)}");

                var sos = bundle.LoadAllAssets<ScriptableObject>();
                Debug.Log($"[probecutt] LoadAllAssets<ScriptableObject>: {sos.Length}");

                var by_path = bundle.LoadAsset<Gallop.Live.Cutt.LiveTimelineWorkSheet>(
                    "assets/_gallopresources/bundle/resources/cutt/cutt_son1004/son1004_camera.asset");
                Debug.Log($"[probecutt] LoadAsset by path: {(by_path == null ? "NULL" : by_path.name + " len=" + by_path.TotalTimeLength)}");

                // probe the data prefab bundle plus the SO bundle it references, so the control's PPtr resolves.
                var rows2 = meta.lookup(new HashSet<string> { "cutt/cutt_son1004/cutt_son1004", "cutt/cutt_son1004/data" });
                var row2 = rows2.GetValueOrDefault("cutt/cutt_son1004/cutt_son1004");
                var row_so = rows2.GetValueOrDefault("cutt/cutt_son1004/data");
                var b_so = row_so == null ? null : UV2.Data.game_assets.open(row_so, config.data_root);
                if (b_so != null)
                {
                    var so_assets = b_so.LoadAllAssets<Gallop.Live.Cutt.LiveTimelineData>();
                    Debug.Log($"[probecutt] data-SO bundle LoadAllAssets<LiveTimelineData>: {so_assets.Length}");
                    foreach (var s in so_assets)
                        Debug.Log($"[probecutt]   SO: {(s == null ? "NULL" : s.name + " timeLength=" + s.timeLength + " msi=" + (s.characterSettings != null ? s.characterSettings.motionSequenceIndices.Count : -1) + " sheets=" + (s.worksheetList != null ? s.worksheetList.Count : -1))}");
                }
                if (row2 != null)
                {
                    var b2 = UV2.Data.game_assets.open(row2, config.data_root);
                    Debug.Log($"[probecutt] data bundle: {b2.GetAllAssetNames().Length} named assets");
                    foreach (var n2 in b2.GetAllAssetNames())
                        Debug.Log($"[probecutt]   data container: {n2}");
                    var main = b2.LoadAllAssets<GameObject>();
                    Debug.Log($"[probecutt] data LoadAllAssets<GameObject>: {main.Length}");
                    if (main.Length > 0)
                    {
                        var go = UnityEngine.Object.Instantiate(main[0]);
                        foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true))
                            Debug.Log($"[probecutt]   comp: {(c == null ? "MISSING SCRIPT" : c.GetType().Name + " on " + c.gameObject.name)}");
                        var ctrl = go.GetComponentInChildren<Gallop.Live.Cutt.LiveTimelineControl>(true);
                        var td = ctrl != null ? ctrl.data : null;
                        Debug.Log($"[probecutt]   LiveTimelineControl: {(ctrl == null ? "NO" : "bound")}");
                        Debug.Log($"[probecutt]   LiveTimelineData: {(td == null ? "NO" : "YES timeLength=" + td.timeLength + " msi=" + (td.characterSettings != null ? td.characterSettings.motionSequenceIndices.Count : -1) + " worksheets=" + (td.worksheetList != null ? td.worksheetList.Count : -1))}");
                        var dso = b2.LoadAllAssets<Gallop.Live.Cutt.LiveTimelineData>();
                        Debug.Log($"[probecutt] data LoadAllAssets<LiveTimelineData>: {dso.Length}");
                        foreach (var s in dso)
                            Debug.Log($"[probecutt]   data SO: {(s == null ? "NULL" : s.name + " timeLength=" + s.timeLength + " msi=" + (s.characterSettings != null ? s.characterSettings.motionSequenceIndices.Count : -1) + " sheets=" + (s.worksheetList != null ? s.worksheetList.Count : -1))}");
                        UnityEngine.Object.Destroy(go);
                    }
                }
                else
                {
                    Debug.Log("[probecutt] no meta row for cutt/cutt_son1004/cutt_son1004");
                }
            }
            catch (Exception e)
            {
                Debug.Log($"[probecutt] failed: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
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
