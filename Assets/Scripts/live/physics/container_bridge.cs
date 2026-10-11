using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // bridges the game's data-only containers (Gallop.CySpringDataContainer,
    // auto-bound from the cloth bundles by the typetree stub) into the typed
    // rows spring_loader consumes.
    public static class container_bridge
    {
        // one container converted: typed rows + collisions + wind + connected.
        public class container_entry
        {
            public bool use_correct_scale;
            public float hip_influence_distance = -1f;
            public float hip_influence_max_distance = -1f;
            public spring_wind_params wind;      // null when wind is disabled
            public List<collision_param> collisions = new();
            public List<spring_element_params> roots = new();
            public Dictionary<string, spring_element_params> children = new();
            public List<spring_loader.connected_row> connected = new();
        }

        public static container_entry convert(Gallop.CySpringDataContainer c)
        {
            if (c == null) return null;
            var entry = new container_entry
            {
                use_correct_scale = c.UseCorrectScaleCalc != 0,
                hip_influence_distance = c.IsEnableHipMoveParam != 0 ? c.HipMoveInfluenceDistance : -1f,
                hip_influence_max_distance = c.IsEnableHipMoveParam != 0 ? c.HipMoveInfluenceMaxDistance : -1f,
            };

            bool any_wind = c.enableVerticalWind != 0 || c.enableHorizontalWind != 0;
            if (any_wind)
            {
                entry.wind = new spring_wind_params
                {
                    enable_vertical = c.enableVerticalWind != 0,
                    enable_horizontal = c.enableHorizontalWind != 0,
                    center_angle_vertical = c.centerWindAngleSlow,
                    center_angle_horizontal = c.centerWindAngleSlow,
                    angle_width_vertical = c.verticalAngleWidthSlow,
                    angle_width_horizontal = c.horizontalAngleWidthSlow,
                    cycle_vertical_slow = Mathf.Max(c.verticalCycleSlow, 1e-3f),
                    cycle_horizontal_slow = Mathf.Max(c.horizontalCycleSlow, 1e-3f),
                    cycle_vertical_fast = Mathf.Max(c.verticalCycleFast, 1e-3f),
                    cycle_horizontal_fast = Mathf.Max(c.horizontalCycleFast, 1e-3f),
                    direction = Vector3.forward,
                    right = Vector3.right,
                };
            }

            foreach (var row in c.collisionParam)
            {
                if (row == null || string.IsNullOrEmpty(row._collisionName)) continue;
                if (row._radius <= 0f) continue; // the game rejects zero-radius rows
                entry.collisions.Add(new collision_param
                {
                    type = row._type,
                    collision_name = row._collisionName,
                    link_bone = row._targetObjectName,
                    offset = row._offset,
                    offset2 = row._offset2,
                    radius = row._radius,
                    is_other_target = row._isOtherTarget,
                });
            }

            var children = new Dictionary<string, spring_element_params>();
            foreach (var root in c.springParam)
            {
                if (root == null || string.IsNullOrEmpty(root._boneName)) continue;
                var root_p = to_params(root._boneName, root._stiffnessForce, root._dragForce,
                    root._gravity, root._collisionRadius, root._needEnvCollision,
                    root._collisionNameList, root._isLimit, root._limitAngleMin,
                    root._limitAngleMax, root.MoveSpringApplyRate, root._needSimulateEndBone,
                    root._verticalWindRateSlow, root._horizontalWindRateSlow,
                    root._verticalWindRateFast, root._horizontalWindRateFast);
                entry.roots.Add(root_p);
                foreach (var child in root._childElements)
                {
                    if (child == null || string.IsNullOrEmpty(child._boneName)) continue;
                    if (children.ContainsKey(child._boneName)) continue; // first wins
                    children[child._boneName] = to_params(child._boneName, child._stiffnessForce,
                        child._dragForce, child._gravity, child._collisionRadius,
                        child._needEnvCollision, child._collisionNameList, child._isLimit,
                        child._limitAngleMin, child._limitAngleMax, 1f, 1,
                        child._verticalWindRateSlow, child._horizontalWindRateSlow,
                        child._verticalWindRateFast, child._horizontalWindRateFast);
                }
            }
            entry.children = children;

            foreach (var cb in c.ConnectedBoneList)
            {
                if (cb == null) continue;
                entry.connected.Add(new spring_loader.connected_row
                {
                    bone_a = cb.Bone1,
                    bone_b = cb.Bone2,
                    intensity = cb.Intensity,
                });
            }
            return entry;
        }

        static spring_element_params to_params(string name, float stiffness, float drag,
            float gravity, float col_radius, byte need_env, List<string> col_names,
            byte is_limit, Vector3 limit_min, Vector3 limit_max, float move_rate,
            byte need_end, float v_slow, float h_slow, float v_fast, float h_fast)
        {
            return new spring_element_params
            {
                bone_name = name,
                stiffness = stiffness,
                drag = drag,
                gravity = gravity,
                collision_radius = col_radius,
                need_env_collision = need_env,
                is_limit = is_limit,
                limit_min = limit_min,
                limit_max = limit_max,
                move_rate = move_rate,
                need_simulate_end = need_end,
                wind_rates = new Vector4(v_slow, v_fast, h_slow, h_fast),
                collision_names = col_names ?? new List<string>(),
            };
        }
    }
}
