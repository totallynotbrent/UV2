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
            bool free_clock = false;
            foreach (var a in pre_args)
            {
                if (a == "-dumpicons") dump_icons = true;
                if (a == "-dumpshaders") dump_shaders = true;
                if (a == "-uv2poseprobe") pose_probe = true;
                if (a == "-uv2freeclock") free_clock = true;
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

            // frame-perfect comparison mode: -uv2frame <seconds> seeks the
            // song clock after the concert opens, settles one frame, and
            // writes a named png. pairs with the user's exe frames so
            // both machines capture the same song time.
            var frame_args = System.Environment.GetCommandLineArgs();
            float seek_s = -1f;
            for (int i = 0; i < frame_args.Length - 1; i++)
                if (frame_args[i] == "-uv2frame" && float.TryParse(frame_args[i + 1], out var s)) seek_s = s;

            if (method_name == "build_concert_scene")
            {
                scene_bootstrap.build_concert_scene(free_clock);

                if (seek_s >= 0f)
                {
                    var loader = UnityEngine.Object.FindObjectOfType<UV2.Live.stage_loader>();
                    // wait for the open to finish, then seek and settle.
                    float open_deadline = Time.realtimeSinceStartup + 90f;
                    while (loader != null && !loader.opened && Time.realtimeSinceStartup < open_deadline)
                        yield return null;
                    if (loader != null && loader.song_clock != null)
                    {
                        loader.song_clock.pause();
                        loader.song_clock.seek(seek_s);
                        trace_log.write($"frame mode: seeked clock to {seek_s:0.00}s");
                        for (int f = 0; f < 3; f++) yield return null;
                    }
                }

                // benchmark mode: -uv2bench <song_seconds> plays the concert
                // and quits once the song clock passes the stop point, so the
                // mask sweep script can step through configurations unattended.
                float bench_stop = -1f;
                for (int i = 0; i < pre_args.Length - 1; i++)
                    if (pre_args[i] == "-uv2bench" && float.TryParse(pre_args[i + 1], out var bs)) bench_stop = bs;
                if (bench_stop >= 0f)
                {
                    // re-find the loader every frame: the concert window
                    // spawns it after the boot ui, so a single up-front
                    // Find can return null while the loader doesn't exist
                    // yet and silently fall into the quit path.
                    UV2.Live.stage_loader loader = null;
                    float open_deadline = Time.realtimeSinceStartup + 180f;
                    while (Time.realtimeSinceStartup < open_deadline)
                    {
                        if (loader == null)
                            loader = UnityEngine.Object.FindObjectOfType<UV2.Live.stage_loader>();
                        if (loader != null && loader.opened && loader.song_clock != null) break;
                        yield return null;
                    }
                    if (loader != null && loader.opened && loader.song_clock != null)
                    {
                        trace_log.write($"bench: playing to {bench_stop:0.0}s then quitting");
                        while (loader.song_clock.time < bench_stop) yield return null;
                        trace_log.write($"bench: reached {loader.song_clock.time:0.0}s, quitting");
                        // give the last capture a moment to flush to disk.
                        yield return new WaitForSeconds(2f);
                        Application.Quit();
                        yield break;
                    }
                    trace_log.write($"bench: song clock never opened within 180s, quitting (loader null: {loader == null})");
                    Application.Quit();
                    yield break;
                }
            }
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
                    // frame mode names the shot with the seeked song time so
                    // captures from both machines pair by filename.
                    var sel_now = UV2.App.selection_store.load();
                    string shot_path = System.IO.Path.Combine(shot_dir,
                        seek_s >= 0f && sel_now != null
                            ? $"{method_name}_{sel_now.music_id}_{seek_s:0.0}s.png"
                            : $"{method_name}_{System.DateTime.Now:HHmmss}.png");
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
