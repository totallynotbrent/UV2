using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's objectList: the named stage props the song
    // moves, rotates, scales and shows. the game's stage controller resolves
    // each entry by name against the loaded stage and applies the key's
    // offset onto the prop's authored pose; renderEnable toggles the prop's
    // renderers. the offsets are relative (OffsetValueType picks local or
    // world space), so the prefab placement is preserved.
    public class object_director : MonoBehaviour
    {
        private class target
        {
            public object_track track;
            public Transform transform;
            public Renderer[] renderers;
            public Vector3 base_position;
            public Quaternion base_rotation;
            public Vector3 base_scale;
            public bool resolved;
        }

        private live_worksheet ws;
        private readonly List<target> targets = new();
        private readonly List<Transform> roots = new();
        private int last_visible = -1;

        public void open(live_worksheet worksheet, List<Transform> root_list)
        {
            ws = worksheet;
            roots.Clear();
            if (root_list != null)
            {
                for (int i = 0; i < root_list.Count; i++)
                    if (root_list[i] != null) roots.Add(root_list[i]);
            }
            targets.Clear();
            if (ws?.stage_objects == null) return;
            for (int ti = 0; ti < ws.stage_objects.Count; ti++)
            {
                var t = ws.stage_objects[ti];
                if (t == null || t.keys.Count == 0) continue;
                targets.Add(new target { track = t });
            }
            trace_log.write($"objects: tracks {targets.Count}");
        }

        // finds the prop by name; unity's spawned copies carry a "(Clone)"
        // token, which the game's name key strips before hashing.
        private static string strip_clone(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            int idx = n.IndexOf("(Clone)", System.StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? n.Substring(0, idx).TrimEnd() : n;
        }

        private void resolve(target tgt)
        {
            if (tgt.resolved || roots.Count == 0) return;
            string wanted = strip_clone(tgt.track.name);
            for (int ri = 0; ri < roots.Count; ri++)
            {
                var root = roots[ri];
                if (root == null) continue;
                var hits = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < hits.Length; i++)
                {
                    var tr = hits[i];
                    if (tr == null) continue;
                    if (!string.Equals(strip_clone(tr.name), wanted, System.StringComparison.Ordinal)) continue;
                    tgt.transform = tr;
                    tgt.base_position = tr.localPosition;
                    tgt.base_rotation = tr.localRotation;
                    tgt.base_scale = tr.localScale;
                    tgt.renderers = tr.GetComponentsInChildren<Renderer>(true);
                    tgt.resolved = true;
                    trace_log.write($"objects: '{wanted}' resolved ({tgt.renderers.Length} renderers)");
                    return;
                }
            }
            tgt.resolved = true;
            trace_log.write($"objects: '{wanted}' NOT FOUND");
        }

        public void update(float song_time)
        {
            if (targets.Count == 0) return;
            int visible = 0;
            for (int ti = 0; ti < targets.Count; ti++)
            {
                var tgt = targets[ti];
                if (tgt == null) continue;
                resolve(tgt);
                if (tgt.transform == null) continue;

                var keys = tgt.track.keys;
                int i = key_eval.bracket(keys, song_time);
                if (i < 0) i = 0;
                var cur = keys[i];
                var next = i + 1 < keys.Count ? keys[i + 1] : null;
                float k = next != null && next.interpolate_type != 0
                    ? key_eval.interp(cur, next, song_time)
                    : 0f;

                var tr = tgt.transform;
                if (tgt.track.enable_position != 0)
                {
                    Vector3 p = next != null && next.interpolate_type != 0
                        ? Vector3.Lerp(cur.position, next.position, k)
                        : cur.position;
                    if (cur.offset_value_type == 0)
                        tr.localPosition = tgt.base_position + p;
                    else
                        tr.position = tgt.base_position + p;
                }
                if (tgt.track.enable_rotate != 0)
                {
                    Vector3 r = next != null && next.interpolate_type != 0
                        ? Vector3.Lerp(cur.rotate, next.rotate, k)
                        : cur.rotate;
                    tr.localRotation = tgt.base_rotation * Quaternion.Euler(r);
                }
                if (tgt.track.enable_scale != 0)
                {
                    Vector3 s = next != null && next.interpolate_type != 0
                        ? Vector3.Lerp(cur.scale, next.scale, k)
                        : cur.scale;
                    tr.localScale = Vector3.Scale(tgt.base_scale, s);
                }

                bool show = cur.render_enable != 0;
                if (tgt.renderers != null)
                {
                    for (int ri = 0; ri < tgt.renderers.Length; ri++)
                    {
                        var r = tgt.renderers[ri];
                        if (r == null) continue;
                        if (r.enabled != show) r.enabled = show;
                    }
                }
                if (show) visible++;
            }
            if (visible != last_visible)
            {
                trace_log.write($"objects: visible {visible}/{targets.Count}");
                last_visible = visible;
            }
        }
    }
}
