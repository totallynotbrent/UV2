using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // drives the pen-light textures and scroll from the song's choreography.
    public static class cyalume
    {
        // one choreography row: the pattern signature + its timing.
        private class pattern_row
        {
            public int pattern_id;
            public float start_time;
            public float play_speed = 1f;
            public bool pause_like;
            public string move_type = "";
            public string color_pattern = "";
        }

        private static readonly Dictionary<int, Texture2D> textures = new();
        private static readonly List<pattern_row> rows = new();
        private static readonly Dictionary<int, Texture2D> overrides = new();
        private static int last_pattern = -1;
        private static float last_scroll = float.NaN;
        private static int animation_frame_count = 32;
        private static bool loaded;

        private static readonly List<Renderer> pen_renderers = new();
        private static readonly Dictionary<Renderer, MaterialPropertyBlock> pen_blocks = new();

        // resolves the song's pen-light textures and choreography rows.
        public static void bind(int music_id)
        {
            textures.Clear();
            overrides.Clear();
            rows.Clear();
            last_pattern = -1;
            last_scroll = float.NaN;

            load_textures(music_id);
            load_overrides(music_id);
            load_choreography(music_id);
            loaded = textures.Count > 0;
            trace_log.write($"cyalume: {textures.Count} pattern textures resolved for song {music_id}, {rows.Count} choreography rows, {overrides.Count} character overrides");
            foreach (var kv in textures)
                trace_log.write($"cyalume: pattern texture {kv.Key} = {(kv.Value != null ? kv.Value.name : "null")}");
            foreach (var r in rows)
                trace_log.write($"cyalume: choreography at {r.start_time:F2}s move '{r.move_type}' colour '{r.color_pattern}' pattern {r.pattern_id} speed {r.play_speed:F3}");
            write_pattern_images();
        }

        // the pattern textures: live/cyalume/m{song}/tex_live_cyalume_m{song}_{NNN}.
        private static void load_textures(int music_id)
        {
            var wanted = new HashSet<string>();
            for (int i = 0; i < 100; i++)
                wanted.Add($"live/cyalume/m{music_id}/tex_live_cyalume_m{music_id}_{i:000}");
            using (var meta = meta_reader.reader.open(config.meta_db_path))
            {
                if (meta == null) return;
                foreach (var kv in meta.lookup(wanted))
                {
                    var bundle = game_assets.open(kv.Value, config.data_root);
                    if (bundle == null) continue;
                    var tex = bundle.LoadAsset<Texture2D>(kv.Key.Substring(kv.Key.LastIndexOf('/') + 1));
                    if (tex == null)
                    {
                        foreach (var n in bundle.GetAllAssetNames())
                        {
                            tex = bundle.LoadAsset<Texture2D>(n);
                            if (tex != null) break;
                        }
                    }
                    if (tex != null)
                    {
                        var suffix = kv.Key.Substring(kv.Key.LastIndexOf('_') + 1);
                        if (int.TryParse(suffix, out int pattern_id))
                            textures[pattern_id] = tex;
                    }
                }
            }
        }

        // loads per-character override textures from the livesettings type=16 rows.
        private static void load_overrides(int music_id)
        {
            var rows_l = read_livesettings(music_id);
            var wanted = new HashSet<string>();
            var chara_by_suffix = new List<(string, int)>();
            foreach (var r in rows_l)
            {
                if (r.type != 16) continue;
                if (string.IsNullOrEmpty(r.param2) || !int.TryParse(r.param4, out int chara_id)) continue;
                wanted.Add($"live/cyalume/m{music_id}/tex_live_cyalume_m{music_id}_{r.param2}");
                chara_by_suffix.Add((r.param2, chara_id));
            }
            if (wanted.Count == 0) return;
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return;
            var resolved = meta.lookup(wanted);
            foreach (var (suffix, chara_id) in chara_by_suffix)
            {
                var row = resolved.GetValueOrDefault($"live/cyalume/m{music_id}/tex_live_cyalume_m{music_id}_{suffix}");
                if (row == null) continue;
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) continue;
                var tex = bundle.LoadAsset<Texture2D>($"tex_live_cyalume_m{music_id}_{suffix}");
                if (tex == null)
                {
                    foreach (var n in bundle.GetAllAssetNames())
                    {
                        tex = bundle.LoadAsset<Texture2D>(n);
                        if (tex != null) break;
                    }
                }
                if (tex != null)
                {
                    overrides[chara_id] = tex;
                    trace_log.write($"cyalume: chara {chara_id} override texture _{suffix} resolved");
                }
            }
        }

        // parses the choreography csv, one row per pattern change keyed by move_type + color_pattern.
        private static void load_choreography(int music_id)
        {
            var row = meta_row($"live/musicscores/m{music_id}/m{music_id}_cyalume");
            if (row == null)
            {
                trace_log.write($"cyalume: no choreography csv for song {music_id}");
                return;
            }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return;
            var asset = bundle.LoadAsset<TextAsset>($"m{music_id}_cyalume");
            var csv = asset != null ? asset.text : null;
            if (string.IsNullOrEmpty(csv)) return;

            var signatures = new Dictionary<string, int>();
            var lines = csv.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                var cells = line.Split(',');
                if (cells.Length < 4) continue;
                if (!float.TryParse(cells[0], out float time_ms)) continue;
                // csv times above 1000 are milliseconds, below are seconds.
                float start = time_ms > 1000f ? time_ms / 1000f : time_ms;
                string move_type = cells[1];
                string color_pattern = cells[3];
                bool pause_like = move_type.Trim().Equals("Pause", StringComparison.OrdinalIgnoreCase);

                int pattern_id;
                if (pause_like)
                {
                    // pause rows keep the previous visual pattern.
                    pattern_id = rows.Count > 0 ? rows[rows.Count - 1].pattern_id : 0;
                }
                else
                {
                    string signature = move_type + "|" + color_pattern;
                    if (!signatures.TryGetValue(signature, out pattern_id))
                    {
                        pattern_id = signatures.Count;
                        signatures[signature] = pattern_id;
                    }
                }

                float play_speed = 1f;
                if (cells.Length > 2 && float.TryParse(cells[2], out float bpm) && bpm > 0f)
                    play_speed = Mathf.Max(0.01f, bpm / 140f);

                rows.Add(new pattern_row
                {
                    pattern_id = pattern_id,
                    start_time = start,
                    play_speed = play_speed,
                    pause_like = pause_like,
                    move_type = move_type,
                    color_pattern = color_pattern,
                });
            }
        }

        // records the pen-light renderers (cyalume_r/d meshes) found under a stage root.
        public static void record_pen_meshes(Transform stage_root)
        {
            if (stage_root == null) return;
            if (!_pen_recorded_roots.Add(stage_root)) return;
            foreach (var r in stage_root.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name;
                if (n.StartsWith("cyalume_r") || n.StartsWith("cyalume_d"))
                {
                    pen_renderers.Add(r);
                    pen_blocks[r] = new MaterialPropertyBlock();
                }
            }
            trace_log.write($"cyalume: {pen_renderers.Count} pen-light renderers recorded");
        }

        public static void reset_pen_meshes()
        {
            pen_renderers.Clear();
            pen_blocks.Clear();
            _pen_recorded_roots.Clear();
        }

        private static readonly HashSet<Transform> _pen_recorded_roots = new();

        // per-frame: applies the current pattern's texture and scroll offset to the pen meshes.
        public static void update(float time_sec)
        {
            if (!loaded || pen_renderers.Count == 0 || rows.Count == 0) return;

            var current = current_row(time_sec);
            if (current == null) return;

            // the pattern texture: exact id first, pattern 0 fallback, any.
            Texture2D tex = null;
            if (!textures.TryGetValue(current.pattern_id, out tex) || tex == null)
                if (!textures.TryGetValue(0, out tex) || tex == null)
                    tex = textures.Values.FirstOrDefault(t => t != null);
            if (tex == null) return;

            if (current.pattern_id != last_pattern)
            {
                last_pattern = current.pattern_id;
                foreach (var kv in pen_blocks)
                {
                    var r = kv.Key;
                    var mpb = kv.Value;
                    r.GetPropertyBlock(mpb);
                    mpb.SetTexture(id_main_tex, tex);
                    r.SetPropertyBlock(mpb);
                }
            }

            // the pattern's frames advance at the song's own tempo; each
            // light in the crowd keeps its own phase on the sheet, so the
            // crowd shimmers instead of marching in step.
            float frame;
            if (current.pause_like)
            {
                frame = 0f;
            }
            else
            {
                float delta = Mathf.Max(0f, time_sec - current.start_time);
                frame = delta * current.play_speed * cyalume_spread.SHEET_ROWS;
            }
            if (float.IsNaN(last_scroll) || Mathf.Abs(last_scroll - frame) > 0.001f)
            {
                last_scroll = frame;
                foreach (var kv in pen_blocks)
                {
                    var r = kv.Key;
                    var mpb = kv.Value;
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(id_frame, frame);
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        // the last choreography row at or before the time.
        private static pattern_row current_row(float time_sec)
        {
            pattern_row current = null;
            foreach (var r in rows)
            {
                if (r.start_time <= time_sec) current = r;
                else break;
            }
            return current;
        }

        // the per-character override texture for a chara id, or null when none is authored.
        public static Texture2D override_for_chara(int chara_id) =>
            overrides.TryGetValue(chara_id, out var tex) ? tex : null;

        public static int texture_count => textures.Count;

        // the resolved pattern textures, for the crowd probe to write down.
        public static IEnumerable<KeyValuePair<int, Texture2D>> pattern_textures() => textures;

        // writes the pattern sheets beside the player so the sprites can be
        // looked at directly; the crowd's light field is drawn from them.
        private static void write_pattern_images()
        {
            // a bench aid: the dumps litter the install folder, so they only
            // happen with diagnostics on.
            if (!UV2.App.diag.enabled) return;
            foreach (var kv in textures)
            {
                var tex = kv.Value;
                if (tex == null) continue;
                try
                {
                    // the sheet comes from an asset bundle and cannot be read
                    // back from cpu memory, so it is copied through the gpu.
                    var rt = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    var previous = RenderTexture.active;
                    var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    copy.Apply();
                    RenderTexture.active = previous;
                    string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), $"uv2_cyalume_{kv.Key}.png");
                    System.IO.File.WriteAllBytes(path, copy.EncodeToPNG());
                    UnityEngine.Object.Destroy(copy);
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                    trace_log.write($"cyalume: pattern {kv.Key} ({tex.width}x{tex.height}) written to {path}");
                }
                catch (Exception e)
                {
                    trace_log.write($"cyalume: pattern {kv.Key} dump failed: {e.GetType().Name}: {e.Message}");
                }
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

        private static readonly int id_main_tex = Shader.PropertyToID("_MainTex");
        private static readonly int id_main_tex_st = Shader.PropertyToID("_MainTex_ST");
        private static readonly int id_frame = Shader.PropertyToID("_Frame");
    }
}
