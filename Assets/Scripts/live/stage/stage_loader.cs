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
                // the worksheet comes straight from the game's cutt camera bundle,
                // deserialized by the generated stub; no extraction, no sidecar.
                ws = worksheet_reader.load(sel.music_id);
                if (ws == null)
                {
                    last_error = $"no worksheet in the cutt bundles for song {sel.music_id}";
                    return false;
                }
                clock = new timeline_clock();
                trace_log.write($"worksheet bound: {ws.camera_pos.Count} cam keys, {ws.motion_sequences.Count} motion seqs, {ws.formation.Count} formation groups, total {ws.total_frames} frames");

                // the game's shader bundle must be resident before any material-bearing
                // bundle loads, or their shader externals resolve to the magenta fallback.
                var shader_row = meta_row("shader");
                if (shader_row != null)
                {
                    bool shaders_ok = shader_manager.ensure_loaded(shader_row, config.data_root);
                    trace_log.write($"shader bundle: {(shaders_ok ? "loaded" : "FAILED")}");
                }
                else
                    trace_log.write("shader bundle: NO META ROW");

                // stage: materials from the controller's prereq list, then the
                // controller itself, all resolved from the selection's stage id.
                var controllers = manifest_reader.stage_bundles(sel.stage_id);
                string first_controller = controllers.FirstOrDefault(c => meta_row(c) != null)
                                          ?? controllers.FirstOrDefault();
                if (first_controller == null)
                {
                    last_error = $"no stage controller for stage {sel.stage_id}";
                    return false;
                }
                var material_names = manifest_reader.stage_materials(sel.stage_id, first_controller);
                trace_log.write($"stage {sel.stage_id}: controller '{first_controller}', {material_names.Count} material bundles");
                foreach (var name in material_names.Concat(new[] { first_controller }))
                {
                    // the stage controller stub deserializes _stageObjects from the
                    // controller itself, so the plain game bundle carries everything.
                    var b = load_bundle_keep(name);
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
                                trace_log.write($"stage controller instantiated: {stage.name}");
                                shader_manager.fix_game_shaders(stage.transform, "stage");

                                // the controller prefab is a shell: the StageController stub
                                // deserialized the game's own _stageObjects list, which names
                                // every stage root. instantiate them all under one stage root.
                                var ctrl = stage.GetComponent<Gallop.Live.StageController>();
                                var geo_root = new GameObject("stage_geometry");
                                int placed = 0;
                                if (ctrl != null && ctrl._stageObjects != null)
                                {
                                    foreach (var go in ctrl._stageObjects)
                                    {
                                        if (go == null) continue;
                                        var piece = Instantiate(go, geo_root.transform);
                                        piece.name = go.name;
                                        placed++;
                                    }
                                }
                                Debug.Log($"[stage_loader] stage geometry: {placed} roots placed, renderers {geo_root.GetComponentsInChildren<Renderer>(true).Length}");
                                trace_log.write($"stage geometry: {placed} roots, {geo_root.GetComponentsInChildren<Renderer>(true).Length} renderers");
                                shader_manager.fix_game_shaders(geo_root.transform, "stage_geometry");
                                shader_manager.audit_shaders(geo_root.transform, "stage_geometry");
                            }
                        }
                    }
                }

                // cast
                int cast_loaded = 0, cast_missed = 0;
                foreach (var slot in sel.slots.Where(s => s.chara_id > 0))
                {
                    var root = load_character(slot.chara_id, slot.dress_id);
                    if (root != null) { chara_roots.Add(root); cast_loaded++; }
                    else cast_missed++;
                }
                trace_log.write($"cast: {cast_loaded} loaded, {cast_missed} missed, {chara_roots.Count} roots total");

                // motion clips: one bundle per authored motion name in the worksheet.
                var clip_names = new HashSet<string>();
                foreach (var seq in ws.motion_sequences)
                    foreach (var key in seq)
                        if (!string.IsNullOrEmpty(key.motion_name))
                            clip_names.Add(key.motion_name);
                var clips = load_clips(clip_names);
                trace_log.write($"motion: {clip_names.Count} authored names -> {clips.Count} clips loaded");

                wire_drivers(clips, sel.music_id);
                trace_log.write("drivers wired: camera_director, formation, motion; concert open returning true");
                return true;
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] open failed: {e}");
                return false;
            }
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
            int chara_renderers = instance.GetComponentsInChildren<Renderer>(true).Length;
            trace_log.write($"chara {chara_id} dress {dress_id} -> {body.prefab}: {chara_renderers} renderers");
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

        // loads every motion clip bundle for the song; each motion name resolves
        // to the game's 3d/motion/live/body bundle holding the authored clip.
        private Dictionary<string, AnimationClip> load_clips(HashSet<string> motion_names)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var motion_name in motion_names)
            {
                // the authored form is son1004/anm_liv_son1004_1st: the meta db keys
                // the motion bundle by song + clip name (disk lowercases _L/_R).
                string song_part = motion_name.Substring(0, motion_name.IndexOf('/'));
                string clip = motion_name.Substring(motion_name.LastIndexOf('/') + 1);
                string name = $"3d/motion/live/body/{song_part}/{clip.ToLowerInvariant()}";
                var row = meta_row(name);
                if (row == null)
                    row = meta_row($"3d/motion/live/body/{song_part}/{clip.ToUpperInvariant()}");
                if (row == null) continue;
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) continue;
                foreach (var asset in bundle.GetAllAssetNames())
                {
                    var loaded_clip = bundle.LoadAsset<AnimationClip>(asset);
                    if (loaded_clip != null) clips[loaded_clip.name] = loaded_clip;
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

        private void wire_drivers(Dictionary<string, AnimationClip> clips, int music_id)
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
            global_shade.publish(FindObjectOfType<Camera>());

            // heartbeat: camera state + what is actually visible, once per second.
            if (Time.time - _last_beat >= 1f)
            {
                _last_beat = Time.time;
                var cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
                if (cam == null) { trace_log.write($"beat {Time.time:0}: NO CAMERA"); return; }
                int visible = 0, total = 0;
                foreach (var r in FindObjectsOfType<Renderer>())
                {
                    total++;
                    if (r.isVisible) visible++;
                }
                int playing = 0;
                foreach (var a in FindObjectsOfType<Animation>())
                    if (a.isPlaying) playing++;
                trace_log.write($"beat t={clock?.time ?? 0f:0.0}s cam_pos {cam.transform.position} fov {cam.fieldOfView:0.0} renderers {visible}/{total} visible animations_playing {playing}");
            }
        }

        private float _last_beat = -1f;
    }
}
