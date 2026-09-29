using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // resolves chara-relative camera targets: the 21-member parts table decoded
    // from GetPositionWithCharacters (out/chara_relative_parts_decoded.md).
    public static class chara_parts
    {
        public const int FACE = 0;
        public const int WAIST = 1;
        public const int LEFT_HAND_WRIST = 2;
        public const int RIGHT_HAND_ATTACH = 3;
        public const int CHEST = 4;
        public const int FOOT = 5;
        public const int INIT_FACE_HEIGHT = 6;
        public const int CONST_FACE_HEIGHT = 11;
        public const int POSITION = 15;
        public const int POSITION_WITHOUT_OFFSET = 16;
        public const int MAX = 20;

        // world position of one character's authored part, or null when the
        // character does not expose it.
        public static Vector3? part_world(Transform root, int part)
        {
            if (root == null) return null;
            switch (part)
            {
                case INIT_FACE_HEIGHT:
                case CONST_FACE_HEIGHT:
                    var head = root.Find("Character1_Head");
                    if (head != null) return head.position;
                    return null;
                case WAIST:
                    var waist = find_bone(root, "Spine");
                    return waist?.position;
                case CHEST:
                    var chest = find_bone(root, "Chest");
                    return chest?.position;
                case FOOT:
                    var foot = find_bone(root, "Foot");
                    return foot?.position;
                case POSITION:
                case POSITION_WITHOUT_OFFSET:
                    return root.position;
                default:
                    return null;
            }
        }

        // group resolution: average the part over the enabled characters, the
        // accumulation loop GetPositionWithCharacters performs.
        public static Vector3 group_world(List<Transform> chara_roots, int part)
        {
            var values = new List<Vector3>();
            foreach (var root in chara_roots)
            {
                var v = part_world(root, part);
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
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name.Contains(token)) return t;
            }
            return null;
        }
    }
}
