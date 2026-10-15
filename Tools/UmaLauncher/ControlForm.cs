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
        private readonly ComboBox speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly ComboBox camera = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly CheckBox collisions = new() { Text = "collisions", AutoSize = true, Checked = true };
        private readonly TrackBar spring = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 50, Width = 180 };
        private readonly Label spring_label = new() { Text = "spring 1.00", AutoSize = true };
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

            // row 2: camera combo, collisions checkbox, spring slider.
            // the bottom row carries five controls; a non-wrapping flow
            // at 560px pushed the spring slider's label off the edge —
            // the loading label shares the row now that the window is
            // wider and the row carries an explicit 35% band.
            FlowLayoutPanel bottom = new() { Dock = DockStyle.Fill, WrapContents = false };
            bottom.Controls.Add(camera);
            bottom.Controls.Add(collisions);
            bottom.Controls.Add(spring);
            bottom.Controls.Add(spring_label);
            bottom.Controls.Add(loading_label);
            root.Controls.Add(bottom, 0, 2);
            root.SetColumnSpan(bottom, 2);

            Controls.Add(root);

            speed.Items.AddRange(["0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x"]);
            speed.SelectedIndex = 2;
            camera.Items.AddRange(["camera full", "camera partial", "camera free"]);
            camera.SelectedIndex = 0;

            pause.Click += (s, e) => Send(paused: status_paused == 1 ? 0 : 1);
            speed.SelectedIndexChanged += (s, e) => Send(speed: ParseSpeed(speed.SelectedIndex));
            camera.SelectedIndexChanged += (s, e) => Send(camera_mode: camera.SelectedIndex);
            collisions.CheckedChanged += (s, e) => Send(collisions: collisions.Checked ? 1 : 0);
            spring.ValueChanged += (s, e) =>
            {
                spring_label.Text = $"spring {spring.Value / 100f:0.00}";
                Send(spring_rate: spring.Value / 100f);
            };
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

        static float ParseSpeed(int index) => index switch
        {
            0 => 0.5f, 1 => 0.75f, 2 => 1f, 3 => 1.25f, 4 => 1.5f, _ => 2f
        };

        // writes one command block. the player applies the whole block when
        // the sequence number advances, so every field ships every time —
        // absent fields must not fall back to parser defaults.
        void Send(float seek_val = -1f, int paused = -1, float speed = 0f,
            int collisions = -1, float spring_rate = 0f, int camera_mode = -1)
        {
            seq++;
            if (seek_val < 0f) seek_val = _last_seek;
            if (paused < 0) paused = _last_paused;
            if (speed <= 0f) speed = _last_speed;
            if (collisions < 0) collisions = _last_collisions;
            if (spring_rate <= 0f) spring_rate = _last_spring;
            if (camera_mode < 0) camera_mode = _last_camera;
            _last_seek = seek_val; _last_paused = paused; _last_speed = speed;
            _last_collisions = collisions; _last_spring = spring_rate; _last_camera = camera_mode;
            string json = "{\"seq\": " + seq +
                ", \"seek\": " + seek_val.ToString("0.###") +
                ", \"paused\": " + paused +
                ", \"speed\": " + speed.ToString("0.###") +
                ", \"collisions\": " + collisions +
                ", \"spring_rate\": " + spring_rate.ToString("0.###") +
                ", \"camera_mode\": " + camera_mode + "}";
            try { File.WriteAllText(Path.Combine(dir, "control.json"), json); } catch { }
        }

        float _last_seek = -1f;
        int _last_paused;
        float _last_speed = 1f;
        int _last_collisions = 1;
        float _last_spring = 1f;
        int _last_camera;

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
                if (status_cam >= 0 && camera.SelectedIndex != status_cam)
                    camera.SelectedIndex = status_cam;
                if (root.TryGetProperty("loading", out var loading) &&
                    loading.GetString() is string phase && phase.Length > 0)
                    loading_label.Text = phase;
            }
            catch { }
        }

        static string Fmt(float t)
        {
            if (t < 0) t = 0;
            int m = (int)(t / 60f);
            int s = (int)(t % 60f);
            return $"{m}:{s:00}";
        }
    }
}
