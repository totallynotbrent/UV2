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

            // the hip-move damper source: the rig's Position/Hip transform.
            var hip = character.Find("Position/Hip");
            if (hip != null) sim.hip_transform = hip;

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

                // the hip-move damper params: the part's MAIN container carries
                // them (first container of the part wins, matching the game's
                // one springData per parts-array slot).
                if (!part.hip_enabled && entry.hip_influence_distance >= 0f)
                {
                    part.hip_enabled = true;
                    part.hip_influence_distance = entry.hip_influence_distance;
                    part.hip_influence_max_distance = entry.hip_influence_max_distance;
                }

                // collision rows first: chains skip transforms the collision
                // table owns (the game's CreateChild rule).
                var table = table_for(tables, part_id);
                foreach (var row in entry.collisions)
                {
                    Transform target = resolve_target(character, row, log);
                    if (target == null) continue;
                    var col_go = new GameObject(row.collision_name);
                    col_go.transform.SetParent(target, false);
                    // the game's ScaleParams algebra (v1's vendored
                    // ScaleParams_Sphere/Capsule): the sphere rider sits at
                    // defaultOffset / bodyScale; the capsule rider keeps the
                    // RAW defaultOffset (its axis and radius carry the
                    // lossy/bodyScale factor at eval time instead).
                    // v1's collision scale (LivePhysicsConfig SetScale 1.2):
                    // ScaleParams(legacyScale=1.2) divides BOTH the sphere
                    // rider's local offset and the collider radius by 1.2 —
                    // the effective world values land at ~the raw dump numbers
                    // while the unscaled algebra puts them 1.2x over. the
                    // oversized hip sphere reaches past the tail root and its
                    // radial push-out parks the tail up-back every step
                    // (bench16: col off = tail falls to the gravity hang).
                    float body_scale = position_body_scale(character);
                    float collision_scale = 1.2f;
                    if (row.type == (int)spring_collider_type.sphere)
                        col_go.transform.localPosition = row.offset / collision_scale;
                    else if (row.type == (int)spring_collider_type.capsule)
                        col_go.transform.localPosition = row.offset;
                    else
                    {
                        // plane rows are compiled out in the shipped build
                        UnityEngine.Object.Destroy(col_go);
                        continue;
                    }
                    row.rider = col_go.transform;
                    row.body_scale = body_scale;
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

            // the tail split (the game's LoadSpringParam): body rows drop the
            // names the tail spring data references; the tail part's table is
            // the BODY rows FILTERED to those names. the tail container ships
            // no collisionParam rows of its own.
            List<collision_param> body_rows = null;
            tables.TryGetValue(spring_sim.part.body, out body_rows);

            if (body_rows != null && body_rows.Count > 0)
            {
                var tail_names = new HashSet<string>();
                foreach (var t_part in sim.parts)
                    if (t_part != null && t_part.id == spring_sim.part.tail)
                        foreach (var ch in t_part.chains)
                            for (int bi = 0; bi < ch.element_of.Length; bi++)
                                if (ch.element_of[bi] != null)
                                    foreach (var n in ch.element_of[bi].collision_names)
                                        tail_names.Add(n);

                if (tail_names.Count > 0)
                {
                    // body keeps only rows the tail does not claim
                    var body_kept = new List<collision_param>();
                    foreach (var row in body_rows)
                        if (!tail_names.Contains(row.collision_name))
                            body_kept.Add(row);
                    tables[spring_sim.part.body] = body_kept;

                    // tail gets the claimed rows, in body order
                    var tail_rows = new List<collision_param>();
                    foreach (var row in body_rows)
                        if (tail_names.Contains(row.collision_name))
                            tail_rows.Add(row);
                    tables[spring_sim.part.tail] = tail_rows;
                }
            }

            foreach (var kv in tables)
                if (kv.Value.Count > 0)
                    sim.add_collision_table(kv.Key, kv.Value);
            // the tail container's empty table still registers so the loader
            // logs it faithfully (the game keeps the empty array too).

            sim.resolve_chain_collisions();
            sim.resolve_connected(character);
            sim.arm_suppression();

            // one-time bind census: chains + solved bones per part.
            foreach (var p in sim.parts)
                if (p != null && p.chains.Count > 0)
                {
                    int bones = 0;
                    int with_coll = 0;
                    int list_miss = 0;
                    foreach (var c in p.chains)
                    {
                        bones += c.solve_count;
                        for (int bi = 0; bi < c.records.Length && bi < c.element_of.Length; bi++)
                        {
                            if (c.records[bi].active_collisions > 0) with_coll++;
                            else if (c.element_of[bi] != null && c.element_of[bi].collision_names.Count > 0) list_miss++;
                        }
                    }
                    log?.Invoke($"spring bind: part {p.id} chains {p.chains.Count} bones {bones} colliders {with_coll} missing {list_miss}");
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

        // katboi's container layout: each part's Cyspring builds its collision
        // table from its OWN container. the tail's container ships no
        // collisionParam rows, so the Col_B_Hip_Tail/Col_B_Chest_Tail names in
        // its param lists resolve to nothing — those spheres are the tail's
        // occupied space, guarding the BODY/skirt chains, never the tail.

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

        // the rig's body scale: Position.localScale.y (the game's bodyScale
        // lives on the Position node; the character root stays at 1).
        static float position_body_scale(Transform character)
        {
            var position = find_deep(character, "Position");
            if (position == null) return 1f;
            float s = position.localScale.y;
            return s > 1e-5f ? s : 1f;
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
