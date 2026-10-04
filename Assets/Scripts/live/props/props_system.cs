using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;
using Cutt = Gallop.Live.Cutt;

namespace UV2.Live
{
    // spawns and drives the song's chara props, stage dressing, and stand-mic hand ik.
    public static class props_system
    {
        // one chara prop attached to a joint (handheld mic etc).
        private class chara_prop
        {
            public GameObject instance;
            public Transform joint;
            public string joint_name;
            public Gallop.Live.Props data;
        }

        // one stage-dressing instance (the stand) riding an attach track.
        private class stage_prop
        {
            public GameObject instance;
            public int slot;
            public int flag_bit;
            public Gallop.Live.Props data;
        }


        // per-character mic rig nodes and ik hysteresis state.
        private class mic_rig
        {
            public Transform attach_00;        // root-level, sibling of Position
            public Transform attach_loc;        // Position/Mic_Attach_00_loc
            public Transform node_l;           // _loc/Mic_Node_L
            public Transform node_r;           // _loc/Mic_Node_R
            public Transform stand_mic;        // the planted stand's mic_node when the slot has one
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

        // binds the song's props: loads prefabs, spawns mic rigs, attaches chara and stage props.
        public static void bind(List<Cutt.PropsDataGroup> groups, selection_state sel,
            List<Transform> chara_roots, Dictionary<string, List<formation_key>> formation,
            List<props_attach_track> attach_tracks, List<props_render_track> render_tracks)
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
            // settingFlags bits 1/2/4/8... address instances 0/1/2/3 in creation order.
            int instance_index = 0;

