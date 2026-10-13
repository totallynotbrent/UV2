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
