using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's bgColor1 tracks: each named group tints the
    // stage parts whose object names carry the group name (main_c_001,
    // handrail_c_001, chains_c_001, bg_001, petal_long, sky002, ...), and
    // the Chara* groups drive the character toon property block. the game's
    // StageController::UpdateBgColor1 resolves each group to its renderer
    // set by name and does SetColor(_AmbientColor = color * colorPower);
    // property id 0x99 is _AmbientColor, consumed by the game's
    // Gallop/3D/Live/Stage/* shader family that UV2 loads from the shader
    // bundle. (~/umadump/out/uv2_bgcolor1_ambient_decoded.md)
    public class bg_color1_director : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private List<Transform> chara_roots;

        private static readonly int id_ambient = Shader.PropertyToID("_AmbientColor");
        private static readonly int id_chara_color = Shader.PropertyToID("_CharaColor");
        private static readonly int id_light_probe = Shader.PropertyToID("_LightProbeColor");
        private static readonly int id_mul_color0 = Shader.PropertyToID("_MulColor0");
        private static readonly int id_color_power = Shader.PropertyToID("_ColorPower");
        private static readonly int id_toon_dark = Shader.PropertyToID("_ToonDarkColor");
        private static readonly int id_toon_bright = Shader.PropertyToID("_ToonBrightColor");
        private static readonly int id_outline_color = Shader.PropertyToID("_OutlineColor");
        private static readonly int id_outline_width = Shader.PropertyToID("_OutlineWidth");
        private static readonly int id_saturation = Shader.PropertyToID("_Saturation");

        // the chara-tint groups the game's gate allows onto character blocks.
        // CharaBack is NOT in the game's allow set (heap literal evidence) -
        // it feeds stage-side consumers, so it must not touch chara blocks.
        private static readonly HashSet<string> chara_group_names = new()
        {
            "CharaCenter", "CharaLeft", "CharaRight", "CharaColor",
        };

        private MaterialPropertyBlock mpb;
        private float last_logged = -1f;

        // per-group resolved renderer sets (the game resolves by FNV-1 hash of
        // the object/unit name, never substring: uv2_bgcolor1_resolution_decoded.md).
        private readonly Dictionary<string, List<Renderer>> part_sets = new();

        // the game's FNV-1 over the low byte of each char (FNVHash::Generate
        // 0x7ff8e4e11d00: h = h*prime XOR (c & 0xff), mul first).
        private static uint fnv1(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char c in s) h = (h * 0x01000193u) ^ (c & 0xffu);
            return h;
        }

        // strips unity's "(Clone)" token the way the game's work-info key does
        // (GallopGameObjectExtensions::DeleteCloneToken).
        private static string clean_name(string s)
        {
            int i = s.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }

        public void open(live_worksheet worksheet, timeline_clock clock_ref, List<Transform> roots)
        {
            ws = worksheet;
            clock = clock_ref;
            chara_roots = roots;
            mpb = new MaterialPropertyBlock();
            trace_log.write($"bg_color1: {ws.bg_color1.Count} groups " +
                $"({string.Join(",", System.Linq.Enumerable.Select(ws.bg_color1, t => $"{t.name}:{t.keys.Count}"))})");
        }

        // one shading pass per frame; the stage loader calls this from its
        // shade pass so ordering vs global_shade's fallback is deterministic.
        public void update(float song_time)
        {
            if (ws == null || ws.bg_color1.Count == 0) return;

            // stage geometry loads after open(); resolve the part sets once
            // the scene actually has renderers to match against.
            if (!resolved && !try_resolve_part_sets()) return;
            float frame = song_time * 60f;

            bool chara_dirty = false;
            Color last_tint = new(0f, 0f, 0f, 1f);
            string last_name = "";

            foreach (var track in ws.bg_color1)
            {
                var (cur, next, blend) = eval_track(track.keys, frame);
                if (cur == null) continue;

                Color c = lerp_color(cur.color, next?.color, blend);
                float power = lerp_f(cur.power, next?.power, blend);
                Color tint = new(c.r * power, c.g * power, c.b * power, 1f);

                // the game's global publish is last-group-PROCESSED wins
                // (worksheet frame order), regardless of whether the group
                // binds renderers. on 1004 that is smoke_alpha_d_005's
                // power-0 black, not the last resolved part's tint.
                last_tint = tint;
                last_name = track.name;

                if (chara_group_names.Contains(track.name))
                {
                    mpb.SetColor(id_chara_color, c);
                    // the game's chara "ambient": UpdateCharaColor1 publishes the
                    // bgColor1 key color as _LightProbeColor (propid 41,
                    // ModelController::SetLightProbeColor 0x7ff8e51a3320) and
                    // the chara PS multiplies the WHOLE final color by it.
                    // without this, chars render at full albedo - the washed-out
                    // look. (uv2_chara_texture_color_decoded.md §4 fix #1)
                    mpb.SetColor(id_light_probe, c);
                    mpb.SetColor(id_toon_dark, lerp_color(cur.toon_dark_color, next?.toon_dark_color, blend));
                    mpb.SetColor(id_toon_bright, lerp_color(cur.toon_bright_color, next?.toon_bright_color, blend));
                    mpb.SetColor(id_outline_color, lerp_color(cur.outline_color, next?.outline_color, blend));
                    mpb.SetFloat(id_saturation, lerp_f(cur.saturation, next?.saturation, blend));
                    // the game's tint block carries the outline width scaled
                    // from the key's outlineWidthPower (fork + gap doc §2.3b).
                    mpb.SetFloat(id_outline_width, Mathf.Max(0f, lerp_f(cur.outline_width_power, next?.outline_width_power, blend)) * 0.325f);
                    chara_dirty = true;
                }
                else if (part_sets.TryGetValue(track.name, out var renderers) && renderers.Count > 0)
                {
                    // the game's slow path tints resolved stage parts via
                    // _MulColor0 (0x94) + _ColorPower (0x97) - MPB when a
                    // block is cached, else the instance material. the fast
                    // path's _AmbientColor (0x99) global publish stays.
                    // (uv2_bgcolor1_resolution_decoded.md §3.3)
                    mpb.SetColor(id_mul_color0, tint);
                    mpb.SetFloat(id_color_power, power);
                    foreach (var r in renderers)
                    {
                        if (r == null) continue;
                        try { r.SetPropertyBlock(mpb); }
                        catch { /* destroyed mid-shutdown */ }
                    }
                }
            }

            if (chara_dirty) apply_chara_block();

            // the game publishes the winning group's tint as the shader
            // global _AmbientColor EVERY frame (slow path AND fast path of
            // StageController::UpdateBgColor1; last group in frame order
            // wins). stage parts with no group of their own read this, not
            // the trilight fallback. decode: uv2_bgcolor1_ambient_decoded.md
            // 'gate polarity resolution'.
            if (!string.IsNullOrEmpty(last_name))
                Shader.SetGlobalColor(id_ambient, last_tint);

            maybe_log(last_name, last_tint, song_time);
        }

        // finds the bracket around frame and the blend toward the next key.
        private (bg_color1_key cur, bg_color1_key next, float blend) eval_track(
            List<bg_color1_key> keys, float frame)
        {
            bg_color1_key cur = null, next = null;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].frame <= frame) { cur = keys[i]; next = i + 1 < keys.Count ? keys[i + 1] : null; }
                else break;
            }
            if (cur == null) return (null, null, 0f);

            float blend = 0f;
            if (next != null && next.interpolate_type != 0)
            {
                float span = next.frame - cur.frame;
                if (span > 0f) blend = Mathf.Clamp01((frame - cur.frame) / span);
            }
            return (cur, next, blend);
        }

        // matches every stage renderer whose object name contains the group
        // name, mirroring the game's name-resolved stage part sets. returns
        // true once at least one group has a set (or the stage is up).
        private bool resolved;
        private int resolve_attempts;

        private bool try_resolve_part_sets()
        {
            // stage geometry loads after open(); resolve the part sets once
            // the scene actually has renderers to match against.
            var all = FindObjectsOfType<Renderer>();
            if (all.Length == 0 && resolve_attempts++ < 600) return false;
            resolved = true;

            // the game's registries (StartStageObject @0x7ff8e4db0360):
            // unitNameMap[FNV(unit.name)] = unit; workInfo[FNV(name no
            // "(Clone)")] = objects. a group only ever tints objects whose
            // FNV-1 matches its TimelineNameHash EXACTLY - unit name or
            // object name; unknown hashes are silent no-ops, and the unit
            // table also expands a matched unit to every object inside it.
            // UV2 has no authored unit table, so both hops collapse to one
            // exact-hash rule on renderer names (unit object + unit name).
            foreach (var track in ws.bg_color1)
            {
                if (chara_group_names.Contains(track.name)) continue;
                uint want = fnv1(track.name);
                var set = new List<Renderer>();
                foreach (var r in all)
                {
                    if (r == null || r.transform == null) continue;
                    // object hop: exact FNV of the clean name; parent hop:
                    // exact FNV of the parent (unit) name.
                    if (fnv1(clean_name(r.transform.name)) == want
                        || (r.transform.parent != null && fnv1(clean_name(r.transform.parent.name)) == want))
                        set.Add(r);
                }
                if (set.Count > 0) part_sets[track.name] = set;
            }

            var bound = new List<string>();
            foreach (var kv in part_sets) if (kv.Value.Count > 0) bound.Add($"{kv.Key}:{kv.Value.Count}");
            trace_log.write($"bg_color1: part sets {string.Join(",", bound)}");
            return true;
        }

        private static Color lerp_color(Color a, Color? b, float t)
            => b.HasValue && t > 0f ? Color.Lerp(a, b.Value, t) : a;

        private static float lerp_f(float a, float? b, float t)
            => b.HasValue && t > 0f ? Mathf.Lerp(a, b.Value, t) : a;

        private void apply_chara_block()
        {
            if (chara_roots == null) return;
            foreach (var root in chara_roots)
            {
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    try { r.SetPropertyBlock(mpb); }
                    catch { /* destroyed mid-shutdown */ }
                }
            }
        }

        // one trace line every 5s of song time keeps the bench readable.
        private void maybe_log(string part, Color tint, float song_time)
        {
            if (last_logged >= 0f && song_time - last_logged < 5f) return;
            last_logged = song_time;
            trace_log.write($"bg_color1: {part} tint ({tint.r:0.00},{tint.g:0.00},{tint.b:0.00})");
        }
    }
}
