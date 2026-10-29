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
        int fps_frames;
        float fps_value;
        float fps_next_second;
        float fps_window_start;

        // the camera mode the bridge last applied; the launcher echoes it in
        // status.json so its radios stay in sync.
        int cam_mode_applied = -1;

        public void open(UV2.Live.stage_loader stage)
        {
            loader = stage;
            clock = stage.song_clock;
            fps_window_start = Time.unscaledTime;
            fps_next_second = fps_window_start + 1f;
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
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
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

                if (cmd.seek >= 0f)
                {
                    clock.seek(cmd.seek);
                    // a scrub is a teleport: re-seed every sim from the
                    // post-seek pose (the game's ResetNativeCloth path).
                    if (physics_director.instance != null)
                        physics_director.instance.reset_all_to_pose();
                }
                if (cmd.paused == 1) clock.pause();
                else clock.resume();
                // negatives are reverse playback: only a true zero is "no
                // command" (the window never sends one, but keep the guard).
                if (cmd.speed != 0f) clock.set_timescale(cmd.speed);
                // the retired physics controls: collisions and spring_rate
                // are accepted for launcher compatibility but no longer
                // drive anything (the v1 physics stack owns its tuning).
                if (cmd.voices >= 0 && vocal_mixer.voices_enabled != (cmd.voices == 1))
                {
                    vocal_mixer.voices_enabled = cmd.voices == 1;
                    trace_log.write($"control bridge: voices {(vocal_mixer.voices_enabled ? "on" : "off (mouth closed)")}");
                }
                if (cmd.camera_mode >= 0 && director != null)
                {
                    director.mode = (camera_mode)cmd.camera_mode;
                    cam_mode_applied = cmd.camera_mode;
                    trace_log.write($"control bridge: camera mode {cmd.camera_mode}");
                }
                apply_layers(cmd.layers, cmd.raw, cmd.tonemap);
                apply_light(cmd.light_freeze, cmd.light_yaw);
                apply_mix(cmd.bgm_gain, cmd.stems);
                trace_log.write($"control bridge: applied seq {cmd.seq} (seek {cmd.seek:0.0} paused {cmd.paused} speed {cmd.speed:0.00} col {cmd.collisions} rate {cmd.spring_rate:0.00} cam {cmd.camera_mode})");
            }
            catch { }
        }

        // the launcher owns the post processing switches, so the player
        // takes them as a command instead of drawing its own panel.
        // layers is one character per layer in the fixed order below: 1
        // leaves the song's own choice alone, 0 silences the layer. the
        // sun shafts slot trails the list.
        static readonly char[] layer_order =
        {
            'b', 'd', 'f', 't', 'g', 'r', 'l', 'c', 'o'
        };

        void apply_layers(string layers, int raw, int tonemap)
        {
            if (!string.IsNullOrEmpty(layers) && layers.Length >= layer_order.Length)
            {
                for (int i = 0; i < layer_order.Length; i++)
                    apply_layer(layer_order[i], layers[i]);
                // sun shafts trail the layer list.
                if (layers.Length > layer_order.Length)
                    apply_layer('s', layers[layer_order.Length]);
            }
            if (raw == 1)
            {
                // raw render strips the whole chain, so the picture shows
                // only what the song's geometry draws.
                foreach (char letter in layer_order) UV2.Live.fog_fade_feature.force_off.Add(letter);
                UV2.Live.fog_fade_feature.force_off.Add('s');
                UV2.Live.fog_fade_feature.force_on.Clear();
                UV2.Live.fog_fade_feature.tonemap_on = false;
            }
            else if (raw == 0)
            {
                UV2.Live.fog_fade_feature.tonemap_on = tonemap != 0;
            }
        }

        // one layer state character: 0 leaves the song's own choice alone,
        // 1 silences the layer, 2 forces it on.
        void apply_layer(char letter, char state)
        {
            if (state == '1')
            {
                UV2.Live.fog_fade_feature.force_off.Add(letter);
                UV2.Live.fog_fade_feature.force_on.Remove(letter);
            }
            else if (state == '2')
            {
                UV2.Live.fog_fade_feature.force_on.Add(letter);
                UV2.Live.fog_fade_feature.force_off.Remove(letter);
            }
            else
            {
                UV2.Live.fog_fade_feature.force_off.Remove(letter);
                UV2.Live.fog_fade_feature.force_on.Remove(letter);
            }
        }

        void apply_light(int freeze, float yaw)
        {
            if (loader == null) return;
            var light = loader.global_light_ref;
            if (light == null) return;
            if (freeze >= 0) light.set_frozen(freeze == 1);
            if (yaw > -900f) light.set_light_yaw(yaw);
        }

        void apply_mix(float bgm_gain, string stems)
        {
            if (loader == null) return;
            if (bgm_gain >= 0f) loader.set_background_volume(bgm_gain);
            if (string.IsNullOrEmpty(stems)) return;
            var mixer = loader.vocal_mixer_ref;
            if (mixer == null) return;
            foreach (string part in stems.Split(';'))
            {
                string[] bits = part.Split(':');
                if (bits.Length != 3) continue;
                if (!int.TryParse(bits[0], out int group)) continue;
                if (!float.TryParse(bits[2], out float gain)) continue;
                mixer.set_stem_gain(group, gain);
            }
        }

        // how many standing places have a live light state, so the
        // diagnostics page can show it.
        int lit_slot_count()
        {
            if (loader == null || loader.global_light_ref == null) return 0;
            int count = 0;
            foreach (var state in loader.global_light_ref.slot_states)
                if (state.valid) count++;
            return count;
        }

        // raw render silences every layer at once.
        bool all_layers_off()
        {
            foreach (char letter in layer_order)
                if (!UV2.Live.fog_fade_feature.force_off.Contains(letter)) return false;
            return true;
        }

        // which post processing layers are silenced right now.
        string layer_state()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (char letter in layer_order)
                if (UV2.Live.fog_fade_feature.force_off.Contains(letter)) parts.Add(letter.ToString());
            if (UV2.Live.fog_fade_feature.force_off.Contains('s')) parts.Add("s");
            if (!UV2.Live.fog_fade_feature.tonemap_on) parts.Add("tm");
            return string.Join(",", parts);
        }

        // the singer's stems, so the launcher can build a slider per voice.
        string stem_list()
        {
            if (loader == null) return "";
            var mixer = loader.vocal_mixer_ref;
            if (mixer == null) return "";
            var groups = mixer.stem_groups();
            if (groups.Count == 0) return "";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var (group, chara_id) in groups)
            {
                string name = loader.chara_display_name(chara_id).Replace(";", ",").Replace(":", ",");
                parts.Add(group + ":" + chara_id + ":" + name);
            }
            return string.Join(";", parts);
        }

        // republishes status.json when a field changes; the launcher polls it.
        void publish_status()
        {
            try
            {
                // fps: the rendered frames in the window that just closed,
                // divided by the length of that window. the old maths mixed
                // the boundary timestamps together, so the number it showed
                // was neither the instantaneous nor the averaged rate.
                fps_frames++;
                if (Time.unscaledTime >= fps_next_second)
                {
                    float window = Time.unscaledTime - fps_window_start;
                    if (window > 0.001f) fps_value = fps_frames / window;
                    fps_frames = 0;
                    fps_window_start = Time.unscaledTime;
                    fps_next_second = Time.unscaledTime + 1f;
                }
                var s = new control_status
                {
                    time = clock.time,
                    length = loader.music_source != null && loader.music_source.clip != null
                        ? loader.music_source.clip.length : 0f,
                    paused = clock.paused ? 1 : 0,
                    camera_mode = director != null ? (int)director.mode : cam_mode_applied,
                    loading = load_progress.current,
                    opened = loader.opened ? 1 : 0,
                    voices = vocal_mixer.voices_enabled ? 1 : 0,
                    fps = fps_value,
                    stems = stem_list(),
                    bgm_gain = loader != null ? loader.background_level : 1f,
                    chara_count = loader != null ? loader.characters.Count : 0,
                    lit_count = lit_slot_count(),
                    layers = layer_state(),
                    frozen = loader != null && loader.global_light_ref != null && loader.global_light_ref.is_frozen ? 1 : 0,
                    yaw = loader != null && loader.global_light_ref != null ? loader.global_light_ref.yaw_bias : 0f,
                    raw = all_layers_off() ? 1 : 0,
                    version = Application.version,
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
            public float spring_rate = -1f;
            public int camera_mode = -1;
            public int voices = -1;
            public string layers = "";
            public int raw = -1;
            public int tonemap = -1;
            public int light_freeze = -1;
            public float light_yaw = -999f;
            public float bgm_gain = -1f;
            public string stems = "";
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
            public int voices = 1;
            public float fps;
            public string stems = "";
            public float bgm_gain = 1f;
            public int chara_count;
            public int lit_count;
            public string layers = "";
            public int frozen;
            public float yaw;
            public int raw;
            public string version = "";
        }
    }
}
