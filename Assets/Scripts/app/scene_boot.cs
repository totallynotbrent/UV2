using System;
using System.Collections;
using UnityEngine;

namespace UV2.App
{
    // late scene boot: dispatches to the concert bootstrap after the scene loads.
    public class scene_boot : MonoBehaviour
    {
        public string type_name;
        public string method_name;

        private IEnumerator Start()
        {
            var pre_args = System.Environment.GetCommandLineArgs();
            bool dump_icons = false;
            bool dump_shaders = false;
            bool pose_probe = false;
            foreach (var a in pre_args)
            {
                if (a == "-dumpicons") dump_icons = true;
                if (a == "-dumpshaders") dump_shaders = true;
                if (a == "-uv2poseprobe") pose_probe = true;
            }

            trace_log.open();
            trace_log.write($"args: {string.Join(" ", System.Environment.GetCommandLineArgs())}");

            if (pose_probe)
            {
                pose_probe_runner.run();
                Application.Quit();
                yield break;
            }

            if (dump_shaders)
            {
                scene_bootstrap.dump_shader_map();
                Application.Quit();
                yield break;
            }

            bool probe_cutt = false;
            foreach (var a in pre_args) if (a == "-probecutt") probe_cutt = true;
            if (probe_cutt)
            {
                scene_bootstrap.probe_cutt_binding();
                Application.Quit();
                yield break;
            }
            if (dump_icons)
            {
                scene_bootstrap.dump_icons();
                Application.Quit();
                yield break;
            }

            if (method_name == "build_concert_scene") scene_bootstrap.build_concert_scene();
            else Debug.LogError($"[scene_boot] unknown bootstrap: {method_name}");

            // headless e2e evidence: dump the rendered frame after the ui settles.
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-uv2shot")
                {
                    int delay = int.TryParse(args[i + 1], out var d) ? d : 3;
                    for (int f = 0; f < delay; f++) yield return null;
                    string shot_dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "e2e_shots");
                    System.IO.Directory.CreateDirectory(shot_dir);
                    string shot_path = System.IO.Path.Combine(shot_dir, $"{method_name}_{System.DateTime.Now:HHmmss}.png");
                    ScreenCapture.CaptureScreenshot(shot_path);
                    Debug.Log($"[scene_boot] screenshot requested: {shot_path}");
                    yield return new WaitForSeconds(1.5f);
                    Debug.Log($"[scene_boot] screenshot write complete: {System.IO.File.Exists(shot_path)} {shot_path}");
                }
            }
            Destroy(gameObject);
        }
    }
}
