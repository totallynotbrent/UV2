using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // per-character vocal stems mixed by the song's part table, using the
    // game's own placement model (heap-verified tables; umadump/out/
    // uv2_vocal_cue_decode.md + uv2_vocal_lead_switch_decoded.md). each
    // frame it gates stems on their part group flag, picks the take from
    // the flag value, and applies gain/pan from the placement row and the
    // csv's override columns.
    public class vocal_mixer : MonoBehaviour
    {
        private class stem
        {
            public AudioSource[] sources;  // one per take, all rolling in sync
            public int group;
            public int wave;          // the currently-audible take
            public int wave_target;    // the flag-selected take
            public float volume;
            public bool started;
        }

        private readonly List<stem> stems = new();
        private float[] part_times;
        private int[][] part_rows;   // per row: sing flags (0 silent, 1 main, 2 extra)
        private float[][] part_vols; // per row: per-position volume overrides (>=900 unset)
        private float[][] part_pans; // per row: per-position pan overrides (>=900 unset)
        private float[] part_rates;  // per row: global volume-rate override (>=900 unset)
        private float last_volume = 1f;
        private const float fade_rate = 20f;  // ~50ms ramp between gain steps
        private const float unset = 900f;     // the game's unset-override sentinel

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
        private int last_override_sec = -2;

        // decodes each member's bank as a coroutine (18 hca decodes take
        // ~15s and open waits on this method); the part table gates stems
        // silent until they materialize.
        public void open(timeline_clock clock_ref, IReadOnlyList<Transform> chara_roots,
            List<slot_pick> slots, int music_id)
        {
            clock = clock_ref;
            var go = new GameObject("live_vocals");
            go.transform.SetParent(transform, false);

            var rows = load_part_table(music_id);
            if (rows != null && rows.Count > 0)
            {
                part_times = rows.Select(r => r.time).ToArray();
                part_rows = rows.Select(r => r.flags).ToArray();
                part_vols = rows.Select(r => r.vols).ToArray();
                part_pans = rows.Select(r => r.pans).ToArray();
                part_rates = rows.Select(r => r.rate).ToArray();
            }
            else
            {
                trace_log.write($"vocals: no part table for song {music_id}; stems run uncut");
                part_times = null;
                part_rows = null;
            }

            if (chara_roots.Count == 0) return;

            int cast_index = 0;
            var roots_list = new List<Transform>();
            var chara_ids = new List<int>();
            var positions = new List<int>();
            for (int i = 0; i < slots.Count && cast_index < chara_roots.Count; i++)
            {
                var slot = slots[i];
                if (slot.chara_id <= 0) continue;
                var root = chara_roots[cast_index++];
                if (root == null) continue;
                roots_list.Add(root);
                chara_ids.Add(slot.chara_id);
                positions.Add(slot.position);
            }
            stem_roots = roots_list.ToArray();
            trace_log.write($"vocals: {chara_ids.Count} stems pending (lazy decode)");
            StartCoroutine(decode_stems(go.transform, music_id, chara_ids, positions));
        }

        private readonly List<int> slot_positions = new();

        // stage position id -> part-table column, center-out (1 = center,
        // 2 = left, 3 = right, 4 = lleft...), matching the csv's authoring
        // order. -1 = no column (backdancer); those stems stay silent.
        private static int position_column(int position, int width)
        {
            if (position < 1 || position > width) return -1;
            if (position == 1) return width / 2;             // center
            int dist = position / 2;
            bool left = (position & 1) == 0;
            int col = left ? width / 2 - dist : width / 2 + dist;
            return col >= 0 && col < width ? col : -1;
        }

        private System.Collections.IEnumerator decode_stems(Transform parent, int music_id,
            List<int> chara_ids, List<int> positions)
        {
            int width = part_width();
            for (int i = 0; i < chara_ids.Count; i++)
            {
                // the hca decode runs on a worker (pure arrays, no unity api)
                // and the AudioClip is created on the main thread when it
                // lands: the load-phase frame hitches the main-thread decode
                // caused (1s frames across the first 15s) are gone.
                // backdancer columns are known before the bank loads: skip
                // their ~15s decodes entirely — the mix gates them silent
                // anyway (audit 4.2 cost note).
                int col = position_column(positions[i], width);
                if (col < 0) { trace_log.write($"vocals: stem {chara_ids[i]} backdancer column {col} skipped"); yield return null; continue; }
                var bank_task = load_bank(music_id, chara_ids[i]);
                if (bank_task.bank == null) { yield return null; continue; }
                // decode every take (0 = main, n-1 = the lead-switch
                // SelectorLabel_{n-1} alternates; 1059's banks carry 3 waves)
                // in the worker; AudioClip.Create stays on the main thread
                // (uv2_vocal_generality_audit.md 4.5).
                float t0 = Time.realtimeSinceStartup;
                var pcm_task = System.Threading.Tasks.Task.Run(() =>
                    bank_task.waves.Select(w => live_audio.decode_wave_pcm(bank_task.bank, w)).ToArray());
                while (!pcm_task.IsCompleted) yield return null;
                trace_log.write($"vocals: stem {chara_ids[i]} decoded on worker in {Time.realtimeSinceStartup - t0:0.00}s");
                var clips = new List<AudioClip>();
                foreach (var (pcm, channels, rate) in pcm_task.Result)
                {
                    if (pcm == null || pcm.Length == 0) continue;
                    var clip = AudioClip.Create($"voc_{chara_ids[i]}_{clips.Count}",
                        pcm.Length / channels, channels, rate, false);
                    clip.SetData(pcm, 0);
                    clips.Add(clip);
                }
                if (clips.Count == 0) { yield return null; continue; }
                // the game decodes every take into parallel wave streams and
                // the lead switch only flips which buffer is audible
                // (uv2_vocal_lead_switch_decoded.md): one source per take,
                // all rolling from the same playhead, never re-seeked.
                var sources = new AudioSource[clips.Count];
                for (int c = 0; c < clips.Count; c++)
                {
                    var source = parent.gameObject.AddComponent<AudioSource>();
                    source.clip = clips[c];
                    source.loop = false;
                    source.volume = 0f;
                    sources[c] = source;
                }
                stems.Add(new stem { sources = sources, group = position_column(positions[i], width),
                    wave = -1, wave_target = 0, volume = 0f });
                slot_positions.Add(positions[i]);
                yield return null;
            }
            lock_positions();
            trace_log.write($"vocals: {stems.Count} stems bound, positions " +
                string.Join(",", slot_positions) + ", waves " +
                string.Join(",", stems.Select(s => s.sources.Length)));
            bound = stems.Count > 0;
        }

        // reads the member's bank with its wave list (takes 0/1).
        private (byte[] bank, List<awb_wave> waves) load_bank(int music_id, int chara_id)
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
            return (bank, waves);
        }

        private void lock_positions()
        {
            int width = part_width();
            for (int i = 0; i < stems.Count && i < slot_positions.Count; i++)
                stems[i].group = position_column(slot_positions[i], width);
        }

        // the csv's own column count tells the layout (the game's CsvLabel /
        // CsvLabel7 enums, uv2_vocal_lead_switch_decoded.md §2): 5 sing
        // columns (+ optional 5 volume + 5 pan = 15 value cols) or 7 sing
        // columns (+ 7 volume + 7 pan + rate = 22 value cols). the sing
        // region size is the stage-column width; the rest are overrides.
        private static int sing_width(int csv_cols)
        {
            int values = csv_cols - 1;
            if (values <= 0) return 0;
            if (values <= 5) return values;             // bare 5-pos table
            if (values <= 15) return 5;                 // + volume/pan overrides
            return 7;                                    // 7-pos table (+rate)
        }

        // the part table's slot width, 0 with no table.
        private int part_width()
            => part_rows != null && part_rows.Length > 0 ? part_rows[0].Length : 0;

        // the part table from the meta manifest: m<song>_part, a csv text asset
        // inside a dat bundle. returns null when the song has none.
        private List<(float time, int[] flags, float[] vols, float[] pans, float rate)> load_part_table(int music_id)
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

            var out_rows = new List<(float, int[], float[], float[], float)>();
            int width = 0;
            bool header = true;
            foreach (var line in csv.Split('\n'))
            {
                var cols = line.Trim().Split(',');
                if (header)
                {
                    header = false;
                    width = sing_width(cols.Length);
                    continue;
                }
                if (cols.Length < 2) continue;
                // invariant culture: comma-decimal locales would silently
                // fail every 0.56-style value into unset (audit 4.1).
                if (!float.TryParse(cols[0], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float time)) continue;
                // only the sing region gates stems; members beyond it are
                // backdancers with no column.
                int w = width > 0 ? width : sing_width(cols.Length);
                var flags = new int[w];
                for (int g = 0; g < w; g++)
                    int.TryParse(cols[g + 1], out flags[g]);
                var vols = new float[w];
                var pans = new float[w];
                float rate = unset;
                for (int g = 0; g < w; g++)
                {
                    vols[g] = unset;
                    pans[g] = unset;
                    int rest = w + 1 + g;   // volume block follows the sing block
                    if (rest < cols.Length && float.TryParse(cols[rest], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float v))
                        vols[g] = v;
                    int prest = w + 1 + w + g; // then the pan block
                    if (prest < cols.Length && float.TryParse(cols[prest], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float p))
                        pans[g] = p;
                }
                // the 7-pos table's tail column: a global volume-rate override
                // (CsvLabel7 col 22, e.g. 1059 carries 1 on 13 rows).
                int ridx = w + 1 + 2 * w;
                if (ridx < cols.Length && float.TryParse(cols[ridx], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float rt))
                    rate = rt;
                out_rows.Add((time / 1000f, flags, vols, pans, rate));
            }
            trace_log.write($"vocals: part table {music_id}: {out_rows.Count} rows, sing width {width}");
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
                if (s.group >= 0 && (uncut || active[s.group] > 0)) singing.Add(s);
            singing.Sort((a, b) => a.group.CompareTo(b.group));

            // trace each section change so the bench can assert the ride.
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

            // the current section's override columns (>=900 = unset, the
            // game's sentinel; uv2_vocal_lead_switch_decoded.md §4): a set
            // volume replaces the placement gain, a set pan replaces the
            // placement pan, and a set global rate replaces the singer-count
            // rate term, per position.
            int sec = part_section(t);
            float[] vol_over = sec >= 0 && part_vols != null && sec < part_vols.Length ? part_vols[sec] : null;
            float[] pan_over = sec >= 0 && part_pans != null && sec < part_pans.Length ? part_pans[sec] : null;
            float rate_over = sec >= 0 && part_rates != null && sec < part_rates.Length ? part_rates[sec] : unset;
            // override columns are silent in the placement trace; log the
            // composed targets once per override section so benches can
            // assert the game's vol x rate product (audit 4.4).
            if ((vol_over != null || rate_over < unset) && sec != last_override_sec)
            {
                last_override_sec = sec;
                var od = new List<string>();
                foreach (var s in stems)
                    if (s.group >= 0 && (uncut || active[s.group] > 0))
                    {
                        string v = vol_over != null && s.group < vol_over.Length && vol_over[s.group] < unset
                            ? vol_over[s.group].ToString("0.000") : "-";
                        od.Add($"g{s.group}:{v}x{rate_over:0.00}");
                    }
                if (od.Count > 0)
                    trace_log.write($"vocals: t={t:0.0} override section={sec} rate={rate_over:0.00} " + string.Join(" ", od));
            }

            foreach (var s in stems)
            {
                bool on = s.group >= 0 && (uncut || active[s.group] > 0);
                float target = 0f;
                float pan = 0f;

                if (on)
                {
                    int rank = singing.IndexOf(s);
                    var slot = row[Mathf.Clamp(rank, 0, row.Length - 1)];
                    float rate = rate_over < unset ? rate_over : part_volume_rates[n];
                    // the game's UpdatePartParamer applies the per-slot vol
                    // override to the gain and THEN multiplies by the rate —
                    // the override never replaces the rate factor
                    // (uv2_vocal_generality_audit.md 4.4).
                    float gain = part_type_gains[slot.type];
                    if (vol_over != null && s.group < vol_over.Length && vol_over[s.group] < unset)
                        gain = vol_over[s.group];
                    target = gain * rate;
                    pan = slot.pan;
                    if (pan_over != null && s.group < pan_over.Length && pan_over[s.group] < unset)
                        pan = pan_over[s.group];

                    // flag n selects wave n-1 (SelectorLabel_{n-1}); the length
                    // guard keeps 1-wave banks on the main take, the game's
                    // no-op selector behavior (uv2_vocal_generality_audit.md
                    // 4.5, uv2_vocal_lead_switch_decoded.md 3-4).
                    int wave = !uncut ? Mathf.Clamp(active[s.group] - 1, 0, s.sources.Length - 1) : 0;
                    s.wave_target = wave;
                    if (!s.started && s.sources.Length > 0)
                    {
                        // every take starts together at the song playhead and
                        // rolls unbroken for the whole song; the switch below
                        // only moves the volume, so both takes stay sample
                        // aligned across the switch like the game's two
                        // always-running wave streams.
                        start_stem(s, t);
                        s.started = true;
                    }
                }

                s.volume = Mathf.MoveTowards(s.volume, target * last_volume, fade_rate * Time.deltaTime);
                if (s.wave_target != s.wave && s.wave >= 0)
                    trace_log.write($"vocals: take switch g{s.group} {s.wave} -> {s.wave_target} at t={t:0.0}s");
                s.wave = s.wave_target;
                for (int w = 0; w < s.sources.Length; w++)
                {
                    var src_audio = s.sources[w];
                    if (src_audio == null) continue;
                    src_audio.volume = w == s.wave ? s.volume : 0f;
                    src_audio.panStereo = pan;
                }
            }
        }

        // starts every take at the song playhead, once per song (the clips
        // are full-length takes that wrap when the song outlasts them).
        private void start_stem(stem s, float t)
        {
            for (int w = 0; w < s.sources.Length; w++)
            {
                var src_audio = s.sources[w];
                if (src_audio == null || src_audio.clip == null) continue;
                if (src_audio.clip.length > 0f) src_audio.time = t % src_audio.clip.length;
                src_audio.Play();
            }
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
