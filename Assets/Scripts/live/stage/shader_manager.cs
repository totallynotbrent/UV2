using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.Data;

namespace UV2.Live
{
    // loads the game's own shader bundle first so the externals in character and
    // stage material bundles resolve to real shaders instead of the magenta fallback.
    public static class shader_manager
    {
        private static AssetBundle bundle;

        // opens the shader bundle once; call before loading any other game bundle.
        public static bool ensure_loaded(meta_reader.asset_row row, string data_root)
        {
            if (bundle != null) return true;
            if (row == null)
            {
                Debug.LogWarning("[shader_manager] no meta row for the shader bundle");
                return false;
            }
            bundle = game_assets.open(row, data_root);
            if (bundle == null)
            {
                Debug.LogWarning("[shader_manager] shader bundle open failed");
                return false;
            }
            Debug.Log($"[shader_manager] shader bundle loaded: {bundle.GetAllAssetNames().Length} shaders");
            return true;
        }

        // maps a game shader name to the loaded shader, for manual reassignment.
        public static Shader find(string shader_name)
        {
            if (bundle == null) return null;
            if (!bundle.Contains(shader_name)) return null;
            return bundle.LoadAsset<Shader>(shader_name);
        }

        // the offline-built material -> shader asset path map (datapack).
        private static Dictionary<string, string> mat_map;

        // fixes a hierarchy's materials by reassigning the shader the game intended,
        // using the datapack map. returns the number of materials fixed.
        public static int fix_game_shaders(Transform root, string context)
        {
            if (bundle == null || mat_map == null) return 0;
            int fixed_count = 0, unknown = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    // log what shader the material carries so mismatches are visible.
                    if (m.shader != null && m.shader.name != "Hidden/InternalErrorShader")
                        Debug.Log($"[shader_manager] {context}: material {m.name} already on {m.shader.name}");
                    if (!mat_map.TryGetValue(m.name, out var shader_path)) { unknown++; continue; }
                    var sh = bundle.Contains(shader_path) ? bundle.LoadAsset<Shader>(shader_path) : null;
                    if (sh == null) { unknown++; continue; }
                    var fixed_mat = new Material(sh);
                    fixed_mat.name = m.name;
                    // copy the game material's texture/color properties across.
                    copy_properties(m, fixed_mat);
                    mats[i] = fixed_mat;
                    changed = true;
                    fixed_count++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            Debug.Log($"[shader_manager] {context}: {fixed_count} materials reshaded, {unknown} unknown");
            return fixed_count;
        }

        // logs every material's live shader so fallback coverage is auditable.
        public static void audit_shaders(Transform root, string context)
        {
            int ok = 0, fallback = 0, nullmat = 0;
            var names = new System.Text.StringBuilder();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { nullmat++; continue; }
                    if (m.shader == null) { fallback++; names.Append($" {m.name}=NULLSHADER"); continue; }
                    if (m.shader.name == "Hidden/InternalErrorShader") { fallback++; names.Append($" {m.name}=ERROR"); }
                    else { ok++; names.Append($" {m.name}={m.shader.name}"); }
                }
            }
            Debug.Log($"[shader_manager] {context}: {ok} ok, {fallback} fallback, {nullmat} null");
            foreach (var chunk in split_log(names.ToString(), 900))
                Debug.Log($"[shader_manager] {context} materials: {chunk}");
        }

        // splits long audit lines into loggable chunks.
        private static System.Collections.Generic.IEnumerable<string> split_log(string s, int size)
        {
            for (int i = 0; i < s.Length; i += size)
                yield return s.Substring(i, System.Math.Min(size, s.Length - i));
        }

        // copies settable texture/color/float properties from the game material.
        private static void copy_properties(Material from, Material to)
        {
            var src_en = from.shader;
            var dst_shader = to.shader;
            if (src_en == null || dst_shader == null) return;
            int n = from.shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string prop = from.shader.GetPropertyName(i);
                var type = from.shader.GetPropertyType(i);
                try
                {
                    switch (type)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            var tex = from.GetTexture(prop);
                            if (tex != null) to.SetTexture(prop, tex);
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            to.SetColor(prop, from.GetColor(prop));
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            to.SetFloat(prop, from.GetFloat(prop));
                            break;
                    }
                }
                catch { }
            }
            // fallback properties the old shaders commonly use.
            var common = new[] { "_MainTex", "_Color", "_Main2Tex", "_Cutoff" };
            foreach (var prop in common)
            {
                try
                {
                    if (to.HasProperty(prop))
                    {
                        if (from.HasProperty(prop))
                        {
                            if (from.GetTexture(prop) != null) to.SetTexture(prop, from.GetTexture(prop));
                            if (from.HasColor(prop)) to.SetColor(prop, from.GetColor(prop));
                        }
                    }
                }
                catch { }
            }
        }

        // loads the material map from the datapack sidecar.
        public static bool load_map()
        {
            if (mat_map != null) return true;
            string path = System.IO.Path.Combine(UV2.App.config.datapack_path, "material_shader_map.json");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "data", "material_shader_map.json");
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[shader_manager] no material map at {path}");
                return false;
            }
            var text = System.IO.File.ReadAllText(path);
            var parsed = MiniJson.Parse(text) as Dictionary<string, object>;
            if (parsed == null)
            {
                Debug.LogWarning("[shader_manager] material map parse failed");
                return false;
            }
            mat_map = new Dictionary<string, string>();
            foreach (var kv in parsed)
                mat_map[kv.Key] = kv.Value as string;
            Debug.Log($"[shader_manager] material map loaded: {mat_map.Count} entries");
            return true;
        }
    }
}
