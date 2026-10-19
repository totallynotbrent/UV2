using UnityEngine;

namespace UV2.Live
{
    // collider types as the shipped build dispatches them: 0 = sphere,
    // 2 = capsule, 1 = plane (compiled out — never materialized).
    public enum spring_collider_type { sphere = 0, plane = 1, capsule = 2 }

    // one character-body collider in world space, rebuilt each frame from the
    // collision transform the loader created under the target bone.
    // decoded placement (verified against CySpringCollisionRuntimeData):
    //   sphere: the rider's world position = target bone position + offset;
    //           the sim reads the rider's transform.position directly.
    //   capsule: rider localPosition = offset; axis = (offset2 - offset)
    //           scaled by the rider's parent lossyScale.
    //   radius: the game's ScaleParams — effective = authored *
    //           (lossyScaleY / BodyScale) for both shapes; BodyScale =
    //           chara_height/160.7529, and the Position bone's localScale
    //           equals it, so standard-height casts evaluate to raw.
    public struct spring_collider
    {
        public spring_collider_type type;
        public Vector3 position;           // sphere center / capsule start
        public Vector3 position2;          // capsule end (unused for spheres)
        public float radius;

        // evaluates world-space placement from one collision row's rider
        // transform (the GameObject parented under the target bone).
        public static spring_collider from_rider(collision_param p)
        {
            var c = new spring_collider
            {
                radius = p.radius,
                type = (spring_collider_type)p.type,
            };
            Transform rider = p.rider;
            if (rider == null) return c;

            float body_scale = p.body_scale > 1e-5f ? p.body_scale : 1f;
            float lossy_y = rider.lossyScale.y > 1e-6f ? rider.lossyScale.y : 1f;
            float scale = lossy_y / body_scale;
            if (p.type == (int)spring_collider_type.capsule)
            {
                Vector3 parent_scale = rider.parent != null
                    ? rider.parent.lossyScale
                    : Vector3.one;
                c.position = rider.position;
                c.position2 = rider.position + Vector3.Scale(p.offset2 - p.offset, parent_scale);
            }
            else
            {
                c.position = rider.position;
            }
            c.radius *= scale;
            return c;
        }
    }

    // one deserialized row from the container collisionParam list. the loader
    // resolves the target bone once, parents the rider GameObject, and the
    // sim reads world placement from the rider every frame.
    public class collision_param
    {
        public int type;                   // 0 sphere, 2 capsule, 1 plane (unused)
        public string collision_name;      // the name spring bones reference
        public string link_bone;           // _targetObjectName: the bone it rides
        public Vector3 offset;
        public Vector3 offset2;
        public float radius;
        public byte is_other_target;       // resolve against another character
        public Transform rider;            // the created child transform
        public float body_scale = 1f;      // the rig's body scale at bind
    }
}
