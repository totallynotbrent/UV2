// creates the urp assets if missing and wires them into the graphics settings
// so the project renders through the universal pipeline like the game expects.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UV2Build
{
    public static class urp_setup
    {
        public static void ensure()
        {
            // unity generates fresh assets with clean guids when none exist.
            var guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            UniversalRenderPipelineAsset pipe;
            if (guids.Length == 0)
            {
                // the menu wizard is the only creation path that wires every
                // resource reference (textures, shaders, post data) into the
                // assets; hand-rolled CreateInstance assets serialize with
                // null resource blocks and the player null-refs on them.
                AssetDatabase.Refresh();
                EditorApplication.ExecuteMenuItem("Assets/Create/Rendering/URP Asset (with Universal Renderer)");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            }
            pipe = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                AssetDatabase.GUIDToAssetPath(guids[0]));

            // the game's stage look: no hdr, soft main-light shadows, no msaa.
            pipe.supportsHDR = false;
            // the lod cross-fade dither textures live in a resource block we do
            // not ship; the serialized asset carries enableLODCrossFade=0.
            var pso = new SerializedObject(pipe);
            pso.FindProperty("m_EnableLODCrossFade").intValue = 0;
            pso.FindProperty("m_LODCrossFadeDitheringType").intValue = 0;
            pso.ApplyModifiedPropertiesWithoutUndo();
            pipe.msaaSampleCount = 1;
            pipe.shadowDistance = 100f;
            pipe.shadowCascadeCount = 2;

            // the global settings asset carries the shader resources the
            // pipeline dereferences every frame; Ensure() creates and links it.
            // the type is internal, so reach it through the pipeline's own
            // assembly by name.
            var settings_type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            var ensure = settings_type?.GetMethod("Ensure",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            ensure?.Invoke(null, new object[] { "Assets/Settings/", true });
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipe;
            AssetDatabase.SaveAssets();
            Debug.Log($"[urp_setup] pipeline assigned: {pipe.name}");
        }
    }
}
