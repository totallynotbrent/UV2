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
    // execution order 10: after formation_driver (default 0) so a warm-up key
    // that crosses this frame schedules + reseeds BEFORE the step consumes the
    // pose the motion pass just wrote — the mid-song keys co-locate with clip
    // swaps and a late reseed lets the first post-swap step ingest the pose
    // jump as velocity (the 150+deg tail spike at the f836 key).
    [DefaultExecutionOrder(10)]
    public class physics_director : MonoBehaviour
    {
        // springs hold from concert open until the formation driver applies
        // the start-pose cut: stepping between the animation snap and the cut
        // whips the chains off their anchors, and the cut's reset then
        // captures that mid-whip frame (compressed segment lengths, up-point
        // axes) — the "hair points up after the cut" defect. the game runs
        // ResetNativeCloth synchronously with the pose change, before the
        // first live solve; the hold reproduces that ordering without
        // depending on script execution order.
        public static bool springs_held = true;

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
            public bool reseed_on_warm;     // re-seed verlet before a mid-song warm-up
            public skirt_controller skirt;   // knee-correction pass (may be null)
            public int warm_steps_pending;  // async burst: steps left to run
        }

        readonly List<chara_sim> sims = new();
        timeline_clock clock;

        // amortized warm-up: burst steps run per frame while a warm is in
        // flight. 4 steps/frame = a 60-step burst over ~15 frames, each
        // paying ~4 solves instead of one frame paying 60 (~180ms).
        const int WARM_STEPS_PER_FRAME = 4;
        int render_count;
        int real_solve_count;
        float probe_t;

        // 60hz step gate: the game's solver cadence is one verlet step per
        // 60th of a second (its fps-mode field + every serialized rate
        // assume it), so per-second spring displacement scales with solve
        // count. at uncapped render rates that means N/60 times the
        // game's motion and integrator-edge oscillation on stiff roots —
        // the jitter seen on a 116fps machine. the gate lets at most one
        // step per 1/60s of accumulated frame time: 120fps renders solve
        // on alternate frames (60 solves/s), 45fps solves every frame
        // (45 solves/s, same as a 45fps game). surplus time is discarded —
        // no catch-up bursts; a real stall still reseeds via the 0.5s gap.
        const float STEP_INTERVAL = 1f / 60f;
        float step_bank;

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
            var sim = new spring_sim(character_root);
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

            var cs_entry = new chara_sim { root = character_root, sim = sim, refs = refs.ToArray() };
            // the game reserves the 1s warm-up pre-roll at every character
            // setup (ReserveWarmingUpCySpring): the first LateUpdate after the
            // animation is live settles the chains onto the real start pose.
            cs_entry.warm_scheduled = true;
            cs_entry.warm_time = 1f;
            sims.Add(cs_entry);
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
        // reseed: also re-seed the verlet (ResetNativeCloth) before the warm-up
        // burst — the t=0 cut path reseeds itself and passes false; mid-song keys
        // pass true (their verlet state points at the pre-swap clip pose).
        public void schedule_warm_up(spring_sim sim, byte warm_up_flag, float warm_time, bool reseed = false)
        {
            if (sim == null || warm_up_flag == 0) return;
            foreach (var cs in sims)
                if (cs.sim == sim) { cs.warm_scheduled = true; cs.warm_time = warm_time; cs.reseed_on_warm = reseed; }
        }

        // the in-concert control panel iterates the bound sims (scrub and
        // settings apply to every character on stage).
        public System.Collections.Generic.IEnumerable<spring_sim> all_sims()
        {
            foreach (var cs in sims)
                if (cs?.sim != null) yield return cs.sim;
        }

        // a seek is a teleport: the game re-runs ResetNativeCloth on every
        // cut/teleport, and without it the verlet targets keep pointing at
        // the old pose's world positions — the chains whip toward a frame
        // that no longer exists. replays the full reset + suppression +
        // warm-up so the springs re-capture the post-seek pose.
        public void reset_all_to_pose()
        {
            int n = 0;
            foreach (var cs in sims)
            {
                if (cs?.sim == null) continue;
                cs.sim.reset_to_pose();
                cs.sim.arm_suppression();
                cs.warm_scheduled = true;
                if (cs.warm_time <= 0f) cs.warm_time = 1f;
                n++;
            }
            if (n > 0)
                trace_log.write($"seek reset: {n} sims reset + suppressed + warm-up");
        }

        // a frame gap beyond the catch-up drain window (alt-tab, load hitch,
        // debugger pause) is a pose discontinuity just like a teleport: the
        // animation advanced whole seconds while no steps ran. re-seed every
        // live sim to the current pose so the springs re-capture instead of
        // whipping from the stale verlet — the game's ResetNativeCloth answer,
        // reused. the amortized warm staggers the cost across frames.
        void schedule_stall_reseed()
        {
            int n = 0;
            foreach (var cs in sims)
            {
                if (cs?.sim == null) continue;
                cs.sim.reset_to_pose();
                cs.sim.arm_suppression();
                n++;
            }
            if (n > 0)
                trace_log.write($"stall reseed: {n} sims reset + suppressed");
        }

        // runtime collision kill switch (the control panel's toggle).
        public static bool collisions_enabled = true;

        // per-part amplitude rate from the formation keys (CySpringRate).
        public void set_spring_rate(spring_sim sim, spring_sim.part part, float rate)
        {
            // the game's rate table is per root group (hair/tail/body) but the
            // formation key carries one scalar: every group's forces scale by
            // it. rate 0 = springs off (the control window's slider at its
            // minimum): the scaled force terms (stiffness/drag/gravity, plus
            // the hip blend pinned to the rest line) all go quiet, so the
            // chains ride the animation — wind-only songs still flap in the
            // wind, matching the shipped solver (which scales the force then
            // adds wind unscaled). negative = unset = keep the current rate.
            if (sim == null || rate < 0f) return;
            foreach (var p in sim.parts)
                if (p != null) p.spring_rate = rate;
            // also pre-seed parts that bind later.
            pending_rate = rate;
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

            // cadence probe: renders + solves per second, logged once a
            // second. bench evidence of the accumulator's actual rate.
            render_count++;

            // CONTRACT SCHEDULING (doc lines 488-496, Q7): the live path is
            // a per-render-frame discrete step — ONE verlet step per solve
            // call, every rate divided by the caller's step scale. no
            // accumulator, no catch-up, no substepping. the previous
            // wall-clock accumulator burst up to 120 steps after a stall and
            // ran the sim at render-rate-scaled time whenever fps != 60 —
            // diverging from the game everywhere fps wasn't exactly 60.
            // the step scale normalizes the force response for render rate:
            // at 120fps each step carries half a frame's forces.
            // a real stall (dt over half a second) is a pose discontinuity
            // like a teleport: re-seed every live sim so the verlet never
            // steps from a pose that no longer exists.
            if (!paused && dt > 0.5f && !springs_held)
                schedule_stall_reseed();

            // step gate: bank this frame's time, spend one interval at a
            // time, and drop the rest. at 116fps this steps on ~every other
            // frame; below 60fps every frame steps. paused frames bank
            // nothing (the game holds the pose while its clock is zero).
            if (!paused)
            {
                step_bank += dt;
                if (step_bank > 0.25f) step_bank = STEP_INTERVAL;
            }
            bool step_frame = !paused && step_bank >= STEP_INTERVAL;
            if (step_frame)
            {
                step_bank -= STEP_INTERVAL;
                real_solve_count++;
            }
            probe_t += dt;
            if (probe_t >= 1f)
            {
                UV2.App.trace_log.write($"phys cadence: {render_count} renders/s, {real_solve_count} solves/s, avg dt {probe_t / render_count * 1000f:0.0}ms");
                render_count = 0;
                real_solve_count = 0;
                probe_t = 0f;
            }

            // one sim's warm burst per frame, not all at once: a seek or cut
            // schedules 18 characters and running 18 x 60-step bursts in a
            // single frame was the 123ms lag spikes (the game staggers the
            // same work across its own frames). the first scheduled sim
            // (list order = bind order) wins each frame; the rest keep their
            // chains held for the frames they wait, which is also the
            // pre-cut posture so nothing regresses while queued.
            chara_sim warm_pick = null;
            foreach (var cs in sims)
                if (cs != null && cs.sim != null && cs.warm_scheduled)
                {
                    warm_pick = cs;
                    break;
                }

            foreach (var cs in sims)
            {
                if (cs?.sim == null) continue;
                if (cs.warm_scheduled)
                {
                    if (!ReferenceEquals(cs, warm_pick)) continue;
                    // warm-up: the game's 60-step settle burst, spread
                    // across real frames instead of one synchronous chunk.
                    // a synchronous burst cost 60 full solves in a single
                    // frame (~180ms in C#) at every warm key — the
                    // user-visible lag spike. async: begin the burst (park,
                    // colliders, suppression) on this frame, then amortize
                    // the steps at WARM_STEPS_PER_FRAME until done, history
                    // re-zeroed each step, collisions off throughout.
                    // a mid-song warm-up key co-locates with a motion-clip
                    // swap: the verlet still points at the pre-swap pose
                    // while the bones sit in the new one, and a warm-up from
                    // that folded state leaves the chain stuck 100+deg off
                    // (the aim pull cannot unfold a target that passed
                    // through its base). the game re-seeds (ResetNativeCloth)
                    // before every warm-up, not just the t=0 cut.
                    if (cs.reseed_on_warm)
                    {
                        cs.sim.reset_to_pose();
                        cs.sim.arm_suppression();
                        cs.reseed_on_warm = false;
                    }
                    cs.sim.warm_begin(cs.warm_time);
                    cs.warm_steps_pending = cs.sim.warm_steps_total();
                    cs.warm_scheduled = false;
                    continue;
                }
                if (cs.warm_steps_pending > 0)
                {
                    // amortized burst steps: run a few per frame. the sim
                    // stays parked across the whole burst; live stepping for
                    // this character resumes once the burst completes (the
                    // one-frame synchronous burst behaved identically: the
                    // character is settled and invisible-twitch-free either
                    // way, just without stalling the frame).
                    int ran = cs.sim.warm_step(Mathf.Min(WARM_STEPS_PER_FRAME, cs.warm_steps_pending));
                    cs.warm_steps_pending -= ran;
                    if (cs.warm_steps_pending == 0) cs.sim.warm_end();
                    continue;
                }
                if (springs_held)
                {
                    // pre-cut hold: take no physics steps and write nothing —
                    // the animator's start pose must reach the cut untouched
                    // (write-back here would paint the stale bind-pose rotations
                    // over it). the cut's reset is the first thing allowed to
                    // touch the verlet state after the pose change.
                    if (cs.skirt != null) cs.skirt.update();
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
                // one discrete step per rendered frame (Q7); the step
                // scale rides the frame's dt so force terms normalize
                // against the render rate (the stepScale division).
                cs.sim.begin(dt);
                cs.sim.update(physics_director.collisions_enabled, dt);
                cs.sim.end();
                // the skirt knee-correction runs after the spring solve (the
                // game's SkirtController pass rides LateUpdate too).
                if (cs.skirt != null) cs.skirt.update();
            }
        }
    }
}
