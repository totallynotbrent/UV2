using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // loads a character's spring containers, binds chains to the live
    // skeleton, and returns a ready spring_sim.
    //
    // container load order head, body, bust, tail (the game's order); the
    // first container naming a bone claims it. part mapping (PARTS_NUM = 3):
    //   head part = head container (+ bust container as ADD chains),
    //   body part = body container (wind + tail collider rows),
    //   tail part = tail container.
    //
    // collision runtime rules (decoded, verified against the fork):
    //   each collision row materializes a child GameObject under its target
    //   bone; sphere: world position += offset; capsule: localPosition =
    //   offset (the axis rides offset2 - offset, scaled at eval time).
    //   body rows drop the names the tail spring data references; the tail
    //   part's table keeps only those names.
    public static class spring_loader
    {
        // one loaded container bundle: the instance stays alive (it owns the
        // container component the collision transforms were read from).
        public class container_ref
        {
            public spring_source source;
            public Gallop.CySpringDataContainer container;
            public GameObject instance;
        }

        public enum spring_source { head = 0, body = 1, bust = 2, tail = 3 }

        public class connected_row
        {
            public string bone_a;
            public string bone_b;
            public float intensity;
        }

        // loads the container bundles for one character. missing bundles are
        // fine (skipped); the caller checks the bind result.
        //   body_folder: "3d/chara/body/bdyNNNN_SS"
        //   cloth_index: the worn dress's cloth slot (00 base)
        //   bust_index:  chara_data.bust (shared bodies only)
        //   tail_model_id: chara_data.tail_model_id (0/-1 = none)
        public static List<container_ref> load_containers(
            int chara_id, string body_folder, int cloth_index,
            int bust_index, bool chara_specific_body, int tail_model_id,
            Func<string, GameObject> load_prefab)
        {
            var refs = new List<container_ref>();
            if (load_prefab == null) return refs;

            // container bundles live in the body folder's clothes/ segment;
            // character-specific bodies ship one folder (bdyNNNN_00) whose
            // stem names the base prefab.
            string body_stem = $"{body_folder}/clothes/pfb_{body_folder.Split('/')[3]}";

            void add(spring_source src, string bundle)
            {
                var go = load_prefab(bundle);
                if (go == null) return;
                var container = go.GetComponent<Gallop.CySpringDataContainer>()
                    ?? go.GetComponentInChildren<Gallop.CySpringDataContainer>(true);
                if (container == null)
                {
                    UnityEngine.Object.Destroy(go);
                    return;
                }
                refs.Add(new container_ref { source = src, container = container, instance = go });
            }

            // head cloth: try the dress's hair slot down to 00.
            for (int c = cloth_index; c >= 0; c--)
            {
                int before = refs.Count;
                add(spring_source.head,
                    $"3d/chara/head/chr{chara_id}_00/clothes/pfb_chr{chara_id}_00_cloth{c:00}");
                if (refs.Count > before) break;
            }

            add(spring_source.body, $"{body_stem}_cloth{cloth_index:00}");
            if (chara_specific_body)
                add(spring_source.bust, $"{body_stem}_bust_cloth{cloth_index:00}");
            else
                add(spring_source.bust, $"{body_stem}_bust{bust_index}_cloth{cloth_index:00}");
            if (tail_model_id > 0)
                add(spring_source.tail,
                    $"3d/chara/tail/tail{tail_model_id:0000}_00/clothes/pfb_tail{tail_model_id:0000}_00_cloth{cloth_index:00}");
            return refs;
        }

        // binds loaded containers onto the character skeleton: collision
        // GameObjects created under the target bones, chains bound per part,
        // wind + connected bones collected. returns false when nothing bound.
        public static bool attach(Transform character, List<container_ref> refs,
            spring_sim sim, System.Action<string> log)
        {
            if (refs == null || refs.Count == 0 || character == null) return false;

            var tables = new Dictionary<spring_sim.part, List<collision_param>>();
            var claimed = new HashSet<string>(); // first container naming a bone wins

            foreach (var rf in refs)
            {
                var entry = container_bridge.convert(rf.container);
                if (entry == null) continue;
                spring_sim.part part_id = rf.source == spring_source.tail
                    ? spring_sim.part.tail
                    : rf.source == spring_source.body ? spring_sim.part.body : spring_sim.part.head;
                var part = sim.part_or_create(part_id);

                // collision rows first: chains skip transforms the collision
                // table owns (the game's CreateChild rule).
                var table = table_for(tables, part_id);
                foreach (var row in entry.collisions)
                {
                    Transform target = resolve_target(character, row, log);
                    if (target == null) continue;
                    var col_go = new GameObject(row.collision_name);
                    col_go.transform.SetParent(target, false);
                    if (row.type == (int)spring_collider_type.sphere)
                        col_go.transform.position += row.offset;
                    else if (row.type == (int)spring_collider_type.capsule)
                        col_go.transform.localPosition = row.offset;
                    else
                    {
                        // plane rows are compiled out in the shipped build
                        UnityEngine.Object.Destroy(col_go);
                        continue;
                    }
                    row.rider = col_go.transform;
                    table.Add(row);
                }

                int chain_index = 0;
                foreach (var root_p in entry.roots)
                {
                    string root_name = root_p.bone_name;
                    if (string.IsNullOrEmpty(root_name)) { chain_index++; continue; }
                    Transform root_t = find_deep(character, root_name);
                    if (root_t == null)
                    {
                        log?.Invoke($"spring: root '{root_name}' missing on {character.name}");
                        chain_index++;
                        continue;
                    }
                    if (!claimed.Add(root_name)) { chain_index++; continue; }
                    var chain = new spring_chain();
                    float phase = (chain_index / (float)Mathf.Max(entry.roots.Count, 1)) * (Mathf.PI / 2f);
                    if (!chain.bind(root_t, root_p, entry.children, phase))
                    {
                        log?.Invoke($"spring: chain '{root_name}' degenerate, skipped");
                        chain_index++;
                        continue;
                    }
                    part.chains.Add(chain);
                    chain_index++;
                }

                if (entry.wind != null)
                {
                    part.has_wind = true;
                    if (sim.wind == null) sim.wind = spring_wind_factory.build(entry.wind, character);
                }
                foreach (var row in entry.connected) sim.pending_connected.Add(row);
            }

            // the tail split: body rows drop names the tail spring data uses;
            // the tail part's table keeps only those names.
            split_tail_collisions(tables, refs);

            foreach (var kv in tables)
                if (kv.Value.Count > 0)
                    sim.add_collision_table(kv.Key, kv.Value);

            sim.resolve_chain_collisions();
            sim.resolve_connected(character);
            sim.arm_suppression();

            // one-time bind census: chains + solved bones per part.
            foreach (var p in sim.parts)
                if (p != null && p.chains.Count > 0)
                {
                    int bones = 0;
                    foreach (var c in p.chains) bones += c.solve_count;
                    log?.Invoke($"spring bind: part {p.id} chains {p.chains.Count} bones {bones}");
                }
            return true;
        }

        static List<collision_param> table_for(Dictionary<spring_sim.part, List<collision_param>> tables,
            spring_sim.part part)
        {
            if (!tables.TryGetValue(part, out var t))
            {
                t = new List<collision_param>();
                tables[part] = t;
            }
            return t;
        }

        // the game's LoadSpringParam special cases: the tail part keeps only
        // the body rows whose names the tail spring data references (its own
        // container carries no collisionParam rows).
        static void split_tail_collisions(Dictionary<spring_sim.part, List<collision_param>> tables,
            List<container_ref> refs)
        {
            var tail_ref = refs.Find(r => r.source == spring_source.tail);
            if (tail_ref == null || !tables.TryGetValue(spring_sim.part.body, out var body)) return;

            var tail_names = new HashSet<string>();
            foreach (var root in tail_ref.container.springParam)
            {
                if (root == null) continue;
                add_names(tail_names, root._collisionNameList);
                foreach (var ch in root._childElements)
                    if (ch != null) add_names(tail_names, ch._collisionNameList);
            }
            if (tail_names.Count == 0) return;

            var tail = table_for(tables, spring_sim.part.tail);
            var moved = new List<collision_param>();
            foreach (var row in body)
                if (row != null && tail_names.Contains(row.collision_name))
                    moved.Add(row);
            foreach (var row in moved) body.Remove(row);
            tail.AddRange(moved);
        }

        static void add_names(HashSet<string> set, List<string> names)
        {
            if (names == null) return;
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) set.Add(n);
        }

        static Transform resolve_target(Transform character, collision_param row,
            System.Action<string> log)
        {
            if (row.is_other_target != 0)
            {
                // other-character targets ride the shared cast table the
                // director wires before attach; per-character attach skips them.
                return null;
            }
            var t = find_deep(character, row.link_bone);
            if (t == null)
                log?.Invoke($"spring: collision '{row.collision_name}' target '{row.link_bone}' missing on {character.name}");
            return t;
        }

        static Transform find_deep(Transform root, string name)
        {
            if (root == null || root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = find_deep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }

    // the deserialized wind block from a container (cycles/angles/widths).
    public class spring_wind_params
    {
        public float cycle_vertical_slow;
        public float cycle_vertical_fast;
        public float cycle_horizontal_slow;
        public float cycle_horizontal_fast;
        public float angle_width_vertical;
        public float angle_width_horizontal;
        public float center_angle_vertical;
        public float center_angle_horizontal;
        public bool enable_vertical;
        public bool enable_horizontal;
        public Vector3 direction = Vector3.forward;
        public Vector3 right = Vector3.right;
        public byte is_local_direction;
        public float power_scale = 1f;
    }

    public static class spring_wind_factory
    {
        // builds the four direction cones exactly as the decoded
        // CreateWindControlData: AngleAxis(center + half, right) * direction
        // and mirrored pairs, with the -1 center multiplier baked in.
        public static spring_wind build(spring_wind_params p, Transform root)
        {
            Quaternion root_local = root != null ? root.localRotation : Quaternion.identity;
            Vector3 dir = safe_norm(p.direction, Vector3.forward);
            Vector3 right = safe_norm(p.right, Vector3.right);
            if (p.is_local_direction != 0)
            {
                dir = root_local * dir;
                right = root_local * right;
                dir = safe_norm(dir, Vector3.forward);
                right = safe_norm(right, Vector3.right);
            }

            float center_v = -p.center_angle_vertical;
            float v_half = p.angle_width_vertical * 0.5f;
            float h_half = p.angle_width_horizontal * 0.5f;

            var w = new spring_wind
            {
                dir_upper = Quaternion.AngleAxis(center_v + v_half, right) * dir,
                dir_downer = Quaternion.AngleAxis(center_v - v_half, right) * dir,
                dir_right = Quaternion.AngleAxis(h_half, Vector3.up) * dir,
                dir_left = Quaternion.AngleAxis(-h_half, Vector3.up) * dir,
                cycle_vertical = p.cycle_vertical_slow,
                cycle_horizontal = p.cycle_horizontal_slow,
                enable_vertical = p.enable_vertical,
                enable_horizontal = p.enable_horizontal,
                power_scale = p.power_scale,
            };
            return w;
        }

        static Vector3 safe_norm(Vector3 v, Vector3 fallback)
        {
            return v.sqrMagnitude > 1e-10f ? v.normalized : fallback;
        }
    }
}
