using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // loads the four spring containers (head/body/bust/tail) for one
    // character, merges them by bone name (load order head, body, bust, tail;
    // the first container naming a bone wins), and binds chains to the live
    // skeleton. deserialization of the raw container objects happens in the
    // asset layer; this class consumes typed param rows only.
    public static class spring_loader
    {
        public class container_set
        {
            public List<container_entry> entries = new();
        }

        public class container_entry
        {
            public spring_source source;          // head/body/bust/tail
            public List<spring_element_params> roots = new(); // one per top-level springParam
            public Dictionary<string, spring_element_params> children = new();
            public List<collision_param> collisions = new();
            public spring_wind_params wind;       // null when the container is windless
        }

        public enum spring_source { head = 0, body = 1, bust = 2, tail = 3 }

        // merges entries in the given order into per-part chains bound to the
        // character's transforms. returns false when nothing could bind.
        public static bool bind(Transform character, container_set set,
            spring_sim sim, System.Action<string> log)
        {
            int bound = 0;
            var claimed = new HashSet<string>(); // first container naming a bone wins

            foreach (var entry in set.entries)
            {
                if (entry.roots.Count == 0) continue;
                var part = new spring_sim.part_state
                {
                    id = (spring_sim.part)(int)entry.source,
                    has_wind = entry.wind != null,
                };
                int chain_index = 0;
                foreach (var root_p in entry.roots)
                {
                    // the root bone: the first child-element name's parent, i.e.
                    // the transform owning this root element (by convention the
                    // container lists the root transform's own name first).
                    string root_name = root_p.bone_name;
                    Transform root_t = find_deep(character, root_name);
                    if (root_t == null)
                    {
                        log?.Invoke($"spring: root '{root_name}' missing on {character.name}");
                        chain_index++;
                        continue;
                    }
                    if (!claimed.Add(root_name))
                    {
                        chain_index++;
                        continue;
                    }
                    var chain = new spring_chain();
                    float phase = (chain_index / (float)Mathf.Max(entry.roots.Count, 1)) * (Mathf.PI / 2f);
                    if (!chain.bind(root_t, root_p, entry.children, phase))
                    {
                        log?.Invoke($"spring: chain '{root_name}' degenerate, skipped");
                        chain_index++;
                        continue;
                    }
                    part.chains.Add(chain);
                    bound++;
                    chain_index++;
                }
                if (part.chains.Count > 0) sim.parts.Add(part);
                if (entry.wind != null) sim.wind = spring_wind_factory.build(entry.wind);
                if (entry.collisions.Count > 0) sim.collider_params.AddRange(entry.collisions);
            }
            return bound > 0;
        }

        static Transform find_deep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = find_deep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }

    // the deserialized wind block from a body container.
    public class spring_wind_params
    {
        public float cycle_slow;
        public float cycle_fast;
        public float power_v;              // vertical wind weight (v in the census)
        public float power_h;              // horizontal wind weight
        public Vector3 dir_v_slow, dir_v_fast, dir_h_slow, dir_h_fast;
    }

    public static class spring_wind_factory
    {
        public static spring_wind build(spring_wind_params p)
        {
            var w = new spring_wind
            {
                cycle_slow = p.cycle_slow,
                cycle_fast = p.cycle_fast,
                dir_v_slow = p.dir_v_slow,
                dir_v_fast = p.dir_v_fast,
                dir_h_slow = p.dir_h_slow,
                dir_h_fast = p.dir_h_fast,
            };
            w.amplitude_v = 0.015f * Mathf.Max(p.power_v, 0f);
            w.amplitude_h = 0.005f * Mathf.Max(p.power_h, 0f);
            return w;
        }
    }
}
