using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // post-spring hair blender, decoded from the game's
    // Gallop.Model.Component.HairMotionBlendController: after the spring
    // writes final transforms, each blended transform is pulled back
    // toward the cached override-motion pose by BlendRate (lerp on
    // localPosition, Quaternion.Lerp on localRotation, weight clamped to
    // [0,1]).
    //
    // the game only fills the cache when hair override motion tracks run
    // (facial/hair animation layers); UV2's worksheet reader has no such
    // tracks yet, so this controller ships dormant: the director registers
    // transforms and can enable blending once an override source exists.
    public class hair_blend
    {
        public float blend_rate = 1f;   // serialized BlendRate (this+0x50)

        readonly List<Transform> bones = new();
        Vector3[] cached_pos;
        Quaternion[] cached_rot;
        bool enabled;

        // registers the hair transforms to blend (the game's
        // AddTransformRange). resets any prior cache.
        public void add_transform_range(IList<Transform> range)
        {
            if (range == null) return;
            foreach (var t in range)
                if (t != null && !bones.Contains(t)) bones.Add(t);
            cached_pos = new Vector3[bones.Count];
            cached_rot = new Quaternion[bones.Count];
            enabled = bones.Count > 0;
        }

        // caches the current local pose (the game's CacheTransform, driven
        // from PreLateUpdate state): the override-motion pose for this frame.
        public void cache_transform()
        {
            if (!enabled) return;
            for (int i = 0; i < bones.Count; i++)
            {
                cached_pos[i] = bones[i].localPosition;
                cached_rot[i] = bones[i].localRotation;
            }
        }

        // the after-spring pass (the game's AfterCySpringEndSimulation):
        // pulls each bone toward the cached pose by the clamped rate.
        public void blend_transform()
        {
            if (!enabled) return;
            float w = Mathf.Clamp(blend_rate, 0f, 1f);
            if (w <= 0f) return;
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                b.localPosition = Vector3.Lerp(b.localPosition, cached_pos[i], w);
                b.localRotation = Quaternion.Lerp(b.localRotation, cached_rot[i], w);
            }
        }
    }
}
