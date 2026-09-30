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
        // e2e switch: run the timeline on real time even when audio binds.
        public static bool force_free_clock;
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

                // selections written by older launchers carry no stage id; resolve
                // it from the game's livesettings the same way the catalog does.
                if (sel.stage_id <= 0)
                {
                    sel.stage_id = resolve_stage_id(sel.music_id);
                    trace_log.write($"stage id resolved at runtime: {sel.stage_id}");
                }

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

                start_music(sel.music_id);
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

            // the body ships without its head: the head prefab lives in the
            // chr{chara}_00 head bundle (mchr for mini casts); parent its Head
            // bone onto the body's Head bone so both share one skeleton.
            bool mini = body.bundle.StartsWith("3d/chara/mini/");
            int head_renderers = attach_head(instance.transform, chara_id, mini);

            int chara_renderers = instance.GetComponentsInChildren<Renderer>(true).Length;
            trace_log.write($"chara {chara_id} dress {dress_id} -> {body.prefab}: {chara_renderers} renderers (head +{head_renderers})");
            shader_manager.fix_game_shaders(instance.transform, $"chara {chara_id}");

            // generic bodies ship with null texture slots; the game assigns
            // per-character variant textures at runtime (v1's IsGeneric path).
            // runs after the shader swap so the material properties exist.
            assign_generic_body_textures(instance.transform, chara_id, body.bundle);
            return instance.transform;
        }

        // finds the shader's main texture property name (the gallop family
        // may not call it _MainTex).
        private static string shader_tex_prop(Shader sh)
        {
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    var n = sh.GetPropertyName(i);
                    if (n.StartsWith("_M") || n.StartsWith("_T")) return n;
                }
            }
            return null;
        }

        // assigns the generic body's four texture slots from the install's
        // per-character variant rows; names follow the game's default costume
        // family: diff/shad_c keyed on (skin, bust), base/ctrl keyed on bust.
        private void assign_generic_body_textures(Transform body_root, int chara_id, string body_bundle)
        {
            string folder = body_bundle.Split('/')[3];
            using var db = master_db.reader.open(config.master_db_path);
            if (db == null) return;
            var rows = db.query(
                $"SELECT skin, bust FROM chara_data WHERE id={chara_id}");
            if (rows.Count == 0) return;
            int skin = (int)rows[0].get_int(0);
            int bust = (int)rows[0].get_int(1);

            bool any_null = false;
            string slot_report = "";
            foreach (var r in body_root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                slot_report += $"[{r.name}:mats={mats.Length}] ";
                foreach (var m in mats)
                {
                    if (m == null) { slot_report += "nullmat "; continue; }
                    var sh = m.shader;
                    string sh_desc = sh == null ? "noshader" : $"{sh.name}/props:{sh.GetPropertyCount()}";
                    slot_report += $"{m.name.Replace("(Instance)","")}[{sh_desc}{(sh != null && sh.GetPropertyCount() > 0 && shader_tex_prop(sh) != null ? "/tex=" + shader_tex_prop(sh) : "")}] ";
                    Texture cur = null;
                    try { cur = m.GetTexture("_MainTex"); } catch { }
                    slot_report += $":main={(cur == null ? "null" : "set")} ";
                    if (cur == null && m.name.Contains("bdy")) any_null = true;
                }
            }
            trace_log.write($"generic body {chara_id} slots: {slot_report}");
            if (!any_null) return;

            string tex_dir = $"3d/chara/body/{folder}/textures";
            var names = new HashSet<string>
            {
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_diff",
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_shad_c",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_base",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_ctrl",
            };
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return;
            var tex_rows = meta.lookup(names);
            if (tex_rows.Count == 0) { trace_log.write($"generic body {chara_id}: no texture rows"); return; }

            var loaded = new Dictionary<string, Texture2D>();
            foreach (var kv in tex_rows)
            {
                var bundle = game_assets.open(kv.Value, config.data_root);
                if (bundle == null) continue;
                foreach (var n in bundle.GetAllAssetNames())
                {
                    var t = bundle.LoadAsset<Texture2D>(n);
                    if (t != null) loaded[n] = t;
                }
            }
            if (loaded.Count == 0) { trace_log.write($"generic body {chara_id}: textures failed to load"); return; }

            int assigned = 0;
            foreach (var r in body_root.GetComponentsInChildren<Renderer>(true))
            {
                // the body material asset is shared cast-wide; instance it so
                // each character keeps their own skin/bust variant.
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null || !m.name.Contains("bdy")) continue;
                    Texture cur = null;
                    try { cur = m.GetTexture("_MainTex"); } catch { }
                    if (cur == null)
                    {
                        var diff = loaded.FirstOrDefault(kv => kv.Key.Contains($"_{skin}_{bust}_diff")).Value;
                        if (diff != null) { m.SetTexture("_MainTex", diff); assigned++; }
                    }
                    Texture toon = null;
                    try { toon = m.GetTexture("_ToonMap"); } catch { }
                    if (toon == null)
                    {
                        var shad = loaded.FirstOrDefault(kv => kv.Key.Contains($"_{skin}_{bust}_shad_c")).Value;
                        if (shad != null) { m.SetTexture("_ToonMap", shad); assigned++; }
                    }
                    Texture tri = null;
                    try { tri = m.GetTexture("_TripleMaskMap"); } catch { }
                    if (tri == null)
                    {
                        var base_t = loaded.FirstOrDefault(kv => kv.Key.Contains($"_0_{bust}_base")).Value;
                        if (base_t != null) { m.SetTexture("_TripleMaskMap", base_t); assigned++; }
                    }
                    Texture opt = null;
                    try { opt = m.GetTexture("_OptionMaskMap"); } catch { }
                    if (opt == null)
                    {
                        var ctrl = loaded.FirstOrDefault(kv => kv.Key.Contains($"_0_{bust}_ctrl")).Value;
                        if (ctrl != null) { m.SetTexture("_OptionMaskMap", ctrl); assigned++; }
                    }
                }
                r.materials = mats;
            }
            trace_log.write($"generic body {chara_id}: {assigned} texture slots assigned ({loaded.Count} textures loaded)");
        }

        // loads the character's head prefab and parents its Head bone under the
        // body's Head bone; mini casts use the chibi head tree. returns the
        // renderers the head added (0 on failure).
        private int attach_head(Transform body_root, int chara_id, bool mini)
        {
            string head_name = mini
                ? $"3d/chara/mini/head/mchr{chara_id:d4}_00/pfb_mchr{chara_id:d4}_00_hair"
                : $"3d/chara/head/chr{chara_id}_00/pfb_chr{chara_id}_00";
            var row = meta_row(head_name);
            if (row == null)
            {
                trace_log.write($"chara {chara_id}: no head bundle row {head_name}");
                return 0;
            }
            if (!string.IsNullOrEmpty(row.prereq))
                foreach (var pre in row.prereq.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var pre_row = meta_row(pre.Trim());
                    if (pre_row != null) game_assets.open(pre_row, config.data_root);
                }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) { trace_log.write($"chara {chara_id}: head bundle open failed"); return 0; }

            string[] all = bundle.GetAllAssetNames();
            string prefab_name = all.FirstOrDefault(n => n.EndsWith($"/{row.name.Split('/').Last()}.prefab"))
                                 ?? all.FirstOrDefault(n => n.EndsWith(".prefab"));
            var prefab = string.IsNullOrEmpty(prefab_name) ? null : bundle.LoadAsset<GameObject>(prefab_name);
            if (prefab == null) { trace_log.write($"chara {chara_id}: head LoadAsset null"); return 0; }

            var head = Instantiate(prefab);
            var body_head_bone = body_root.GetComponentInChildren<Transform>().Find("Head")
                ?? find_deep(body_root, "Head");
            var head_head_bone = find_deep(head.transform, "Head");
            if (body_head_bone == null || head_head_bone == null)
            {
                trace_log.write($"chara {chara_id}: head bones not found (body {body_head_bone != null}, head {head_head_bone != null})");
                return head.GetComponentsInChildren<Renderer>(true).Length;
            }
            head_head_bone.SetParent(body_head_bone, false);
            // the head prefab root stays as a sibling shell; keep it for materials.
            return head.GetComponentsInChildren<Renderer>(true).Length;
        }

        // depth-first name search through a hierarchy.
        private Transform find_deep(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.name == name) return c;
                var hit = find_deep(c, name);
                if (hit != null) return hit;
            }
            return null;
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
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 100f;
            }
            // one listener for the whole concert; audio dies without it.
            if (cam.GetComponent<AudioListener>() == null && FindObjectOfType<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();

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

        // samples the worksheet's global-light track for the current frame and
        // hands the interpolated key to the shade publisher.
        private void update_light_track()
        {
            if (ws == null || ws.global_light.Count == 0) return;
            float frame = clock?.time ?? 0f;
            frame *= 60f;

            var keys = ws.global_light;
            int last = keys.Count - 1;
            if (frame <= keys[0].frame) { global_shade.set_light_track(keys[0], 0f); return; }
            if (frame >= keys[last].frame) { global_shade.set_light_track(keys[last], 1f); return; }

            for (int i = 0; i < last; i++)
            {
                if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                {
                    float span = keys[i + 1].frame - keys[i].frame;
                    float blend = span <= 0 ? 0f : (frame - keys[i].frame) / span;
                    global_shade.set_light_track(keys[i], blend);
                    return;
                }
            }
        }

        private AudioSource music_source;

        // resolves the song's instrumental bank from the install, decodes it,
        // and starts playback; the concert clock locks to the source.
        private void start_music(int music_id)
        {
            // the game ships _01 and _02 oke variants per song; take whichever
            // the install carries.
            var candidates = new[]
            {
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_01.awb",
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_02.awb",
            };
            meta_reader.asset_row bank_row = null;
            foreach (var c in candidates)
            {
                bank_row = meta_row(c);
                if (bank_row != null) break;
            }
            if (bank_row == null)
            {
                trace_log.write($"music: no oke bank for song {music_id}");
                return;
            }

            string path = System.IO.Path.Combine(config.data_root, "dat", bank_row.hash.Substring(0, 2), bank_row.hash);
            if (!System.IO.File.Exists(path))
            {
                trace_log.write($"music: oke bank missing on disk: {path}");
                return;
            }
            byte[] bank = System.IO.File.ReadAllBytes(path);
            var waves = live_audio.parse_afs2(bank);
            if (waves.Count == 0)
            {
                trace_log.write("music: afs2 parse found no waves");
                return;
            }

            var clip = live_audio.decode_wave(bank, waves[0], $"oke_{music_id}");
            if (clip == null)
            {
                trace_log.write("music: oke decode failed");
                return;
            }

            var go = new GameObject("live_music");
            music_source = go.AddComponent<AudioSource>();
            music_source.clip = clip;
            music_source.loop = false;
            music_source.Play();
            if (!force_free_clock) clock.bind_master(music_source);
            else trace_log.write("clock: forced free-run for this run");
            trace_log.write($"music: oke playing ({clip.frequency}Hz, {clip.length:0.0}s, {waves.Count} waves)");
        }

        // reads the song's livesettings from the game install and picks the
        // stage id row, for selections that never carried one.
        private int resolve_stage_id(int music_id)
        {
            var ls_row = meta_row("livesettings");
            if (ls_row == null) return -1;
            var ls_bundle = game_assets.open(ls_row, config.data_root);
            if (ls_bundle == null) return -1;
            if (!ls_bundle.Contains(music_id.ToString())) return -1;
            var text = ls_bundle.LoadAsset<TextAsset>(music_id.ToString());
            var rows = livesettings.parse_csv(text != null ? text.text : null);
            return livesettings.stage_id(rows);
        }

        private void Update()
        {
            clock?.advance(Time.deltaTime);
            motion?.play();
            update_light_track();
            global_shade.publish(FindObjectOfType<Camera>());
            global_shade.publish_chara_block(chara_roots);

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
                // center-pixel color: a uniform gray reading means nothing
                // rendered even when the renderer counts say otherwise.
                var probe = new Texture2D(1, 1);
                probe.ReadPixels(new Rect(cam.pixelWidth / 2, cam.pixelHeight / 2, 1, 1), 0, 0);
                probe.Apply();
                var px = probe.GetPixel(0, 0);
                trace_log.write($"px {px.r:0.00},{px.g:0.00},{px.b:0.00}");
                Destroy(probe);

                // one character's head bone: movement across beats proves the
                // direct-sample motion actually poses the cast.
                string pose = "";
                if (chara_roots.Count > 0)
                {
                    var head = find_deep(chara_roots[0], "Head");
                    if (head != null) pose = $" head {head.position} hrot {head.localEulerAngles}";
                }
                trace_log.write($"beat t={clock?.time ?? 0f:0.0}s cam_pos {cam.transform.position} fwd {cam.transform.forward} fov {cam.fieldOfView:0.0} renderers {visible}/{total} visible animations_playing {playing}{pose}");

                // once: the render state of chara 1 - shader, keywords, clip
                // distances - so a black frame on a real gpu points at the
                // exact material the gpu rejected.
                if (!_render_state_dumped && chara_roots.Count > 0)
                {
                    _render_state_dumped = true;
                    var r0 = chara_roots[0].GetComponentsInChildren<Renderer>().FirstOrDefault();
                    if (r0 != null)
                    {
                        var m0 = r0.sharedMaterial;
                        trace_log.write($"render_state: chara1 '{r0.name}' shader '{(m0 != null ? m0.shader.name : "<null>")}' keywords [{(m0 != null ? string.Join(",", m0.shaderKeywords) : "")}] bounds {r0.bounds}");
                    }
                    var stage_r = FindObjectsOfType<Renderer>().FirstOrDefault(x => x.name.Contains("env"));
                    if (stage_r != null)
                    {
                        var ms = stage_r.sharedMaterial;
                        trace_log.write($"render_state: stage '{stage_r.name}' shader '{(ms != null ? ms.shader.name : "<null>")}' keywords [{(ms != null ? string.Join(",", ms.shaderKeywords) : "")}] bounds {stage_r.bounds}");
                    }
                }
            }
        }

        private float _last_beat = -1f;
        private bool _render_state_dumped;
    }
}
