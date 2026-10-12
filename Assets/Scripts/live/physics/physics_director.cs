using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // scene driver for the spring solver: one component per live stage,
    // registered by stage_loader. runs in LateUpdate AFTER the pose (motion
    // sampled in Update) — the decoded order the game uses. one solver step
    // per rendered frame; the song clock gates dt (paused = no phase advance).
    public class physics_director : MonoBehaviour
    {
        public static physics_director instance { get; private set; }

        // kill switch: UV2_PHYSICS=0 disables the whole pass (benching).
        public static bool enabled_by_env =>
            System.Environment.GetEnvironmentVariable("UV2_PHYSICS") != "0";

        class chara_sim
        {
            public Transform root;
            public spring_sim sim;
            public spring_loader.container_ref[] refs;
            public float warm_time;         // 0 = default 1.0s
            public bool warm_scheduled;
            public skirt_controller skirt;   // knee-correction pass (may be null)
        }

        readonly List<chara_sim> sims = new();
        timeline_clock clock;

        // fixed 60hz physics stepping: the solver's constants are authored
        // for exactly 60 steps per second (the game's own scheduling at
        // fps mode 1). rendering may run faster or stall; the accumulator
        // steps the sim on a 1/60s grid, capped at 2 catch-up steps per
        // frame so a stall never produces giant solve steps.
        const float PHYSICS_STEP = 1f / 60f;
        const int MAX_STEPS_PER_FRAME = 2;
        float step_acc;

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

        // binds one character: loads its containers, merges, binds. returns
        // the sim (null when nothing bound — the character has no spring
        // data or its bundles are missing).
        public spring_sim attach_character(int chara_id, string body_folder,
            int cloth_index, int bust_index, bool chara_specific_body,
            int tail_model_id, Transform character_root, Func<string, GameObject> load_prefab)
        {
            var refs = spring_loader.load_containers(chara_id, body_folder,
                cloth_index, bust_index, chara_specific_body, tail_model_id,
                load_prefab);
            if (refs.Count == 0) return null;
            var sim = new spring_sim();
            var log = new Action<string>(m => trace_log.write(m));
            if (character_root == null || !spring_loader.attach(character_root, refs, sim, log))
            {
                foreach (var rf in refs)
                    if (rf.instance != null) Destroy(rf.instance);
                return null;
            }

            // container instances ride the director (not the character) so a
            // character despawn cannot destroy the shared data; the collision
            // riders live under the character's bones.
            foreach (var rf in refs)
                if (rf.instance != null)
                    rf.instance.transform.SetParent(transform, false);

            sims.Add(new chara_sim { root = character_root, sim = sim, refs = refs.ToArray() });
            // a formation key may have set the amplitude rate before the part
            // bound; every part starts at the current rate.
            if (pending_rate != 1f)
                foreach (var p in sim.parts)
                    if (p != null) p.spring_rate = pending_rate;
            trace_log.write($"physics: chara {chara_id} bound ({refs.Count} containers)");
            return sim;
        }

        // binds the skirt knee-correction pass: loads the body folder's skirt
        // parameter asset (ast_bdyNNNNNN_skirtNN pairs with the cloth slot)
        // and seeds the per-column bind pose. no asset = no skirt pass.
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
            var cs = sims.Find(s => s.sim != null && s.root == character_root);
            var skirt = new skirt_controller();
            if (skirt.create(character_root, asset))
            {
                if (cs != null) cs.skirt = skirt;
                trace_log.write($"skirt: chara {chara_id} bound ({asset._skirtBone.Count} columns, knee {asset._kneeColliderRadius}, influence {asset._influenceAngle}/{asset._influenceMaxAngle})");
            }
            else trace_log.write($"skirt: chara {chara_id} asset loaded but no columns bound");
        }

        // schedules the warm-up pass from the formation keys (the game's
        // warmUpCySpring byte: 0 = skip, else run; warm time 0 = 1.0s).
        public void schedule_warm_up(spring_sim sim, byte warm_up_flag, float warm_time)
        {
            if (sim == null || warm_up_flag == 0) return;
            foreach (var cs in sims)
                if (cs.sim == sim) { cs.warm_scheduled = true; cs.warm_time = warm_time; }
        }

        // per-part amplitude rate from the formation keys (CySpringRate).
        public void set_spring_rate(spring_sim sim, spring_sim.part part, float rate)
        {
            // the game's rate table is per root group (hair/tail/body) but the
            // formation key carries one scalar: every group's forces scale by
            // it. rate 0 = unset = keep 1.
            if (sim == null) return;
            float r = rate <= 0f ? 1f : rate;
            foreach (var p in sim.parts)
                if (p != null) p.spring_rate = r;
            // also pre-seed parts that bind later.
            pending_rate = r;
        }

        // last formation-key rate; parts created after the key still need it.
        float pending_rate = 1f;

        public void clear()
        {
            foreach (var cs in sims)
                foreach (var rf in cs.refs)
                    if (rf?.instance != null) Destroy(rf.instance);
            sims.Clear();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            // paused (clock held) = IsDeltaTimeZero: skip the solve; the game
            // still runs the write path so the pose stays consistent.
            bool paused = clock != null && clock.paused;

            // gate the whole pass on the accumulator: at 120fps the sim
            // steps every other frame; below 60 it catches up at most 2
            // steps per frame. warm-up runs outside the gate (once, on
            // scheduling). every stepping frame steps ALL characters so
            // their phases stay in lockstep.
            bool step_frame = !paused;
            int steps = 0;
            if (step_frame)
            {
                step_acc += dt;
                steps = 0;
                while (step_acc >= PHYSICS_STEP && steps < MAX_STEPS_PER_FRAME)
                {
                    step_acc -= PHYSICS_STEP;
                    steps++;
                }
                // a stall longer than the cap discards the backlog instead
                // of bursting giant solves; the next frame resumes in phase.
                if (step_acc > MAX_STEPS_PER_FRAME * PHYSICS_STEP)
                    step_acc = 0f;
                step_frame = steps > 0;
            }

            foreach (var cs in sims)
            {
                if (cs?.sim == null) continue;
                if (cs.warm_scheduled)
                {
                    // warm-up: fixed 60fps steps, history re-zeroed each step,
                    // no collisions. runs once, on the frame it is scheduled.
                    cs.sim.warm_up(cs.warm_time);
                    cs.warm_scheduled = false;
                    continue;
                }
                if (!step_frame)
                {
                    // non-step frame (120fps cadence or catch-up exhausted):
                    // hold the last solved pose — the write path runs every
                    // frame so the rig never flashes the raw animation pose.
                    cs.sim.hold();
                    if (cs.skirt != null) cs.skirt.update();
                    continue;
                }
                cs.sim.begin(PHYSICS_STEP * steps);
                cs.sim.update(collision_switch: true);
                cs.sim.end();
                // the skirt knee-correction runs after the spring solve (the
                // game's SkirtController pass rides LateUpdate too).
                if (cs.skirt != null) cs.skirt.update();
            }
        }
    }
}
