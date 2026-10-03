using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;
using Cutt = Gallop.Live.Cutt;

namespace UV2.Live
{
    // the props system: the cutt data asset's propsSettings.propsDataGroup names
    // every prop instance the song can activate (chara props by major/minor id,
    // stage dressing by the common bundle code), the formation track's
    // ik_system=4 keys mark the stand-mic characters, and the motion clips
    // animate the mic rig nodes (Mic_Attach_00, Mic_Attach_00_loc/Mic_Node_L/R)
    // the live system spawns under the character's Position root.
    public static class props_system
    {
        // one resolved chara prop instance: the prefab copy + its attach bone.
        private class chara_prop
        {
            public GameObject instance;
            public Transform joint;
            public string joint_name;
        }

        // one planted stage-dressing instance (stand mic, taiko, light rig).
        private class stage_prop
        {
            public GameObject instance;
            public int slot;
        }

        // per-character mic rig state: the spawned nodes + the last ik targets.
        private class mic_rig
        {
            public Transform attach_00;        // Mic_Attach_00 under Position
            public Transform attach_loc;       // Position/Mic_Attach_00_loc
            public Transform node_l;           // Mic_Node_L
            public Transform node_r;           // Mic_Node_R
            public readonly Dictionary<string, Vector3> last_target = new();
            public readonly Dictionary<string, float> last_weight = new();
            public readonly HashSet<string> engaged = new();
        }

        private static readonly List<chara_prop> chara_props = new();
        private static readonly List<stage_prop> stage_props = new();
        private static readonly Dictionary<int, mic_rig> rigs = new();
        private static readonly Dictionary<int, Dictionary<string, Transform>> bone_cache = new();
        private static bool bound;

        // clears every spawned instance + registry; call between concerts.
        public static void reset()
        {
            foreach (var p in chara_props) if (p.instance != null) UnityEngine.Object.Destroy(p.instance);
            foreach (var p in stage_props) if (p.instance != null) UnityEngine.Object.Destroy(p.instance);
            chara_props.Clear();
            stage_props.Clear();
            rigs.Clear();
            bone_cache.Clear();
            bound = false;
        }

        // resolves the song's propsDataGroup against the loaded cast, loads the
        // prefabs, attaches chara props to the named joints, plants stage
        // dressing at the targeted slots, and spawns the mic rig nodes for the
        // characters the formation track marks with ik_system=4. call once after
        // the cast phase; never throws into the caller.
        public static void bind(List<Cutt.PropsDataGroup> groups, selection_state sel,
            List<Transform> chara_roots, Dictionary<string, List<formation_key>> formation)
        {
            reset();
            bound = true;
            if (groups == null || groups.Count == 0)
            {
                trace_log.write("props: no propsDataGroup authored for this song");
                return;
            }

            int resolved = 0, unresolved = 0, attached = 0, planted = 0;
            var unresolved_names = new List<string>();

            // the characters the formation track runs mic-stand IK on: their rig
            // nodes must exist before the clip's mic curves can bind.
            var mic_slots = mic_stand_slots(formation, sel.slots.Count);
            foreach (var kv in mic_slots)
            {
                if (kv.Key - 1 < chara_roots.Count)
                    spawn_mic_rig(kv.Key, chara_roots[kv.Key - 1]);
            }

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                if (g == null) { unresolved++; unresolved_names.Add($"group {gi}: null"); continue; }

                // gender-diff groups swap the major/minor by the target's sex row.
                int major = g.charaPropsMajorId;
                int minor = g.charaPropsMinorId;

                var slots = target_slots(g, sel, major);
                if (slots.Count == 0)
                {
                    unresolved++;
                    unresolved_names.Add($"group {gi} '{g.propsName}': no member matched the conditions");
                    continue;
                }

                foreach (int slot in slots)
                {
                    Transform root = slot - 1 >= 0 && slot - 1 < chara_roots.Count ? chara_roots[slot - 1] : null;
                    if (root == null) { unresolved++; continue; }

                    int group_major = major, group_minor = minor;
                    if (g.isUseGenderDiffPropsId != 0)
                    {
                        var ids = gender_ids(g, sel, slot);
                        group_major = ids.major; group_minor = ids.minor;
                    }

                    GameObject prefab = null;
                    if (g.isCharaProps != 0 && group_major > 0)
                        prefab = load_chara_prop(g.IsToonProp != 0, g.IsRichProp != 0, group_major, group_minor);
                    else if (!string.IsNullOrEmpty(g.propsName))
                        prefab = load_stage_prop(g.propsName);

                    if (prefab == null)
                    {
                        unresolved++;
                        unresolved_names.Add($"group {gi} '{g.propsName}' slot {slot}: prefab unresolved");
                        continue;
                    }

                    if (g.isCharaProps != 0 && group_major > 0)
                    {
                        if (attach_chara_prop(prefab, g, root, slot))
                        { attached++; resolved++; }
                        else
                        { unresolved++; unresolved_names.Add($"group {gi} '{g.propsName}' slot {slot}: no attach joint"); }
                    }
                    else
                    {
                        if (plant_stage_prop(prefab, g, root, slot))
                        { planted++; resolved++; }
                        else
                        { unresolved++; unresolved_names.Add($"group {gi} '{g.propsName}' slot {slot}: plant failed"); }
                    }
                }
            }

