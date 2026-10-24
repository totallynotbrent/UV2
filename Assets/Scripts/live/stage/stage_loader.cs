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

    // assembles the concert from the manifest and wires the timeline drivers onto it.
    public class stage_loader : MonoBehaviour
    {
        // runs the timeline on real time even when audio binds.
        public static bool force_free_clock;
        private live_worksheet ws;

        // the worksheet's volumeLight tracks for the shafts walker
        // (the volume SunShafts entry drives the radial pass).
        private static stage_loader _instance;
        public static List<UV2.Live.volume_track_container> volume_tracks_view
            => _instance != null ? _instance.ws?.volume_tracks : null;
        private timeline_clock clock;
        private readonly List<Transform> chara_roots = new();
        // per-chara face morph databases loaded from the head bundles.
        private readonly Dictionary<int, Gallop.FaceDrivenKeyTarget> face_targets = new();
        private camera_director director;
        private formation_driver formation;
        private bg_color1_director bg_color1;
        // parked at stage instantiation; the bg_color1 director binds it
        // in wire_drivers (the stage phase runs before the director exists).
        private Transform _stage_instance_root;
        private motion_player motion;
        private vocal_mixer vocals;

        public IReadOnlyList<Transform> characters => chara_roots;

        // error string from the last open attempt; null when it succeeded.
        public string last_error { get; private set; }

        // builds the concert in phases, yielding between them so the ui paints progress.
        private System.Collections.IEnumerator open_core(selection_state sel)
        {
            if (!run_phase_worksheet(sel)) yield break;
            volume_uv_scroll.reset();
            mob_control.reset();
            cyalume.reset_pen_meshes();
            crowd_rig.reset();
            audience_keys.reset();
            props_system.reset();
            UV2.UI.load_progress.reset();
            UV2.UI.load_progress.report($"binding worksheet ({ws.camera_pos.Count} cam keys)", 0.04f);
            trace_log.write($"worksheet bound: {ws.camera_pos.Count} cam keys, {ws.motion_sequences.Count} motion seqs, {ws.formation.Count} formation groups, total {ws.total_frames} frames");
            yield return null;

            // the vocal stems decode in parallel with stage + cast loading
            // (begun below) so the early song sections carry vocals on every
            // song; the bind at the end gates the music start.
            var vocals_go = new GameObject("vocal_mixer");
            vocals = vocals_go.AddComponent<vocal_mixer>();
            vocals.begin_decode(sel.music_id, sel.slots);

            // the instrumental decodes on a worker too: its ~1.2s hca decode
            // was a single long main-thread frame right at the open.
            var oke_task = start_oke_decode(sel.music_id);

            if (!run_phase_stage(sel)) yield break;
            int stage_step = 0;
            var stage_bundles = _stage_material_names.Concat(new[] { _stage_first_controller }).ToList();
            foreach (var name in stage_bundles)
            {
                // the trailing bundle name segment identifies what is loading.
                string short_name = name.Split('/').Last();
                UV2.UI.load_progress.report($"loading stage {sel.stage_id}: {short_name} ({++stage_step}/{stage_bundles.Count})",
                    0.04f + 0.30f * stage_step / stage_bundles.Count);
                yield return null;
                yield return run_phase_stage_bundle(name);
                if (!stage_bundles_ok) yield break;
            }

            // instantiates the laser and spotlight fixtures for the stage.
            yield return instantiate_laser_fixtures();
            yield return instantiate_spotlight_fixtures();
            // blink fixtures the stage hierarchy lacks load as standalone
            // prefabs: the worksheet names the family (e.g. wash_ground),
            // but a stage id in the name may not match the loaded stage —
            // 1004's worksheet asks for 10112_blinklight_wash_ground while
            // the only such fixture in the game ships under live10101.
            yield return instantiate_blink_fixtures(sel);

            blink_lights.bind(ws?.blink_tracks, null);
            spot_lights.bind(ws?.spot_tracks);
            laser_lights.bind(ws?.laser_tracks, Camera.main);
            foot_light.reset();
            foot_light.bind(ws?.foot_light);
            volume_uv_scroll.bind(ws?.volume_tracks, ws?.uv_scroll_tracks);
            wash_light.bind(ws?.wash_tracks);
            additional_light.bind(ws?.additional_tracks);

            // wires the audience, mob, and cyalume tracks onto the crowd rig.
            UV2.UI.load_progress.report("binding crowd", 0.36f);
            yield return null;
            bind_crowd(sel);

            int cast_step = 0, cast_total = sel.slots.Count(s => s.chara_id > 0);
            // spring physics director exists before the cast so load_character
            // can bind each character's containers as it loads.
            var physics_go = new GameObject("physics_director");
            var physics = physics_go.AddComponent<physics_director>();
            physics.bind_clock(clock);
            foreach (var slot in sel.slots.Where(s => s.chara_id > 0))
            {
                UV2.UI.load_progress.report($"loading {chara_name(slot.chara_id)} (cast {++cast_step}/{cast_total})",
                    0.36f + 0.52f * cast_step / Mathf.Max(1, cast_total));
                yield return null;
                var staged = new System.Collections.Generic.List<Transform>();
                yield return run_phase_character_steps(slot, staged);
                if (staged.Count == 0) { _cast_missed++; continue; }
                var root = staged[0];
                chara_roots.Add(root);
                UV2.Live.chara_parts.record(root);
                UV2.Live.chara_parts.record_height(root, slot.chara_id);
                yield return null;
            }
            trace_log.write($"cast: {chara_roots.Count} loaded, {_cast_missed} missed, {chara_roots.Count} roots total");
            // resolves the song's props against the loaded cast.
            UV2.UI.load_progress.report("binding props", 0.90f);
            yield return null;
            bind_props(sel);

            UV2.UI.load_progress.report("loading motion clips", 0.93f);
            yield return null;
            var clips = run_phase_clips(sel);
            if (clips == null) yield break;

            wire_drivers(clips, sel.music_id, sel.slots.Select(x => x.chara_id).ToList());
            trace_log.write("drivers wired: camera_director, formation, motion; concert open returning true");

            // the vocal stems: bind what the parallel decode produced and
            // keep every stem silent until its part row arrives — the bgm
            // below is the timing master and must start only once the whole
            // stage is live and every stem exists.
            UV2.UI.load_progress.report("binding vocals", 0.96f);
            yield return vocals.bind(clock, chara_roots, sel.slots, sel.music_id);

            UV2.UI.load_progress.report("concert ready", 1f);
            _open_ok = true;

            // the music starts last, with everything visible: starting it
            // mid-load made the song audible during the loading phase and
            // led the visible concert by the remaining load time.
            start_music(sel.music_id, oke_task);
        }

        // loads the worksheet and resolves the stage manifest; false on failure.
        private bool run_phase_worksheet(selection_state sel)
        {
            try
            {
                ws = worksheet_reader.load(sel.music_id);
                _instance = this;
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

        // clones the controller's loose laser fixtures, one per distinct worksheet entry.
        // logs the live render state of the beam/mirror/floor meshes a frame
        // after stage bind so a gpu log can distinguish "not bound" from
        // "bound but not rendered" on dx11.
        private System.Collections.IEnumerator probe_beam_after_frame(Transform root)
        {
            yield return null;
            probe_beam_render_state(root);
        }

        private void probe_beam_render_state(Transform root)
        {
            if (root == null) return;
            var cam = Camera.main;
            int probed = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("uv_light") && !n.Contains("beam") && !n.Contains("mirror")
                    && !n.Contains("ground") && !n.Contains("monitor")) continue;
                probed++;
                bool in_frustum = false;
                if (cam != null)
                {
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    in_frustum = GeometryUtility.TestPlanesAABB(planes, r.bounds);
                }
                var mats = new System.Text.StringBuilder();
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { mats.Append(" null"); continue; }
                    mats.Append(" ").Append(m.name).Append("[").Append(m.shader != null ? m.shader.name : "NULLSHADER").Append("]");
                }
                trace_log.write($"render probe '{r.gameObject.name}' layer={r.gameObject.layer} enabled={r.enabled} active={r.gameObject.activeInHierarchy} frustum={in_frustum} mats={mats.ToString().Trim()}");
            }
            trace_log.write($"render probe complete: {probed} meshes");
        }

        // auto-attaches mirror_reflection to renderers that carry mirror
        // geometry or reflection-aware materials; the game's stage prefab
        // expects us to do this (the fork does the same pass via
        // StageController.AutoAttachMirrorReflectionComponents).
        private void attach_mirror_reflections(Transform root)
        {
            if (root == null) return;
            int attached = 0;
            var rends = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                string path = "";
                var t = r.transform;
                while (t != null)
                {
                    path = "/" + t.name + path;
                    t = t.parent;
                }
                string lower = path.ToLowerInvariant();
                bool wants_mirror = lower.Contains("mirror");
                if (!wants_mirror)
                {
                    var mats = r.sharedMaterials;
                    foreach (var m in mats)
                    {
                        if (m == null) continue;
                        if (m.HasProperty("_ReflectionTex") || m.HasProperty("_ReflectionRate"))
                        {
                            wants_mirror = true;
                            break;
                        }
                    }
                }
                if (!wants_mirror) continue;
                if (r.GetComponent<mirror_reflection>() != null) continue;
                var mr = r.gameObject.AddComponent<mirror_reflection>();
                var cam = Camera.main;
                if (cam != null) mr.ConfigureFor(cam);
                attached++;
            }
            trace_log.write($"mirror reflections attached: {attached}");
        }

        private System.Collections.IEnumerator instantiate_laser_fixtures()
        {
            var ctrl = _stage_controller;
            if (ctrl == null || ctrl._laserObjects == null) yield break;
            var geo = GameObject.Find("stage_geometry");
            var parent = geo != null ? geo.transform : null;
            var wanted = new SortedSet<(string fixture, int index)>();
            if (ws?.laser_tracks != null)
                foreach (var t in ws.laser_tracks)
                {
                    if (string.IsNullOrEmpty(t.name)) continue;
                    int sep = t.name.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (sep > 0 && int.TryParse(t.name.Substring(sep + 3), out int idx))
                        wanted.Add((t.name.Substring(0, sep), idx));
                    else
                        wanted.Add((t.name, -1));
                }
            int placed = 0;
            foreach (var (fixture, index) in wanted)
            {
                GameObject src = null;
                foreach (var go in ctrl._laserObjects)
                    if (go != null && (go.name == fixture || go.name.Replace("(Clone)", "") == fixture))
                    { src = go; break; }
                if (src == null)
                {
                    trace_log.write($"laser fixture: no loose object '{fixture}' in the stage bundle");
                    continue;
                }
                var piece = Instantiate(src, parent);
                piece.name = index >= 0 ? $"{fixture} - {index}" : fixture;
                placed++;
                foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                    blink_lights.record_stage_child(child.name, child.gameObject);
            }
            trace_log.write($"laser fixtures instantiated: {placed}");
        }

        // instantiates missing blink fixtures: worksheet tracks whose root
        // name is absent from the stage hierarchy resolve through the meta
        // store (sourceresources fixture prefabs), stage id flexible.
        private System.Collections.IEnumerator instantiate_blink_fixtures(selection_state sel)
        {
            if (ws?.blink_tracks == null) yield break;
            var geo = GameObject.Find("stage_geometry");
            var parent = geo != null ? geo.transform : null;
            int placed = 0;
            foreach (var t in ws.blink_tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                if (!blink_lights.is_blink_root_name(t.name)) continue;
                if (blink_lights.find_stage_object(t.name) != null) continue;
                // the family suffix drops the stage id: pfb_env_live<NNNNN>_
                // blinklight_<family>. try the named id first, then the
                // 10101 standalone (the only stages that ship standalone
                // blink fixtures).
                string family = t.name.Substring(t.name.IndexOf("blinklight_", StringComparison.Ordinal));
                string[] candidates =
                {
                    $"sourceresources/3d/env/live/live{sel.stage_id}/prefabs/{t.name}",
                    $"sourceresources/3d/env/live/live10101/prefabs/pfb_env_live10101_{family}",
                };
                GameObject piece = null;
                foreach (var path in candidates)
                {
                    var row = meta_row(path);
                    if (row == null) continue;
                    if (!string.IsNullOrEmpty(row.prereq))
                        foreach (var pre in row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var pre_row = meta_row(pre.Trim());
                            if (pre_row != null) game_assets.open(pre_row, config.data_root);
                        }
                    var bundle = game_assets.open(row, config.data_root);
                    if (bundle == null) continue;
                    foreach (var asset_name in bundle.GetAllAssetNames())
                        if (asset_name.EndsWith(".prefab"))
                        {
                            piece = bundle.LoadAsset<GameObject>(asset_name);
                            break;
                        }
                    if (piece != null) break;
                }
                if (piece == null)
                {
                    trace_log.write($"blink fixture: no prefab for missing '{t.name}'");
                    continue;
                }
                var inst = Instantiate(piece, parent);
                inst.name = t.name;
                placed++;
                foreach (var child in inst.GetComponentsInChildren<Transform>(true))
                    blink_lights.record_stage_child(child.name, child.gameObject);
                blink_lights.record_stage_child(t.name, inst);
            }
            if (placed > 0)
            {
                trace_log.write($"blink fixtures instantiated from prefabs: {placed}");
                blink_lights.bind(ws.blink_tracks, null);
            }
            yield return null;
        }

        // instantiates the spotlight fixtures from the common spotlight3d bundle.
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
            trace_log.write($"spotlight bundle assets: {string.Join(", ", bundle.GetAllAssetNames().Take(8))}");

            var geo = GameObject.Find("stage_geometry");
            var parent = geo != null ? geo.transform : null;
            // the fixture prefabs are reachable only through the controller's AssetHolder table.
            int placed = 0;
            var controller = bundle.LoadAsset<GameObject>("pfb_env_live_cmn_spotlight3d_controller000");
            var holder = controller != null ? controller.GetComponent<Gallop.AssetHolder>() : null;
            if (holder != null)
            {
                foreach (var entry in holder._assetTable.list)
                {
                    if (entry?.Value == null) { trace_log.write("spotlight fixture: a table entry did not bind"); continue; }
                    if (entry.Value is not GameObject entry_go) { trace_log.write($"spotlight fixture '{entry.Key}': not a prefab ({entry.Value.GetType().Name})"); continue; }
                    var piece = Instantiate(entry_go, parent);
                    piece.name = entry_go.name;
                    // the game instances a fixture only when a spotlight3d
                    // key activates it: pre-placed fixtures start hidden.
                    piece.SetActive(false);
                    placed++;
                    foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                        blink_lights.record_stage_child(child.name, child.gameObject);
                    blink_lights.record_stage_child(entry.Key, piece);
                    trace_log.write($"spotlight fixture '{entry.Key}' -> '{entry.Value.name}' placed (hidden)");
                }
            }
            else
            {
                trace_log.write("spotlight fixtures: no AssetHolder on the controller");
            }
            trace_log.write($"spotlight fixtures instantiated: {placed}");
        }

        private bool run_phase_stage(selection_state sel) => true;

        private System.Collections.IEnumerator run_phase_stage_bundle(string name)
        {
            var b = load_bundle_keep_safe(name);
            Debug.Log($"[stage_loader] bundle {name}: {(b == null ? "FAILED" : "ok, assets: " + b.GetAllAssetNames().Length)}");
            if (b != null && name.Contains("controller"))
            {
                // the prereq chain yields between bundle groups; runs outside
                // try/catch so the paint can happen (cs1626 forbids yielding
                // inside a try that catches).
                yield return load_controller_prereqs_steps(name);
                try
                {
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
                            // records the prefab's own children too; some fixtures live outside _stageObjects.
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
                            // the stage phase runs before wire_drivers creates the
                            // bg_color1 director, so park the roots and bind later.
                            _stage_instance_root = stage.transform;
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
                                    foreach (var child in piece.GetComponentsInChildren<Transform>(true))
                                        blink_lights.record_stage_child(child.name, child.gameObject);
                                }
                            }
                            var fixture_names = new System.Collections.Generic.List<string>();
                            foreach (var child in geo_root.GetComponentsInChildren<Transform>(true))
                            {
                                var lower = child.name.ToLowerInvariant();
                                if ((lower.Contains("spotlight") || lower.Contains("laser")) && fixture_names.Count < 24)
                                    fixture_names.Add(child.name);
                            }
                            trace_log.write($"stage light fixtures: {string.Join(", ", fixture_names)}");
                            var crowd_names = new System.Collections.Generic.List<string>();
                            foreach (var child in geo_root.GetComponentsInChildren<Transform>(true))
                            {
                                var lower = child.name.ToLowerInvariant();
                                if ((lower.Contains("mob") || lower.Contains("cyalume") || lower.Contains("audience")) && crowd_names.Count < 32)
                                    crowd_names.Add(child.name);
                            }
                            trace_log.write($"stage crowd children: {string.Join(", ", crowd_names)}");
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
            // one-shot render-state probe of the beams + mirror + floor so a
            // user's gpu log tells us whether those meshes sit culled, hidden,
            // or shader-fallbacked on dx11.
            // deferred probe: run it one frame later so culling and camera
            // state reflect the live render, not the freshly-instantiated pose.
            StartCoroutine(probe_beam_after_frame(geo_root.transform));
            attach_mirror_reflections(geo_root.transform);
                            volume_uv_scroll.record_stage_materials(geo_root.transform);
                            // spawns the cyalume rig before the crowd records it.
                            crowd_rig.start_stage_animations(geo_root.transform);
                            crowd_rig.spawn_cyalume_rig(geo_root.transform);
                            mob_control.record_rig(geo_root.transform);
                            cyalume.record_pen_meshes(geo_root.transform);
                            shader_manager.fix_game_shaders(geo_root.transform, "stage_geometry");
                            shader_manager.audit_shaders(geo_root.transform, "stage_geometry");
                        }
                    }
                }
                catch (Exception e)
                {
                    last_error = $"{e.GetType().Name}: {e.Message}";
                    Debug.LogError($"[stage_loader] stage bundle phase failed: {e}");
                    _stage_bundle_failed = true;
                }
            }
        }

        private bool _stage_bundle_failed;

        // true when every stage bundle phase finished without a failure.
        private bool stage_bundles_ok => !_stage_bundle_failed;

        // opens the controller's prereq bundles so its external refs resolve on load.
        // opens a batch of prereq bundles with yields between groups so the
        // loading bar paints; the stage controller's 33-bundle chain was one
        // ~7.6s main-thread block that froze the window.
        private System.Collections.IEnumerator load_controller_prereqs_steps(string controller_name)
        {
            var row = meta_row(controller_name);
            if (row == null || string.IsNullOrEmpty(row.prereq)) yield break;
            int opened = 0;
            foreach (var pre in row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                var pre_name = pre.Trim();
                if (string.IsNullOrEmpty(pre_name)) continue;
                var pre_row = meta_row(pre_name);
                if (pre_row == null) continue;
                if (game_assets.open(pre_row, config.data_root) != null) opened++;
                // opens one transitive level of prereqs; materials reference the shader and texture bundles.
                if (!string.IsNullOrEmpty(pre_row.prereq))
                {
                    foreach (var pre2 in pre_row.prereq.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        var pre2_row = meta_row(pre2.Trim());
                        if (pre2_row != null) game_assets.open(pre2_row, config.data_root);
                    }
                }
                // the ui paints between each bundle group.
                yield return null;
            }
            trace_log.write($"stage controller prereqs: {opened} opened for {controller_name}");
        }

        // wires the crowd rows; failures are traced, not fatal.
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

        // binds the props against the loaded cast; failures are traced, not fatal.
        private void bind_props(selection_state sel)
        {
            try
            {
                var groups = worksheet_reader.load_props_groups(sel.music_id);
                props_system.bind(groups, sel, chara_roots, ws?.formation, ws?.props_attach, ws?.props_render);
            }
            catch (Exception e)
            {
                trace_log.write($"props bind failed: {e.GetType().Name}: {e.Message}");
                Debug.LogError($"[stage_loader] props bind failed: {e}");
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

        // the per-character load split into yield points so the loading bar
        // paints between its heavy halves: the load phase held one ~1.8s
        // main-thread frame per chara (bundle open + head merge + shader fix)
        // and the window could not pump between characters, freezing the bar.
        private System.Collections.IEnumerator run_phase_character_steps(UV2.App.slot_pick slot,
            System.Collections.Generic.List<Transform> sink)
        {
            Transform root = null;
            try { root = load_character_head(slot.chara_id, slot.dress_id); }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] character phase failed: {e}");
            }
            if (root == null) yield break;
            // let the ui paint the bar before the shader fix + physics bind.
            yield return null;
            try
            {
                finish_character(root, slot.chara_id, slot.dress_id);
                sink.Add(root);
            }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] character finish failed: {e}");
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

        public bool opened { get { return _open_ok; } }
        private bool _open_ok;

        // the song clock, exposed for the -uv2frame seek mode in scene_boot.
        public timeline_clock song_clock { get { return clock; } }

        // seeks the song clock and drives every driver at the seeked time.
        // the drivers also run on their own LateUpdate, but that order is
        // undefined: a capture fired from another component's LateUpdate can
        // land before the camera and the pose were ever applied at the
        // scrubbed frame. calling apply_at here makes the frame deterministic.
        public void seek_and_apply(float seconds)
        {
            if (clock == null) return;
            clock.pause();
            clock.seek(seconds);
            apply_at(seconds);
        }

        // drives the camera, the formation and the pose at one song time.
        public void apply_at(float t)
        {
            motion?.apply_at(t);
            formation?.apply_at(t);
            director?.apply_at(t);
        }

        // the music source, exposed for the control panel's seek bridge.
        public AudioSource music_source { get { return _music_source; } }
        private AudioSource _music_source;

        public System.Collections.IEnumerator open(selection_state sel) { yield return open_core(sel); }

        // load_bundle_keep wrapped for the coroutine path: a throw here must
        // mark the stage failed instead of aborting the open coroutine.
        private AssetBundle load_bundle_keep_safe(string name)
        {
            try { return load_bundle_keep(name); }
            catch (Exception e)
            {
                last_error = $"{e.GetType().Name}: {e.Message}";
                Debug.LogError($"[stage_loader] stage bundle open failed: {e}");
                _stage_bundle_failed = true;
                return null;
            }
        }

        private AssetBundle load_bundle_keep(string name)
        {
            var row = meta_row(name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            return bundle;
        }

        // loads the character's body prefab and merges its head onto the skeleton.
        // the game's per-character body scale (UmaContainerCharacter):
        // Position.localScale = scale/160.7529, with scale = chara_data.scale
        // in cm (135..180 across the cast). height 0/missing falls back to 1.
        // spring bone distances are measured from the live rig at bind, and
        // collider riders parent under the scaled bones, so both follow
        // automatically; collider radii and record collision_radius are the
        // authored values, which are correct under this scale by the game's
        // own ScaleParams algebra (legacyScale cancels the bone lossyScale).
        private void apply_chara_scale(Transform character_root, int chara_id)
        {
            float body_scale = 1f;
            try
            {
                var db = UV2.Data.master_db.reader.open(config.master_db_path);
                if (db != null)
                {
                    try
                    {
                        var rows = db.query($"SELECT scale FROM chara_data WHERE id={chara_id}");
                        if (rows.Count > 0)
                        {
                            int cm = (int)rows[0].get_int(0);
                            if (cm > 0) body_scale = cm / 160.7529f;
                        }
                    }
                    finally { db.Dispose(); }
                }
            }
            catch (System.Exception e)
            {
                trace_log.write($"chara {chara_id}: scale read failed ({e.Message}), using 1.0");
            }
            var position = character_root.Find("Position");
            if (position != null)
            {
                position.localScale = new Vector3(body_scale, body_scale, body_scale);
                trace_log.write($"chara {chara_id}: body scale {body_scale:0.000} applied to Position");
            }
        }

        // loads the character's bundles and instantiates the body with its
        // head merged and tail mounted (the half of the load that resolves
        // asset references); the finish half below does the runtime passes.
        private Transform load_character_head(int chara_id, int dress_id)
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

            // loads the prereq bundles first so the prefab's material refs resolve.
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

            // the body prefab ships without a head; it merges on from the head bundle.
            int head_renderers = attach_head(instance.transform, chara_id);

            // the tail mesh carries the Sp_Hi_Tail bones the spring chains bind.
            int tail_renderers = mount_tail(instance.transform, chara_id, dress_id);
            trace_log.write($"chara {chara_id} dress {dress_id} -> {body.prefab}: "
                + $"{instance.GetComponentsInChildren<Renderer>(true).Length} renderers (head +{head_renderers}, tail +{tail_renderers})");
            return instance.transform;
        }

        // the runtime half of the character load: shader fix, height scale,
        // generic textures, and the spring container bind.
        private void finish_character(Transform instance, int chara_id, int dress_id)
        {
            var body_opt = manifest_reader.chara_body(chara_id, dress_id);
            var body = body_opt.Value;

            shader_manager.fix_game_shaders(instance, $"chara {chara_id}");

            // the game scales the whole rig by the chara's height
            // (UmaContainerCharacter: Position.localScale = scale/160.7529),
            // and every spring length/collider offset rides that scale.
            apply_chara_scale(instance, chara_id);

            // generic bodies ship null texture slots; assign them after the shader swap.
            assign_generic_body_textures(instance, chara_id, body.bundle);

            // spring physics: containers load after the skeleton is complete
            // (head merged), so every spring root resolves by name.
            if (physics_director.instance != null)
            {
                var (body_folder, chara_body, bust, tail_id) = chara_container_info(chara_id, dress_id);
                if (body_folder != null)
                {
                    int cloth_idx = cloth_slot(dress_id);
                    var sim = physics_director.instance.attach_character(
                        chara_id, body_folder, cloth_idx, bust,
                        chara_body, tail_id, instance, load_container_prefab);
                    if (sim != null)
                    {
                        chara_springs[chara_id] = sim;
                        chara_springs_roots[instance] = chara_id;
                    }
                    // the skirt knee-correction pass rides the v1 bridge
                    // (attach_character returns null there by design, so the
                    // skirt binds unconditionally).
                    physics_director.instance.attach_skirt(chara_id, body_folder,
                        cloth_idx, instance, load_asset_object);
                }
            }
        }

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

            // loads the prereq bundles first so the prefab's material refs resolve.
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

            // the body prefab ships without a head; it merges on from the head bundle.
            int head_renderers = attach_head(instance.transform, chara_id);

            // the tail mesh carries the Sp_Hi_Tail bones the spring chains bind.
            int tail_renderers = mount_tail(instance.transform, chara_id, dress_id);

            int chara_renderers = instance.GetComponentsInChildren<Renderer>(true).Length;
            trace_log.write($"chara {chara_id} dress {dress_id} -> {body.prefab}: {chara_renderers} renderers (head +{head_renderers})");
            shader_manager.fix_game_shaders(instance.transform, $"chara {chara_id}");

            // the game scales the whole rig by the chara's height
            // (UmaContainerCharacter: Position.localScale = scale/160.7529),
            // and every spring length/collider offset rides that scale.
            apply_chara_scale(instance.transform, chara_id);

            // generic bodies ship null texture slots; assign them after the shader swap.
            assign_generic_body_textures(instance.transform, chara_id, body.bundle);

            // spring physics: containers load after the skeleton is complete
            // (head merged), so every spring root resolves by name.
            if (physics_director.instance != null)
            {
                var (body_folder, chara_body, bust, tail_id) = chara_container_info(chara_id, dress_id);
                if (body_folder != null)
                {
                    int cloth_idx = cloth_slot(dress_id);
                    var sim = physics_director.instance.attach_character(
                        chara_id, body_folder, cloth_idx, bust,
                        chara_body, tail_id, instance.transform, load_container_prefab);
                    if (sim != null)
                    {
                        chara_springs[chara_id] = sim;
                        chara_springs_roots[instance.transform] = chara_id;
                    }
                    // the skirt knee-correction pass rides the v1 bridge
                    // (attach_character returns null there by design, so the
                    // skirt binds unconditionally).
                    physics_director.instance.attach_skirt(chara_id, body_folder,
                        cloth_idx, instance.transform, load_asset_object);
                }
            }
            return instance.transform;
        }

        // loads any single asset from a bundle by manifest name (skirt
        // parameter ScriptableObjects; falls back to the first asset).
        UnityEngine.Object load_asset_object(string bundle_name)
        {
            var row = meta_row(bundle_name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            foreach (var n in bundle.GetAllAssetNames())
            {
                var a = bundle.LoadAsset<UnityEngine.Object>(n);
                if (a != null) return a;
            }
            return null;
        }

        // resolves the tail mesh prefab for one character and merges it onto
        // the body (the fork's exclusive-then-shared order):
        //   1. exclusive tail{dress_chara}_{sub} (costume-specific model),
        //   2. shared tail{tail_model_id:0000}_00 from the chara row.
        // the tail's textures load by manifest name; missing texture rows
        // leave the material slots to the bundle's own defaults.
        private int mount_tail(Transform character, int chara_id, int dress_id)
        {
            var (folder, chara_body, bust, tail_id) = chara_container_info(chara_id, dress_id);
            if (tail_id <= 0) return 0;

            // the exclusive tail names the character + costume sub slot.
            string sub = chara_body && folder != null ? folder.Substring(folder.Length - 2) : "00";
            string excl_tail = $"3d/chara/tail/tail{chara_id}_{sub}/pfb_tail{chara_id}_{sub}";
            string shared_tail = $"3d/chara/tail/tail{tail_id:0000}_00/pfb_tail{tail_id:0000}_00";

            var tail_instance = load_mesh_prefab(excl_tail);
            if (tail_instance == null)
                tail_instance = load_mesh_prefab(shared_tail);
            if (tail_instance == null)
            {
                trace_log.write($"chara {chara_id}: no tail prefab ({excl_tail} | {shared_tail})");
                return 0;
            }

            string tail_stem = tail_instance.name.Replace("(Clone)", "");
            int renderers = tail_mount.mount(character, tail_instance, s => trace_log.write($"chara {chara_id}: {s}"));
            // textures load after the mount: the renderer must live under the
            // character to be found. the tail stem in the manifest strips the
            // pfb_ prefix (tex_tail0001_00_1001_diff, not tex_pfb_...).
            load_tail_textures(character, chara_id, tail_stem);
            return renderers;
        }

        // loads texture bundles named tex_{tail}_{head}_{suffix} into the
        // tail's materials: per-head rows first (diff/shad), then the shared
        // _0000 base/ctrl set. the manifest names the tail without the pfb_
        // prefix (tex_tail0001_00_1001_diff).
        private void load_tail_textures(Transform character, int chara_id, string tail_stem)
        {
            string stem = tail_stem.StartsWith("pfb_") ? tail_stem.Substring(4) : tail_stem;
            string dir = $"3d/chara/tail/{stem}/textures";
            var names = new HashSet<string>();
            foreach (var suffix in new[] { "diff", "shad_c" })
                names.Add($"{dir}/tex_{stem}_{chara_id}_{suffix}");
            names.Add($"{dir}/tex_{stem}_0000_base");
            names.Add($"{dir}/tex_{stem}_0000_ctrl");
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return;
            var rows = meta.lookup(names);
            var loaded = new Dictionary<string, Texture2D>();
            foreach (var kv in rows)
            {
                var bundle = game_assets.open(kv.Value, config.data_root);
                if (bundle == null) continue;
                foreach (var n in bundle.GetAllAssetNames())
                {
                    var t = bundle.LoadAsset<Texture2D>(n);
                    if (t != null) loaded[n] = t;
                }
            }
            if (loaded.Count == 0) return;
            int assigned = 0;

            // the tail prefab ships an unresolved material slot; the tail's
            // own materials bundle (sourceresources/.../mtl_{stem}) carries
            // the real material.
            Material tail_mat = null;
            try
            {
                var mtl_row = meta_row($"sourceresources/3d/chara/tail/{stem}/materials/mtl_{stem}");
                if (mtl_row != null)
                {
                    var mtl_bundle = game_assets.open(mtl_row, config.data_root);
                    if (mtl_bundle != null)
                        tail_mat = mtl_bundle.LoadAsset<Material>(mtl_bundle.GetAllAssetNames().FirstOrDefault());
                }
            }
            catch { }

            foreach (var r in character.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.Contains("Tail") && !r.name.Contains("tail")) continue;
                var mats = r.materials;
                bool changed = false;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var m = mats[mi];
                    // a null slot or a material whose shader never resolved
                    // renders magenta: the mtl bundle's material replaces it.
                    if ((m == null || m.shader == null) && tail_mat != null)
                    {
                        m = tail_mat;
                        mats[mi] = m;
                        changed = true;
                    }
                    if (m == null || m.shader == null) continue;
                    Texture tex = null;
                    try { tex = m.GetTexture("_MainTex"); } catch { }
                    if (tex != null) continue;
                    foreach (var kv in loaded)
                    {
                        string prop = kv.Key.Contains("diff") ? "_MainTex"
                            : kv.Key.Contains("shad") ? "_ToonMap"
                            : kv.Key.Contains("base") ? "_TripleMaskMap"
                            : "_OptionMaskMap";
                        try
                        {
                            if (m.GetTexture(prop) == null)
                            {
                                m.SetTexture(prop, kv.Value);
                                assigned++;
                            }
                        } catch { }
                    }
                }
                if (changed) r.materials = mats;
            }
            trace_log.write($"chara {chara_id} tail textures: {loaded.Count} loaded, {assigned} slots assigned, mat {(tail_mat == null ? "none" : "set")}");
        }

        // loads a mesh prefab bundle by manifest name and returns the live instance.
        private GameObject load_mesh_prefab(string bundle_name)
        {
            var row = meta_row(bundle_name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            string want = bundle_name.Split('/').Last();
            string prefab_name = bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith($"/{want}.prefab"))
                                 ?? bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith(".prefab"));
            var prefab = string.IsNullOrEmpty(prefab_name) ? null : bundle.LoadAsset<GameObject>(prefab_name);
            return prefab == null ? null : Instantiate(prefab);
        }

        // per-character spring sims (formation keys address them by chara id).
        readonly Dictionary<int, spring_sim> chara_springs = new();
        // root transform per chara id, for the formation driver's key routing.
        readonly Dictionary<Transform, int> chara_springs_roots = new();

        // resolves the container bundle folder + axes for one character from
        // the master db (mirrors manifest_reader.chara_body's naming rules):
        // body folder, whether it is character-specific, the bust size
        // (shared bodies), and the tail model id.
        (string folder, bool chara_body, int bust, int tail_id) chara_container_info(
            int chara_id, int dress_id)
        {
            var db = UV2.Data.master_db.reader.open(config.master_db_path);
            if (db == null) return (null, false, 0, 0);
            try
            {
            var dress_rows = db.query(
                $"SELECT chara_id, body_type, body_type_sub, tail_model_id FROM dress_data WHERE id={dress_id}");
            if (dress_rows.Count == 0) return (null, false, 0, 0);
            int dress_chara = (int)dress_rows[0].get_int(0);
            int body_type = (int)dress_rows[0].get_int(1);
            int body_sub = (int)dress_rows[0].get_int(2);
            int dress_tail = (int)dress_rows[0].get_int(3);

            if (dress_chara != 0)
            {
                // character-specific body: one folder, tail id from the chara row.
                var chara_rows = db.query(
                    $"SELECT bust, tail_model_id FROM chara_data WHERE id={dress_chara}");
                int bust = 0, tail_id = dress_tail;
                if (chara_rows.Count > 0)
                {
                    bust = (int)chara_rows[0].get_int(0);
                    if (tail_id < 0) tail_id = (int)chara_rows[0].get_int(1);
                }
                return ($"3d/chara/body/bdy{dress_chara}_{body_sub:d2}", true, bust, tail_id);
            }

            // shared body: the folder rides the dress's body_type; the bust
            // size and tail come from the character row.
            var rows = db.query(
                $"SELECT bust, tail_model_id FROM chara_data WHERE id={chara_id}");
            if (rows.Count == 0) return (null, false, 0, 0);
            int bust2 = (int)rows[0].get_int(0);
            int tail2 = dress_tail;
            if (tail2 < 0) tail2 = (int)rows[0].get_int(1);
            return ($"3d/chara/body/bdy{body_type:d4}_{body_sub:d2}", false, bust2, tail2);
            }
            finally { db.Dispose(); }
        }

        // the dress's cloth slot: the container bundles ship cloth00..04 and
        // the worn dress resolves one. until the cloth index decode lands,
        // every character binds cloth00 (the base slot the containers carry
        // for all bodies) — the loader falls back down from the slot given.
        int cloth_slot(int dress_id) => 0;

        // loads a container prefab bundle by manifest name (instances are
        // plain GameObjects with one data-only component).
        GameObject load_container_prefab(string bundle_name)
        {
            var row = meta_row(bundle_name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            string want = bundle_name.Split('/').Last();
            string prefab_name = bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith($"/{want}.prefab"))
                                 ?? bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith(".prefab"));
            var prefab = string.IsNullOrEmpty(prefab_name) ? null : bundle.LoadAsset<GameObject>(prefab_name);
            return prefab == null ? null : Instantiate(prefab);
        }

        // finds the shader's main texture property; it may not be _MainTex.
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

        // fills the generic body's texture slots from per-character rows keyed on skin and bust.
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
            // tries both texture name shapes; whichever exists wins.
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
                // instances the shared body materials so each character keeps its variant.
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.name.Contains("tear"))
                    {
                        // tear materials ship a null main slot; assign the shared tear texture.
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

        // merges the head prefab's meshes onto the body skeleton; returns the renderers added.
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

            // the face morph database rides the head as a ScriptableObject
            // referenced by the root's AssetHolder table ('facial_target').
            var holder = head.GetComponent<Gallop.AssetHolder>();
            Gallop.FaceDrivenKeyTarget face_target = null;
            if (holder?._assetTable?.list != null)
                foreach (var entry in holder._assetTable.list)
                    if (entry.Key == "facial_target" && entry.Value is Gallop.FaceDrivenKeyTarget ft)
                    {
                        face_target = ft;
                        break;
                    }
            face_targets[chara_id] = face_target;
            trace_log.write($"chara {chara_id}: face target {(face_target == null ? "MISS" : $"{face_target._mouthTarget.Count} mouth / {face_target._eyeTarget.Count} eye / {face_target._eyebrowTarget.Count} eyebrow")}");

            // remaps the head's skinned meshes onto the body's bones so they deform together.
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

            // reparents the surviving head objects and destroys the replaced bone copies.
            while (head.transform.childCount > 0)
                head.transform.GetChild(0).SetParent(body_root);
            foreach (var dead in replaced)
                if (dead != null) Destroy(dead.gameObject);
            Destroy(head);

            trace_log.write($"chara {chara_id}: head merged onto body skeleton ({remapped_total} bones remapped, {kept} private kept, body skeleton {body_bones.Count})");
            return head_renderers;
        }

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

        // logs materials whose shaders resolved to fallbacks.
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

        // loads the motion clips from their 3d/motion/live/body bundles.
        private Dictionary<string, AnimationClip> load_clips(HashSet<string> motion_names)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var motion_name in motion_names)
            {
                // disk motion names are lowercased; try the lowercase form first, then upper.
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

        private void wire_drivers(Dictionary<string, AnimationClip> clips, int music_id, List<int> chara_ids)
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

            // phase 4: the postfx director drives the volume bloom + fog/fade.
            var postfx_go = new GameObject("postfx_director");
            var postfx = postfx_go.AddComponent<postfx_director>();
            postfx.open(ws, clock, cam, chara_roots);

            // phase 4: the facial director drives mouth weight + gaze from the cutt facial tracks.
            var facial_go = new GameObject("facial_director");
            var facial = facial_go.AddComponent<facial_director>();
            facial.open(ws, clock, chara_roots, face_targets, chara_ids);

            var formation_go = new GameObject("formation_driver");
            formation = formation_go.AddComponent<formation_driver>();

            // the bgColor1 tracks drive ambient + chara tint per frame.
            var bgc_go = new GameObject("bg_color1_director");
            bg_color1 = bgc_go.AddComponent<bg_color1_director>();
            bg_color1.open(ws, clock, chara_roots);
            // BgBL's ambient sweep runs over the controller instance +
            // stage_geometry (the game's ShaderParamObject hierarchy).
            var geo = GameObject.Find("stage_geometry");
            bg_color1.bind_stage(_stage_instance_root, geo != null ? geo.transform : null);
            var spring_sims = new Dictionary<int, spring_sim>(chara_springs);
            formation.open(ws, clock, chara_roots, spring_sims);
            foreach (var kv in chara_springs_roots) formation.root_chara[kv.Key] = kv.Value;

            var motion_go = new GameObject("motion_player");
            motion = motion_go.AddComponent<motion_player>();
            var seq_map = slot_sequences.for_song(music_id);
            Debug.Log($"[stage_loader] slot->sequence map: {(seq_map == null ? "none" : seq_map.Count + " slots")}");
            motion.open(ws, clock, chara_roots, seq_map ?? new List<int>());

            // binds every loaded clip that matches a sequence's motion name
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

        // samples the global-light track and hands the interpolated key to the shade publisher.
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

        // resolves the instrumental bank and starts its decode on a worker;
        // null task when the bank is missing (start_music falls back to the
        // old inline decode path).
        private System.Threading.Tasks.Task<(float[] pcm, int channels, int rate)?> start_oke_decode(int music_id)
        {
            // oke_02 is the karaoke master; oke_01 carries guide vocals on
            // some songs and doubles the chara stems when played with them.
            var candidates = new[]
            {
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_02.awb",
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_01.awb",
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
                return null;
            }

            string path = System.IO.Path.Combine(config.data_root, "dat", bank_row.hash.Substring(0, 2), bank_row.hash);
            if (!System.IO.File.Exists(path))
            {
                trace_log.write($"music: oke bank missing on disk: {path}");
                return null;
            }
            byte[] bank = System.IO.File.ReadAllBytes(path);
            var waves = live_audio.parse_afs2(bank);
            if (waves.Count == 0)
            {
                trace_log.write("music: afs2 parse found no waves");
                return null;
            }
            trace_log.write($"music: oke decode started on worker (take {bank_row.name.Substring(bank_row.name.LastIndexOf('_') + 1)}, {waves.Count} waves)");
            return System.Threading.Tasks.Task.Run<(float[] pcm, int channels, int rate)?>(
                () => live_audio.decode_wave_pcm(bank, waves[0]));
        }

        // decodes the song's instrumental bank and starts playback; the clock locks to it.
        private void start_music(int music_id,
            System.Threading.Tasks.Task<(float[] pcm, int channels, int rate)?> oke_task = null)
        {
            // tries the _02 (karaoke) oke first, falls back to _01; take
            // whichever the install carries.
            var candidates = new[]
            {
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_02.awb",
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_oke_01.awb",
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

            AudioClip clip = null;
            if (oke_task != null)
            {
                // the worker rode the load window; this wait is the leftover
                // tail only, not a fresh ~1.2s main-thread decode.
                var decoded = oke_task.Result;
                if (decoded.HasValue)
                {
                    var (pcm, channels, rate) = decoded.Value;
                    if (pcm != null && pcm.Length > 0)
                    {
                        clip = AudioClip.Create($"oke_{music_id}", pcm.Length / channels, channels, rate, false);
                        clip.SetData(pcm, 0);
                    }
                }
                if (clip == null) trace_log.write("music: worker oke decode failed, decoding inline");
            }
            if (clip == null)
            {
                clip = live_audio.decode_wave(bank, waves[0], $"oke_{music_id}");
            }
            if (clip == null)
            {
                trace_log.write("music: oke decode failed");
                return;
            }

            var go = new GameObject("live_music");
            _music_source = go.AddComponent<AudioSource>();
            _music_source.clip = clip;
            _music_source.loop = false;
            _music_source.Play();
            if (!force_free_clock) clock.bind_master(_music_source);
            else trace_log.write("clock: forced free-run for this run");
            trace_log.write($"music: oke playing ({clip.frequency}Hz, {clip.length:0.0}s, take {bank_row.name.Substring(bank_row.name.LastIndexOf('_') + 1)})");
        }

        // the cast slot's display name from the text table (category 6).
        private string chara_name(int chara_id)
        {
            try
            {
                using var db = master_db.reader.open(config.master_db_path);
                if (db == null) return $"chara {chara_id}";
                foreach (var r in db.query(
                    $"SELECT `index`, text FROM text_data WHERE category=6"))
                    if ((int)r.get_int(0) == chara_id)
                    {
                        string n = r.get_text(1);
                        db.Dispose();
                        return string.IsNullOrEmpty(n) ? $"chara {chara_id}" : n;
                    }
            }
            catch { }
            return $"chara {chara_id}";
        }

        // picks the stage id from the song's livesettings for selections without one.
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
            diag.frame_begin();
            _diag_hook();
            clock?.advance(Time.deltaTime);
            diag_pass("motion", () => motion?.play());
            diag_pass("lights_track", update_light_track);
            diag_pass("blink", () => blink_lights.update(clock?.time ?? 0f, ws?.blink_tracks));
            diag_pass("spot", () => spot_lights.update(clock?.time ?? 0f, ws?.spot_tracks, chara_roots));
            diag_pass("laser", () => laser_lights.update(clock?.time ?? 0f, ws?.laser_tracks, chara_roots));
            diag_pass("shafts", () => shafts_lights.update(clock?.time ?? 0f, ws?.shafts_tracks));
            diag_pass("volume", () => volume_uv_scroll.update_volume(clock?.time ?? 0f, ws?.volume_tracks));
            diag_pass("uvscroll", () => volume_uv_scroll.update_uv_scroll(clock?.time ?? 0f, ws?.uv_scroll_tracks));
            diag_pass("wash", () => wash_light.update(clock?.time ?? 0f, ws?.wash_tracks));
            diag_pass("additional", () => additional_light.update(clock?.time ?? 0f, ws?.additional_tracks));
            diag_pass("lightproj", () => light_projection_lights.update(clock?.time ?? 0f, ws?.light_projection));
            diag_pass("transmitted", () => transmitted_light.update(clock?.time ?? 0f, ws?.transmitted));
            diag_pass("crowd", () =>
            {
                crowd_rig.update(clock?.time ?? 0f, ws?.audience_tracks);
                audience_keys.update(clock?.time ?? 0f, ws?.audience_tracks);
                mob_control.update(clock?.time ?? 0f, ws?.mob_groups, ws?.cyalume_groups);
                cyalume.update(clock?.time ?? 0f);
            });
            diag_pass("shade", () =>
            {
                global_shade.publish(FindObjectOfType<Camera>());
                // bg_color1 first: its Chara* groups overwrite the whole
                // property block on the character renderers, which wipes any
                // earlier block. the game re-pins the toon-light state every
                // frame AFTER every other writer (fork Director.cs: the
                // global-light mpb is re-applied per frame so "any competing
                // writer loses"), so the chara pin must be the last writer.
                bg_color1?.update(clock?.time ?? 0f);
                global_shade.publish_chara_block(chara_roots);
            });

            // foot lights run after the pose so they track the characters exactly.
            diag_pass("foot", () => foot_light.update(clock?.time ?? 0f, ws?.foot_light, chara_roots));

            // the props pass runs after the pose so the hand ik lands correctly.
            diag_pass("props", () =>
                props_system.update(clock?.time ?? 0f, ws?.formation, chara_roots, ws?.props_attach, ws?.props_render));
            diag.report(Time.time);

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
                // a uniform gray center pixel means nothing rendered even when counts say otherwise.
                var probe = new Texture2D(1, 1);
                probe.ReadPixels(new Rect(cam.pixelWidth / 2, cam.pixelHeight / 2, 1, 1), 0, 0);
                probe.Apply();
                var px = probe.GetPixel(0, 0);
                trace_log.write($"px {px.r:0.00},{px.g:0.00},{px.b:0.00}");
                Destroy(probe);
                // a center hit naming its mesh separates a scene-geometry white
                // from a material/texture bind failure. renderer bounds, not
                // physics: the live scene carries no colliders.
                string center = "none";
                foreach (var r in FindObjectsOfType<Renderer>())
                {
                    if (!r.isVisible) continue;
                    var b = r.bounds;
                    var ro = new Ray(cam.transform.position, cam.transform.forward);
                    if (b.IntersectRay(ro) && b.SqrDistance(cam.transform.position) < 400f)
                    {
                        center = r.name;
                        break;
                    }
                }
                trace_log.write($"center hit {center}");

                // head-bone movement across beats proves the motion actually poses the cast.
                string pose = "";
                if (chara_roots.Count > 0)
                {
                    var head = find_deep(chara_roots[0], "Head");
                    if (head != null) pose = $" head {head.position} hrot {head.localEulerAngles}";
                }

                // the stand should hold its world spot while the chara glides under it.
                if (diag.enabled && chara_roots.Count > 0)
                {
                    var stands = GameObject.FindObjectsOfType<Transform>(true)
                        .Where(t => t.name.StartsWith("prop_stage")).Take(4);
                    foreach (var s in stands)
                    {
                        int slot_digits = s.name.LastIndexOf("slot");
                        int slot = slot_digits > 0 && int.TryParse(s.name.Substring(slot_digits + 4), out int sv) ? sv : -1;
                        string mic_info = "";
                        if (slot > 0 && slot - 1 < chara_roots.Count)
                            mic_info = $" | chara {chara_roots[slot - 1].position}";
                        trace_log.write($"props bench: {s.name} at {s.position} (parent {s.parent?.name}, active {s.gameObject.activeSelf}){mic_info}");
                    }
                }

                trace_log.write($"beat t={clock?.time ?? 0f:0.0}s cam_pos {cam.transform.position} fwd {cam.transform.forward} fov {cam.fieldOfView:0.0} renderers {visible}/{total} visible animations_playing {playing}{pose}");

                // the flagged characters' average height drives the camera layer offset rate.
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

                // dumps chara 1's render state once so a black frame points at the rejected material.
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

        // one-shot bench setup on the first enabled frame.
        private bool _diag_hooks_installed;
        private void _diag_hook()
        {
            if (!diag.enabled) return;
            if (!_diag_hooks_installed)
            {
                _diag_hooks_installed = true;
                // v-sync off plus the fps cap so the perf report reads true frame costs.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = config.target_fps;
                trace_log.write($"perf target {config.target_fps}fps, v-sync off");
            }
        }

        // per-subsystem wrappers: called from Update, each brackets one pass.
        private void diag_pass(string name, System.Action pass)
        {
            if (!diag.enabled) { pass(); return; }
            diag.begin(name);
            try { pass(); }
            finally { diag.end(name); }
        }
    }
}

