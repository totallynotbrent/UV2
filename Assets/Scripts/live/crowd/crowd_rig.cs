using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // instantiates the song's audience prefabs and plays their crowd clips.
    public static class crowd_rig
    {
        // one instance per worksheet entry: the resolved root + its animator.
        private class crowd_instance
        {
            public GameObject root;
            public Animation anim;
            public string playing_clip;
        }

        private static readonly List<crowd_instance> instances = new();
        private static readonly Dictionary<string, GameObject> prefab_cache = new();
        private static readonly Dictionary<string, AnimationClip> clip_cache = new();
        private static readonly List<AnimationClip> song_clip_list = new();
        private static readonly List<GameObject> stage_audience_table = new();
        private static readonly HashSet<Transform> anim_started_roots = new();

        public static int instance_count => instances.Count;

        public static Transform instance_root(int entry_index) =>
            entry_index >= 0 && entry_index < instances.Count && instances[entry_index] != null
                ? instances[entry_index].root.transform
                : null;

        // records the stage controller's audience prefab table.
        public static void record_stage_audience_table(Gallop.Live.StageController ctrl)
        {
            stage_audience_table.Clear();
            if (ctrl?._audienceObjects == null) return;
            foreach (var go in ctrl._audienceObjects)
                if (go != null) stage_audience_table.Add(go);
            trace_log.write($"crowd rig: stage audience table {stage_audience_table.Count} prefab(s)");
        }

        // spawns the pen-light prefabs under every stage cyalume controller.
        public static int spawn_cyalume_rig(Transform stage_root)
        {
            if (stage_root == null) return 0;
            int spawned = 0;
            bool diag_done = false;
            foreach (var ctrl in stage_root.GetComponentsInChildren<Transform>(true))
            {
                if (!ctrl.name.Contains("cyalume_controller")) continue;
                var holder = ctrl.GetComponent<Gallop.AssetHolder>();
                if (!diag_done)
                {
                    diag_done = true;
                    trace_log.write($"cyalume spawn diag: controller '{ctrl.name}' holder={(holder != null)} table={(holder?._assetTable != null)} list={(holder?._assetTable?.list?.Count ?? -1)}");
                }
                if (holder?._assetTable?.list == null) continue;
                foreach (var entry in holder._assetTable.list)
                {
                    if (entry?.Value == null) continue;
                    if (entry.Value is not UnityEngine.GameObject entry_go) continue;
                    var piece = UnityEngine.Object.Instantiate(entry_go, ctrl);
                    piece.name = entry_go.name.Replace("pfb_env_live_cmn_", "");
                    spawned++;
                }
            }
            if (spawned > 0)
                trace_log.write($"crowd rig: spawned {spawned} pen-light/mob objects from the controller asset tables");
            return spawned;
        }

        // writes the song's pen-light pattern textures beside the trace so the
        // sprites that light the audience can be looked at directly.
        public static void dump_pattern_images(Transform root)
        {
            foreach (var kv in cyalume.pattern_textures())
            {
                var tex = kv.Value;
                if (tex == null) continue;
                try
                {
                    var png = tex.EncodeToPNG();
                    string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), $"uv2_cyalume_{kv.Key}.png");
                    System.IO.File.WriteAllBytes(path, png);
                    trace_log.write($"cyalume: pattern {kv.Key} ({tex.width}x{tex.height}) written to {path}");
                }
                catch (Exception e)
                {
                    trace_log.write($"cyalume: pattern {kv.Key} dump failed: {e.GetType().Name}");
                }
            }
        }

        // describes the crowd geometry the stage carries so the portrait of
        // the audience area is on record: every pen light, mob and flare
        // mesh with its vertex count, world footprint and material.
        public static void dump_crowd_structure(Transform root)
        {
            if (root == null) return;
            int shown = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string owner = r.gameObject.name;
                if (!(owner.Contains("cyalume", StringComparison.OrdinalIgnoreCase)
                      || owner.Contains("audience", StringComparison.OrdinalIgnoreCase)
                      || owner.Contains("mob", StringComparison.OrdinalIgnoreCase)
                      || owner.Contains("flare", StringComparison.OrdinalIgnoreCase)
                      || owner.Contains("lash", StringComparison.OrdinalIgnoreCase))) continue;
                if (shown >= 22) break;
                shown++;
                int verts = 0;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) verts = mf.sharedMesh.vertexCount;
                var sk = r as SkinnedMeshRenderer;
                if (sk != null && sk.sharedMesh != null) verts = sk.sharedMesh.vertexCount;
                var b = r.bounds;
                string mesh_extra = "";
                var mesh = mf != null ? mf.sharedMesh : (sk != null ? sk.sharedMesh : null);
                if (mesh != null)
                {
                    var cols = mesh.colors32;
                    string col_note = "no_colors";
                    if (cols != null && cols.Length > 0)
                    {
                        int sr = 0, sg = 0, sb = 0, sa = 0, ma = 0;
                        foreach (var c in cols) { sr += c.r; sg += c.g; sb += c.b; sa += c.a; if (c.a > ma) ma = c.a; }
                        int n = cols.Length;
                        col_note = $"vc_avg({sr / n},{sg / n},{sb / n},{sa / n}) vc_alpha_max {ma}";
                    }
                    var uv = mesh.uv;
                    string uv_note = "no_uv";
                    if (uv != null && uv.Length > 0)
                    {
                        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                        foreach (var t in uv)
                        {
                            if (t.x < u0) u0 = t.x; if (t.x > u1) u1 = t.x;
                            if (t.y < v0) v0 = t.y; if (t.y > v1) v1 = t.y;
                        }
                        uv_note = $"uv[{u0:0.00},{u1:0.00}]x[{v0:0.00},{v1:0.00}]";
                    }
                    // the game may carry the sprite placement in another
                    // channel that its controller copies into uv0 at run time.
                    int uv2_len = mesh.uv2 != null ? mesh.uv2.Length : 0;
                    int uv3_len = mesh.uv3 != null ? mesh.uv3.Length : 0;
                    int uv4_len = mesh.uv4 != null ? mesh.uv4.Length : 0;
                    if (uv2_len + uv3_len + uv4_len > 0)
                        uv_note += $" uv2({uv2_len}) uv3({uv3_len}) uv4({uv4_len})";
                    mesh_extra = $" {{{col_note} {uv_note} inst={(sk != null ? sk.sharedMesh.blendShapeCount : -1)}}}";
                }
                string mats = "";
                var mm = r.sharedMaterial;
                if (mm != null)
                {
                    string col = "";
                    if (mm.HasProperty("_Color")) col += $" c={mm.GetColor("_Color")}";
                    if (mm.HasProperty("_BaseColor")) col += $" bc={mm.GetColor("_BaseColor")}";
                    if (mm.HasProperty("_EmissionColor")) col += $" em={mm.GetColor("_EmissionColor")}";
                    var main = mm.HasProperty("_MainTex") ? mm.GetTexture("_MainTex") : null;
                    mats = $" [{mm.name}|{mm.shader.name}{col} tex={(main != null ? main.name : "-")} q={mm.renderQueue}]";
                    // the pen light field lives or dies on its material, so
                    // every property it carries is written down.
                    if (owner.Contains("cyalume", StringComparison.OrdinalIgnoreCase)
                        || owner.Contains("mob", StringComparison.OrdinalIgnoreCase))
                    {
                        // the game's crowd shaders carry a handful of colour
                        // and brightness properties; their names are probed by
                        // hand because the material api differs per unity
                        // version.
                        string[] wanted_properties =
                        {
                            "_MainTex", "_BaseMap", "_Tex", "_PatternTex", "_CyalumeTex", "_DetailTex",
                            "_Color", "_BaseColor", "_TintColor", "_ColorPower", "_Brightness", "_Intensity",
                            "_EmissionColor", "_GlowColor", "_PenColor", "_LightColor", "_CandleColor",
                            "_Rate", "_Power", "_Gain", "_Opacity", "_Alpha", "_Cutoff",
                            "_Scroll", "_Speed", "_Phase", "_Blend", "_Fade", "_Cull"
                        };
                        var sb = new System.Text.StringBuilder();
                        foreach (var pn in wanted_properties)
                        {
                            if (!mm.HasProperty(pn)) continue;
                            string pv = "?";
                            var tx = mm.GetTexture(pn);
                            if (tx != null) pv = tx.name;
                            else
                            {
                                try { pv = mm.GetColor(pn).ToString(); }
                                catch
                                {
                                    try { pv = mm.GetVector(pn).ToString(); }
                                    catch { try { pv = mm.GetFloat(pn).ToString("0.000"); } catch { pv = "?"; } }
                                }
                            }
                            if (sb.Length > 260) break;
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(pn).Append('=').Append(pv);
                        }
                        mats += $" {{props:{sb}}}";
                    }
                }
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                var bound = block.GetTexture(Shader.PropertyToID("_MainTex"));
                if (bound != null) mats += $" [block _MainTex={bound.name}]";
                trace_log.write($"crowd mesh '{owner}' {r.GetType().Name} verts={verts} bounds c({b.center.x:F1},{b.center.y:F1},{b.center.z:F1}) s({b.size.x:F1},{b.size.y:F1},{b.size.z:F1}) en={r.enabled} act={r.gameObject.activeInHierarchy} vis={r.isVisible}{mesh_extra}{mats}");
            }
            trace_log.write($"crowd structure: {shown} mesh(es) described");
        }

        // loads the song's crowd clips from the livesettings type=10 rows.
        public static void bind_song_clips(int music_id)
        {
            song_clip_list.Clear();
            clip_cache.Clear();
            var rows = read_livesettings(music_id);
            int clip_rows = 0;
            foreach (var r in rows)
            {
                if (r.type != 10 || string.IsNullOrEmpty(r.param1)) continue;
                clip_rows++;
                var clip = load_clip(r.param1);
                if (clip != null) song_clip_list.Add(clip);
            }
            trace_log.write($"crowd rig: {clip_rows} livesettings clip rows -> {song_clip_list.Count} crowd clips loaded (song {music_id})");
        }

        // instantiates one crowd instance per audienceList entry keyed by list position.
        public static void bind(List<audience_track> tracks, Transform parent, int music_id)
        {
            instances.Clear();
            if (tracks == null) return;
            int resolved = 0;
            var missing = new List<string>();
            foreach (var track in tracks)
            {
                if (string.IsNullOrEmpty(track.name)) { instances.Add(null); continue; }
                var instance = resolve_instance(track, parent);
                if (instance == null)
                {
                    missing.Add(track.name);
                    instances.Add(null);
                    continue;
                }
                instances.Add(instance);
                resolved++;
            }
            trace_log.write($"crowd rig: {resolved}/{tracks.Count} audience instances bound, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"crowd rig unresolved: {m}");
        }

        // resolves the entry's prefab: stage table first, then a name match, then the common bundle.
        private static crowd_instance resolve_instance(audience_track track, Transform parent)
        {
            GameObject prefab = null;
            if (track.object_index >= 0 && track.object_index < stage_audience_table.Count)
                prefab = stage_audience_table[track.object_index];
            if (prefab == null)
            {
                var short_name = track.name.Replace("pfb_env_live_cmn_", "");
                foreach (var go in stage_audience_table)
                    if (go != null && go.name.Contains(short_name)) { prefab = go; break; }
            }
            if (prefab == null) prefab = load_prefab(track.name);
            if (prefab == null) return null;

            var root = UnityEngine.Object.Instantiate(prefab, parent);
            root.name = $"audience_{instances.Count}_{track.name}";
            var c = new crowd_instance
            {
                root = root,
                anim = root.GetComponent<Animation>() ?? root.GetComponentInChildren<Animation>(true),
            };
            // the crowd colliders only serve gameplay raycasts; a viewer does not need them.
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(col);
            shader_manager.fix_game_shaders(root.transform, "crowd");
            return c;
        }

        // the common-bundle prefab: 3d/env/live/common/cyalume_audience/<name>.
        private static GameObject load_prefab(string name)
        {
            if (prefab_cache.TryGetValue(name, out var cached)) return cached;
            var row = meta_row($"3d/env/live/common/cyalume_audience/{name}");
            if (row == null)
            {
                trace_log.write($"crowd rig: no manifest row for audience prefab {name}");
                return null;
            }
            load_prereqs(row);
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            var prefab = bundle.LoadAsset<GameObject>(name);
            if (prefab == null)
            {
                var all = bundle.GetAllAssetNames();
                var prefab_name = all.FirstOrDefault(n => n.EndsWith(".prefab"));
                if (prefab_name != null) prefab = bundle.LoadAsset<GameObject>(prefab_name);
            }
            if (prefab != null) prefab_cache[name] = prefab;
            return prefab;
        }

        // one crowd clip from the motions folder; the bundle stays loaded.
        private static AnimationClip load_clip(string name)
        {
            if (clip_cache.TryGetValue(name, out var cached)) return cached;
            var row = meta_row($"3d/env/live/common/cyalume_audience/motions/{name}");
            if (row == null)
            {
                trace_log.write($"crowd rig: no manifest row for crowd clip {name}");
                return null;
            }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            var clip = bundle.LoadAsset<AnimationClip>(name);
            if (clip == null)
            {
                foreach (var n in bundle.GetAllAssetNames())
                {
                    clip = bundle.LoadAsset<AnimationClip>(n);
                    if (clip != null) break;
                }
            }
            if (clip != null) clip_cache[name] = clip;
            return clip;
        }

        private static void load_prereqs(meta_reader.asset_row row)
        {
            if (string.IsNullOrEmpty(row.prereq)) return;
            foreach (var pre in row.prereq.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var pre_row = meta_row(pre.Trim());
                if (pre_row != null) game_assets.open(pre_row, config.data_root);
            }
        }

        // plays every stage-embedded Animation once per stage root.
        public static void start_stage_animations(Transform stage_root)
        {
            if (stage_root == null || !anim_started_roots.Add(stage_root)) return;
            int started = 0;
            foreach (var anim in stage_root.GetComponentsInChildren<Animation>(true))
            {
                if (anim == null || anim.clip == null) continue;
                anim.Play(anim.clip.name);
                started++;
            }
            trace_log.write($"crowd rig: {started} stage animations started on '{stage_root.name}'");
        }

        public static void reset()
        {
            anim_started_roots.Clear();
            stage_audience_table.Clear();
            instances.Clear();
            prefab_cache.Clear();
            clip_cache.Clear();
            song_clip_list.Clear();
        }

        // plays each entry's authored crowd clip for the frame's key.
        public static void update(float time_sec, List<audience_track> tracks)
        {
            if (tracks == null || tracks.Count == 0 || instances.Count == 0) return;
            float frame = time_sec * 60f;
            for (int entry = 0; entry < tracks.Count && entry < instances.Count; entry++)
            {
                var c = instances[entry];
                if (c == null || c.root == null) continue;
                var keys = tracks[entry].keys;
                if (keys == null || keys.Count == 0) continue;

                audience_key a, b;
                float blend;
                if (frame <= keys[0].frame) { a = b = keys[0]; blend = 0f; }
                else if (frame >= keys[keys.Count - 1].frame) { a = b = keys[keys.Count - 1]; blend = 0f; }
                else
                {
                    a = b = keys[keys.Count - 1]; blend = 0f;
                    for (int i = 0; i < keys.Count - 1; i++)
                    {
                        if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                        {
                            a = keys[i]; b = keys[i + 1];
                            // the NEXT key's interpolate type drives the blend.
                            blend = key_eval.interp(a, b, time_sec);
                            break;
                        }
                    }
                }

                c.root.SetActive(true);
                play_clip(c, blend < 0.5f ? a : b);
            }
        }

        // plays the key's selected crowd clip at its speed, stopping on a negative index.
        private static void play_clip(crowd_instance c, audience_key key)
        {
            if (c.anim == null) return;
            if (key.animation_index < 0 || key.animation_index >= song_clip_list.Count)
            {
                if (c.playing_clip != null)
                {
                    c.anim.Stop();
                    c.playing_clip = null;
                }
                return;
            }
            var clip = song_clip_list[key.animation_index];
            if (clip == null) return;
            if (c.playing_clip != clip.name)
            {
                if (!c.anim.GetClip(clip.name)) c.anim.AddClip(clip, clip.name);
                var state = c.anim[clip.name];
                if (state != null)
                {
                    state.wrapMode = key.animation_wrap_mode == 1 ? WrapMode.Once
                        : key.animation_wrap_mode == 4 ? WrapMode.PingPong
                        : key.animation_wrap_mode == 8 ? WrapMode.ClampForever
                        : WrapMode.Loop;
                }
                c.anim.Play(clip.name);
                c.playing_clip = clip.name;
            }
            var st = c.anim[clip.name];
            if (st != null)
            {
                st.speed = key.animation_speed > 0f ? key.animation_speed : 1f;
                if (key.use_animation_time != 0)
                {
                    st.time = key.animation_time;
                }
                else if (!float.IsNaN(st.time) && key.animation_offset_time != 0f && st.time == 0f)
                {
                    // the offset is a start-phase into the clip, not a duration.
                    st.time = Mathf.Repeat(key.animation_offset_time, st.length > 0f ? st.length : 1f);
                }
                if (st.length > 0f) st.time = Mathf.Repeat(st.time, st.length);
            }
        }

        private static List<UV2.Data.livesettings_row> read_livesettings(int music_id)
        {
            var row = meta_row("livesettings");
            if (row == null) return new();
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return new();
            if (!bundle.Contains(music_id.ToString())) return new();
            var text = bundle.LoadAsset<TextAsset>(music_id.ToString());
            return livesettings.parse_csv(text != null ? text.text : null);
        }

        private static meta_reader.asset_row meta_row(string name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { name });
            return rows?.GetValueOrDefault(name);
        }
    }
}
