using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;

namespace UV2Build
{
    public static class player_builder
    {
        // the player icon, set whenever the editor loads the project so
        // every build path carries it, not just the ones that call this.
        [InitializeOnLoadMethod]
        private static void set_icon_on_load()
        {
            EditorApplication.delayCall += () => apply_icon(false);
        }

        // the window and file icon, baked into the player settings so every
        // build carries it.
        public static void apply_icon(bool reimport = true)
        {
            // unity wants one texture per icon size, smallest first.
            int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
            var icons = new System.Collections.Generic.List<Texture2D>();
            foreach (int size in sizes)
            {
                string path = $"Assets/Icons/Player/icon_{size}.png";
                if (reimport && AssetImporter.GetAtPath(path) is TextureImporter imp)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.mipmapEnabled = false;
                    imp.filterMode = FilterMode.Point;
                    imp.SaveAndReimport();
                }
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (icon != null) icons.Add(icon);
            }
            if (icons.Count == 0) return;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons.ToArray(), IconKind.Any);
        }

        public static void BuildWindows()
        {
            var scenes = new[] { "Assets/Scenes/Concert.unity" };
            apply_icon();
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/UmaLive.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[player_builder] OK {report.summary.outputPath} ({report.summary.totalSize} bytes)");
                return;
            }
            Debug.LogError($"[player_builder] FAILED {report.summary.result}");
            foreach (BuildStep step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError(msg.content);
                }
            }
        }
    }
}
