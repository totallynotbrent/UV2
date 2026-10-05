using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UV2.Live
{
    // resolves chara-relative camera targets: the parts enum picks a per-character anchor and the flagged anchors average with height-scaled layer offsets.
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
        public const int INITIAL_HEIGHT_FACE = 17;
        public const int INITIAL_HEIGHT_CHEST = 18;
        public const int INITIAL_HEIGHT_WAIST = 19;
        public const int MAX = 20;

        // base height for the layer-offset ratio (height_value / 158).
        public const float BASE_HEIGHT = 158f;

        // per-character anchor cache: rest-pose heights, load-time position, cm height.
        private class chara_anchor
        {
            public Vector3 initial_position;
            public float head_height;
            public float waist_height;
            public float chest_height;
            public float height_value = BASE_HEIGHT;
        }
        private static readonly Dictionary<Transform, chara_anchor> anchors = new();

        // captures rest-pose heights once per character; safe to call repeatedly.
        public static void record(Transform root)
        {
            if (root == null || anchors.ContainsKey(root)) return;
            var a = new chara_anchor();
            var position_bone = find_bone(root, "Position") ?? root;
            // the game's liveCharaInitialPosition: the "Position" bone's world
            // transform at load; the full-vector part anchors (6-8, 17-19) ride it.
            a.initial_position = position_bone.position;
            var head = find_bone(root, "Head");
            var waist = find_bone(root, "Waist");
            var chest = find_bone(root, "Chest");
            if (head != null) a.head_height = position_bone.InverseTransformPoint(head.position).y + 0.1f;
            if (waist != null) a.waist_height = position_bone.InverseTransformPoint(waist.position).y;
            if (chest != null) a.chest_height = position_bone.InverseTransformPoint(chest.position).y;
            anchors[root] = a;
        }

        // stores the character's cm height; falls back to the base height when missing.
        public static void record_height(Transform root, int chara_id)
        {
            if (root == null) return;
            if (!anchors.TryGetValue(root, out var a)) a = null;
            if (a == null)
            {
                a = new chara_anchor { initial_position = root.position };
                anchors[root] = a;
            }
            float h = query_height(chara_id);
            if (h > 0f) a.height_value = h;
        }

        // reads one character's cm height (chara_data.scale) from the master db.
        private static float query_height(int chara_id)
        {
            if (chara_id <= 0) return 0f;
            try
            {
                using var db = UV2.Data.master_db.reader.open(UV2.App.config.master_db_path);
                if (db == null) return 0f;
                foreach (var r in db.query($"SELECT scale FROM chara_data WHERE id={chara_id}"))
                    return r.get_int(0);
            }
            catch (Exception) { }
            return 0f;
        }

        // the character's cm height (liveCharaHeightValue); the base height when uncached.
        public static float height_value(Transform root)
        {
            return anchors.TryGetValue(root, out var a) ? a.height_value : BASE_HEIGHT;
        }

        // the per-character scale applied to the layer offset.
        public static float height_ratio(Transform root)
        {
            return height_value(root) / BASE_HEIGHT;
        }

        // world position of one character's authored part; null when the bone is missing.
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
                // const-height anchors supply only the y; the key's chara_pos supplies x/z.
                // init/initial-height anchors (parts 6-8, 17-19) are FULL vectors: the
                // character's load-time x/z with the rest-pose bone height — this is the
                // game's liveCharaInitialHeight*Position, not a const-y lookup.
                case CONST_FACE_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_cf))
                        return new Vector3(0f, a_cf.head_height, 0f);
                    return null;
                case CONST_WAIST_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_cw))
                        return new Vector3(0f, a_cw.waist_height, 0f);
                    return null;
                case CONST_CHEST_HEIGHT:
                    if (anchors.TryGetValue(root, out var a_cc))
                        return new Vector3(0f, a_cc.chest_height, 0f);
                    return null;
                case CONST_FOOT_HEIGHT:
                    return Vector3.zero;
                case INIT_FACE_HEIGHT:
                case INITIAL_HEIGHT_FACE:
                case INIT_WAIST_HEIGHT:
                case INITIAL_HEIGHT_WAIST:
                case INIT_CHEST_HEIGHT:
                case INITIAL_HEIGHT_CHEST:
                    if (!anchors.TryGetValue(root, out var a)) return null;
                    float y = part == INIT_CHEST_HEIGHT || part == INITIAL_HEIGHT_CHEST ? a.chest_height
                            : part == INIT_WAIST_HEIGHT || part == INITIAL_HEIGHT_WAIST ? a.waist_height
                            : a.head_height;
                    return new Vector3(a.initial_position.x, y, a.initial_position.z);
                case POSITION:
                case POSITION_WITHOUT_OFFSET:
                    return root.position;
                default:
                    return null;
            }
        }

        // flags==0 means the stage center; otherwise the flagged anchors average.
        public static Vector3 group_world(List<Transform> chara_roots, int flags, int part)
        {
            return group_world(chara_roots, flags, part, Vector3.zero);
        }

        // averages the flagged anchors plus the layer offset scaled by each character's height ratio.
        public static Vector3 group_world(List<Transform> chara_roots, int flags, int part, Vector3 layer_offset)
        {
            if (flags == 0) return Vector3.zero; // the stage center
            var values = new List<Vector3>();
            for (int i = 0; i < chara_roots.Count && i < MAX; i++)
            {
                if ((flags & (1 << i)) == 0) continue;
                var v = part_world(chara_roots[i], part);
                if (v.HasValue) values.Add(v.Value + layer_offset * height_ratio(chara_roots[i]));
            }
            if (values.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        // the average cm height of the flagged characters; zero flags means zero.
        public static float group_height(List<Transform> chara_roots, int flags)
        {
            if (flags == 0) return 0f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < chara_roots.Count && i < MAX; i++)
            {
                if ((flags & (1 << i)) == 0) continue;
                sum += height_value(chara_roots[i]);
                n++;
            }
            return n > 0 ? sum / n : 0f;
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
