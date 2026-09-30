using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UV2.Live
{
    // resolves chara-relative camera targets exactly as the game's
    // GetPositionWithCharacters does: the parts enum selects a per-character
    // anchor, the position flags select which characters contribute, and the
    // flagged anchors are averaged. heights for the const/init parts are
    // captured once at load from the rest pose.
    public static class chara_parts
    {
        public const int FACE = 0;
        public const int WAIST = 1;
        public const int LEFT_HAND_WRIST = 2;
        public const int RIGHT_HAND_ATTACH = 3;
        public const int CHEST = 4;
        public const int FOOT = 5;
        public const int INIT_FACE_HEIGHT = 6;
        public const int INIT_WAIST_HEIGHT = 7;
        public const int INIT_CHEST_HEIGHT = 8;
        public const int RIGHT_HAND_WRIST = 9;
        public const int LEFT_HAND_ATTACH = 10;
        public const int CONST_FACE_HEIGHT = 11;
        public const int CONST_CHEST_HEIGHT = 12;
        public const int CONST_WAIST_HEIGHT = 13;
        public const int CONST_FOOT_HEIGHT = 14;
        public const int POSITION = 15;
        public const int POSITION_WITHOUT_OFFSET = 16;
        public const int MAX = 20;

        // per-character anchor cache captured at load: rest-pose bone heights
        // and the load-time position, matching the locator's Init call.
        private class chara_anchor
        {
            public Vector3 initial_position;
            public float head_height;
            public float waist_height;
            public float chest_height;
        }
        private static readonly Dictionary<Transform, chara_anchor> anchors = new();

        // captures the rest-pose heights once per character; safe to call
        // repeatedly during load.
        public static void record(Transform root)
        {
            if (root == null || anchors.ContainsKey(root)) return;
            var a = new chara_anchor { initial_position = root.position };
            var position_bone = find_bone(root, "Position") ?? root;
            var head = find_bone(root, "Head");
            var waist = find_bone(root, "Waist");
            var chest = find_bone(root, "Chest");
            if (head != null) a.head_height = position_bone.InverseTransformPoint(head.position).y + 0.1f;
            if (waist != null) a.waist_height = position_bone.InverseTransformPoint(waist.position).y;
            if (chest != null) a.chest_height = position_bone.InverseTransformPoint(chest.position).y;
            anchors[root] = a;
        }

        // world position of one character's authored part; null when the
        // character does not expose the bone.
        public static Vector3? part_world(Transform root, int part)
        {
            if (root == null) return null;
            switch (part)
            {
                case FACE:
                    var head = find_bone(root, "Head");
                    return head == null ? null : head.position + new Vector3(0f, 0.1f, 0f);
                case WAIST:
                    return find_bone(root, "Waist")?.position;
                case LEFT_HAND_WRIST:
                    return find_bone(root, "Wrist_L")?.position;
                case RIGHT_HAND_WRIST:
                    return find_bone(root, "Wrist_R")?.position;
                case LEFT_HAND_ATTACH:
                    return find_bone(root, "Hand_Attach_L")?.position;
                case RIGHT_HAND_ATTACH:
                    return find_bone(root, "Hand_Attach_R")?.position;
                case CHEST:
                    return find_bone(root, "Chest")?.position;
                case FOOT:
                    var chest = find_bone(root, "Chest");
                    return chest == null ? null : new Vector3(chest.position.x, 0f, chest.position.z);
                case INIT_FACE_HEIGHT:
                case CONST_FACE_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_head))
                        return new Vector3(a_head.initial_position.x, a_head.head_height, a_head.initial_position.z);
                    return null;
                case INIT_WAIST_HEIGHT:
                case CONST_WAIST_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_waist))
                        return new Vector3(a_waist.initial_position.x, a_waist.waist_height, a_waist.initial_position.z);
                    return null;
                case INIT_CHEST_HEIGHT:
                case CONST_CHEST_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_chest))
                        return new Vector3(a_chest.initial_position.x, a_chest.chest_height, a_chest.initial_position.z);
                    return null;
                case CONST_FOOT_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_foot))
                        return new Vector3(a_foot.initial_position.x, 0f, a_foot.initial_position.z);
                    return null;
                case POSITION:
                case POSITION_WITHOUT_OFFSET:
                    return root.position;
                default:
                    return null;
            }
        }

        // group resolution: flags==0 means the stage center, matching the
        // game's zero-flag shortcut; otherwise the flagged anchors average.
        public static Vector3 group_world(List<Transform> chara_roots, int flags, int part)
        {
            if (flags == 0) return Vector3.zero; // the stage center
            var values = new List<Vector3>();
            for (int i = 0; i < chara_roots.Count && i < MAX; i++)
            {
                if ((flags & (1 << i)) == 0) continue;
                var v = part_world(chara_roots[i], part);
                if (v.HasValue) values.Add(v.Value);
            }
            if (values.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        // bone lookup tolerant of naming variants in the game rigs.
        private static Transform find_bone(Transform root, string token)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == token) return t;
            }
            // second pass: substring match for rigs with suffixed bone names
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains(token)) return t;
            }
            return null;
        }
    }
}
