using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        }

        private void LateUpdate()
        {
            if (ws == null || clock == null) return;
            float t = clock.time;

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

                    bool visible = cur.visible != 0;
                    if (next != null) visible = next.visible != 0 || visible;
                    tr.gameObject.SetActive(visible);
                    apply_spring_keys(skv.Key, cur);
                }
            }
        }

        // fires the warm-up byte once per key and keeps the amplitude rate
        // current while the bracket holds (the game's per-key path).
        float last_warm_time = -1f;
        private void apply_spring_keys(int slot, formation_key cur)
        {
            if (spring_sims == null) return;
            var tr = slots.TryGetValue(slot, out var t) ? t : null;
            if (tr == null) return;
            if (!root_chara.TryGetValue(tr, out int chara_id)) return;
            if (!spring_sims.TryGetValue(chara_id, out var sim) || sim == null) return;
            if (physics_director.instance == null) return;

            if (cur.warm_up_cy_spring != 0 && last_warm_time != clock.time)
            {
                // fire once per key change (bracket index movement).
                physics_director.instance.schedule_warm_up(sim, cur.warm_up_cy_spring, cur.cy_spring_warm_up_time);
                last_warm_time = clock.time;
            }
            physics_director.instance.set_spring_rate(sim, spring_sim.part.head, cur.cy_spring_rate);
        }

        // chara id per root, wired by stage_loader after the cast loads.
        public readonly Dictionary<Transform, int> root_chara = new();

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
