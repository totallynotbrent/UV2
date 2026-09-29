using System.Linq;
using System.Collections.Generic;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.App
{
    // player-side motion binding probe: instantiates the real body prefab, binds
    // the real dance clip, samples it at several times, and writes the head/hip
    // rotations plus curve-binding coverage to the trace log.
    public static class pose_probe_runner
    {
        public static void run()
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            if (meta == null)
            {
                trace_log.write("pose_probe: meta db unavailable");
                return;
            }
            var rows = meta.lookup(new HashSet<string>
            {
                "3d/chara/body/bdy0050_00/pfb_bdy0050_00_00_1_0_2",
                "3d/motion/live/body/son1004/anm_liv_son1004_1st",
            });
            meta_reader.asset_row body_row = null, clip_row = null;
            rows.TryGetValue("3d/chara/body/bdy0050_00/pfb_bdy0050_00_00_1_0_2", out body_row);
            rows.TryGetValue("3d/motion/live/body/son1004/anm_liv_son1004_1st", out clip_row);
            if (body_row == null || clip_row == null)
            {
                trace_log.write($"pose_probe: rows body={body_row != null} clip={clip_row != null}");
                return;
            }

            var body_bundle = game_assets.open(body_row, config.data_root);
            var clip_bundle = game_assets.open(clip_row, config.data_root);
            trace_log.write($"pose_probe: bundles body={body_bundle != null} clip={clip_bundle != null}");
            if (body_bundle == null || clip_bundle == null) return;

            string prefab_name = body_bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith(".prefab"));
            string clip_name = clip_bundle.GetAllAssetNames().FirstOrDefault(n => n.EndsWith(".anim"));
            trace_log.write($"pose_probe: names prefab='{prefab_name}' clip='{clip_name}'");
            var prefab = prefab_name == null ? null : body_bundle.LoadAsset<GameObject>(prefab_name);
            var clip = clip_name == null ? null : clip_bundle.LoadAsset<AnimationClip>(clip_name);
            trace_log.write($"pose_probe: prefab={prefab != null} clip={clip != null} legacy={clip != null && clip.legacy}");
            if (prefab == null || clip == null) return;

            var instance = Object.Instantiate(prefab);
            var anim = instance.GetComponent<Animation>();
            if (anim == null) anim = instance.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.AddClip(clip, clip.name);

            Transform head = null, hip = null;
            foreach (var t in instance.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Head" && head == null) head = t;
                if (t.name == "Hip" && hip == null) hip = t;
            }
            trace_log.write($"pose_probe: head={head != null} hip={hip != null}");

            var state = anim[clip.name];
            foreach (float t in new[] { 0f, 5f, 10f, 60f, 100f })
            {
                state.enabled = true;
                state.weight = 1f;
                state.time = t;
                anim.Sample();
                trace_log.write($"pose_probe t={t}: head local {head?.localEulerAngles} hip local {hip?.localEulerAngles} state_norm {state.normalizedTime:0.00} len {state.length:0.0}");
            }

        }
    }
}
