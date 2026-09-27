using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UV2Build
{
    public static class player_builder
    {
        public static void BuildWindows()
        {
            var scenes = new[] { "Assets/Scenes/Concert.unity" };
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/UV2.exe",
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
