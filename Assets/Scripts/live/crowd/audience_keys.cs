using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the audienceList key track: per-entry transform + cyalume tint.
    public static class audience_keys
    {
        // one entry's shading state, resolved at bind.
        private class entry_state
        {
            public Transform root;
            public List<Renderer> body_renderers = new();
            public List<Renderer> glow_renderers = new();
            public MaterialPropertyBlock body_block;
            public MaterialPropertyBlock glow_block;
        }

        // entry-indexed to match the crowd rig's instantiation order.
        private static readonly List<entry_state> entries = new();

        public static void reset() => entries.Clear();

        // binds one shading state per audienceList entry, null when the rig instance is missing.
        public static void bind(List<audience_track> tracks)
        {
            entries.Clear();
            if (tracks == null || tracks.Count == 0)
            {
                trace_log.write("audience keys: no authored audienceList track");
                return;
            }
            int bound_count = 0;
            for (int entry = 0; entry < tracks.Count; entry++)
            {
                var track = tracks[entry];
                var root = crowd_rig.instance_root(entry);
                var state = resolve(root);
                if (state == null)
                {
                    if (!string.IsNullOrEmpty(track.name))
                        trace_log.write($"audience keys: entry {entry} ({track.name}) has no rig instance");
                    entries.Add(null);
                    continue;
                }
                entries.Add(state);
                bound_count++;
            }
            trace_log.write($"audience keys: {bound_count}/{tracks.Count} entries shaded");
        }

        // the entry's rig: the body renderers vs the cyalume/glow children.
        private static entry_state resolve(Transform root)
        {
            if (root == null) return null;
            var state = new entry_state
            {
                root = root,
                body_block = new MaterialPropertyBlock(),
                glow_block = new MaterialPropertyBlock(),
            };
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("glow") || r.name.Contains("cyalume"))
                    state.glow_renderers.Add(r);
                else
                    state.body_renderers.Add(r);
            }
            return state;
        }

        // samples every entry's key track and publishes the transform + tint.
        public static void update(float time_sec, List<audience_track> tracks)
        {
            if (tracks == null || tracks.Count == 0 || entries.Count == 0) return;
            float frame = time_sec * 60f;
            for (int entry = 0; entry < tracks.Count && entry < entries.Count; entry++)
            {
                var state = entries[entry];
                if (state == null || state.root == null) continue;
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

                state.root.localPosition = Vector3.Lerp(a.position, b.position, blend);
                state.root.localRotation = Quaternion.Euler(Vector3.Lerp(a.rotate, b.rotate, blend));
                state.root.localScale = Vector3.Lerp(a.scale, b.scale, blend);

                // the body tint plus glow color * power on the additive children.
                if (state.body_renderers.Count > 0)
                {
                    state.body_block.SetColor(id_color, Color.Lerp(a.cyalume_color, b.cyalume_color, blend));
                    foreach (var r in state.body_renderers) r.SetPropertyBlock(state.body_block);
                }
                if (state.glow_renderers.Count > 0)
                {
                    var glow = Color.Lerp(a.cyalume_glow_color, b.cyalume_glow_color, blend)
                               * Mathf.Lerp(a.cyalume_glow_color_power, b.cyalume_glow_color_power, blend);
                    state.glow_block.SetColor(id_color, glow);
                    foreach (var r in state.glow_renderers) r.SetPropertyBlock(state.glow_block);
                }
            }
        }

        private static readonly int id_color = Shader.PropertyToID("_Color");
    }
}
