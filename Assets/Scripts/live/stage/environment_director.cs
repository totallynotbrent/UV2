using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's environment track: the concert's floor mirror.
    // son1004_full_data.json ships one group named "Environment" whose keys
    // carry isValidMirror, isMirror and the authored mirrorReflectionRate
    // (0.27 to 0.51 across the song, versus the hardcoded 1.0 the fork
    // published). isMirror turns the mirror on from frame 632; the rate is
    // blended between keys with the authored interpolation.
    public class environment_director : MonoBehaviour
    {
        private live_worksheet ws;
        private environment_track track;
        private List<mirror_reflection> mirrors = new();
        private bool mirrors_resolved;
        private float last_rate = -1f;
        private bool last_on;
        private int last_characters = -1;

        public void open(live_worksheet worksheet)
        {
            ws = worksheet;
            if (ws?.environments == null || ws.environments.Count == 0) return;
            track = ws.environments.Find(t => t.name == "Environment");
            if (track == null) track = ws.environments[0];
            trace_log.write($"environment: track '{track.name}' keys {track.keys.Count}");
        }

        // the mirror components ride on the stage floor renderer; they are
        // attached after the stage is instantiated, so resolve them late.
        private void resolve_mirrors()
        {
            if (mirrors_resolved) return;
            mirrors.Clear();
            mirrors.AddRange(FindObjectsByType<mirror_reflection>(FindObjectsSortMode.None));
            mirrors_resolved = mirrors.Count > 0;
            trace_log.write($"environment: mirrors resolved {mirrors.Count}");
        }

        public void update(float song_time)
        {
            if (track == null || track.keys.Count == 0) return;
            resolve_mirrors();
            if (mirrors.Count == 0) return;

            int i = key_eval.bracket(track.keys, song_time);
            if (i < 0) i = 0;
            var cur = track.keys[i];
            var next = i + 1 < track.keys.Count ? track.keys[i + 1] : null;

            // the mirror needs the concert camera to build its reflection
            // camera and texture; without them the floor samples unity's
            // white default and reads as a blown-out slab.
            var cam = Camera.main;
            if (cam == null)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (var c in cams)
                    if (c != null && c.name == "main_camera") { cam = c; break; }
            }
            for (int mi = 0; mi < mirrors.Count; mi++)
            {
                var m = mirrors[mi];
                if (m == null) continue;
                if (!m.is_configured && m.ensure_configured(cam))
                    trace_log.write($"environment: mirror configured with camera '{cam.name}'");
            }

            // the discrete mirror flags hold their key's value; the rate
            // ramps linearly between keys, because the environment key
            // carries no interpolation metadata of its own.
            bool on = cur.is_valid_mirror != 0 && cur.is_mirror != 0;
            float rate = cur.mirror_reflection_rate;
            if (next != null)
            {
                float span = next.time - cur.time;
                if (span > 0f)
                {
                    float raw = Mathf.Clamp01((song_time - cur.time) / span);
                    rate = key_eval.lerp_f(cur.mirror_reflection_rate, next.mirror_reflection_rate, raw);
                }
            }

            bool changed = on != last_on || Mathf.Abs(rate - last_rate) > 1e-04f
                           || cur.character_mirror != last_characters;
            for (int mi = 0; mi < mirrors.Count; mi++)
            {
                var m = mirrors[mi];
                if (m == null) continue;
                m.set_authored_mirror(on, rate);
            }
            if (changed)
            {
                trace_log.write($"environment: mirror {(on ? "on" : "off")} rate {rate:0.000} characters {cur.character_mirror}");
                last_on = on;
                last_rate = rate;
                last_characters = cur.character_mirror;
            }
        }
    }
}
