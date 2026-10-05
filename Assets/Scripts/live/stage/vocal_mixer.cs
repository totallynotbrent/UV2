using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // per-character vocal stems mixed by the song's part table: at open it
    // decodes every cast member's vocal bank, then each frame it sets each
    // member's volume from the part group its stage column belongs to.
    // the part table (m<song>_part) is a per-time activation of five groups
    // lleft..rright; the groups are stage columns, derived at runtime from
    // the slot x positions so no song carries hardcoded positions.
    public class vocal_mixer : MonoBehaviour
    {
        private class stem
        {
            public AudioSource source;
            public int group;
            public float volume;
            public bool started;
        }

        private readonly List<stem> stems = new();
        private float[] part_times;
        private int[][] part_rows;   // per row: 5 group flags
        private float last_volume = 1f;
        private const float fade_rate = 20f;  // ~50ms ramp between 0 and 1

        private timeline_clock clock;
        private bool bound;

        // decodes each member's vocal bank and starts it muted; volume comes
        // from the part table every frame. members without a bank are skipped.
        public void open(timeline_clock clock_ref, IReadOnlyList<Transform> chara_roots,
            List<slot_pick> slots, int music_id)
        {
            clock = clock_ref;
            var go = new GameObject("live_vocals");
            go.transform.SetParent(transform, false);

            // the part csv: rows of time,lleft,left,center,right,rright.
            var rows = load_part_table(music_id);
            if (rows != null && rows.Count > 0)
            {
                part_times = rows.Select(r => r.time).ToArray();
                part_rows = rows.Select(r => r.flags).ToArray();
            }
            else
            {
                trace_log.write($"vocals: no part table for song {music_id}; stems run uncut");
                part_times = null;
                part_rows = null;
            }

            if (chara_roots.Count == 0) return;

            // stage columns: quantize slot x into 5 groups across the cast's
            // live x span. members on the same column share a group.
            float xmin = float.MaxValue, xmax = float.MinValue;
            foreach (var root in chara_roots)
            {
                if (root == null) continue;
                float x = root.position.x;
                xmin = Mathf.Min(xmin, x);
                xmax = Mathf.Max(xmax, x);
            }
            float span = Mathf.Max(0.01f, xmax - xmin);

            int cast_index = 0;
            for (int i = 0; i < slots.Count && cast_index < chara_roots.Count; i++)
            {
                var slot = slots[i];
                if (slot.chara_id <= 0) continue;
                var root = chara_roots[cast_index++];
                if (root == null) continue;

                int group = Mathf.Clamp(Mathf.RoundToInt((root.position.x - xmin) / span * 4f), 0, 4);
                var source = start_stem(go.transform, music_id, slot.chara_id);
                if (source == null) continue;
                stems.Add(new stem { source = source, group = group, volume = 0f });
            }
            trace_log.write($"vocals: {stems.Count} stems bound, groups " +
                string.Join(",", stems.Select(s => s.group)));
            bound = stems.Count > 0;
        }

        // decodes one member's vocal bank and starts playback muted.
        private AudioSource start_stem(Transform parent, int music_id, int chara_id)
        {
            var candidates = new[]
            {
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_chara_{chara_id}_01.awb",
                $"sound/l/{music_id}/snd_bgm_live_{music_id}_chara_{chara_id}_02.awb",
            };
            meta_reader.asset_row row = null;
            using (var meta = meta_reader.reader.open(config.meta_db_path))
            {
                var rows = meta?.lookup(new HashSet<string>(candidates));
                foreach (var c in candidates)
                {
                    row = rows?.GetValueOrDefault(c);
                    if (row != null) break;
                }
            }
            if (row == null) return null;

            string path = System.IO.Path.Combine(config.data_root, "dat",
                row.hash.Substring(0, 2), row.hash);
            if (!System.IO.File.Exists(path)) return null;
            byte[] bank = System.IO.File.ReadAllBytes(path);
            var waves = live_audio.parse_afs2(bank);
            if (waves.Count == 0) return null;

            var clip = live_audio.decode_wave(bank, waves[0], $"voc_{chara_id}");
            if (clip == null) return null;

            var source = parent.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = false;
            source.volume = 0f;
            // do NOT Play() here: the stems decode staggered across many
            // seconds while the bgm already runs; a free-running playhead
            // lands each member at a different offset. the volume ride
            // starts the stem aligned to the song clock on first unmute.
            return source;
        }

        // the part table from the meta manifest: m<song>_part, a csv text asset
        // inside a dat bundle. returns null when the song has none.
        private List<(float time, int[] flags)> load_part_table(int music_id)
        {
            meta_reader.asset_row row;
            using (var meta = meta_reader.reader.open(config.meta_db_path))
            {
                var rows = meta?.lookup(new HashSet<string> { $"live/musicscores/m{music_id}/m{music_id}_part" });
                row = rows?.Values.FirstOrDefault();
            }
            if (row == null) return null;

            string csv = null;
            try
            {
                csv = game_assets.load_text(row, config.data_root, $"m{music_id}_part");
            }
            catch (Exception e)
            {
                trace_log.write($"vocals: part decode failed: {e.Message}");
                return null;
            }
            if (string.IsNullOrEmpty(csv)) return null;

            var out_rows = new List<(float, int[])>();
            bool header = true;
            foreach (var line in csv.Split('\n'))
            {
                var cols = line.Trim().Split(',');
                if (header) { header = false; continue; }
                if (cols.Length < 6) continue;
                if (!float.TryParse(cols[0], out float time)) continue;
                var flags = new int[5];
                for (int g = 0; g < 5; g++)
                    int.TryParse(cols[g + 1], out flags[g]);
                out_rows.Add((time / 1000f, flags));
            }
            trace_log.write($"vocals: part table {music_id}: {out_rows.Count} rows");
            return out_rows;
        }

        // the per-frame volume ride: 1 when the member's group is active at
        // the clock, 0 otherwise, both approached with a short ramp. each
        // stem starts on its first unmute, playhead synced to the song clock.
        private void Update()
        {
            if (!bound) return;
            float t = clock?.time ?? 0f;
            int[] active = part_flags(t);

            foreach (var s in stems)
            {
                float target = 1f;
                if (active != null) target = active[s.group] > 0 ? 1f : 0f;

                if (target > 0f && !s.started && s.source != null && s.source.clip != null)
                {
                    // full-length per-character stem: align its playhead to
                    // the song clock, wrapping if the song time exceeds the
                    // clip (the oke and stem lengths can differ slightly).
                    s.source.time = t % s.source.clip.length;
                    s.source.Play();
                    s.started = true;
                }

                s.volume = Mathf.MoveTowards(s.volume, target * last_volume, fade_rate * Time.deltaTime);
                if (s.source != null) s.source.volume = s.volume;
            }
        }

        // binary-search the part row at time t; the row holds until the next.
        private int[] part_flags(float t)
        {
            if (part_times == null || part_times.Length == 0) return null;
            int lo = 0, hi = part_times.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (part_times[mid] <= t) lo = mid;
                else hi = mid - 1;
            }
            return part_rows[lo];
        }

        // the ui volume slider.
        public void set_volume(float v)
        {
            last_volume = Mathf.Clamp01(v);
        }
    }
}
