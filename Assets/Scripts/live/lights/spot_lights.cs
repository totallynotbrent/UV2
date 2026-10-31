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

        // the fork's per-fixture caches: one dedicated beam light + a
        // one-shot material tint (Director 2307-2326: later keys with new
        // colors never re-tint - the cache is per GameObject).
        private static readonly Dictionary<string, Light> fixture_beams = new();

        // binds the containers from the spotlight tracks against the recorded stage children.
        public static void bind(List<spot_track_container> tracks)
        {
            containers.Clear();
            if (tracks == null) return;
            int resolved = 0;
            var missing = new List<string>();
            // the worksheet authors several spotlight containers that name
            // the same fixture ('1st : spotlight3d002', '2nd : ...', '3rd :
            // ...'), and each one is a separate lamp on the rig. handing
            // them one shared object let the last track overwrite the
            // earlier ones, so every track takes its own instance.
            var used = new HashSet<int>();
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
                if (!used.Add(go.GetInstanceID()))
                    go = duplicate_instance(go, t.name);
                var c = new spot_container { root = go.transform, mpb = new MaterialPropertyBlock() };
                foreach (var l in go.GetComponentsInChildren<Light>(true)) c.lights.Add(l);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) c.renderers.Add(r);
                containers[t.name] = c;
                resolved++;
            }
            trace_log.write($"spot lights: {resolved} containers resolved, {missing.Count} unresolved");
            foreach (var m in missing) trace_log.write($"spot light unresolved: {m}");
            // the stage prefab is instantiated once at the root and its
            // stage objects are copied again under stage_geometry, so every
            // fixture exists twice. the copy we drive is the one that
            // carries the authored colour; the twin keeps its prefab
            // appearance and stays lit, painting an extra white beam.
            // parking the twins removes that second beam.
            if (uv2_ab_flags.get_bool("spot_dedupe", false)) cull_duplicates();
            // the bench can park any renderer population by name fragment,
            // so a suspect group can be ablated without a rebuild.
            hide_by_name_flag();
            // dumps what each fixture's renderers actually carry, so a wrong
            // shader or a missing property shows up in the trace rather than
            // as a dull beam nobody can explain.
            foreach (var kv in containers)
            {
                var c = kv.Value;
                if (c?.root == null) continue;
                var rb = new System.Text.StringBuilder();
                foreach (var r in c.renderers)
                {
                    if (r == null) continue;
                    rb.Append("[render ").Append(r.name).Append(" mats ");
                    foreach (var mm in r.sharedMaterials)
                    {
                        if (mm == null) { rb.Append("NULL "); continue; }
                        rb.Append(mm.name).Append('<').Append(mm.shader != null ? mm.shader.name : "NOSHADER");
                        if (mm.HasProperty("_ColorPower"))
                            rb.Append(" pow=").Append(mm.GetFloat("_ColorPower"));
                        if (mm.HasProperty("_MulColor0"))
                            rb.Append(" mul=").Append(mm.GetColor("_MulColor0"));
                        if (mm.HasProperty("_Color"))
                            rb.Append(" col=").Append(mm.GetColor("_Color"));
                        if (mm.HasProperty("_Cutoff"))
                            rb.Append(" cut=").Append(mm.GetFloat("_Cutoff"));
                        rb.Append("] ");
                    }
                }
                trace_log.write($"spot fixture {kv.Key} renderers {c.renderers.Count} lights {c.lights.Count} {rb}");
            }

        }

        // parks every renderer whose hierarchy name contains the fragment
        // named by the hide_fragment flag.
        private static void hide_by_name_flag()
        {
            string frag = uv2_ab_flags.get_string("hide_fragment", "");
            if (string.IsNullOrEmpty(frag)) return;
            var all = Resources.FindObjectsOfTypeAll<Renderer>();
            int hid = 0;
            foreach (var r in all)
            {
                if (r == null) continue;
                string path = r.name;
                var t = r.transform;
                for (int i = 0; i < 4 && t != null; i++)
                {
                    path += "|" + t.name;
                    t = t.parent;
                }
                if (path.IndexOf(frag, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                r.enabled = false;
                hid++;
            }
            trace_log.write($"hide_fragment '{frag}': disabled {hid} renderers");
        }

        // parks the duplicate copies of every bound fixture, keeping the one
        // the track drives.
        private static void cull_duplicates()
        {
            var driven = new HashSet<int>();
            foreach (var c in containers.Values)
                if (c?.root != null) driven.Add(c.root.gameObject.GetInstanceID());
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            int parked = 0;
            foreach (var go in all)
            {
                if (go == null || driven.Contains(go.GetInstanceID())) continue;
                if (string.IsNullOrEmpty(go.name)) continue;
                bool is_fixture = false;
                foreach (var c in containers.Values)
                {
                    if (c?.root == null) continue;
                    var base_name = c.root.name;
                    int colon = base_name.IndexOf(" : ");
                    if (colon > 0) base_name = base_name.Substring(0, colon);
                    if (go.name.StartsWith(base_name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        is_fixture = true;
                        break;
                    }
                }
                if (!is_fixture) continue;
                if (!go.activeSelf) continue;
                go.SetActive(false);
                parked++;
            }
            trace_log.write($"spot dedupe: parked {parked} duplicate fixture copies");
        }

        // clones a fixture so a second track can drive its own lamp.
        private static readonly List<GameObject> spawned_instances = new();

        private static GameObject duplicate_instance(GameObject source, string owner)
        {
            if (source == null) return null;
            var clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
            clone.name = $"{source.name}_{owner}";
            // the game parks a fixture until its key turns it on.
            clone.SetActive(false);
            spawned_instances.Add(clone);
            trace_log.write($"spot light: cloned fixture '{clone.name}' for {owner}");
            return clone;
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

        // clears the per-fixture caches (a new song rebinds).
        public static void reset()
        {
            foreach (var beam in fixture_beams.Values)
                if (beam != null) UnityEngine.Object.Destroy(beam.gameObject);
            fixture_beams.Clear();
            foreach (var inst in spawned_instances)
                if (inst != null) UnityEngine.Object.Destroy(inst);
            spawned_instances.Clear();
            containers.Clear();
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
                // the fork's hang math ONLY: x/z from characterPosition, y
                // from position; the key's rotation is never applied to the
                // fixture (Director 2287-2299).
                c.root.position = new Vector3(char_pos.x, position.y, char_pos.z);
                c.root.localScale = scale;

                // the authored colour and power ride the fixture's own
                // material inputs. the fixture material is
                // gallop/3d/live/stage/lightadd1, whose inputs are _MulColor0
                // and _ColorPower; the spot key's colour and power were
                // being written to _Color and _EmissionColor instead, which
                // that shader never reads, so every beam drew white.
                //
                // the fixture material authors _colorpower=1 and
                // _mulcolor0=white, so the game drives the beam purely
                // through the colour vector at full strength. the authored
                // track colour replaces that white, scaled by the authored
                // power so a dimmer key throws a weaker beam.
                //
                // this is reapplied every frame because the track colour
                // blends over time; applying it once left the beam holding
                // its opening colour.
                {
                    if (c.mpb == null) c.mpb = new MaterialPropertyBlock();
                    // the authored power ranges roughly 0.3 to 0.44, so the
                    // level is normalised against that range and the gain
                    // brings it to the game's apparent strength.
                    float gain = uv2_ab_flags.get_float("spot_color_gain", 0.45f);
                    float level = (power / 0.4f) * gain;
                    c.mpb.SetColor(id_mul_color0, color * level);
                    c.mpb.SetColor(id_mul_color1, color * level);
                    c.mpb.SetFloat(id_color_power, 1f);
                    c.mpb.SetFloat(id_color_power_multiply, 1f);
                    foreach (var r in c.renderers) r.SetPropertyBlock(c.mpb);
                }
                // the cone mesh arrives at its authored size, but the
                // fixture's own driver script is absent from this build, so
                // nothing widens the beam. the bench can grow the cone.
                float width = uv2_ab_flags.get_float("spot_width", 1f);
                if (!Mathf.Approximately(width, 1f))
                    c.root.localScale = new Vector3(scale.x * width, scale.y, scale.z);
                // the beam faces the camera because the fixture's own
                // gallop.live.billboardcontroller turns it to do so, and
                // that script is absent from this build. leaving the cone
                // at its authored yaw draws it edge-on as a needle.
                if (uv2_ab_flags.get_bool("spot_billboard", true))
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var to_cam = cam.transform.position - c.root.position;
                        to_cam.y = 0f;
                        if (to_cam.sqrMagnitude > 0.0001f)
                            c.root.rotation = Quaternion.LookRotation(to_cam.normalized, Vector3.up);
                    }
                }

                // the dedicated beam: one Spot light per fixture (34deg,
                // range 30, shadows none), intensity floor 1.2 with the 4x
                // power scale, aimed at the character slot at height 1.2
                // (DriveRealSpotlight 2330-2368).
                if (!fixture_beams.TryGetValue(t.name, out var beam) || beam == null)
                {
                    var host = new GameObject($"spotlight_beam_{t.name}");
                    host.transform.SetParent(c.root, false);
                    beam = host.AddComponent<Light>();
                    beam.type = LightType.Spot;
                    beam.spotAngle = 34f;
                    beam.range = 30f;
                    beam.shadows = LightShadows.None;
                    fixture_beams[t.name] = beam;
                }
                beam.color = new Color(color.r, color.g, color.b, 1f);
                // drop the inner power floor: max(1.2, 4*max(power, 0.35))
                // made 1.4 the lowest possible intensity even when the key
                // authored power 0 (off). with the floor at 0 the true
                // minimum is 1.2, which is what the decoded comment says.
                beam.intensity = Mathf.Max(1.2f, 4f * Mathf.Max(power, 0f))
                                * uv2_ab_flags.get_float("beam_gain", 1f);
                beam.enabled = active;
                var aim = new Vector3(char_pos.x, 1.2f, char_pos.z);
                beam.transform.rotation = Quaternion.LookRotation(aim - beam.transform.position);
            }
        }

        private static readonly int id_color = Shader.PropertyToID("_Color");
        private static readonly int id_emission_color = Shader.PropertyToID("_EmissionColor");
        private static readonly int id_mul_color0 = Shader.PropertyToID("_MulColor0");
        private static readonly int id_mul_color1 = Shader.PropertyToID("_MulColor1");
        private static readonly int id_color_power = Shader.PropertyToID("_ColorPower");
        private static readonly int id_color_power_multiply = Shader.PropertyToID("_ColorPowerMultiply");
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
            // the fork publishes the CUR key's isActive verbatim (walker
            // 5165) - no blend midpoint pick.
            bool active = a.is_active != 0;
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
