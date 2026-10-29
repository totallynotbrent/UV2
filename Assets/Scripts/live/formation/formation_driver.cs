using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Live;

namespace UV2.Live
{
    // moves characters between formation keys: stage placement per group (center/left1/right1/.../place20).
    // the spring keys ride the same frames: warmUpCySpring schedules the
    // spring warm-up pass, CySpringRate scales the per-part spring amplitude.
    public class formation_driver : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private readonly Dictionary<int, Transform> slots = new();
        private Dictionary<int, spring_sim> spring_sims;

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> chara_roots,
            Dictionary<int, spring_sim> springs = null)
        {
            ws = worksheet;
            clock = timeline;
            spring_sims = springs;
            // slot order = the selection json's position order (1-based) onto the roots
            for (int i = 0; i < chara_roots.Count; i++) slots[i + 1] = chara_roots[i];
            // per-concert hold: every open re-arms it (a second concert in one
            // process would otherwise inherit the released state), and the
            // cut_suppressed one-shot below consumes it exactly once.
            physics_director.springs_held = spring_sims != null && spring_sims.Count > 0;
        }

        private void LateUpdate()
        {
            if (ws == null || clock == null) return;
            apply_at(clock.time);
        }

        // drives the formation for one specific song time.
        // the seek path calls this directly so a capture at a scrubbed frame
        // shows the cast where the timeline puts it, not where the last
        // LateUpdate left it.
        public void apply_at(float t)
        {
            if (ws == null) return;

            // no formation groups at all, or the driver carries no spring sims
            // (nothing can fire the start-pose cut): release the pre-cut hold
            // on the first frame — otherwise springs stay held forever.
            if (physics_director.springs_held &&
                (ws.formation == null || ws.formation.Count == 0 ||
                 spring_sims == null || spring_sims.Count == 0))
            {
                physics_director.springs_held = false;
                trace_log.write("formation: no formation keys, releasing the spring hold");
            }
            // failsafe: the audio clock synced (t is song-relative from here)
            // and the cut window passed without the cut firing (group names
            // that never map to this cast's slots) — release rather than
            // hold the springs for the whole song. gated on audio_synced,
            // not raw t or music_live: the clock free-runs real seconds
            // between concert open and music start (bench: t read 8.6-9.4
            // while the oke had not started), and music_live flips true
            // mid-frame while _time still carries the free-run value —
            // either gate alone releases one frame before the snap.
            else if (physics_director.springs_held &&
                     clock.audio_synced && t > first_key_cut_window + 1f)
            {
                physics_director.springs_held = false;
                trace_log.write($"formation: t={t:0.0} cut window passed without the cut, releasing the spring hold");
            }

            foreach (var kv in ws.formation)
            {
                string group = kv.Key;
                var keys = kv.Value;
                if (keys == null || keys.Count == 0) continue;

                int i = key_eval.bracket(keys, t);
                if (i < 0) continue;
                var cur = keys[i];
                var next = i + 1 < keys.Count ? keys[i + 1] : null;
                float k = key_eval.interp(cur, next, t);

                foreach (var skv in slots)
                {
                    int slot = group_index(group, skv.Key);
                    if (slot < 0) continue;
                    var tr = skv.Value;
                    if (tr == null) continue;

                    Vector3 pos = cur.position;
                    float rot_y = cur.rotation_y;
                    if (next != null)
                    {
                        pos = key_eval.lerp_v3(cur.position, next.position, k);
                        rot_y = key_eval.lerp_f(cur.rotation_y, next.rotation_y, k);
                    }
                    tr.localPosition = pos;
                    tr.localRotation = Quaternion.Euler(0f, rot_y, 0f);

                    // the game reads the CURRENT key's visible flag alone:
                    // SetActive(curKey.visible) with no lookahead. or-ing the
                    // next key in keeps a character on stage until the next
                    // key lands, so the cast count runs high (f5692: the game
                    // shows 7, the or-version shows all 18).
                    bool visible = cur.visible != 0;
                    tr.gameObject.SetActive(visible);
                    apply_spring_keys(skv.Key, cur, i);
                }
            }

            // charaWind: the spring wind is gated by the timeline keys; a song
            // with no enabled key runs its springs windless (1004 is one).
            foreach (var kv in ws.chara_wind)
            {
                var keys = kv.Value;
                if (keys == null || keys.Count == 0) continue;
                int i = key_eval.bracket(keys, t);
                if (i < 0) continue;
                var cur = keys[i];
                foreach (var skv in slots)
                {
                    int slot = group_index(kv.Key, skv.Key);
                    if (slot < 0) continue;
                    if (!root_chara.TryGetValue(skv.Value, out int chara_id)) continue;
                    if (!spring_sims.TryGetValue(chara_id, out var sim) || sim == null) continue;
                    sim.set_wind_enabled(cur.enable_wind != 0);
                }
            }
        }

        // fires the warm-up byte once per key and keeps the amplitude rate
        // current while the bracket holds (the game's per-key path). the
        // latch is PER CHARACTER keyed on the BRACKET INDEX: the per-slot
        // loop calls this once per slot per frame, a single shared latch
        // would schedule only the first slot's sim (17/18 never warmed),
        // and latching on clock.time refires on every frame inside one
        // bracket — the key object stays current for the whole bracket, so
        // 18 sims re-warmed per frame and the sustained 60-step bursts
        // pinned the frame at 160ms (bench41).
        readonly Dictionary<int, int> last_warm_key = new();
        private void apply_spring_keys(int slot, formation_key cur, int bracket_index)
        {
            if (spring_sims == null) return;
            var tr = slots.TryGetValue(slot, out var t) ? t : null;
            if (tr == null) return;
            if (!root_chara.TryGetValue(tr, out int chara_id)) return;
            if (!spring_sims.TryGetValue(chara_id, out var sim) || sim == null) return;
            if (physics_director.instance == null) return;

            // the song's first formation key teleports the cast to the start
            // pose; the game re-arms bone suppression on that cut (spawn and
            // cut/teleport share the SetSuppression path) so springs blend
            // into the new pose instead of whipping from the bind-time pose.
            // the t=0 bracket is the cut; later keys interpolate, not cut.
            // armed for every sim in one pass — the per-slot loop would
            // otherwise latch the flag after the first character.
            if (!cut_suppressed && clock.time <= first_key_cut_window)
            {
                cut_suppressed = true;
                int armed = 0;
                foreach (var sim_kv in spring_sims)
                {
                    if (sim_kv.Value == null) continue;
                    // the game's cut sequence: ResetNativeCloth re-seeds every
                    // record to the placed start pose (zero velocity, aim
                    // re-derived from the placed frame), then the reserved
                    // warm-up settles from there. suppression alone crawls
                    // 1cm/frame from the stale bind pose — the tail's forward
                    // phasing. bind-time warm-up settled the load pose, so
                    // the cut needs the reset + warm-up pair.
                    sim_kv.Value.reset_to_pose();
                    sim_kv.Value.arm_suppression();
                    physics_director.instance.schedule_warm_up(
                        sim_kv.Value, 1, cur.cy_spring_warm_up_time > 0f ? cur.cy_spring_warm_up_time : 1f);
                    armed++;
                }
                // release the pre-cut hold AFTER the reset is staged: the
                // warm-up runs on this frame's physics pass, from the placed
                // pose — the game's ResetNativeCloth → WarmUpCySpring order.
                if (armed > 0)
                {
                    physics_director.springs_held = false;
                    trace_log.write($"formation: t={clock.time:0.0} start-pose cut, {armed} sims reset + suppressed + warm-up");
                }
            }

            if (cur.warm_up_cy_spring != 0 &&
                (!last_warm_key.TryGetValue(chara_id, out int lk) || lk != bracket_index))
            {
                // fire once per key change (bracket index movement). mid-song
                // keys co-locate with motion-clip swaps: re-seed the verlet to
                // the post-swap pose before settling (the t=0 cut's
                // reset + suppress + warm triple, per key).
                physics_director.instance.schedule_warm_up(sim, cur.warm_up_cy_spring, cur.cy_spring_warm_up_time, reseed: true);
                last_warm_key[chara_id] = bracket_index;
            }
            // the rate track is per locator: the game applies the same key to
            // every spring part (head/body/tail, PARTS_NUM=3), so the tail and
            // body chains get the rest-line pull too. an authored 0 in the
            // worksheet schema means unset (default 1): a true off only ever
            // comes from the control window, so coerce here.
            float key_rate = cur.cy_spring_rate > 0f ? cur.cy_spring_rate : 1f;
            physics_director.instance.set_spring_rate(sim, spring_sim.part.head, key_rate);
            physics_director.instance.set_spring_rate(sim, spring_sim.part.body, key_rate);
            physics_director.instance.set_spring_rate(sim, spring_sim.part.tail, key_rate);
        }

        // chara id per root, wired by stage_loader after the cast loads.
        public readonly Dictionary<Transform, int> root_chara = new();
        // one-shot arm of the start-pose cut suppression per concert.
        private bool cut_suppressed;
        // the t=0 formation bracket is the spawn-to-start-pose cut; anything
        // inside this window after the clock starts is that cut, not an
        // interpolated move.
        private const float first_key_cut_window = 0.5f;

        // maps worksheet group names to selection slots: center=1, left1=2, right1=3, left2=4, right2=5, place06..20 -> 6..20.
        private int group_index(string group, int slot)
        {
            switch (group)
            {
                case "center": return slot == 1 ? 1 : -1;
                case "left1": return slot == 2 ? 2 : -1;
                case "right1": return slot == 3 ? 3 : -1;
                case "left2": return slot == 4 ? 4 : -1;
                case "right2": return slot == 5 ? 5 : -1;
                default:
                    if (group.StartsWith("place") && group.Length > 5)
                    {
                        if (int.TryParse(group.Substring(5), out int place) && place >= 6)
                            return slot == place ? place : -1;
                    }
                    return -1;
            }
        }
    }
}
