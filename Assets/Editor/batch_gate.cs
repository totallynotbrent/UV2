using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UV2.App;

namespace UV2Build
{
    // one-shot batch entry: bake scenes, then build the windows player.
    // windows-only: the game's shader blobs are D3D-compiled, so a linux
    // player renders every game material as the magenta fallback and
    // reports false for HasProperty - it can never verify visuals.
    public static class batch_gate
    {
        public static void bake_and_build()
        {
            urp_setup.ensure();
            scene_baker.bake();
            player_builder.apply_icon();
            var scenes = new[] { "Assets/Scenes/Concert.unity" };
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
