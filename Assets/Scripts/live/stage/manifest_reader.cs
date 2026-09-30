using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // resolves a song's stage + a cast member's body bundle at runtime from the
    // game's own master db and meta db: no prebuilt manifest, no sidecar files.
    public static class manifest_reader
    {
        // stage bundle names for a song's stage id (controller variants 000-009).
        public static List<string> stage_bundles(int stage_id)
        {
            var names = new List<string>();
            for (int i = 0; i < 10; i++)
                names.Add($"3d/env/live/live{stage_id}/pfb_env_live{stage_id}_controller{i:d3}");
            return names;
        }

        // the stage material bundles the controller's prereq column names.
        public static List<string> stage_materials(int stage_id, string controller_name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return new List<string>();
            var rows = meta.lookup(new HashSet<string> { controller_name });
            var row = rows.GetValueOrDefault(controller_name);
            if (row?.prereq == null) return new List<string>();
            return row.prereq.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.StartsWith("sourceresources/"))
                .ToList();
        }

        // the chibi tree: per-dress dirs use the dress chara, shared dirs use
        // the body type. mini rows are keyed by the full folder/prefab path,
        // and the dress sub can differ from the row we guessed, so probe both.
        private static (string bundle, string prefab)? mini_body(int dress_chara, int body_type, int body_sub)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return null;

            // per-dress dirs: the dress chara + sub, then the sub-1 sibling
            // (the disk carries _00 and _10-style variants per chara).
            var candidates = new List<(string full, string prefab)>();
            if (dress_chara != 0)
            {
                string p = $"pfb_mbdy{dress_chara:d4}_{body_sub:d2}";
                candidates.Add(($"3d/chara/mini/body/mbdy{dress_chara:d4}_{body_sub:d2}/{p}", p));
                string p2 = $"pfb_mbdy{dress_chara:d4}_10";
                candidates.Add(($"3d/chara/mini/body/mbdy{dress_chara:d4}_10/{p2}", p2));
            }
            else
            {
                string ps = $"pfb_mbdy{body_type:d4}_{body_sub:d2}";
                candidates.Add(($"3d/chara/mini/body/mbdy{body_type:d4}_{body_sub:d2}/{ps}", ps));
            }

            var names = new HashSet<string>(candidates.Select(c => c.full));
            var rows = meta.lookup(names);
            foreach (var (full, prefab) in candidates)
                if (rows.ContainsKey(full))
                    return (full, prefab);
            return null;
        }

        // body bundle + prefab for (chara, dress), computed from the phase-1
        // naming rules against the live master db.
        public static (string bundle, string prefab)? chara_body(int chara_id, int dress_id)
        {
            using var db = master_db.reader.open(config.master_db_path);
            if (db == null) return null;

            // the dress row: its chara and body type columns drive the naming.
            var dress_rows = db.query(
                $"SELECT chara_id, body_type, body_type_sub, costume_type FROM dress_data WHERE id={dress_id}");
            if (dress_rows.Count == 0) return null;
            var dress = dress_rows[0];
            int dress_chara = (int)dress.get_int(0);
            int body_type = (int)dress.get_int(1);
            int body_sub = (int)dress.get_int(2);
            int costume_type = (int)dress.get_int(3);

            // mini-live dresses render the chibi tree instead of the normal body.
            if (costume_type == 1 || body_type == 100)
                return mini_body(dress_chara, body_type, body_sub);

            if (dress_chara != 0)
            {
                // character-specific: one folder, one prefab, both from the ids.
                string folder = $"3d/chara/body/bdy{dress_chara}_{body_sub:d2}";
                string prefab = $"pfb_bdy{dress_chara}_{body_sub:d2}";
                return (folder, prefab);
            }

            // shared: parameterized prefab from the character's body columns.
            var chara_rows = db.query(
                $"SELECT height, shape, bust FROM chara_data WHERE id={chara_id}");
            if (chara_rows.Count == 0) return null;
            var chara = chara_rows[0];
            int height = (int)chara.get_int(0);
            int shape = (int)chara.get_int(1);
            int bust = (int)chara.get_int(2);

            // the variant slot is a per-dress disk fact, not a db column: probe
            // the meta db for the real prefab name (00 first, then 01..).
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null) return null;
            for (int variant = 0; variant < 4; variant++)
            {
                string folder = $"3d/chara/body/bdy{body_type:d4}_00";
                string prefab = $"pfb_bdy{body_type:d4}_00_{variant:d2}_{height}_{shape}_{bust}";
                string full = $"{folder}/{prefab}";
                var rows = meta.lookup(new HashSet<string> { full });
                if (rows.ContainsKey(full))
                    return (full, prefab);
            }
            return null;
        }
    }

    // loads the slot->sequence map from the game's cutt data bundle, deserialized
    // by the LiveTimelineData stub (characterSettings.motionSequenceIndices).
    public static class slot_sequences
    {
        // returns the per-song motionSequenceIndices; null when the data is absent.
        public static List<int> for_song(int music_id)
        {
            try
            {
                string bundle_name = $"cutt/cutt_son{music_id}/data";
                using var meta = meta_reader.reader.open(config.meta_db_path);
                var rows = meta?.lookup(new HashSet<string> { bundle_name });
                var row = rows?.GetValueOrDefault(bundle_name);
                if (row == null)
                {
                    Debug.LogWarning($"[slot_sequences] no cutt data bundle for {music_id}");
                    return null;
                }
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) return null;
                var data = bundle.LoadAllAssets<Gallop.Live.Cutt.LiveTimelineData>().FirstOrDefault();
                if (data?.characterSettings == null)
                {
                    Debug.LogWarning($"[slot_sequences] no LiveTimelineData bound for {music_id}");
                    return null;
                }
                var msi = data.characterSettings.motionSequenceIndices;
                Debug.Log($"[slot_sequences] {music_id}: {msi?.Count ?? 0} slots mapped");
                return msi;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[slot_sequences] {music_id}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }
}
