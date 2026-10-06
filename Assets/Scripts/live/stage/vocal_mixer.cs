using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // per-character vocal stems mixed by the song's part table, using the
    // game's own placement model (decoded from the IL2CPP dump; the tables
    // below are heap-verified, see umadump/out/uv2_vocal_cue_decode.md).
    // at open it decodes each cast member's vocal bank (main take plus the
    // extra take when the bank carries one), then each frame it gates each
    // stem on its part group flag, ranks the singing stems left to right,
    // and applies the placement row's type gain, singer-count rate and pan.
    // the part table (m<song>_part) is a per-time activation of five groups
    // lleft..rright; the groups are stage columns, derived at runtime from
    // the slot x positions so no song carries hardcoded positions.
    public class vocal_mixer : MonoBehaviour
    {
        private class stem
        {
            public AudioSource source;
            public int group;
            public int wave;          // 0 = main take, 1 = extra take
            public AudioClip[] clips; // [main, extra?]
            public float volume;
            public bool started;
        }

        private readonly List<stem> stems = new();
        private float[] part_times;
        private int[][] part_rows;   // per row: 5 group flags (0 silent, 1 main, 2 extra)
        private float last_volume = 1f;
        private const float fade_rate = 20f;  // ~50ms ramp between gain steps

        // the game's gain tables (AudioManager container +0x30/+0x40): gain
        // by placement type, and the rate by how many singers are active.
        private static readonly float[] part_type_gains =
            { 0f, 0.79f, 0.89f, 1.0f, 1.12f, 1.26f };
        private static readonly float[] part_volume_rates =
            { 0f, 0.79f, 0.79f, 0.56f, 0.53f, 0.47f, 0.42f, 0.37f };

        // placement rows by active singer count: (type, pan) per singer,
        // ranked left to right on stage (container +0x38).
        private static readonly (int type, float pan)[][] placement_rows =
        {
            new (int, float)[] { (5, 0.00f) },
            new (int, float)[] { (2, -0.15f), (2, 0.15f) },
            new (int, float)[] { (3, -0.30f), (5, 0.00f), (3, 0.30f) },
            new (int, float)[] { (3, -0.30f), (3, -0.10f), (3, 0.10f), (3, 0.30f) },
            new (int, float)[] { (1, -0.30f), (2, -0.15f), (4, 0.00f), (2, 0.15f), (1, 0.30f) },
            new (int, float)[] { (3, -0.30f), (3, -0.20f), (3, -0.10f), (3, 0.10f), (3, 0.20f), (3, 0.30f) },
            new (int, float)[] { (1, -0.30f), (1, -0.20f), (2, -0.10f), (4, 0.00f), (2, 0.10f), (1, 0.20f), (1, 0.30f) },
        };

        private timeline_clock clock;
        private bool bound;
        private Transform[] stem_roots;   // chara roots, parallel to stems
        private int last_section = -1;

        // decodes each member's vocal bank and starts it muted; volume and
        // pan come from the part table every frame. members without a bank
        // are skipped.
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

            // the stage-column map is built lazily in Update: at open time the
            // roots sit at their spawn x (often the same spot) and the
            // formation has not separated them yet. quantizing now would put
            // the whole cast in one group and gate them together.
            int cast_index = 0;
            var roots_list = new List<Transform>();
            for (int i = 0; i < slots.Count && cast_index < chara_roots.Count; i++)
            {
                var slot = slots[i];
                if (slot.chara_id <= 0) continue;
                var root = chara_roots[cast_index++];
                if (root == null) continue;

                var (source, clips) = start_stem(go.transform, music_id, slot.chara_id);
                if (source == null) continue;
                roots_list.Add(root);
                stems.Add(new stem { source = source, group = 0, wave = 0,
                    clips = clips, volume = 0f });
                // the game maps stage POSITION id to a part column center-out
                // (pos 1 = center, 2 = left, 3 = right, 4 = lleft, ...), matching
                // the csv's lleft..rright column order. this is the authoring
                // order, independent of the runtime x layout.
                slot_positions.Add(slot.position);
            }
            stem_roots = roots_list.ToArray();
            lock_positions();
            trace_log.write($"vocals: {stems.Count} stems bound, positions " +
                string.Join(",", slot_positions) + ", waves " +
                string.Join(",", stems.Select(s => s.clips.Length)));
            bound = stems.Count > 0;
        }

        private readonly List<int> slot_positions = new();

        // stage position id -> part-table column, center-out: pos 1 = center,
        // then alternating right/left (2=left, 3=right, 4=lleft, 5=rright...).
        // the part csv columns are lleft,left,center,right,rright.
        private static int position_column(int position)
        {
            if (position <= 1) return 2;                     // center
            int dist = position / 2;
            bool left = (position & 1) == 0;
            return left ? 2 - dist : 2 + dist;               // left side / right side
        }

        private void lock_positions()
        {
            for (int i = 0; i < stems.Count && i < slot_positions.Count; i++)
                stems[i].group = position_column(slot_positions[i]);
        }

        // decodes one member's vocal bank: one wave per take (0 = main,
        // 1 = extra on songs that record both), and starts playback muted.
        private (AudioSource source, AudioClip[] clips) start_stem(Transform parent,
            int music_id, int chara_id)
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
            if (row == null) return (null, null);

            string path = System.IO.Path.Combine(config.data_root, "dat",
                row.hash.Substring(0, 2), row.hash);
            if (!System.IO.File.Exists(path)) return (null, null);
            byte[] bank = System.IO.File.ReadAllBytes(path);
            var waves = live_audio.parse_afs2(bank);
            if (waves.Count == 0) return (null, null);

            var clips = new List<AudioClip>();
            int take_count = Mathf.Min(waves.Count, 2);
            for (int i = 0; i < take_count; i++)
            {
                var clip = live_audio.decode_wave(bank, waves[i], $"voc_{chara_id}_{i}");
                if (clip != null) clips.Add(clip);
            }
            if (clips.Count == 0) return (null, null);

            var source = parent.gameObject.AddComponent<AudioSource>();
            source.clip = clips[0];
            source.loop = false;
            source.volume = 0f;
            // do NOT Play() here: the stems decode staggered across many
            // seconds while the bgm already runs; a free-running playhead
            // lands each member at a different offset. the volume ride
            // starts the stem aligned to the song clock on first unmute.
            return (source, clips.ToArray());
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

        // the per-frame ride, mirroring the game's UpdatePartParamer: gate
        // each stem on its group flag, rank the singing stems left to right,
        // then gain = part_type_gains[type] * part_volume_rates[n] and the
        // row's pan. the flag value picks the take: 1 = main wave, 2 = extra.
        private void Update()
        {
            if (!bound) return;
            float t = clock?.time ?? 0f;
            int[] active = part_flags(t);
            bool uncut = active == null;

            var singing = new List<stem>();
            foreach (var s in stems)
                if (uncut || active[s.group] > 0) singing.Add(s);
            singing.Sort((a, b) => a.group.CompareTo(b.group));

            // trace each section change so the bench can assert the ride:
            // section index, singing count, per-stem (gain, pan).
            int section = part_section(t);
            if (section != last_section)
            {
                last_section = section;
                int dn = Mathf.Clamp(singing.Count, 1, 7);
                var dbg = new List<string>();
                for (int i = 0; i < singing.Count; i++)
                {
                    var slot = placement_rows[dn - 1][Mathf.Clamp(i, 0, dn - 1)];
                    dbg.Add($"g{singing[i].group}:" +
                        $"{part_type_gains[slot.type] * part_volume_rates[dn]:0.000}@pan{slot.pan:+0.00;-0.00}");
                }
                trace_log.write($"vocals: t={t:0.0} section={section} n={singing.Count} " +
                    string.Join(" ", dbg));
            }

            // the game's tables stop at seven singers; a fuller cast reuses
            // the last row's tail placement for the ranks beyond it.
            int n = Mathf.Clamp(singing.Count, 1, placement_rows.Length);
            var row = placement_rows[n - 1];

            foreach (var s in stems)
            {
                bool on = uncut || active[s.group] > 0;
                float target = 0f;
                float pan = 0f;

                if (on)
                {
                    int rank = singing.IndexOf(s);
                    var slot = row[Mathf.Clamp(rank, 0, row.Length - 1)];
                    target = part_type_gains[slot.type] * part_volume_rates[n];
                    pan = slot.pan;

                    int wave = !uncut && active[s.group] == 2 && s.clips.Length > 1 ? 1 : 0;
                    if (!s.started && s.source != null && s.clips.Length > 0)
                    {
                        play_wave(s, wave, t);
                        s.started = true;
                    }
                    else if (s.started && wave != s.wave)
                    {
                        // main <-> extra take switch, playhead kept on the clock.
                        play_wave(s, wave, t);
                    }
                }

                s.volume = Mathf.MoveTowards(s.volume, target * last_volume, fade_rate * Time.deltaTime);
                if (s.source != null)
                {
                    s.source.volume = s.volume;
                    s.source.panStereo = pan;
                }
            }
        }

        // swaps the stem onto the given take, playhead synced to the song
        // clock (wrapping when the song outlasts the clip).
        private void play_wave(stem s, int wave, float t)
        {
            if (s.source == null || s.clips.Length == 0) return;
            var clip = s.clips[Mathf.Clamp(wave, 0, s.clips.Length - 1)];
            if (clip.length > 0f) s.source.time = t % clip.length;
            s.source.clip = clip;
            s.source.Play();
            s.wave = wave;
        }

        // binary-search the part row at time t; the row holds until the next.
        private int[] part_flags(float t)
        {
            int idx = part_section(t);
            return idx < 0 ? null : part_rows[idx];
        }

        // the section index at time t, or -1 with no part table.
        private int part_section(float t)
        {
            if (part_times == null || part_times.Length == 0) return -1;
            int lo = 0, hi = part_times.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (part_times[mid] <= t) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        // the ui volume slider.
        public void set_volume(float v)
        {
            last_volume = Mathf.Clamp01(v);
        }
    }
}
