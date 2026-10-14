using System;
using System.IO;
using UnityEngine;
using UV2.App;
using UV2.Live;

namespace UV2.UI
{
    // file bridge to the launcher's external control window. the launcher
    // writes control.json next to the exe; this component polls it a few
    // times a second, applies each command once (a monotonically increasing
    // sequence number gates re-application), and publishes status.json for
    // the launcher to read (song time, length, pause state, camera mode).
    public class control_bridge : MonoBehaviour
    {
        timeline_clock clock;
        UV2.Live.stage_loader loader;
        camera_director director;

        string control_path;
        string status_path;
        string last_status;
        float next_poll;
        long last_seq = -1;

        // the camera mode the bridge last applied; the launcher echoes it in
        // status.json so its radios stay in sync.
        int cam_mode_applied = -1;

        public void open(UV2.Live.stage_loader stage)
        {
            loader = stage;
            clock = stage.song_clock;
            director = UnityEngine.Object.FindObjectOfType<camera_director>();
            string dir = System.AppDomain.CurrentDomain.BaseDirectory;
            control_path = Path.Combine(dir, "control.json");
            status_path = Path.Combine(dir, "status.json");
            trace_log.write($"control bridge: polling {control_path}");
            maybe_spawn_control_window(dir);
        }

        // a direct UV2.exe run has no launcher alive to open the external
        // control window; the player asks the bundled launcher (attach mode)
        // to open one against this folder so the controls exist no matter
        // which exe was started. a launcher-launched player skips this: its
        // launcher is alive and owns the window.
        void maybe_spawn_control_window(string dir)
        {
            try
            {
                // the launcher deletes control.json before starting the
                // player and the control window writes it on the first user
                // action; a file present this early means a launcher owns it.
                if (File.Exists(control_path)) return;
                string launcher = Path.Combine(dir, "UmaLauncher.exe");
                if (!File.Exists(launcher)) return;
                if (started_by_launcher(dir))
                {
                    trace_log.write("control bridge: launcher parent detected, skipping attach spawn");
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = $"--control \"{dir}\"",
                    UseShellExecute = false,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                trace_log.write($"control bridge: spawned attach-mode control window ({launcher} --control)");
            }
            catch (Exception e)
            {
                trace_log.write($"control bridge: control window spawn failed: {e.Message}");
            }
        }

        // true when the launcher started this player (its exe name appears
        // in our parent chain). windows builds ship without System.Management
        // so walk the chain via toolhelp-free heuristic: the launcher writes
        // its own pid marker next to the exe right before it starts us.
        bool started_by_launcher(string dir)
        {
            try
            {
                string marker = Path.Combine(dir, "launcher.pid");
                if (!File.Exists(marker)) return false;
                if (!long.TryParse(File.ReadAllText(marker).Trim(), out long pid)) return false;
                var launcher_proc = System.Diagnostics.Process.GetProcessById((int)pid);
                return launcher_proc != null && !launcher_proc.HasExited;
            }
            catch
            {
                // ArgumentException from GetProcessById = the pid is gone;
                // a stale marker belongs to a dead launcher.
                return false;
            }
        }

        void Update()
        {
            if (clock == null) return;
            if (Time.realtimeSinceStartup < next_poll) return;
            next_poll = Time.realtimeSinceStartup + 0.15f;

            poll_control();
            publish_status();
        }

        // applies one pending command block when its sequence number advances.
        // the launcher always writes every field, so missing-field defaults
        // from the parser can never invent a command.
        void poll_control()
        {
            try
            {
                if (!File.Exists(control_path)) return;
                string json = File.ReadAllText(control_path);
                var cmd = JsonUtility.FromJson<control_command>(json);
                if (cmd.seq <= last_seq) return;
                last_seq = cmd.seq;

                if (cmd.seek >= 0f) clock.seek(cmd.seek);
                if (cmd.paused == 1) clock.pause();
                else clock.resume();
                if (cmd.speed > 0f) clock.set_timescale(cmd.speed);
                physics_director.collisions_enabled = cmd.collisions == 1;
                if (cmd.spring_rate > 0f && physics_director.instance != null)
                    foreach (var sim in physics_director.instance.all_sims())
                        physics_director.instance.set_spring_rate(sim, spring_sim.part.head, cmd.spring_rate);
                if (cmd.camera_mode >= 0 && director != null)
                {
                    director.mode = (camera_mode)cmd.camera_mode;
                    cam_mode_applied = cmd.camera_mode;
                    trace_log.write($"control bridge: camera mode {cmd.camera_mode}");
                }
                trace_log.write($"control bridge: applied seq {cmd.seq} (seek {cmd.seek:0.0} paused {cmd.paused} speed {cmd.speed:0.00} col {cmd.collisions} rate {cmd.spring_rate:0.00} cam {cmd.camera_mode})");
            }
            catch { }
        }

        // republishes status.json when a field changes; the launcher polls it.
        void publish_status()
        {
            try
            {
                var s = new control_status
                {
                    time = clock.time,
                    length = loader.music_source != null && loader.music_source.clip != null
                        ? loader.music_source.clip.length : 0f,
                    paused = clock.paused ? 1 : 0,
                    camera_mode = director != null ? (int)director.mode : cam_mode_applied,
                    loading = load_progress.current,
                    opened = loader.opened ? 1 : 0,
                };
                string json = JsonUtility.ToJson(s);
                if (json == last_status) return;
                last_status = json;
                File.WriteAllText(status_path, json);
            }
            catch { }
        }

        [System.Serializable]
        class control_command
        {
            public long seq;
            public float seek = -1f;
            public int paused = -1;
            public float speed;
            public int collisions = -1;
            public float spring_rate;
            public int camera_mode = -1;
        }

        [System.Serializable]
        class control_status
        {
            public float time;
            public float length;
            public int paused;
            public int camera_mode;
            public string loading;
            public int opened;
        }
    }
}
