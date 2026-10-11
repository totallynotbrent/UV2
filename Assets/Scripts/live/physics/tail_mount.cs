using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // mounts a character's tail mesh prefab onto the body rig: the tail
    // prefab ships a mirror Hip/Tail_Ctrl scaffold at the same local
    // offsets; name-matching reparents its Sp_Hi_Tail0_B_* chain and mesh
    // under the body's Tail_Ctrl, and the copies are destroyed.
    public static class tail_mount
    {
        // instantiates the tail prefab (bundle name resolved by the caller's
        // loader) and merges it. returns the merged renderer count, or -1
        // when the bundle was missing (no tail for this character).
        public static int mount(Transform character, GameObject tail_instance,
            System.Action<string> log)
        {
            if (tail_instance == null) return -1;

            // the body's bone table BEFORE the tail instance joins the
            // hierarchy (otherwise its own bones pollute the map and the
            // name match degenerates to identity).
            var body_bones = new Dictionary<string, Transform>();
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones)
                    if (b != null && !body_bones.ContainsKey(b.name))
                        body_bones[b.name] = b;

            tail_instance.transform.SetParent(character, false);
            log?.Invoke($"tail: instance parented, body bone table {body_bones.Count}");

            int renderers = 0;
            var dead = new List<Transform>();
            foreach (var skin in tail_instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderers++;
                if (skin.rootBone != null && body_bones.TryGetValue(skin.rootBone.name, out var new_root))
                    skin.rootBone = new_root;
                var remapped = new Transform[skin.bones.Length];
                for (int i = 0; i < remapped.Length; i++)
                {
                    var src = skin.bones[i];
                    if (src != null && body_bones.TryGetValue(src.name, out var tgt) && tgt != src)
                    {
                        remapped[i] = tgt;
                        src.position = tgt.position;
                        // reparent every child onto the body bone; the copied
                        // scaffold transforms then die empty.
                        while (src.childCount > 0)
                            src.GetChild(0).SetParent(tgt, true);
                        dead.Add(src);
                    }
                    else
                    {
                        remapped[i] = src;
                    }
                }
                skin.bones = remapped;
            }
            log?.Invoke($"tail: skins {renderers}, scaffold bones {dead.Count}");

            // reparent the surviving tail objects and drop the copied bones.
            while (tail_instance.transform.childCount > 0)
                tail_instance.transform.GetChild(0).SetParent(character, true);
            foreach (var d in dead)
                if (d != null) Object.Destroy(d.gameObject);
            Object.Destroy(tail_instance);

            log?.Invoke($"tail mounted: {renderers} renderer(s), {dead.Count} scaffold bone(s) merged");
            return renderers;
        }
    }
}