            trace_log.write($"props: {groups.Count} groups -> {resolved} resolved, {unresolved} unresolved, {attached} chara props attached, {planted} stage props planted, {rigs.Count} mic rigs");
            foreach (var n in unresolved_names) trace_log.write($"props unresolved: {n}");
        }

        // the slots the formation track runs ik_system=4 (mic stand) on, with
        // the count of mic keys each carries; the worksheet's group name maps to
        // the selection slot the same way the formation driver does.
        private static Dictionary<int, int> mic_stand_slots(Dictionary<string, List<formation_key>> formation, int slot_total)
        {
            var found = new Dictionary<int, int>();
            if (formation == null) return found;
            foreach (var kv in formation)
            {
                int slot = formation_slot(kv.Key);
                if (slot < 0) continue;
                int mic_keys = 0;
                foreach (var k in kv.Value)
                    if (k.ik_system == 4) mic_keys++;
                if (mic_keys > 0) found[slot] = mic_keys;
            }
            return found;
        }

        // worksheet group name -> 1-based selection slot (center=1, left1=2,
        // right1=3, left2=4, right2=5, place06..20 -> 6..20).
        private static int formation_slot(string group)
        {
            switch (group)
            {
                case "center": return 1;
                case "left1": return 2;
                case "right1": return 3;
                case "left2": return 4;
                case "right2": return 5;
                default:
                    if (group.StartsWith("place") && group.Length > 5 && int.TryParse(group.Substring(5), out int place))
                        return place >= 6 ? place : -1;
                    return -1;
            }
        }

        // the slots a group targets: the condition list names positions (the
        // any-of semantics the census shows), chara/dress ids pin a member, no
        // conditions means slot 1 (the game's default-attach path).
        private static List<int> target_slots(Cutt.PropsDataGroup g, selection_state sel, int major)
        {
            var slots = new List<int>();
            if (g.propsConditionGroup == null || g.propsConditionGroup.Count == 0)
            {
                if (g.isCharaProps != 0 && major > 0 && sel.slots.Count > 0) slots.Add(sel.slots[0].position);
                return slots;
            }
            foreach (var cg in g.propsConditionGroup)
            {
                if (cg == null) continue;
                bool all = cg.satisfiesAllConditions != 0;
                // satisfiesAll rows AND their conditions; any-of rows OR them.
                // position/chara/dress each match a slot; a row with no match
                // under AND semantics disqualifies the whole group row.
                var row_slots = new List<int>();
                foreach (var c in cg.propsConditionData)
                {
                    if (c == null) continue;
                    int hit = condition_slot(c.Type, c.Value, sel);
                    if (hit > 0 && !row_slots.Contains(hit)) row_slots.Add(hit);
                }
                if (all)
                {
                    // every condition must name the same member: rows that pin
                    // several different members match none.
                    if (row_slots.Count == cg.propsConditionData.Count)
                        foreach (var s in row_slots) if (!slots.Contains(s)) slots.Add(s);
                }
                else
                {
                    foreach (var s in row_slots) if (!slots.Contains(s)) slots.Add(s);
                }
            }
            return slots;
        }

