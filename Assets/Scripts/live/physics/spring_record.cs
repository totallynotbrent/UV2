using UnityEngine;

namespace UV2.Live
{
    // one spring bone's runtime state and serialized params, mirroring the
    // decoded record layout semantically (managed fields, no native layout).
    public struct spring_record
    {
        // solver positions (world)
        public Vector3 self_position;      // parent's solved position this frame
        public Vector3 target_position;    // this bone's verlet position
        public Vector3 prev_target;        // previous frame's target (velocity proxy)

        // rotations
        public Quaternion init_local;      // bind-pose local rotation
        public Quaternion parent_rot;      // parent's final world rotation
        public Quaternion final_rot;       // this frame's solved world rotation
        public Quaternion anim_local;      // the animation's local rotation (gathered)

        // rest direction + segment length (live on the PARENT record of a pair)
        public Vector3 bone_axis;          // rest direction in the bind pose
        public float bone_distance;        // InitBoneDistance (hard constraint)

        // serialized params (per bone, from the containers)
        public float stiffness;
        public float drag;
        public float gravity;
        public float collision_radius;
        public float move_rate;            // MoveSpringApplyRate
        public float dynamic_ratio;        // 1 = normal, <1 switches the lag path
        public Vector4 wind_rates;         // vSlow, vFast, hSlow, hFast

        // limits
        public byte is_limit;
        public Vector3 limit_min;
        public Vector3 limit_max;

        // flags + collision bindings
        public byte skip;                  // whole-chain skip when set on record 0
        public byte check_chara_collision;
        public int active_collisions;      // count (indices resolved per character)
        public int[] collision_indices;    // up to 16 slots; beyond 16 counted only
        public int parent_work;            // index into the character's parent work table

        // per-step force accumulators (cleared/rebuilt each frame)
        public Vector3 force;
        public Vector3 connected_force;

        public bool has_collision_slot(int slot)
        {
            return collision_indices != null && slot < collision_indices.Length;
        }

        public static spring_record bind(Transform bone, Transform parent_bone)
        {
            var r = new spring_record
            {
                init_local = bone.localRotation,
                final_rot = bone.rotation,
                anim_local = bone.localRotation,
                target_position = bone.position,
                prev_target = bone.position,
                dynamic_ratio = 1f,
                check_chara_collision = 1,
                move_rate = 1f,
                wind_rates = Vector4.one,
                limit_min = new Vector3(180f, 180f, 180f),
                limit_max = new Vector3(180f, 180f, 180f),
            };
            // root: axis up, distance 1 (self position comes from the parent work).
            // segment axis/distance live on the PARENT of a pair and are written
            // by the child during bind (see spring_chain.append).
            if (parent_bone == null)
            {
                r.bone_axis = Vector3.up;
                r.bone_distance = 1f;
            }
            return r;
        }
    }
}
