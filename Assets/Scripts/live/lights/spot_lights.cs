using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's spotlight3d containers over the stage hierarchy.
    public static class spot_lights
    {
        private class spot_container
        {
            public Transform root;
            public List<Light> lights = new();
            public List<Renderer> renderers = new();
            public MaterialPropertyBlock mpb;
        }
        private static readonly Dictionary<string, spot_container> containers = new();

        // binds the containers from the spotlight tracks against the recorded stage children.
        public static void bind(List<spot_track_container> tracks)
        {
            containers.Clear();
            if (tracks == null) return;
            int resolved = 0;
            var missing = new List<string>();
            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                // the fork strips the editor ordinal ('1st : x' -> 'x')
                // and resolves the bare name first, then the asset.
                var go = stage_object(strip_ordinal(t.name)) ?? stage_object(t.asset_name);
                if (go == null)
                {
                    missing.Add(t.name + " -> " + t.asset_name);
                    continue;
                }
                var c = new spot_container { root = go.transform, mpb = new MaterialPropertyBlock() };
                foreach (var l in go.GetComponentsInChildren<Light>(true)) c.lights.Add(l);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) c.renderers.Add(r);
                containers[t.name] = c;
                resolved++;
            }
            trace_log.write($"spot lights: {resolved} containers resolved, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"spot light unresolved: {m}");
        }

        // strips an editor ordinal head ('1st : name' -> 'name') like the
        // fork's StripOrdinalPrefix.
        private static string strip_ordinal(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.IndexOf(':');
            if (idx < 0) return name.Trim();
            string head = name.Substring(0, idx).Trim();
            if (head.Length == 0) return name.Trim();
            bool ordinal = head.EndsWith("st") || head.EndsWith("nd") ||
                           head.EndsWith("rd") || head.EndsWith("th");
            return ordinal ? name.Substring(idx + 1).Trim() : name.Trim();
        }

        // resolves the asset name against the stage map, falling back to a live scene search.
        private static GameObject stage_object(string asset_name)
        {
            var go = blink_lights.find_stage_object(asset_name);
            if (go != null) return go;
            return GameObject.Find(asset_name);
        }

        // samples every container per frame; call from the loader's update.
        // the fork's ApplySpotlight3d: the active flag gates the whole
        // fixture (mesh included), the head hangs over the character
        // slot (x/z from characterPosition, y from position).
        public static void update(float time_sec, List<spot_track_container> tracks, List<Transform> chara_roots)
        {
            if (tracks == null || containers.Count == 0) return;
            float frame = time_sec * 60f;
            foreach (var t in tracks)
            {
                if (!containers.TryGetValue(t.name, out var c) || c == null) continue;
                var (position, rotation, scale, color, power, active, char_pos) = t.sample(frame, chara_roots);
                if (c.root == null) continue;
                // the game hides the entire fixture when the key is
                // inactive: the mesh must not sit in frame glowing.
                bool was_active = c.root.gameObject.activeSelf;
                if (was_active != active) c.root.gameObject.SetActive(active);
                if (!active) continue;
                c.root.position = new Vector3(char_pos.x, position.y, char_pos.z);
                c.root.rotation = Quaternion.Euler(rotation);
                c.root.localScale = scale;
                if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                c.mpb.SetColor(id_color, color * power);
                foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
                foreach (var l in c.lights)
                {
                    l.color = color;
                    l.intensity = power;
                    l.enabled = active;
                }
            }
        }

        private static readonly int id_color = Shader.PropertyToID("_Color");
    }

    // one worksheet spotlight3d container: name + key track.
    [Serializable]
    public class spot_track_container
    {
        public string name;
        public string asset_name;
        public List<spot_key> keys = new();

        // brackets + lerps the key pair; position += the character anchor.
        public (Vector3, Vector3, Vector3, Color, float, bool, Vector3) sample(float frame, List<Transform> chara_roots)
        {
            if (keys == null || keys.Count == 0)
                return (Vector3.zero, Vector3.zero, Vector3.one, Color.white, 1f, false, Vector3.zero);
            spot_key a, b;
            float blend;
            if (frame <= keys[0].frame) { a = b = keys[0]; blend = 0f; }
            else if (frame >= keys[keys.Count - 1].frame) { a = b = keys[keys.Count - 1]; blend = 0f; }
            else
            {
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                    {
                        a = keys[i]; b = keys[i + 1];
                        float span = b.frame - a.frame;
                        blend = span <= 0 ? 0f : (frame - a.frame) / span;
                        goto found;
                    }
                }
                a = b = keys[keys.Count - 1]; blend = 0f;
            }
            goto found;
        found:
            Vector3 pos = Vector3.Lerp(a.position, b.position, blend);
            Vector3 rot = Vector3.Lerp(a.rotation, b.rotation, blend);
            Vector3 scale = Vector3.Lerp(a.scale, b.scale, blend);
            Color color = Color.Lerp(a.color, b.color, blend);
            float power = Mathf.Lerp(a.color_power, b.color_power, blend);
            bool active = (blend < 0.5f ? a.is_active : b.is_active) != 0;
            // the fork's ApplySpotlight3d: x/z from characterPosition,
            // y from position (the hang height); no additive anchor.
            Vector3 char_pos = Vector3.Lerp(a.character_position, b.character_position, blend);
            return (pos, rot, scale, color, power, active, char_pos);
        }
    }

    // one spotlight key.
    [Serializable]
    public class spot_key
    {
        public int frame;
        public int is_active;
        public Color color;
        public float color_power;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 character_position;
        public int character_index;
    }
}