            // spawn mic rigs first so stage props can attach to their nodes.
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
                        if (attach_stage_prop(prefab, g, slot, instance_index, root))
                        { planted++; resolved++; instance_index++; }
                        else
                        { unresolved++; unresolved_names.Add($"group {gi} '{g.propsName}' slot {slot}: attach failed"); }
                    }
                }
            }

            trace_log.write($"props: {groups.Count} groups -> {resolved} resolved, {unresolved} unresolved, {attached} chara props attached, {planted} stage props attached, {rigs.Count} mic rigs");
            foreach (var n in unresolved_names) trace_log.write($"props unresolved: {n}");
        }

        // slots running ik_system=4 (mic stand) with their mic key counts.
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

        // worksheet group name -> 1-based slot (center=1, left1=2, right1=3, left2=4, right2=5, place06..20 -> 6..20).
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

        // the slots a group targets; empty conditions default to the first member.
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
                var row_slots = new List<int>();
                foreach (var c in cg.propsConditionData)
                {
                    if (c == null) continue;
                    int hit = condition_slot(c.Type, c.Value, sel);
                    if (hit > 0 && !row_slots.Contains(hit)) row_slots.Add(hit);
                }
                if (all)
                {
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

        // picks the male/female prop id pair from the character's sex (1 = female, 2 = male).
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

        // loads a chara prop prefab from the 3d/chara tree, falling back down the minor series.
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

        // loads a stage prop prefab by propsName code (001 -> prop001) from the common bundle tree.
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

        // joint attach priority: mic anchor first, then hands.
        private static readonly string[] joint_priority =
        {
            "Mic_Attach_00", "Hand_Attach_R", "Hand_Attach_L", "Elbow_R", "Elbow_L",
            "Waist", "Position", "Head",
        };

        // the stand-mic pole telescope: the game's Props::SetScale, decoded from the dump
        // (uv2_props_adjustment_decoded.md). runs once at prop spawn, never per-frame.
        private static void apply_telescope(Gallop.Live.Props props, Transform chara_root, string prop_name, int slot)
        {
            if (props == null || props._adjustmentDataArray == null || props._adjustmentDataArray.Length == 0) return;

            Transform head = find_bone(chara_root, "Head");
            Transform position_node = find_bone(chara_root, "Position");
            if (head == null || position_node == null)
            {
                trace_log.write($"props: telescope '{prop_name}' slot {slot}: no Head/Position on the character, skipping");
                return;
            }

            // D = 2*(hipOffsetY*bodyScale) + headY - positionNodeY; the live path has no hip
            // offset source (master.mdb carries only story/homestory tables), so hip term = 0.
            float head_y = head.position.y;
            float position_y = position_node.position.y;
            float d = head_y - position_y;
            if (props._isInfluenceOfCharaHeight != 0)
            {
                float body_scale = chara_root.localScale.y;
                if (body_scale > 0f) d /= body_scale;
            }

            foreach (var adj in props._adjustmentDataArray)
            {
                if (adj == null || adj.Transform == null) continue;
                Vector3 rate = adj.TransformRate;
                if (rate.x == 0f) rate.x = 1f;
                if (rate.y == 0f) rate.y = 1f;
                if (rate.z == 0f) rate.z = 1f;
                Vector3 target = adj.TargetOffset;
                Vector3 range = adj.OffsetRange;
                // only Y telescopes; X and Z add zero, matching the game's VECTOR3_ZERO lanes.
                adj.Transform.localPosition = new Vector3(
                    (target.x - range.x) * rate.x,
                    (target.y + d - range.y) * rate.y,
                    (target.z - range.z) * rate.z);
            }
            trace_log.write($"props: telescope '{prop_name}' slot {slot}: headY={head_y:0.000} posNodeY={position_y:0.000} D={d:0.000} ({props._adjustmentDataArray.Length} element(s) adjusted)");
        }

        // attaches a chara prop to the best available joint on the character.
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
            var props_data = instance.GetComponentInChildren<Gallop.Live.Props>(true);
            apply_telescope(props_data, root, instance.name, slot);
            chara_props.Add(new chara_prop { instance = instance, joint = best, joint_name = best_name, data = props_data });

            trace_log.write($"props: chara prop {g.charaPropsMajorId}_{g.charaPropsMinorId:00} -> slot {slot} joint '{best_name}' ({instance.GetComponentsInChildren<Renderer>(true).Length} renderers)");
            return true;
        }

        // attaches a stage prop to the slot's mic rig _loc node so it rides the rig.
        private static bool attach_stage_prop(GameObject prefab, Cutt.PropsDataGroup g, int slot, int instance_index, Transform chara_root)
        {
            var rig = rigs.TryGetValue(slot, out var r) ? r : null;
            if (rig == null || rig.attach_loc == null)
            {
                trace_log.write($"props: stage prop '{g.propsName}' slot {slot}: no mic rig on this slot, skipping");
                return false;
            }

            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = $"prop_stage_{g.propsName}_slot{slot}";
            shader_manager.fix_game_shaders(instance.transform, "props");
            var props_data = instance.GetComponentInChildren<Gallop.Live.Props>(true);
            apply_telescope(props_data, chara_root, g.propsName, slot);
            instance.transform.SetParent(rig.attach_loc, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var sp = new stage_prop
            {
                instance = instance,
                slot = slot,
                flag_bit = 1 << instance_index, // 1/2/4: the instance the tracks address
                data = props_data,
            };

            stage_props.Add(sp);

            // the stand's mic_node is the hand-ik lock target for this slot.
            if (rigs.TryGetValue(slot, out var slot_rig))
            {
                var mic_node = instance.transform.Find("standmic/adjust/mic_node");
                if (mic_node == null)
                    foreach (var t in instance.transform.GetComponentsInChildren<Transform>(true))
                        if (t.name == "mic_node") { mic_node = t; break; }
                if (mic_node != null) slot_rig.stand_mic = mic_node;
            }

            trace_log.write($"props: stage prop '{g.propsName}' attached to slot {slot}'s Mic_Attach_00_loc (flag bit {sp.flag_bit}, {instance.GetComponentsInChildren<Renderer>(true).Length} renderers)");
            return true;
        }

        // spawns the mic rig nodes at the paths the motion clips animate.
        private static void spawn_mic_rig(int slot, Transform chara_root)
        {
            Transform position = find_bone(chara_root, "Position");
            if (position == null) { trace_log.write($"props: mic rig slot {slot}: no Position root on the character"); return; }

            // _loc under Position: the clip animates Position/Mic_Attach_00_loc.
            var attach_loc = find_bone(position, "Mic_Attach_00_loc");
            if (attach_loc == null)
            {
                var go = new GameObject("Mic_Attach_00_loc");
                attach_loc = go.transform;
                attach_loc.SetParent(position, false);
            }
            Transform attach_00 = null;
            for (int i = 0; i < chara_root.childCount; i++)
            {
                var ch = chara_root.GetChild(i);
                if (ch.name == "Mic_Attach_00") { attach_00 = ch; break; }
            }
            if (attach_00 == null)
            {
                var go = new GameObject("Mic_Attach_00");
                attach_00 = go.transform;
                attach_00.SetParent(chara_root, false);
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
            trace_log.write($"props: mic rig spawned for slot {slot} (Mic_Attach_00 at chara root + Position/Mic_Attach_00_loc/Mic_Node_L/R)");
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

        // drives the attach and render tracks and the stand-mic hand ik each frame.
        public static void update(float time_sec, Dictionary<string, List<formation_key>> formation,
            List<Transform> chara_roots, List<props_attach_track> attach_tracks,
            List<props_render_track> render_tracks)
        {
            if (!bound) return;

            float frame = time_sec * 60f;

            // each stage prop pairs with track entries matching its settingFlags bit.
            if (stage_props.Count > 0)
            {
                foreach (var sp in stage_props)
                {
                    bool visible = true;
                    foreach (var t in render_tracks)
                    {
                        if (t.keys.Count == 0) continue;
                        if (key_frame_bracket(t.keys, frame, out var rcur, out var rnext) < 0) continue;
                        if ((rcur.setting_flags & sp.flag_bit) == 0) continue;
                        // rendererEnable is a step value per key.
                        visible = rcur.renderer_enable != 0;
                        break;
                    }
                    if (sp.instance != null && sp.instance.activeSelf != visible)
                        sp.instance.SetActive(visible);

                    if (!rigs.TryGetValue(sp.slot, out var rig) || sp.instance == null) continue;
                    foreach (var t in attach_tracks)
                    {
                        if (t.keys.Count == 0) continue;
                        if (key_frame_bracket(t.keys, frame, out var cur, out var next) < 0) continue;
                        if ((cur.setting_flags & sp.flag_bit) == 0) continue;

                        // the joint switch: stand _loc vs the picked-up Mic_Attach_00.
                        Transform parent = cur.attach_joint_name == "Mic_Attach_00" && rig.attach_00 != null
                            ? rig.attach_00
                            : rig.attach_loc;
                        if (sp.instance.transform.parent != parent)
                        {
                            sp.instance.transform.SetParent(parent, false);
                            trace_log.write($"props: slot {sp.slot} stand transferred to {cur.attach_joint_name} at f{cur.frame} (t={time_sec:0.0}s)");
                        }
                        Vector3 off = cur.offset_position;
                        if (next != null && (next.setting_flags & sp.flag_bit) != 0)
                        {
                            float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, time_sec));
                            off = key_eval.lerp_v3(cur.offset_position, next.offset_position, k);
                        }
                        sp.instance.transform.localPosition = off;
                        sp.instance.transform.localRotation = Quaternion.Euler(cur.offset_rotate);
                        Vector3 sc = cur.offset_scale;
                        if (sc.x > 0f || sc.y > 0f || sc.z > 0f) sp.instance.transform.localScale = sc;
                        break;
                    }
                }
            }

            if (rigs.Count == 0 || formation == null) return;

            // stand-mic hand ik for the mic-keyed slots.
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
                    clear_hand(rig, "L");
                    clear_hand(rig, "R");
                    continue;
                }
                if (slot - 1 >= chara_roots.Count) continue;
                var chara = chara_roots[slot - 1];
                if (chara == null) continue;

                float k = key_eval.interp(cur, next, key_eval.span_t(cur, next, time_sec));

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

        // brackets a props key list by frame; returns the index, current + next key.
        private static int key_frame_bracket<T>(List<T> keys, float frame, out T cur, out T next) where T : live_key
        {
            cur = null; next = null;
            if (keys == null || keys.Count == 0) return -1;
            if (frame <= keys[0].frame) { cur = keys[0]; next = keys.Count > 1 ? keys[1] : null; return 0; }
            int last = keys.Count - 1;
            if (frame >= keys[last].frame) { cur = keys[last]; next = null; return last; }
            for (int i = 0; i < last; i++)
            {
                if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                {
                    cur = keys[i];
                    next = keys[i + 1];
                    return i;
                }
            }
            return -1;
        }

        private static void clear_hand(mic_rig rig, string side)
        {
            rig.last_target.Remove(side);
            rig.last_weight.Remove(side);
            rig.engaged.Remove(side);
        }

        private static string arm_bone(string side, int index) => index switch
        {
            0 => side == "L" ? "Arm_L" : "Arm_R",
            1 => side == "L" ? "Elbow_L" : "Elbow_R",
            _ => side == "L" ? "Wrist_L" : "Wrist_R",
        };

        // aims one hand at the slot's lock target with the game's band semantics:
        // full snap when the authored pose brings the wrist near the mic, release beyond.
        private static void aim_hand(Transform chara, Transform node, Vector3 low_off, Vector3 high_off, int slot, string side)
        {
            if (node == null) return;
            if (!rigs.TryGetValue(slot, out var rig)) return;
            var shoulder = find_bone(chara, arm_bone(side, 0));
            var elbow = find_bone(chara, arm_bone(side, 1));
            var wrist = find_bone(chara, arm_bone(side, 2));
            if (shoulder == null || elbow == null || wrist == null) return;

            // the lock target: the planted stand's real mic_node when the slot has one,
            // else the body-side node (handheld-mic songs).
            Transform lock_node = rig.stand_mic != null ? rig.stand_mic : node;
            Vector3 low_target = lock_node.position + chara.TransformVector(low_off);
            Vector3 high_target = lock_node.position + chara.TransformVector(high_off);
            Vector3 target = high_target;
            float weight;

            // binary lock with hysteresis: the game snaps at full weight in the high
            // band and never pulls in the low band; a half-weight lerp floats hands.
            float reach = Vector3.Distance(wrist.position, target);
            bool was_locked = rig.last_weight.TryGetValue(side, out var prev) && prev >= 0.999f;
            if (reach <= lock_snap_dist || (was_locked && reach <= lock_release_dist))
                weight = 1f;
            else
            {
                weight = 0f;
                rig.last_target[side] = target;
                rig.last_weight[side] = weight;
                if (rig.engaged.Contains(side))
                {
                    rig.engaged.Remove(side);
                    trace_log.write($"props: mic ik released slot {slot} {side} hand (reach {reach:0.00}m)");
                }
                return;
            }

            rig.last_target[side] = target;
            rig.last_weight[side] = weight;

            if (!rig.engaged.Contains(side))
            {
                rig.engaged.Add(side);
                trace_log.write($"props: mic ik locked slot {slot} {side} hand (reach {reach:0.00}m, target {target})");
            }

            solve_two_bone(shoulder, elbow, wrist, target);
        }

        // wrist-to-mic distances where the hand locks and releases (hysteresis band).
        private const float lock_snap_dist = 0.50f;
        private const float lock_release_dist = 0.65f;

        // analytic two-bone ik in world space, keeping the elbow's bend plane.
        private static void solve_two_bone(Transform shoulder, Transform elbow, Transform wrist, Vector3 target)
        {
            Vector3 root = shoulder.position;
            Vector3 mid = elbow.position;
            Vector3 end = wrist.position;

            float a = Vector3.Distance(root, mid);
            float b = Vector3.Distance(mid, end);
            if (a <= 0f || b <= 0f) return;

            float d = Vector3.Distance(root, target);
            d = Mathf.Clamp(d, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);

            Vector3 root_to_target = target - root;
            float line_len = root_to_target.magnitude;
            Vector3 line_dir = line_len > 0.0001f ? root_to_target / line_len : Vector3.up;
            Vector3 elbow_offset = mid - root;
            Vector3 perp = elbow_offset - line_dir * Vector3.Dot(elbow_offset, line_dir);
            if (perp.sqrMagnitude < 0.000001f)
                perp = Vector3.Cross(line_dir, Vector3.up).sqrMagnitude > 0.000001f
                    ? Vector3.Cross(line_dir, Vector3.up).normalized
                    : Vector3.Cross(line_dir, Vector3.right).normalized;

            float cos_root = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float along = a * cos_root;
            float out_len = a * Mathf.Sqrt(Mathf.Max(0f, 1f - cos_root * cos_root));
            Vector3 new_mid = root + line_dir * along + perp.normalized * out_len;

            apply_world_rotation(shoulder, mid - root, new_mid - root);
            apply_world_rotation(elbow, target - new_mid, target - elbow.position);
        }

        // rotates a bone from one world-space heading to another (post-animation pass).
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

        private static meta_reader.asset_row meta_row(string name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { name });
            return rows?.GetValueOrDefault(name);
        }
    }
}
