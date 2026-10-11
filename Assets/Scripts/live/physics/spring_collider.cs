using UnityEngine;

namespace UV2.Live
{
    public enum spring_collider_type { sphere = 0, capsule = 1 }

    // one character-body collider in world space, rebuilt each frame from the
    // rig's collision transforms (ikcol + container collision params).
    public struct spring_collider
    {
        public spring_collider_type type;
        public Vector3 position;           // sphere center / capsule start
        public Vector3 position2;          // capsule end (unused for spheres)
        public float radius;
        public string link_bone;           // the transform the collider rides

        // evaluates world-space placement from a collision param row.
        public static spring_collider from_param(collision_param p, Transform owner,
            float height_scale, Vector3 parent_scale)
        {
            var c = new spring_collider
            {
                link_bone = p.link_bone,
                radius = p.radius * height_scale,
            };
            Transform t = owner;
            if (!string.IsNullOrEmpty(p.link_bone))
            {
                var found = owner.GetComponentsInChildren<Transform>();
                for (int i = 0; i < found.Length; i++)
                    if (found[i].name == p.link_bone) { t = found[i]; break; }
            }
            Vector3 base_pos = t != null ? t.position : owner.position;
            Vector3 scale = t != null ? Vector3.Scale(t.lossyScale, parent_scale) : parent_scale;
            Vector3 off = Vector3.Scale(p.offset, scale);
            c.position = base_pos + off;
            if (p.type == (int)spring_collider_type.capsule)
            {
                Vector3 off2 = Vector3.Scale(p.offset2, scale);
                c.position2 = base_pos + off2;
            }
            return c;
        }
    }

    // one deserialized row from the container collisionParam list / ikcols.
    public class collision_param
    {
        public int type;                   // 0 sphere, 1 capsule (plane compiled out)
        public string link_bone;
        public Vector3 offset;
        public Vector3 offset2;
        public float radius;
    }
}