        // one condition row -> the 1-based selection slot it matches, 0 = none.
        private static int condition_slot(int type, int value, selection_state sel)
        {
            switch (type)
            {
                case 1: // chara position: the 0-based member index (0 = center)
                    return value >= 0 && value < sel.slots.Count ? sel.slots[value].position : 0;
                case 2: // chara id: the member wearing this prop
                    foreach (var s in sel.slots) if (s.chara_id == value) return s.position;
                    return 0;
                case 3: // dress id: the member in this dress
                    foreach (var s in sel.slots) if (s.dress_id == value) return s.position;
                    return 0;
                default:
                    return 0;
            }
        }

        // the gender-diff swap: the chara_data row's sex picks the id pair the
        // game stores (sex 1 = female, 2 = male in the db's coding).
        private static (int major, int minor) gender_ids(Cutt.PropsDataGroup g, selection_state sel, int slot)
        {
            int sex = chara_sex(sel, slot);
            if (sex == 2) return (g.MaleCharaPropsMajorId, g.MaleCharaPropsMinorId);
            return (g.FemaleCharaPropsMajorId, g.FemaleCharaPropsMinorId);
        }

        // the slot's chara_data.sex row; 0 when the lookup fails.
        private static int chara_sex(selection_state sel, int slot)
        {
            var pick = sel.slots.FirstOrDefault(s => s.position == slot);
            if (pick == null) return 0;
            try
            {
                using var db = master_db.reader.open(config.master_db_path);
                var rows = db?.query($"SELECT sex FROM chara_data WHERE id={pick.chara_id}");
                if (rows == null || rows.Count == 0) return 0;
                return (int)rows[0].get_int(0);
            }
            catch { return 0; }
        }

