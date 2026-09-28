using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Concert;
using UV2.Data;
using UV2.Live;

namespace UV2.Live
{
    // one song's resolved bundles: stage controller, materials, motion clips.
    [Serializable]
    public class song_manifest
    {
        public string stage_id;
        public List<string> stage_controller = new();
        public List<string> stage_materials = new();
        public Dictionary<string, string> motion_clips = new();
    }

    // assembles the concert from the manifest: stage prefab, cast prefabs, motion
    // clips, then wires the timeline drivers onto them.
    public class stage_loader : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private readonly List<Transform> chara_roots = new();
        private camera_director director;
        private formation_driver formation;
        private motion_player motion;

        public IReadOnlyList<Transform> characters => chara_roots;

        // builds the whole concert for the selection; false when data is missing
        // (caller falls back to the summary screen with the reason shown).
        public string last_error { get; private set; }

        public bool open(selection_state sel)
        {
            try
            {
                var manifest = manifest_reader.song(sel.music_id);
                if (manifest == null)
                {
                    last_error = $"no concert manifest for song {sel.music_id}";
                    return false;
                }

                ws = worksheet_reader.load(sel.music_id);
                if (ws == null)
                {
                    last_error = $"no extracted worksheet for song {sel.music_id}";
                    return false;
                }
                clock = new timeline_clock();

                // the game's shader bundle must be resident before any material-bearing
                // bundle loads, or their shader externals resolve to the magenta fallback.
                var shader_row = meta_row("shader");
                if (shader_row != null)
                    shader_manager.ensure_loaded(shader_row, config.data_root);
                shader_manager.load_map();

                // stage materials first (the controller prefab references them),
                // then the controller itself.
                foreach (var name in manifest.stage_materials.Concat(manifest.stage_controller))
                {
                    // the repacked geo bundle carries the same serialized files plus the
                    // full container; loading both trips unity's duplicate-file check.
                    // the game bundle still loads the controller shell; the geo bundle
                    // loads last, from the datapack, providing every named root.
                    var b = name.Contains("controller") ? load_stage_controller_with_geo(name, manifest) : load_bundle_keep(name);
                    Debug.Log($"[stage_loader] bundle {name}: {(b == null ? "FAILED" : "ok, assets: " + b.GetAllAssetNames().Length)}");
                    if (b != null && name.Contains("controller"))
                    {
                        // instantiate the stage controller prefab
                        string[] all = b.GetAllAssetNames();
                        string prefab_name = all.FirstOrDefault(n => n.EndsWith(".prefab"));
                        if (prefab_name != null)
                        {
                            var prefab = b.LoadAsset<GameObject>(prefab_name);
                            if (prefab != null)
                            {
                                var stage = Instantiate(prefab);
                                Debug.Log($"[stage_loader] stage instantiated: {stage.name}, renderers {stage.GetComponentsInChildren<Renderer>(true).Length}");
                                shader_manager.fix_game_shaders(stage.transform, "stage");

                                // the controller prefab is a shell: the real geometry lives as
                                // standalone root prefabs the game's StageController assembles.
                                // when this bundle is the geo repack its container names every
                                // root, so instantiate them all under one stage root.
                                if (all.Length > 1)
                                {
                                    var geo_root = new GameObject("stage_geometry");
                                    int placed = 0;
                                    foreach (var geo_name in all)
                                    {
                                        if (geo_name == prefab_name) continue;
                                        var go = b.LoadAsset<GameObject>(geo_name);
                                        if (go == null) continue;
                                        var piece = Instantiate(go, geo_root.transform);
                                        piece.name = go.name;
                                        placed++;
                                    }
                                    Debug.Log($"[stage_loader] stage geometry: {placed} roots placed, renderers {geo_root.GetComponentsInChildren<Renderer>(true).Length}");
                                    shader_manager.fix_game_shaders(geo_root.transform, "stage_geometry");
                                    shader_manager.audit_shaders(geo_root.transform, "stage_geometry");
                                }
                                else
                                {
                                    Debug.LogWarning($"[stage_loader] no stage geo repack for {manifest.stage_id}; stage is the controller shell only");
                                }
                            }
                        }
                    }
                }

                // cast
                foreach (var slot in sel.slots.Where(s => s.chara_id > 0))
                {
                    var root = load_character(slot.chara_id, slot.dress_id);
                    if (root != null) chara_roots.Add(root);
                }

                // motion clips: bundle per motionName, bound to the characters
                var clip_names = new HashSet<string>(manifest.motion_clips.Values);
                var clips = load_clips(clip_names);

                wire_drivers(clips, manifest, sel.music_id);
                return true;
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] open failed: {e}");
                return false;
            }
        }

        // the game's controller bundle only lists its shell in the container; the
        // repacked datapack variant carries the shell plus every stage root. loads
        // the repack when it exists (same serialized files, fuller container).
        private AssetBundle load_stage_controller_with_geo(string name, song_manifest manifest)
        {
            string geo_path = System.IO.Path.Combine(config.datapack_path,
                $"stage_geo_{manifest.stage_id}.unity3d");
            if (!System.IO.File.Exists(geo_path))
                geo_path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                    "data", $"stage_geo_{manifest.stage_id}.unity3d");
            if (System.IO.File.Exists(geo_path))
            {
                var geo_bundle = AssetBundle.LoadFromFile(geo_path);
                if (geo_bundle != null)
                {
                    Debug.Log($"[stage_loader] geo repack loaded for {manifest.stage_id}: {geo_bundle.GetAllAssetNames().Length} named assets");
                    return geo_bundle;
                }
            }
            return load_bundle_keep(name);
        }

        // opens a bundle by manifest name and keeps it resident.
        private AssetBundle load_bundle_keep(string name)
        {
            var row = meta_row(name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            return bundle;
        }

        // loads one character's body prefab from the manifest naming.
        private Transform load_character(int chara_id, int dress_id)
        {
            var body_opt = manifest_reader.chara_body(chara_id, dress_id);
            if (body_opt == null)
            {
                Debug.LogWarning($"[stage_loader] no body entry for chara {chara_id} dress {dress_id}");
                return null;
            }
            var body = body_opt.Value;

            var row = meta_row(body.bundle);
            if (row == null) { Debug.LogWarning($"[stage_loader] chara {chara_id}: no meta row for {body.bundle}"); return null; }

            // the body prefab's materials/ikcols live in prereq bundles: load them
            // first so the prefab's external material refs resolve instead of null.
            if (!string.IsNullOrEmpty(row.prereq))
            {
                foreach (var pre in row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var pre_row = meta_row(pre.Trim());
                    var pre_bundle = pre_row == null ? null : game_assets.open(pre_row, config.data_root);
                    Debug.Log($"[stage_loader] chara {chara_id} prereq {pre.Trim()}: {(pre_bundle == null ? "FAILED" : "ok, assets: " + pre_bundle.GetAllAssetNames().Length)}");
                }
            }

            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) { Debug.LogWarning($"[stage_loader] chara {chara_id}: bundle open failed {body.bundle}"); return null; }

            string[] all = bundle.GetAllAssetNames();
            string prefab_name = all.FirstOrDefault(n => n.EndsWith(body.prefab + ".prefab"))
                                 ?? all.FirstOrDefault(n => n.EndsWith(".prefab"));
            if (prefab_name == null)
            {
                Debug.LogWarning($"[stage_loader] chara {chara_id}: no prefab in {body.bundle} (want {body.prefab}; assets: {string.Join(", ", all.Take(4))})");
                bundle.Unload(true);
                return null;
            }

            var prefab = bundle.LoadAsset<GameObject>(prefab_name);
            bundle.Unload(false);
            if (prefab == null) { Debug.LogWarning($"[stage_loader] chara {chara_id}: LoadAsset null for {prefab_name}"); return null; }

            var instance = Instantiate(prefab);
            Debug.Log($"[stage_loader] chara {chara_id} dress {dress_id} -> {body.prefab}");
            shader_manager.fix_game_shaders(instance.transform, $"chara {chara_id}");
            return instance.transform;
        }

        // fixes materials whose shader externals resolved to fallbacks.
        private void fix_character_shaders(GameObject root)
        {
            int fixed_count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var sh = m.shader;
                    if (sh == null || sh.name == "Hidden/InternalErrorShader" ||
                        (sh.name != null && sh.name.Contains("Fallback")))
                    {
                        Debug.LogWarning($"[stage_loader] fallback shader on {r.name}: {m.name}");
                        fixed_count++;
                    }
                }
            }
            if (fixed_count > 0)
                Debug.LogWarning($"[stage_loader] {root.name}: {fixed_count} fallback-shader materials (shaders load order fix pending)");
        }

        private void log_material_state(GameObject root)
        {
            int n = 0, fallback = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    n++;
                    if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                        fallback++;
                }
            }
            Debug.Log($"[stage_loader] {root.name}: {n} materials, {fallback} fallbacks");
        }

        // loads every motion clip bundle for the song.
        private Dictionary<string, AnimationClip> load_clips(HashSet<string> bundle_names)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var name in bundle_names)
            {
                var row = meta_row(name);
                if (row == null) continue;
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) continue;
                foreach (var asset in bundle.GetAllAssetNames())
                {
                    var clip = bundle.LoadAsset<AnimationClip>(asset);
                    if (clip != null) clips[clip.name] = clip;
                }
                bundle.Unload(false);
            }
            Debug.Log($"[stage_loader] motion clips loaded: {clips.Count}");
            return clips;
        }

        private meta_reader.asset_row meta_row(string name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { name });
            return rows?.GetValueOrDefault(name);
        }

        private void wire_drivers(Dictionary<string, AnimationClip> clips, song_manifest manifest, int music_id)
        {
            var cam = FindObjectOfType<Camera>();
            if (cam == null)
            {
                var cam_go = new GameObject("main_camera", typeof(Camera));
                cam = cam_go.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 1000f;
            }

            var director_go = new GameObject("camera_director");
            director = director_go.AddComponent<camera_director>();
            director.open(ws, clock, chara_roots, cam);

            var formation_go = new GameObject("formation_driver");
            formation = formation_go.AddComponent<formation_driver>();
            formation.open(ws, clock, chara_roots);

            var motion_go = new GameObject("motion_player");
            motion = motion_go.AddComponent<motion_player>();
            var seq_map = slot_sequences.for_song(music_id);
            Debug.Log($"[stage_loader] slot->sequence map: {(seq_map == null ? "none" : seq_map.Count + " slots")}");
            motion.open(ws, clock, chara_roots, seq_map ?? new List<int>());

            // bind + start every clip that matches a sequence's motionName
            foreach (var seq in ws.motion_sequences)
            {
                foreach (var key in seq)
                {
                    if (string.IsNullOrEmpty(key.motion_name)) continue;
                    string short_name = key.motion_name.Substring(key.motion_name.LastIndexOf('/') + 1);
                    if (clips.TryGetValue(short_name, out var clip))
                        motion.bind_clip_all(clip);
                }
            }
            motion.play();
        }

        private void Update()
        {
            clock?.advance(Time.deltaTime);
        }
    }
}
