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

        // builds the whole concert for the selection; yields between load
        // phases so the ui paints live progress. C# forbids yields inside
        // try/catch, so each phase is a plain method that returns an error
        // string (null = keep going) and the coroutine yields between phases.
        private System.Collections.IEnumerator open_core(selection_state sel)
        {
            if (!run_phase_worksheet(sel)) yield break;
            volume_uv_scroll.reset();
            mob_control.reset();
            cyalume.reset_pen_meshes();
            crowd_rig.reset();
            audience_keys.reset();
            UV2.UI.load_progress.report($"binding worksheet ({ws.camera_pos.Count} cam keys)");
            trace_log.write($"worksheet bound: {ws.camera_pos.Count} cam keys, {ws.motion_sequences.Count} motion seqs, {ws.formation.Count} formation groups, total {ws.total_frames} frames");
            yield return null;

            if (!run_phase_stage(sel)) yield break;
            int stage_step = 0;
            var stage_bundles = _stage_material_names.Concat(new[] { _stage_first_controller }).ToList();
            foreach (var name in stage_bundles)
            {
                UV2.UI.load_progress.report($"loading stage {sel.stage_id}: bundle {++stage_step}/{stage_bundles.Count}");
                yield return null;
                if (!run_phase_stage_bundle(name)) yield break;
            }

            // the laser + spotlight fixtures come from the controller's
            // loose-object lists and the common spotlight3d bundle.
            yield return instantiate_laser_fixtures();
            yield return instantiate_spotlight_fixtures();

            blink_lights.bind(ws?.blink_tracks, null);
            spot_lights.bind(ws?.spot_tracks);
            laser_lights.bind(ws?.laser_tracks);
            foot_light.reset();
            foot_light.bind(ws?.foot_light);
            volume_uv_scroll.bind(ws?.volume_tracks, ws?.uv_scroll_tracks);
            wash_light.bind(ws?.wash_tracks);
            additional_light.bind(ws?.additional_tracks);

            // the crowd rows: the rig instantiates the audience prefabs, the
            // cyalume textures resolve from the install, the mob/cyalume group
            // tracks drive the crowd rig per the decoded consumers.
            UV2.UI.load_progress.report("binding crowd");
            yield return null;
            bind_crowd(sel);

            int cast_step = 0, cast_total = sel.slots.Count(s => s.chara_id > 0);
            foreach (var slot in sel.slots.Where(s => s.chara_id > 0))
            {
                UV2.UI.load_progress.report($"loading cast {++cast_step}/{cast_total}");
                yield return null;
                var root = run_phase_character(slot);
                if (root == null) { _cast_missed++; continue; }
                chara_roots.Add(root);
                UV2.Live.chara_parts.record(root);
                UV2.Live.chara_parts.record_height(root, slot.chara_id);
            }
            trace_log.write($"cast: {chara_roots.Count} loaded, {_cast_missed} missed, {chara_roots.Count} roots total");

            UV2.UI.load_progress.report("loading motion clips");
            yield return null;
            var clips = run_phase_clips(sel);
            if (clips == null) yield break;

            wire_drivers(clips, sel.music_id);
            trace_log.write("drivers wired: camera_director, formation, motion; concert open returning true");

            UV2.UI.load_progress.report("starting music");
            yield return null;
            start_music(sel.music_id);
            UV2.UI.load_progress.report("concert ready");
            _open_ok = true;
        }

        // non-yielding phase helpers, each wrapped in try/catch, returning
        // false/null on failure with last_error set.
        private bool run_phase_worksheet(selection_state sel)
        {
            try
            {
                ws = worksheet_reader.load(sel.music_id);
                if (ws == null) { last_error = $"no worksheet in the cutt bundles for song {sel.music_id}"; return false; }
                clock = new timeline_clock();
                if (sel.stage_id <= 0)
                {
                    sel.stage_id = resolve_stage_id(sel.music_id);
                    trace_log.write($"stage id resolved at runtime: {sel.stage_id}");
                }
                var shader_row = meta_row("shader");
                if (shader_row != null)
                {
                    bool shaders_ok = shader_manager.ensure_loaded(shader_row, config.data_root);
                    trace_log.write($"shader bundle: {(shaders_ok ? "loaded" : "FAILED")}");
                }
                else
                    trace_log.write("shader bundle: NO META ROW");

                var controllers = manifest_reader.stage_bundles(sel.stage_id);
                _stage_first_controller = controllers.FirstOrDefault(c => meta_row(c) != null)
                                          ?? controllers.FirstOrDefault();
                if (_stage_first_controller == null)
                {
                    last_error = $"no stage controller for stage {sel.stage_id}";
                    return false;
                }
                _stage_material_names = manifest_reader.stage_materials(sel.stage_id, _stage_first_controller);
                trace_log.write($"stage {sel.stage_id}: controller '{_stage_first_controller}', {_stage_material_names.Count} material bundles");
                return true;
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] open phase failed: {e}");
                return false;
            }
        }

        // the laser fixtures live in the stage controller bundle as loose
        // GameObjects bound into _laserObjects (never carried by
        // Instantiate(stagePrefab)); instantiate them like the stage objects.
        private System.Collections.IEnumerator instantiate_laser_fixtures()
        {
            var ctrl = _stage_controller;
            if (ctrl == null || ctrl._laserObjects == null) yield break;
            var geo = GameObject.Find("stage_geometry");
            var parent = geo != null ? geo.transform : null;
            int placed = 0;
            foreach (var go in ctrl._laserObjects)
            {
                if (go == null) continue;
                var piece = Instantiate(go, parent);
                piece.name = go.name;
                placed++;
                foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                    blink_lights.record_stage_child(child.name, child.gameObject);
            }
            trace_log.write($"laser fixtures instantiated: {placed}");
        }

        // the common spotlight3d bundle carries the fixture prefabs; the
        // worksheet's containers bind by the asset names (spotlight3d000..).
        private System.Collections.IEnumerator instantiate_spotlight_fixtures()
        {
            var row = meta_row("3d/env/live/common/spotlight3d/pfb_env_live_cmn_spotlight3d_controller000");
            if (row == null) { trace_log.write("spotlight fixtures: NO META ROW for the common bundle"); yield break; }
            if (!string.IsNullOrEmpty(row.prereq))
            {
                foreach (var pre in row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var pre_row = meta_row(pre.Trim());
                    if (pre_row != null) game_assets.open(pre_row, config.data_root);
                }
            }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) { trace_log.write("spotlight fixtures: bundle FAILED to open"); yield break; }
            // one-shot probe: what the bundle exposes as asset names (the
            // game's container paths + short names differ).
            trace_log.write($"spotlight bundle assets: {string.Join(", ", bundle.GetAllAssetNames().Take(8))}");

            var geo = GameObject.Find("stage_geometry");
            var parent = geo != null ? geo.transform : null;
            // the 3 fixture prefabs are separate root objects in the same
            // serialized file, addressable only through the controller's
            // AssetHolder table (spotlight3dNNN -> the prefab PPtr binds at
            // deserialization); instantiate from that table.
            int placed = 0;
            var controller = bundle.LoadAsset<GameObject>("pfb_env_live_cmn_spotlight3d_controller000");
            var holder = controller != null ? controller.GetComponent<Gallop.AssetHolder>() : null;
            if (holder != null)
            {
                foreach (var entry in holder._assetTable.list)
                {
                    if (entry?.Value == null) { trace_log.write("spotlight fixture: a table entry did not bind"); continue; }
                    var piece = Instantiate(entry.Value, parent);
                    piece.name = entry.Value.name;
                    placed++;
                    foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                        blink_lights.record_stage_child(child.name, child.gameObject);
                    blink_lights.record_stage_child(entry.Key, piece);
                    trace_log.write($"spotlight fixture '{entry.Key}' -> '{entry.Value.name}' placed");
                }
            }
            else
            {
                trace_log.write("spotlight fixtures: no AssetHolder on the controller");
            }
            trace_log.write($"spotlight fixtures instantiated: {placed}");
        }

        private bool run_phase_stage(selection_state sel) => true;

        private bool run_phase_stage_bundle(string name)
        {
            try
            {
                var b = load_bundle_keep(name);
                Debug.Log($"[stage_loader] bundle {name}: {(b == null ? "FAILED" : "ok, assets: " + b.GetAllAssetNames().Length)}");
                if (b != null && name.Contains("controller"))
                {
                    // the controller's externals (crowd rig, audience prefab,
                    // sky, fixtures) live in its prereq bundles: open them
                    // first so the prefab's external refs resolve.
                    load_controller_prereqs(name);
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
                            // the controller prefab's own hierarchy feeds the
                            // light drivers too (the fixtures the game keeps
                            // outside _stageObjects live here).
                            foreach (var child in stage.GetComponentsInChildren<Transform>(true))
                                blink_lights.record_stage_child(child.name, child.gameObject);
                            volume_uv_scroll.record_stage_materials(stage.transform);
                            var ctrl_fixtures = new System.Collections.Generic.List<string>();
                            foreach (var child in stage.GetComponentsInChildren<Transform>(true))
                            {
                                var lower = child.name.ToLowerInvariant();
                                if ((lower.Contains("spotlight") || lower.Contains("laser")) && ctrl_fixtures.Count < 24)
                                    ctrl_fixtures.Add(child.name);
                            }
                            trace_log.write($"controller fixtures: {string.Join(", ", ctrl_fixtures)}");

                            var ctrl = stage.GetComponent<Gallop.Live.StageController>();
                            _stage_controller = ctrl;
                            var geo_root = new GameObject("stage_geometry");
                            // the crowd rows read the stage controller's own
                            // audience table + the mob/cyalume rig roots.
                            crowd_rig.record_stage_audience_table(ctrl);
                            mob_control.record_rig(stage.transform);
                            cyalume.record_pen_meshes(stage.transform);
                            crowd_rig.start_stage_animations(stage.transform);
                            int placed = 0;
                            if (ctrl != null && ctrl._stageObjects != null)
                            {
                                foreach (var go in ctrl._stageObjects)
                                {
                                    if (go == null) continue;
                                    var piece = Instantiate(go, geo_root.transform);
                                    piece.name = go.name;
                                    placed++;
                                    // every stage child by name feeds the light
                                    // drivers' object resolution.
                                    foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                                        blink_lights.record_stage_child(child.name, child.gameObject);
                                }
                            }
                            // one-shot probe: the light fixture children the
                            // stage carries (spotlight/laser object names).
                            var fixture_names = new System.Collections.Generic.List<string>();
                            foreach (var child in geo_root.GetComponentsInChildren<Transform>(true))
                            {
                                var lower = child.name.ToLowerInvariant();
                                if ((lower.Contains("spotlight") || lower.Contains("laser")) && fixture_names.Count < 24)
                                    fixture_names.Add(child.name);
                            }
                            trace_log.write($"stage light fixtures: {string.Join(", ", fixture_names)}");
                            // one-shot probe: the crowd rig children the stage
                            // carries (mob/cyalume/audience object names).
                            var crowd_names = new System.Collections.Generic.List<string>();
                            foreach (var child in geo_root.GetComponentsInChildren<Transform>(true))
                            {
                                var lower = child.name.ToLowerInvariant();
                                if ((lower.Contains("mob") || lower.Contains("cyalume") || lower.Contains("audience")) && crowd_names.Count < 32)
                                    crowd_names.Add(child.name);
                            }
                            trace_log.write($"stage crowd children: {string.Join(", ", crowd_names)}");
                            // one-shot probe: the direct children of the
                            // cyalume controller object (the mob/pen-light
                            // group roots + leaf meshes).
                            var ctrl_probe = geo_root.GetComponentsInChildren<Transform>(true)
                                .FirstOrDefault(t => t.name.Contains("cyalume_controller"));
                            if (ctrl_probe != null)
                            {
                                var kid_names = new System.Collections.Generic.List<string>();
                                foreach (var kid in ctrl_probe.GetComponentsInChildren<Transform>(true))
                                    if (kid_names.Count < 20) kid_names.Add(kid.name);
                                trace_log.write($"cyalume controller '{ctrl_probe.name}' children: {string.Join(", ", kid_names)}");
                            }

                            Debug.Log($"[stage_loader] stage geometry: {placed} roots placed, renderers {geo_root.GetComponentsInChildren<Renderer>(true).Length}");
                            trace_log.write($"stage geometry: {placed} roots, {geo_root.GetComponentsInChildren<Renderer>(true).Length} renderers");
                            volume_uv_scroll.record_stage_materials(geo_root.transform);
                            // the geometry pass instantiates more of the
                            // stage's authored Animation objects + crowd meshes;
                            // the cyalume controllers spawn their pen-light
                            // tables before the crowd rig records.
                            crowd_rig.start_stage_animations(geo_root.transform);
                            crowd_rig.spawn_cyalume_rig(geo_root.transform);
                            mob_control.record_rig(geo_root.transform);
                            cyalume.record_pen_meshes(geo_root.transform);
                            shader_manager.fix_game_shaders(geo_root.transform, "stage_geometry");
                            shader_manager.audit_shaders(geo_root.transform, "stage_geometry");
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] stage bundle phase failed: {e}");
                return false;
            }
        }

        // opens every prereq bundle the stage controller names (crowd rig,
        // audience prefab, sky, fixtures + the material sources), so the
        // controller prefab's external object refs resolve on load.
        private void load_controller_prereqs(string controller_name)
        {
            var row = meta_row(controller_name);
            if (row == null || string.IsNullOrEmpty(row.prereq)) return;
            int opened = 0;
            foreach (var pre in row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                var pre_name = pre.Trim();
                if (string.IsNullOrEmpty(pre_name)) continue;
                var pre_row = meta_row(pre_name);
                if (pre_row == null) continue;
                if (game_assets.open(pre_row, config.data_root) != null) opened++;
                // one level of transitive prereqs (materials reference the
                // shader bundle + their textures' bundles).
                if (!string.IsNullOrEmpty(pre_row.prereq))
                {
                    foreach (var pre2 in pre_row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        var pre2_row = meta_row(pre2.Trim());
                        if (pre2_row != null) game_assets.open(pre2_row, config.data_root);
                    }
                }
            }
            trace_log.write($"stage controller prereqs: {opened} opened for {controller_name}");
        }

        // the crowd bind: one phase that wires every crowd row. failures land
        // in the trace, never abort the concert (a song without crowd tracks
        // authored is off by authoring, not an error).
        private void bind_crowd(selection_state sel)
        {
            try
            {
                var crowd_root = new GameObject("crowd").transform;
                crowd_rig.bind_song_clips(sel.music_id);
                crowd_rig.bind(ws?.audience_tracks, crowd_root, sel.music_id);
                audience_keys.bind(ws?.audience_tracks);
                cyalume.bind(sel.music_id);
                trace_log.write($"crowd bound: {crowd_rig.instance_count} instances, {cyalume.texture_count} cyalume textures, mob groups {(ws?.mob_groups?.Count ?? 0)}, cyalume groups {(ws?.cyalume_groups?.Count ?? 0)}");
            }
            catch (Exception e)
            {
                trace_log.write($"crowd bind failed: {e.GetType().Name}: {e.Message}");
                Debug.LogError($"[stage_loader] crowd bind failed: {e}");
            }
        }

        private Transform run_phase_character(UV2.App.slot_pick slot)
        {
            try { return load_character(slot.chara_id, slot.dress_id); }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] character phase failed: {e}");
                return null;
            }
        }

        private System.Collections.Generic.Dictionary<string, AnimationClip> run_phase_clips(selection_state sel)
        {
            try
            {
                var clip_names = new HashSet<string>();
                foreach (var seq in ws.motion_sequences)
                    foreach (var key in seq)
                        if (!string.IsNullOrEmpty(key.motion_name))
                            clip_names.Add(key.motion_name);
                var clips = load_clips(clip_names);
                trace_log.write($"motion: {clip_names.Count} authored names -> {clips.Count} clips loaded");
                return clips;
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] clips phase failed: {e}");
                return null;
            }
        }

        private System.Collections.Generic.List<string> _stage_material_names;
        private string _stage_first_controller;
        private int _cast_missed;
        private Gallop.Live.StageController _stage_controller;

        // true once open_core ran to completion.
        public bool opened { get { return _open_ok; } }
        private bool _open_ok;

        public System.Collections.IEnumerator open(selection_state sel) { yield return open_core(sel); }

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
            // chr{chara}_00 head bundle; it merges onto the body skeleton.
            int head_renderers = attach_head(instance.transform, chara_id);

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
            // the generic families use two name shapes: the plain 5-segment
            // form and a variant-segment form (bdy0001/0003/0006/0009/0015).
            // both go to the lookup; whichever exists wins.
            var names = new HashSet<string>
            {
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_diff",
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_shad_c",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_base",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_ctrl",
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_00_diff",
                $"{tex_dir}/tex_{folder}_00_{skin}_{bust}_00_shad_c",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_00_base",
                $"{tex_dir}/tex_{folder}_00_0_{bust}_00_ctrl",
                "3d/chara/common/textures/tex_chr_tear00",
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

            var tear_tex = loaded.Values.FirstOrDefault(t => t.name.Contains("tear"));
            int assigned = 0;
            foreach (var r in body_root.GetComponentsInChildren<Renderer>(true))
            {
                // the body material asset is shared cast-wide; instance it so
                // each character keeps their own skin/bust variant.
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.name.Contains("tear"))
                    {
                        // the tear materials ship a null main slot; the game
                        // assigns the shared tear texture at runtime.
                        Texture cur_tear = null;
                        try { cur_tear = m.GetTexture("_MainTex"); } catch { }
                        if (cur_tear == null && tear_tex != null)
                        {
                            m.SetTexture("_MainTex", tear_tex);
                            assigned++;
                        }
                        continue;
                    }
                    if (!m.name.Contains("bdy")) continue;
                    Texture cur = null;
                    try { cur = m.GetTexture("_MainTex"); } catch { }
                    if (cur == null)
                    {
                        var diff = loaded.FirstOrDefault(kv => kv.Key.Contains($"_{skin}_{bust}_diff")
                                                              || kv.Key.Contains($"_{skin}_{bust}_00_diff")).Value;
                        if (diff != null) { m.SetTexture("_MainTex", diff); assigned++; }
                    }
                    Texture toon = null;
                    try { toon = m.GetTexture("_ToonMap"); } catch { }
                    if (toon == null)
                    {
                        var shad = loaded.FirstOrDefault(kv => kv.Key.Contains($"_{skin}_{bust}_shad_c")
                                                              || kv.Key.Contains($"_{skin}_{bust}_00_shad_c")).Value;
                        if (shad != null) { m.SetTexture("_ToonMap", shad); assigned++; }
                    }
                    Texture tri = null;
                    try { tri = m.GetTexture("_TripleMaskMap"); } catch { }
                    if (tri == null)
                    {
                        var base_t = loaded.FirstOrDefault(kv => kv.Key.Contains($"_0_{bust}_base")
                                                              || kv.Key.Contains($"_0_{bust}_00_base")).Value;
                        if (base_t != null) { m.SetTexture("_TripleMaskMap", base_t); assigned++; }
                    }
                    Texture opt = null;
                    try { opt = m.GetTexture("_OptionMaskMap"); } catch { }
                    if (opt == null)
                    {
                        var ctrl = loaded.FirstOrDefault(kv => kv.Key.Contains($"_0_{bust}_ctrl")
                                                              || kv.Key.Contains($"_0_{bust}_00_ctrl")).Value;
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
        private int attach_head(Transform body_root, int chara_id)
        {
            string head_name = $"3d/chara/head/chr{chara_id}_00/pfb_chr{chara_id}_00";
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
            int head_renderers = head.GetComponentsInChildren<Renderer>(true).Length;

            // the game's rig is one skeleton shared by body and head meshes;
            // remap every head skinned mesh onto the body's bones by name so
            // the head deforms with (and is culled with) the body skeleton.
            var body_bones = new Dictionary<string, Transform>();
            foreach (var smr in body_root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones)
                    if (b != null && !body_bones.ContainsKey(b.name)) body_bones[b.name] = b;

            var replaced = new List<Transform>();
            int remapped_total = 0, kept = 0;
            foreach (var skin in head.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.rootBone != null && body_bones.TryGetValue(skin.rootBone.name, out var new_root))
                    skin.rootBone = new_root;
                var remapped = new Transform[skin.bones.Length];
                for (int i = 0; i < remapped.Length; i++)
                {
                    var src = skin.bones[i];
                    if (src != null && body_bones.TryGetValue(src.name, out var tgt))
                    {
                        remapped[i] = tgt;
                        src.position = tgt.position;
                        while (src.childCount > 0) src.GetChild(0).SetParent(tgt);
                        if (!replaced.Contains(src)) replaced.Add(src);
                        remapped_total++;
                    }
                    else
                    {
                        remapped[i] = src;
                        if (src != null) kept++;
                    }
                }
                skin.bones = remapped;
            }

            // surviving head objects (meshes + private physics bones) live
            // under the character root; the replaced copies are torn down.
            while (head.transform.childCount > 0)
                head.transform.GetChild(0).SetParent(body_root);
            foreach (var dead in replaced)
                if (dead != null) Destroy(dead.gameObject);
            Destroy(head);

            trace_log.write($"chara {chara_id}: head merged onto body skeleton ({remapped_total} bones remapped, {kept} private kept, body skeleton {body_bones.Count})");
            return head_renderers;
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
                    global_shade.set_light_track(keys[i], keys[i + 1], blend);
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
                UV2.UI.load_progress.report("starting music");
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
            blink_lights.update(clock?.time ?? 0f, ws?.blink_tracks);
            spot_lights.update(clock?.time ?? 0f, ws?.spot_tracks, chara_roots);
            laser_lights.update(clock?.time ?? 0f, ws?.laser_tracks, chara_roots);
            volume_uv_scroll.update_volume(clock?.time ?? 0f, ws?.volume_tracks);
            volume_uv_scroll.update_uv_scroll(clock?.time ?? 0f, ws?.uv_scroll_tracks);
            wash_light.update(clock?.time ?? 0f, ws?.wash_tracks);
            additional_light.update(clock?.time ?? 0f, ws?.additional_tracks);
            // the crowd rows: audience transforms + clips, the mob/cyalume
            // group matrices, the pen-light pattern + scroll.
            crowd_rig.update(clock?.time ?? 0f, ws?.audience_tracks);
            audience_keys.update(clock?.time ?? 0f, ws?.audience_tracks);
            mob_control.update(clock?.time ?? 0f, ws?.mob_groups, ws?.cyalume_groups);
            cyalume.update(clock?.time ?? 0f);
            global_shade.publish(FindObjectOfType<Camera>());
            global_shade.publish_chara_block(chara_roots);

            // the foot lights run in the game's LATE pass: the characters are
            // already posed for this frame so the lights track them exactly.
            foot_light.update(clock?.time ?? 0f, ws?.foot_light, chara_roots);

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

                // the layer band + cast heights: the flagged characters' average
                // cm height drives the offset rate, so this line is the
                // head-height evidence for the camera framing.
                if (ws != null && ws.camera_lookat.Count > 0)
                {
                    int li = UV2.Live.key_eval.bracket(ws.camera_lookat, clock?.time ?? 0f);
                    if (li >= 0)
                    {
                        var lk = ws.camera_lookat[li];
                        if (lk.look_at_type == 1)
                        {
                            float avg = UV2.Live.chara_parts.group_height(chara_roots, lk.look_at_chara_pos);
                            trace_log.write($"camera layer: lookat key f{lk.frame} flags {lk.look_at_chara_pos} parts {lk.look_at_chara_parts} avg height {avg:0.0}cm");
                        }
                    }
                }

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
