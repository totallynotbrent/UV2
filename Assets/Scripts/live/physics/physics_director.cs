using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // physics entry point: binds characters and drives the concert-facing
    // control surface. the solve itself lives in the v1 stack
    // (v1_physics_bridge): hair and tail ride katboi's dynamicbone
    // conversion from umaviewer v1, body and bust ride the game's own
    // native solver through CySpringController. execution order 10 keeps
    // the director after the formation driver so the hold flag it writes
    // in its keys lands before the bridge's LateUpdate solve.
    [DefaultExecutionOrder(10)]
    public class physics_director : MonoBehaviour
    {
        // springs hold from concert open until the formation driver applies
        // the start-pose cut: the first solves happen against the real
        // placed pose, never the raw bind pose.
        public static bool springs_held = true;

        public static physics_director instance { get; private set; }

        // kill switch: UV2_PHYSICS=0 disables the whole pass (benching).
        public static bool enabled_by_env =>
            System.Environment.GetEnvironmentVariable("UV2_PHYSICS") != "0";

        // runtime collision kill switch (the control panel's toggle). the
        // v1 stack owns its collider geometry; the toggle informs the
        // bridge only.
        public static bool collisions_enabled = true;

        v1_physics_bridge v1_bridge;
        int bridge_chars;
        timeline_clock clock;
        int render_count;
        int real_solve_count;
        float probe_t;

        void Awake()
        {
            instance = this;
            enabled = enabled_by_env;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            clear();
        }

        public void bind_clock(timeline_clock c) => clock = c;

        // binds one character onto the v1 stack. returns null always: the
        // bridge owns the physics, there is no spring_sim to hand back, and
        // callers treat null as "no per-sim keys" (the formation driver's
        // dictionary stays empty and its warm/rate branches no-op).
        public spring_sim attach_character(int chara_id, string body_folder,
            int cloth_index, int bust_index, bool chara_specific_body,
            int tail_model_id, Transform character_root, Func<string, GameObject> load_prefab)
        {
            if (v1_bridge == null)
            {
                var bridge_go = new GameObject("v1_physics_bridge");
                v1_bridge = bridge_go.AddComponent<v1_physics_bridge>();
            }
            if (v1_bridge.attach_character(chara_id, body_folder, cloth_index,
                    bust_index, chara_specific_body, tail_model_id,
                    character_root, load_prefab))
                bridge_chars++;
            return null;
        }

        // binds the skirt knee-correction pass through the v1 stack: the
        // game's SkirtParamDataAsset feeds v1's SkirtController, which the
        // cyspring controller links natively (CreateLinkSkirtController).
        public void attach_skirt(int chara_id, string body_folder, int cloth_index,
            Transform character_root, Func<string, UnityEngine.Object> load_asset)
        {
            if (character_root == null || load_asset == null) return;
            string stem = body_folder.Split('/')[3];
            string bundle = $"{body_folder}/clothes/ast_{stem}_skirt{cloth_index:00}";
            var asset = load_asset(bundle) as Gallop.SkirtParamDataAsset;
            if (asset == null)
            {
                trace_log.write($"skirt: chara {chara_id} no param asset ({bundle})");
                return;
            }
            if (v1_bridge != null)
                v1_bridge.attach_skirt(chara_id, character_root, asset);
        }

        // formation warm-up keys: the v1 stack's dynamicbone path settles
        // from the current pose on its own; accepted and ignored.
        public void schedule_warm_up(spring_sim sim, byte warm_up_flag, float warm_time, bool reseed = false)
        {
        }

        // the in-concert control panel iterates bound sims; the bridge path
        // has none.
        public System.Collections.Generic.IEnumerable<spring_sim> all_sims()
        {
            yield break;
        }

        // a seek is a teleport: the bridge's controllers re-capture from
        // the current pose on the next begin (the native path re-derives
        // targets from the rig, so the stale-pose whip has no analogue).
        public void reset_all_to_pose()
        {
            if (v1_bridge != null) v1_bridge.reset();
            trace_log.write("seek reset: v1 physics re-captured");
        }

        // formation spring-rate keys: v1's dynamicbone conversion bakes its
        // tuning constants at init; no runtime rate path. accepted and
        // ignored (same as v1, which never wired the rate track).
        public void set_spring_rate(spring_sim sim, spring_sim.part part, float rate)
        {
        }

        public void clear()
        {
            if (v1_bridge != null) v1_bridge.clear();
            bridge_chars = 0;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            // the hold flag drives the bridge: pre-cut frames solve nothing
            // so the start-pose cut lands on settled chains. a hold release
            // (the cut) re-captures the placed pose before the first solve.
            if (v1_bridge != null)
                v1_bridge.set_held(springs_held);

            // cadence probe: rendered frames per second, logged once a
            // second. the v1 stack solves in its own LateUpdate; the solves
            // count stays zero here since there is no gated step anymore.
            render_count++;
            probe_t += dt;
            if (probe_t >= 1f)
            {
                trace_log.write($"phys cadence: {render_count} renders/s, {real_solve_count} solves/s, avg dt {probe_t / render_count * 1000f:0.0}ms");
                render_count = 0;
                real_solve_count = 0;
                probe_t = 0f;
            }
        }
    }
}
