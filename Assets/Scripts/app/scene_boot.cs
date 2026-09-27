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