        // loads a chara prop prefab by kind + major/minor id from the game's
        // 3d/chara/<kind> tree; falls back down the minor series (the game's
        // own fallback shape) when the exact minor is absent.
        private static GameObject load_chara_prop(bool toon, bool rich, int major, int minor)
        {
            string kind = rich ? "richprop" : toon ? "toonprop" : "prop";
            string folder = rich ? "rich_prop" : toon ? "toon_prop" : "prop";
            string prefix = rich ? "pfb_rich_prop" : toon ? "pfb_toon_prop" : "pfb_chr_prop";
            for (int v = minor; v >= 0; v--)
            {
                string prefab_name = $"{prefix}{major}_{v:00}";
                string bundle_name = $"3d/chara/{kind}/{folder}{major}_{v:00}/{prefab_name}";
                var row = meta_row(bundle_name);
                if (row == null) continue;
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) continue;
                if (!string.IsNullOrEmpty(row.prereq))
                    foreach (var pre in row.prereq.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var pre_row = meta_row(pre.Trim());
                        if (pre_row != null) game_assets.open(pre_row, config.data_root);
                    }
                string[] all = bundle.GetAllAssetNames();
                string asset = all.FirstOrDefault(n => n.EndsWith(prefab_name + ".prefab"))
                               ?? all.FirstOrDefault(n => n.EndsWith(".prefab"));
                if (asset == null) continue;
                var prefab = bundle.LoadAsset<GameObject>(asset);
                if (prefab != null) return prefab;
            }
            return null;
        }

        // loads a stage-dressing prefab from the common prop bundle tree by
        // the propsName code the cutt data carries (001 -> prop001).
        private static GameObject load_stage_prop(string props_name)
        {
            if (string.IsNullOrEmpty(props_name)) return null;
            string code = props_name.PadLeft(3, '0');
            string bundle_name = $"3d/env/live/common/prop/pfb_env_live_cmn_prop{code}";
            var row = meta_row(bundle_name);
            if (row == null) return null;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            if (!string.IsNullOrEmpty(row.prereq))
                foreach (var pre in row.prereq.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pre_row = meta_row(pre.Trim());
                    if (pre_row != null) game_assets.open(pre_row, config.data_root);
                }
            string[] all = bundle.GetAllAssetNames();
            string asset = all.FirstOrDefault(n => n.EndsWith($".prefab"));
            if (asset == null) return null;
            return bundle.LoadAsset<GameObject>(asset);
        }

        // the attach order the game's authored joint lists show: the mic anchor
        // first (a handheld mic rides Mic_Attach_00, never the _loc locator),
        // then the locators, then the hands.
        private static readonly string[] joint_priority =
        {
            "Mic_Attach_00", "Hand_Attach_R", "Hand_Attach_L", "Elbow_R", "Elbow_L",
            "Waist", "Position", "Head",
        };

        // attaches one chara prop copy to the best joint the group names on
        // this character; the mic anchor wins when the group lists it, with a
        // hand fallback (the game's handheld mic rides the hand when the rig
        // carries no mic anchor: the attach node list is advisory candidate
        // anchors, v1's resolution order).
        private static bool attach_chara_prop(GameObject prefab, Cutt.PropsDataGroup g, Transform root, int slot)
        {
            var names = g.attachJointNames;
            if (names == null || names.Count == 0) return false;
            Transform best = null;
            string best_name = null;
            int best_rank = int.MaxValue;
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                int rank = Array.IndexOf(joint_priority, n);
                if (rank < 0) rank = joint_priority.Length;
                if (rank >= best_rank) continue;
                var joint = find_bone(root, n);
                if (joint == null) continue;
                best = joint;
                best_name = n;
                best_rank = rank;
            }
            if (best == null)
            {
                // the rig carries no named anchor: fall back to the hands the
                // joints list implies (Hand_Attach_R then L), never drop.
                foreach (var n in joint_priority)
                {
                    if (n.StartsWith("Mic_")) continue;
                    var joint = find_bone(root, n);
                    if (joint == null) continue;
                    best = joint;
                    best_name = n;
                    break;
                }
            }
            if (best == null) return false;

            var instance = UnityEngine.Object.Instantiate(prefab, best);
            instance.name = $"prop_{g.charaPropsMajorId}_{g.charaPropsMinorId}_{best_name}";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            shader_manager.fix_game_shaders(instance.transform, "props");
            chara_props.Add(new chara_prop { instance = instance, joint = best, joint_name = best_name });
            trace_log.write($"props: chara prop {g.charaPropsMajorId}_{g.charaPropsMinorId:00} -> slot {slot} joint '{best_name}' ({instance.GetComponentsInChildren<Renderer>(true).Length} renderers)");
            return true;
        }

        // plants one stage-dressing copy at the performer's feet: the stand
        // mic prefab roots at its base so position zero lands base-down, and
        // parenting under the character node inherits the formation glide.
        private static bool plant_stage_prop(GameObject prefab, Cutt.PropsDataGroup g, Transform root, int slot)
        {
            var instance = UnityEngine.Object.Instantiate(prefab, root);
            instance.name = $"prop_stage_{g.propsName}_slot{slot}";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            shader_manager.fix_game_shaders(instance.transform, "props");
            stage_props.Add(new stage_prop { instance = instance, slot = slot });
            // the character node origin is the performer's feet; the stand
            // prefab's own origin sits mid-pole (the mesh bounds span both
            // sides of zero), so lift the root by its bounds bottom so the
            // base lands on the floor instead of sinking under it.
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float lift = root.position.y - b.min.y;
                var lp = instance.transform.localPosition;
                instance.transform.localPosition = new Vector3(lp.x, lp.y + lift, lp.z);
                var after = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) after.Encapsulate(renderers[i].bounds);
                trace_log.write($"props: stage prop '{g.propsName}' bounds y [{b.min.y:0.000}..{b.max.y:0.000}] lifted {lift:0.000} -> [{after.min.y:0.000}..{after.max.y:0.000}] vs chara root y {root.position.y:0.000}");
            }
            trace_log.write($"props: stage prop '{g.propsName}' planted at slot {slot} ({renderers.Length} renderers)");
            return true;
        }

        // spawns the runtime mic rig nodes under the character's Position root:
        // the motion clip animates Position/Mic_Attach_00_loc/Mic_Node_L/R, so
        // the nodes must exist with exactly those names under that parent or
        // the clip's mic curves silently no-op.
        private static void spawn_mic_rig(int slot, Transform chara_root)
        {
            var position = find_bone(chara_root, "Position");
            if (position == null) { trace_log.write($"props: mic rig slot {slot}: no Position root on the character"); return; }

            var attach_loc = find_bone(position, "Mic_Attach_00_loc");
            if (attach_loc == null)
            {
                var go = new GameObject("Mic_Attach_00_loc");
                attach_loc = go.transform;
                attach_loc.SetParent(position, false);
            }
            Transform attach_00 = find_bone(position, "Mic_Attach_00");
            if (attach_00 == null)
            {
                var go = new GameObject("Mic_Attach_00");
                attach_00 = go.transform;
                attach_00.SetParent(position, false);
            }
            var node_l = find_bone(attach_loc, "Mic_Node_L");
            if (node_l == null)
            {
                var go = new GameObject("Mic_Node_L");
                node_l = go.transform;
                node_l.SetParent(attach_loc, false);
            }
            var node_r = find_bone(attach_loc, "Mic_Node_R");
            if (node_r == null)
            {
                var go = new GameObject("Mic_Node_R");
                node_r = go.transform;
                node_r.SetParent(attach_loc, false);
            }
            rigs[slot] = new mic_rig
            {
                attach_00 = attach_00,
                attach_loc = attach_loc,
                node_l = node_l,
                node_r = node_r,
            };
            trace_log.write($"props: mic rig spawned for slot {slot} (Mic_Attach_00 + Mic_Attach_00_loc/Mic_Node_L/R under Position)");
        }

        // depth-first transform search with a per-character cache.
        private static Transform find_bone(Transform root, string name)
        {
            int id = root.GetInstanceID();
            if (!bone_cache.TryGetValue(id, out var by_name))
            {
                by_name = new Dictionary<string, Transform>();
                bone_cache[id] = by_name;
            }
            if (by_name.TryGetValue(name, out var hit)) return hit;
            Transform found = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) { found = t; break; }
                if (!by_name.ContainsKey(t.name)) by_name[t.name] = t;
            }
            by_name[name] = found;
            return found;
        }

        // per-frame: drives the stand-mic hand IK for every rigged slot. the
        // formation track's current key selects the mic system and carries the
        // L/R enable gates + High/Low offset pairs (lerped between keys the way
        // the game's formation consumer does); the stand-node update's decoded
        // height band picks the target + weight: at/above high -> full pull,
        // between high and low -> the per-character height rate, below -> the
        // last target keeps tracking (state 4).
        public static void update(float time_sec, Dictionary<string, List<formation_key>> formation, List<Transform> chara_roots)
        {
            if (!bound || rigs.Count == 0 || formation == null) return;

            foreach (var kv in formation)
            {
                int slot = formation_slot(kv.Key);
                if (slot < 0 || !rigs.TryGetValue(slot, out var rig)) continue;
                var keys = kv.Value;
                if (keys == null || keys.Count == 0) continue;

                int i = key_eval.bracket(keys, time_sec);
                if (i < 0) continue;
                var cur = keys[i];
                var next = i + 1 < keys.Count ? keys[i + 1] : null;

                if (cur.ik_system != 4)
                {
                    // off the mic section: release both hands so a finished
                    // stand pass never leaves a hand pinned.
                    clear_hand(rig, "L");
                    clear_hand(rig, "R");
                    continue;
                }
                if (slot - 1 >= chara_roots.Count) continue;
                var chara = chara_roots[slot - 1];
                if (chara == null) continue;

                float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, time_sec));

                // the offsets interpolate between keys like the game's
                // CalculateInterpolationValue formation consumer.
                Vector3 l_high = cur.ik_l_high, l_low = cur.ik_l_low;
                Vector3 r_high = cur.ik_r_high, r_low = cur.ik_r_low;
                if (next != null && next.ik_system == 4)
                {
                    l_high = key_eval.lerp_v3(cur.ik_l_high, next.ik_l_high, k);
                    l_low = key_eval.lerp_v3(cur.ik_l_low, next.ik_l_low, k);
                    r_high = key_eval.lerp_v3(cur.ik_r_high, next.ik_r_high, k);
                    r_low = key_eval.lerp_v3(cur.ik_r_low, next.ik_r_low, k);
                }

                if (cur.ik_enabled_l != 0) aim_hand(chara, rig.node_l, l_low, l_high, slot, "L");
                else clear_hand(rig, "L");

                if (cur.ik_enabled_r != 0) aim_hand(chara, rig.node_r, r_low, r_high, slot, "R");
                else clear_hand(rig, "R");
            }
        }

        // clears one side's tracking state when its gate is off.
        private static void clear_hand(mic_rig rig, string side)
        {
            rig.last_target.Remove(side);
            rig.last_weight.Remove(side);
            rig.engaged.Remove(side);
        }

        // the character's arm bone names per side (the body rig's chain).
        private static string arm_bone(string side, int index) => index switch
        {
            0 => side == "L" ? "Arm_L" : "Arm_R",
            1 => side == "L" ? "Elbow_L" : "Elbow_R",
            _ => side == "L" ? "Wrist_L" : "Wrist_R",
        };

        // one hand's pull toward its mic node: the decoded stand-node band
        // picks the target + ik strength (height >= high -> full weight on the
        // high target, >= low -> the per-character height rate on the low
        // target, below -> keep tracking the last target), then an analytic
        // two-bone solve (the game's IKSolverLimb path) bends the shoulder ->
        // elbow -> wrist chain toward it. the offset applies in the
        // character's local space (TransformVector) like the component write.
        private static void aim_hand(Transform chara, Transform node, Vector3 low_off, Vector3 high_off, int slot, string side)
        {
            if (node == null) return;
            if (!rigs.TryGetValue(slot, out var rig)) return;
            var shoulder = find_bone(chara, arm_bone(side, 0));
            var elbow = find_bone(chara, arm_bone(side, 1));
            var wrist = find_bone(chara, arm_bone(side, 2));
            if (shoulder == null || elbow == null || wrist == null) return;

            Vector3 low_target = node.position + chara.TransformVector(low_off);
            Vector3 high_target = node.position + chara.TransformVector(high_off);
            float hand_y = wrist.position.y;

            float weight;
            Vector3 target;
            if (hand_y >= high_target.y)
            {
                weight = 1f;
                target = high_target;
            }
            else if (hand_y >= low_target.y)
            {
                // between the thresholds the game applies the character's
                // height rate (ModelController::GetHeightRate) as ik strength.
                weight = height_rate(chara);
                target = rig.last_target.TryGetValue(side, out var last) ? last : low_target;
            }
            else
            {
                // below the low threshold the stand-node keeps tracking (state 4):
                // persist the previous target + strength rather than forcing low.
                weight = rig.last_weight.TryGetValue(side, out var w) ? w : 0.6f;
                target = rig.last_target.TryGetValue(side, out var t) ? t : low_target;
            }
            rig.last_target[side] = target;
            rig.last_weight[side] = weight;

            if (!rig.engaged.Contains(side))
            {
                rig.engaged.Add(side);
                trace_log.write($"props: mic ik engaged slot {slot} {side} hand (target {target}, weight {weight:0.00})");
            }

            // the band weight scales the pull: the solve runs toward the
            // current-pose-to-target lerp point, not the raw target.
            Vector3 pull = Vector3.Lerp(wrist.position, target, Mathf.Clamp01(weight));
            solve_two_bone(shoulder, elbow, wrist, pull);
        }

        // analytic two-bone ik in world space: keeps the elbow's bend plane,
        // clamps the target to the chain's reach, and writes the shoulder then
        // elbow rotations with world FromToRotation (each write updates the
        // child chain's world pose before the next read, like the game's
        // limb solver passes).
        private static void solve_two_bone(Transform shoulder, Transform elbow, Transform wrist, Vector3 target)
        {
            Vector3 root = shoulder.position;
            Vector3 mid = elbow.position;
            Vector3 end = wrist.position;

            float a = Vector3.Distance(root, mid);
            float b = Vector3.Distance(mid, end);
            if (a <= 0f || b <= 0f) return;

            // clamp into the reachable annulus so degenerate poses never fold.
            float d = Vector3.Distance(root, target);
            d = Mathf.Clamp(d, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);

            // the bend plane: the elbow's current offset from the root-target
            // line keeps the arm's authored bend direction.
            Vector3 root_to_target = target - root;
            float line_len = root_to_target.magnitude;
            Vector3 line_dir = line_len > 0.0001f ? root_to_target / line_len : Vector3.up;
            Vector3 elbow_offset = mid - root;
            Vector3 perp = elbow_offset - line_dir * Vector3.Dot(elbow_offset, line_dir);
            if (perp.sqrMagnitude < 0.000001f)
                perp = Vector3.Cross(line_dir, Vector3.up).sqrMagnitude > 0.000001f
                    ? Vector3.Cross(line_dir, Vector3.up).normalized
                    : Vector3.Cross(line_dir, Vector3.right).normalized;

            // the law of cosines places the elbow on the bend plane.
            float cos_root = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float along = a * cos_root;
            float out_len = a * Mathf.Sqrt(Mathf.Max(0f, 1f - cos_root * cos_root));
            Vector3 new_mid = root + line_dir * along + perp.normalized * out_len;

            // rotate the shoulder to the new elbow, then the elbow to the target.
            apply_world_rotation(shoulder, mid - root, new_mid - root);
            apply_world_rotation(elbow, target - new_mid, target - elbow.position);
        }

        // rotates one bone so its world-space direction vector turns from the
        // current heading to the desired heading (post-animation ik pass).
        private static void apply_world_rotation(Transform bone, Vector3 from, Vector3 to)
        {
            float from_mag = from.magnitude;
            float to_mag = to.magnitude;
            if (from_mag < 0.00001f || to_mag < 0.00001f) return;
            Vector3 from_dir = from / from_mag;
            Vector3 to_dir = to / to_mag;
            float dot = Vector3.Dot(from_dir, to_dir);
            if (dot > 0.99999f) return;
            var turn = Quaternion.FromToRotation(from_dir, to_dir);
            bone.rotation = turn * bone.rotation;
        }

        // the character's height rate from the local scale product (the
        // decoded GetHeightRate transform; the viewer applies no parent scale
        // so lossyScale carries the product).
        private static float height_rate(Transform chara)
        {
            float total_scale = Mathf.Max(0.01f, chara.lossyScale.y);
            return Mathf.Clamp((total_scale - 0.827f) / 0.338f, 0f, 1.6665f);
        }

        private static meta_reader.asset_row meta_row(string name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { name });
            return rows?.GetValueOrDefault(name);
        }
    }
}
