using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UV2.App;

namespace UV2Build
{
    // one-shot batch entry: bake scenes, then build the linux player for the phase-1 gate.
    public static class batch_gate
    {
        public static void bake_and_build()
        {
            scene_baker.bake();
            player_builder.BuildWindows();
        }

        public static void bake_and_build_linux()
        {
            scene_baker.bake();
            var scenes = new[] { "Assets/Scenes/Picker.unity", "Assets/Scenes/Concert.unity" };
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/UV2",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[batch_gate] OK {report.summary.outputPath} ({report.summary.totalSize} bytes)");
                return;
            }
            Debug.LogError($"[batch_gate] FAILED {report.summary.result}");
            foreach (BuildStep step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError(msg.content);
                }
            }
            System.Environment.Exit(1);
        }
    }
}
