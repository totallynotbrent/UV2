using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // skirt knee-correction solver, decoded from the game's
    // SkirtController + UpdateSkirtNativePluginOne pipeline: per frame the
    // skirt root resets to its bind rotation, each enabled column
    // accumulates a correction angle via max() from the knee/ankle
    // triangle, gated by a linear influence fade, and the root rotates
    // around Vector3.left by the result.
    public class skirt_controller
    {
        public class column
        {
            public Transform root;
            public Transform child;
            public Quaternion root_init_rotation;
            public Vector3 child_init_local_pos;
            public bool check_knee_right;
            public bool check_knee_left;
        }

        Transform character;
        Transform center;
        Transform knee_l;
        Transform knee_r;
        Transform ankle_l;
        Transform ankle_r;
        float knee_radius;
        float ankle_radius;
        float influence_angle;
        float influence_max_angle;
        readonly List<column> columns = new();

        static readonly Vector3 skirt_normal = Vector3.up;
        static readonly Vector3 rot_axis = Vector3.left;

        // build from the deserialized asset: resolve the rig bones, seed the
        // per-column bind pose, scale radii (floor(x*1000)/1000 rule).
        public bool create(Transform character_root, Gallop.SkirtParamDataAsset asset)
        {
            if (character_root == null || asset == null) return false;
            character = character_root;

            center = find(character_root, asset._centerBoneName);
            knee_l = find(character_root, asset._kneeLBoneName);
            knee_r = find(character_root, asset._kneeRBoneName);
            ankle_l = find(character_root, asset._ankleLBoneName);
            ankle_r = find(character_root, asset._ankleRBoneName);
            if (knee_l == null || knee_r == null) return false;

            float model_scale = character_root.lossyScale.x;
            knee_radius = asset.IsScaleCollision != 0
                ? Mathf.Floor(asset._kneeColliderRadius * model_scale * 1000f) / 1000f
                : asset._kneeColliderRadius;
            ankle_radius = asset.IsScaleCollision != 0
                ? Mathf.Floor(asset._ankleColliderRadius * model_scale * 1000f) / 1000f
                : asset._ankleColliderRadius;
            influence_angle = asset._influenceAngle;
            influence_max_angle = asset._influenceMaxAngle;

            foreach (var e in asset._skirtBone)
            {
                var root_t = find(character_root, e._rootBoneName);
                var child_t = find(character_root, e._childBoneName);
                if (root_t == null || child_t == null) continue;
                columns.Add(new column
                {
                    root = root_t,
                    child = child_t,
                    root_init_rotation = root_t.localRotation,
                    child_init_local_pos = child_t.localPosition,
                    check_knee_right = e._isCheckRightLeg != 0,
                    check_knee_left = e._isCheckLeftLeg != 0,
                });
            }
            return columns.Count > 0;
        }

        // per-frame pass (the game's three loops folded into one walk):
        // reset, evaluate, rotate.
        public void update()
        {
            if (columns.Count == 0) return;
            foreach (var c in columns)
            {
                // pass 1: the skirt root's rotation resets to bind.
                c.root.localRotation = c.root_init_rotation;

                // pass 2: the correction angle from the knee/ankle triangle.
                float evaluation = -300f;
                eval_knee(c, knee_r, knee_radius, c.check_knee_right, ref evaluation);
                eval_knee(c, knee_l, knee_radius, c.check_knee_left, ref evaluation);
                if (ankle_r != null)
                    eval_knee(c, ankle_r, ankle_radius, c.check_knee_right, ref evaluation);
                if (ankle_l != null)
                    eval_knee(c, ankle_l, ankle_radius, c.check_knee_left, ref evaluation);

                // pass 3: apply when positive.
                if (evaluation > 0f)
                    c.root.Rotate(rot_axis, evaluation, Space.Self);
            }
        }

        // the decoded per-column math (UpdateSkirtNativePluginOne, literal):
        // triangle over the leg collider, projected correction angle, and
        // the linear influence fade.
        void eval_knee(column c, Transform leg, float radius, bool enabled, ref float evaluation)
        {
            if (!enabled || leg == null || radius <= 0f) return;

            Vector3 root_pos = c.root.position;
            Vector3 parent_pos = c.root.parent != null ? c.root.parent.position : root_pos;
            Vector3 child_pos = c.child.position;
            Vector3 leg_pos = leg.position;

            Quaternion init_world = c.root.parent != null
                ? c.root.parent.rotation * c.root_init_rotation
                : c.root_init_rotation;
            Vector3 init_child_pos = root_pos + init_world * c.child_init_local_pos;
            Vector3 init_normal = init_world * skirt_normal;
            Vector3 axis = init_world * rot_axis;

            // triangle: child position against the leg sphere.
            Vector3 span = child_pos - leg_pos;
            float span_len = span.magnitude;
            if (span_len < 1e-8f) return;
            float h2 = span.sqrMagnitude - radius * radius;
            float h = h2 > 0f ? Mathf.Sqrt(h2) : 0f;

            // projected push direction: t*axis + h*(h/span)*legDir, dotted with
            // the axis x span normal, applied back onto the span vector.
            Vector3 leg_dir = (leg_pos - parent_pos).normalized;
            Vector3 cross_n = Vector3.Cross(axis, span).normalized;
            float t = radius / span_len * h;
            Vector3 blend = axis * t + leg_dir * (h * (h / span_len));
            float dotv = Vector3.Dot(blend, cross_n);
            Vector3 projected = span + cross_n * dotv;

            // raw correction angle: rest child vs projected deflection.
            Vector3 rest_dir = (init_child_pos - root_pos).normalized;
            Vector3 push_dir = projected.normalized;
            float raw_angle = Vector3.Angle(push_dir, rest_dir);

            // gate: the child's offset from the leg sphere measured along the
            // panel's init normal — inside the projection (dot + radius < 0)
            // flips the correction negative.
            float gate = Vector3.Dot(span, init_normal) + radius;
            if (gate < 0f)
            {
                // child inside the leg's normal projection: correction flips.
                float v = raw_angle * -1f;
                if (v > evaluation) evaluation = v;
                return;
            }

            // positive branch: the deflected child direction rotated by the
            // raw angle around the column axis, compared against the init
            // normal in degrees for the influence fade.
            Quaternion rot = Quaternion.AngleAxis(raw_angle, axis)
                * (c.root.parent != null ? c.root.parent.rotation : Quaternion.identity);
            Vector3 deflected = rot * push_dir;
            Vector3 deflected_pos = parent_pos + deflected * (child_pos - parent_pos).magnitude;
            Vector3 def_dir = (deflected_pos - root_pos).normalized;
            float cosv = Mathf.Clamp(Vector3.Dot(def_dir, init_normal.normalized), -1f, 1f);
            float deg = Mathf.Acos(cosv) * Mathf.Rad2Deg;

            if (deg > influence_max_angle)
            {
                float v = 0f;
                if (v > evaluation) evaluation = v;
                return;
            }
            float fade = 1f;
            if (deg > influence_angle)
                fade = 1f - (deg - influence_angle) /
                    (influence_max_angle - influence_angle);
            float val = fade * raw_angle;
            if (val > evaluation) evaluation = val;
        }

        static Transform find(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = find(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
