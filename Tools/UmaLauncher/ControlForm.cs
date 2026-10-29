using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace UmaLauncher
{
    // external control window for a running concert: the player polls a
    // control.json command file and publishes status.json next to its exe;
    // this form is the writer/reader side. opens when the launcher starts a
    // concert and closes when the player exits.
    public class ControlForm : Form
    {
        private readonly string dir;
        private readonly Process? player;
        private readonly bool owns_player;
        private readonly System.Windows.Forms.Timer poll = new() { Interval = 250 };

        private readonly TrackBar seek = new() { Minimum = 0, Maximum = 1000, TickFrequency = 0, Height = 40, Dock = DockStyle.Fill };
        private readonly Label time_label = new() { Text = "0:00 / 0:00", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
        private readonly Button pause = new() { Text = "pause", Width = 90, Height = 32 };
        // fine speed control: 0.01 steps via arrows or the mouse wheel once
        // focused, range [-3, 3] so reverse playback is reachable (the
        // player clamps the same range on its side).
        private readonly NumericUpDown speed = new() { DecimalPlaces = 2, Increment = 0.01m, Minimum = -3m, Maximum = 3m, Value = 1.00m, Width = 76 };
        private readonly ComboBox camera = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly CheckBox voices = new() { Text = "voices", AutoSize = true, Checked = true };
        private readonly Label fps_label = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
        private readonly Label loading_label = new() { AutoSize = true, ForeColor = Color.DimGray };

        private long seq;
        private bool dragging;
        private float status_time, status_length;
        private int status_paused = -1, status_cam = -1;
        private bool exited;

        public ControlForm(string base_dir, Process? player_proc, bool owns = true)
        {
            dir = base_dir;
            player = player_proc;
            owns_player = owns;
            Text = "concert";
            StartPosition = FormStartPosition.Manual;
            // a fixed 210px height clips the bottom control row on
            // high-dpi laptop screens (windows scales the title bar and
            // fonts but the table's implicit row heights overflow the
            // client area): explicit percent row styles stretch all three
            // rows into whatever height the window has.
            MinimumSize = new Size(560, 220);
            Size = new Size(600, 240);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MinimizeBox = false;

            TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));

            // row 0: seek bar across the top.
            root.Controls.Add(seek, 0, 0);
            root.SetColumnSpan(seek, 2);

            // row 1: time+pause left, speed right.
            FlowLayoutPanel left = new() { Dock = DockStyle.Fill, WrapContents = false };
            left.Controls.Add(time_label);
            left.Controls.Add(pause);
            root.Controls.Add(left, 0, 1);
            FlowLayoutPanel right = new() { Dock = DockStyle.Fill, WrapContents = false };
            right.Controls.Add(new Label { Text = "speed", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
            right.Controls.Add(speed);
            root.Controls.Add(right, 1, 1);

            // row 2: camera combo, voices checkbox, fps, loading state.
            FlowLayoutPanel bottom = new() { Dock = DockStyle.Fill, WrapContents = false };
            bottom.Controls.Add(camera);
            bottom.Controls.Add(voices);
            bottom.Controls.Add(fps_label);
            bottom.Controls.Add(loading_label);
            root.Controls.Add(bottom, 0, 2);
            root.SetColumnSpan(bottom, 2);

            Controls.Add(root);

            camera.Items.AddRange(["camera full", "camera partial", "camera free"]);
            camera.SelectedIndex = 0;

            pause.Click += (s, e) => Send(paused: status_paused == 1 ? 0 : 1);
            speed.ValueChanged += (s, e) => Send(speed: (float)speed.Value);
            camera.SelectedIndexChanged += (s, e) =>
            {
                last_camera_pick = DateTime.UtcNow;
                Send(camera_mode: camera.SelectedIndex);
            };
            voices.CheckedChanged += on_voices_changed;
            seek.MouseDown += (s, e) => dragging = true;
            seek.MouseUp += (s, e) => { dragging = false; Send(seek_val: status_length > 0 ? status_length * seek.Value / 1000f : -1f); };

            poll.Tick += (s, e) => Poll();
            poll.Start();
            FormClosed += (s, e) =>
            {
                poll.Stop();
                // only a window that started the player kills it on close;
                // the attach window leaves the concert running.
                if (owns_player)
                    try { if (player != null && !player.HasExited) player.Kill(); } catch { }
            };
        }

        // writes one command block. the player applies the whole block when
        // the sequence number advances, so every field ships every time —
        // absent fields must not fall back to parser defaults. fields the
        // user has not touched keep their last SENT value, but seek is
        // single-shot: re-shipping an old seek on every later button made
        // each spring-rate tweak yank the song back (the trace showed every
        // seq carrying seek 12.4 after one scrub). the player treats a
        // negative seek as "no seek this block".
        void Send(float seek_val = float.NaN, int paused = -1, float speed = 0f,
            int voices = -1, int camera_mode = -1)
        {
            seq++;
            if (float.IsNaN(seek_val)) seek_val = _no_seek;
            if (paused < 0) paused = _last_paused;
            if (speed == 0f) speed = _last_speed;
            if (voices < 0) voices = _last_voices;
            if (camera_mode < 0) camera_mode = _last_camera;
            _last_seek = seek_val; _last_paused = paused; _last_speed = speed;
            _last_voices = voices; _last_camera = camera_mode;
            string json = "{\"seq\": " + seq +
                ", \"seek\": " + (seek_val < 0f ? "-1" : seek_val.ToString("0.###")) +
                ", \"paused\": " + paused +
                ", \"speed\": " + speed.ToString("0.###") +
                ", \"voices\": " + voices +
                ", \"camera_mode\": " + camera_mode + "}";
            try { File.WriteAllText(Path.Combine(dir, "control.json"), json); } catch { }
        }

        const float _no_seek = -1f;
        float _last_seek = -1f;
        int _last_paused;
        float _last_speed = 1f;
        int _last_voices = 1;
        int _last_camera;

        // user-owned camera selection: the poll used to snap the combo from
        // the player's (up to 250ms stale) status, which fired
        // SelectedIndexChanged again and re-sent the OLD mode — the camera
        // flip-flop (1 -> 0 -> 0 -> 1) in the trace. the combo only follows
        // the player when the player disagrees AND the user is not mid-
        // selection (no local change in the last second).
        DateTime last_camera_pick = DateTime.MinValue;

        // the seek trackbar follows the player's status only when the user
        // is not dragging and no local seek command is outstanding (the
        // player echoes seeks in status; following it re-sent the same seek).
        void Poll()
        {
            if (exited) return;
            if (player != null)
            {
                try
                {
                    if (player.HasExited) { exited = true; Close(); return; }
                }
                catch { }
            }
            else
            {
                // attach mode: no player handle, follow the status file. when
                // the player stops publishing (exited or never started) for a
                // long stretch, close quietly.
                try
                {
                    string path = Path.Combine(dir, "status.json");
                    if (File.Exists(path) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddSeconds(-30))
                    { exited = true; Close(); return; }
                }
                catch { }
            }

            try
            {
                string path = Path.Combine(dir, "status.json");
                if (!File.Exists(path)) return;
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                status_time = root.GetProperty("time").GetSingle();
                status_length = root.GetProperty("length").GetSingle();
                status_paused = root.GetProperty("paused").GetInt32();
                status_cam = root.GetProperty("camera_mode").GetInt32();

                if (!dragging && status_length > 0)
                    seek.Value = Math.Clamp((int)(status_time / status_length * 1000f), 0, 1000);
                time_label.Text = $"{Fmt(status_time)} / {Fmt(status_length)}" +
                    (status_paused == 1 ? "  (paused)" : "");
                if (status_paused == 1) pause.Text = "resume"; else pause.Text = "pause";
                if (root.TryGetProperty("fps", out var fpsEl))
                {
                    float fps = fpsEl.GetSingle();
                    if (fps > 0f) fps_label.Text = $"{fps:0} fps";
                }
                if (root.TryGetProperty("voices", out var voicesEl))
                {
                    int v = voicesEl.GetInt32();
                    if (v >= 0 && voices.Checked != (v == 1))
                    {
                        // the player echoed a voices change (not user-initiated
                        // here); sync the checkbox without re-sending.
                        voices.CheckedChanged -= on_voices_changed;
                        voices.Checked = v == 1;
                        voices.CheckedChanged += on_voices_changed;
                    }
                }
                // follow the player's camera mode only outside the grace
                // window after a user pick; the player applies the pick and
                // echoes it back, and from then the echo is in sync.
                if (status_cam >= 0 && camera.SelectedIndex != status_cam &&
                    (DateTime.UtcNow - last_camera_pick).TotalSeconds > 1.0)
                    camera.SelectedIndex = status_cam;
                if (root.TryGetProperty("loading", out var loading) &&
                    loading.GetString() is string phase && phase.Length > 0)
                    loading_label.Text = phase;
            }
            catch { }
        }

        void on_voices_changed(object? s, EventArgs e) => Send(voices: voices.Checked ? 1 : 0);

        static string Fmt(float t)
        {
            if (t < 0) t = 0;
            int m = (int)(t / 60f);
            int s = (int)(t % 60f);
            return $"{m}:{s:00}";
        }
    }
}
