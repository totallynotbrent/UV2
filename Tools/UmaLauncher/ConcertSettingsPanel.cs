using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace UmaLauncher
{
    // the inspector for a running concert, docked under the transport
    // controls in the concert window. it owns the post processing layer
    // switches, the key light aids, the mix and the readouts, and every
    // change goes out to the player over the control.json bridge.
    //
    // the player is only a viewer: the concert window is where a concert
    // gets controlled.
    public class ConcertSettingsPanel : Panel
    {
        // one post processing layer the viewer can switch.
        private sealed class LayerRow
        {
            public char Letter;
            public string Name;
            // song = the song's own choice, off = silenced, on = forced on.
            public ComboBox Choice = new() { DropDownStyle = ComboBoxStyle.DropDownList };
            public Label State = new() { AutoSize = true, ForeColor = Color.DimGray };
        }

        // one singing column the viewer can mix.
        private sealed class StemRow
        {
            public int Group;
            public int CharaId;
            public string Name = "";
            public TrackBar Bar = new() { Minimum = 0, Maximum = 150, TickFrequency = 25 };
            public Label Value = new() { AutoSize = true, ForeColor = Color.DimGray };
        }

        // the layer order the player expects in the layers field.
        static readonly (char Letter, string Name)[] LayerOrder =
        {
            ('b', "bloom"),
            ('d', "depth of field"),
            ('f', "film gradient"),
            ('t', "tilt shift"),
            ('g', "colour grade"),
            ('r', "radial blur"),
            ('l', "lens distortion"),
            ('c', "clarity"),
            ('o', "fog and fade"),
            ('s', "sun shafts"),
        };

        readonly List<LayerRow> layers = new();
        readonly List<StemRow> stems = new();

        readonly CheckBox raw = new() { Text = "raw render", AutoSize = true };
        readonly CheckBox tone_map = new() { Text = "tone map", AutoSize = true, Checked = true };
        readonly CheckBox freeze_light = new() { Text = "freeze key light", AutoSize = true };
        readonly TrackBar yaw = new() { Minimum = -180, Maximum = 180, TickFrequency = 30 };
        readonly Label yaw_value = new() { AutoSize = true, ForeColor = Color.DimGray };
        readonly TrackBar bgm = new() { Minimum = 0, Maximum = 100, TickFrequency = 25, Value = 100 };
        readonly Label bgm_value = new() { AutoSize = true, ForeColor = Color.DimGray };
        readonly FlowLayoutPanel stem_box = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        readonly Label stem_note = new() { AutoSize = true, ForeColor = Color.DimGray, Text = "" };

        // the stage and diagnostics readouts.
        readonly Label stage_characters = Readout();
        readonly Label stage_lit = Readout();
        readonly Label stage_camera = Readout();
        readonly Label stage_voices = Readout();
        readonly Label diag_fps = Readout();
        readonly Label diag_frame = Readout();
        readonly Label diag_time = Readout();
        readonly Label diag_layers = Readout();
        readonly Label diag_light = Readout();
        readonly Label diag_version = Readout();

        readonly TabControl tabs = new() { Dock = DockStyle.Fill };

        // the transport window that forwards every change to the player.
        readonly ControlForm owner;

        public ConcertSettingsPanel(ControlForm concert)
        {
            owner = concert;
            Dock = DockStyle.Fill;
            Padding = new Padding(8);

            TabPage look = new() { Text = "look" };
            TabPage sound = new() { Text = "sound" };
            TabPage stage = new() { Text = "stage" };
            TabPage diag = new() { Text = "diagnostics" };

            build_look(look);
            build_sound(sound);
            build_stage(stage);
            build_diagnostics(diag);

            tabs.TabPages.Add(look);
            tabs.TabPages.Add(sound);
            tabs.TabPages.Add(stage);
            tabs.TabPages.Add(diag);
            Controls.Add(tabs);
        }

        static Label Readout() =>
            new() { AutoSize = true, ForeColor = Color.DimGray, Text = "-" };

        void build_look(TabPage page)
        {
            FlowLayoutPanel box = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };

            box.Controls.Add(SectionLabel("post processing"));
            foreach (var (letter, name) in LayerOrder)
            {
                var row = new LayerRow { Letter = letter, Name = name };
                row.Choice.Items.AddRange(["song", "off", "on"]);
                row.Choice.SelectedIndex = 0;
                row.Choice.Width = 80;
                row.Choice.SelectedIndexChanged += (s, e) =>
                {
                    if (updating) return;
                    send_layers();
                };
                FlowLayoutPanel line = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
                var label = new Label { Text = name, AutoSize = true, Width = 150 };
                label.Text = Trim(label, 150);
                line.Controls.Add(label);
                line.Controls.Add(row.Choice);
                line.Controls.Add(row.State);
                box.Controls.Add(line);
                layers.Add(row);
            }

            box.Controls.Add(SectionLabel("render"));
            box.Controls.Add(raw);
            box.Controls.Add(tone_map);
            raw.CheckedChanged += (s, e) =>
            {
                foreach (var row in layers) row.Choice.Enabled = !raw.Checked;
                tone_map.Enabled = !raw.Checked;
                send_layers();
            };
            tone_map.CheckedChanged += (s, e) => send_layers();

            box.Controls.Add(SectionLabel("key light"));
            box.Controls.Add(freeze_light);
            freeze_light.CheckedChanged += (s, e) => send_settings();

            FlowLayoutPanel yaw_row = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            yaw_row.Controls.Add(new Label { Text = "yaw", AutoSize = true, Width = 150 });
            yaw.Width = 160;
            yaw.TickStyle = TickStyle.None;
            yaw.ValueChanged += (s, e) =>
            {
                yaw_value.Text = $"{yaw.Value}°";
                send_settings();
            };
            yaw_row.Controls.Add(yaw);
            yaw_row.Controls.Add(yaw_value);
            box.Controls.Add(yaw_row);
            yaw_value.Text = "0°";

            page.Controls.Add(box);
        }

        void build_sound(TabPage page)
        {
            FlowLayoutPanel box = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };

            box.Controls.Add(SectionLabel("mix"));
            FlowLayoutPanel bgm_row = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            bgm_row.Controls.Add(new Label { Text = "background", AutoSize = true, Width = 150 });
            bgm.Width = 160;
            bgm.TickStyle = TickStyle.None;
            bgm.ValueChanged += (s, e) =>
            {
                bgm_value.Text = $"{bgm.Value}%";
                send_mix();
            };
            bgm_row.Controls.Add(bgm);
            bgm_row.Controls.Add(bgm_value);
            box.Controls.Add(bgm_row);
            bgm_value.Text = "100%";

            box.Controls.Add(SectionLabel("voices"));
            box.Controls.Add(stem_box);
            box.Controls.Add(stem_note);

            page.Controls.Add(box);
        }

        void build_stage(TabPage page)
        {
            TableLayoutPanel box = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            box.Controls.Add(Caption("characters"), 0, row++);
            box.Controls.Add(stage_characters, 1, row - 1);
            box.Controls.Add(Caption("lit places"), 0, row++);
            box.Controls.Add(stage_lit, 1, row - 1);
            box.Controls.Add(Caption("camera"), 0, row++);
            box.Controls.Add(stage_camera, 1, row - 1);
            box.Controls.Add(Caption("voices"), 0, row++);
            box.Controls.Add(stage_voices, 1, row - 1);

            page.Controls.Add(box);
        }

        void build_diagnostics(TabPage page)
        {
            TableLayoutPanel box = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            box.Controls.Add(Caption("fps"), 0, row++);
            box.Controls.Add(diag_fps, 1, row - 1);
            box.Controls.Add(Caption("frame"), 0, row++);
            box.Controls.Add(diag_frame, 1, row - 1);
            box.Controls.Add(Caption("time"), 0, row++);
            box.Controls.Add(diag_time, 1, row - 1);
            box.Controls.Add(Caption("layers off"), 0, row++);
            box.Controls.Add(diag_layers, 1, row - 1);
            box.Controls.Add(Caption("key light"), 0, row++);
            box.Controls.Add(diag_light, 1, row - 1);
            box.Controls.Add(Caption("version"), 0, row++);
            box.Controls.Add(diag_version, 1, row - 1);

            page.Controls.Add(box);
        }

        static Label Caption(string text) =>
            new() { Text = text, AutoSize = true, ForeColor = Color.DimGray };

        static Label SectionLabel(string text)
        {
            var label = new Label { Text = text, AutoSize = true };
            label.Font = new Font(label.Font, FontStyle.Bold);
            label.Margin = new Padding(0, 10, 0, 2);
            return label;
        }

        // guards the callbacks while the panel reflects the player's state.
        bool updating;

        // the player publishes the singing columns once the stems decode,
        // so the sliders are built then and not before.
        public void set_stems(List<(int Group, int CharaId, string Name)> found)
        {
            if (found.Count == stems.Count) return;
            stem_box.SuspendLayout();
            stem_box.Controls.Clear();
            stems.Clear();
            foreach (var (group, chara_id, name) in found)
            {
                var row = new StemRow { Group = group, CharaId = chara_id, Name = name };
                FlowLayoutPanel line = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
                var label = new Label { Text = name, AutoSize = true, Width = 150 };
                label.Text = Trim(label, 150);
                row.Bar.Width = 160;
                row.Bar.Height = 22;
                row.Bar.TickStyle = TickStyle.None;
                row.Bar.Value = 100;
                row.Bar.ValueChanged += (s, e) =>
                {
                    row.Value.Text = $"{row.Bar.Value}%";
                    if (!updating) send_mix();
                };
                row.Value.Text = "100%";
                line.Controls.Add(label);
                line.Controls.Add(row.Bar);
                line.Controls.Add(row.Value);
                stem_box.Controls.Add(line);
                stems.Add(row);
            }
            stem_box.ResumeLayout();
            stem_note.Text = found.Count == 0
                ? ""
                : $"{found.Count}";
            if (!updating) send_mix();
        }

        // the readouts the player publishes.
        public void set_readouts(int characters, int lit, int camera_mode, bool voices,
            float fps, float time, string length, string layers_off, bool frozen, float yaw_deg,
            string version)
        {
            updating = true;
            stage_characters.Text = characters.ToString();
            stage_lit.Text = lit.ToString();
            stage_camera.Text = camera_mode switch
            {
                0 => "full",
                1 => "partial",
                2 => "free",
                _ => "-",
            };
            stage_voices.Text = voices ? "on" : "off";
            diag_fps.Text = fps > 0f ? fps.ToString("0") : "-";
            diag_frame.Text = time > 0f ? $"{(time / 60f):0}:{(time % 60f):00}" : "-";
            diag_time.Text = length ?? "-";
            diag_layers.Text = string.IsNullOrEmpty(layers_off) ? "none" : layers_off;
            diag_light.Text = frozen ? $"frozen {yaw_deg:0}°" : $"{yaw_deg:0}°";
            diag_version.Text = string.IsNullOrEmpty(version) ? "-" : version;
            updating = false;
        }

        static string Trim(Label label, int width)
        {
            string text = label.Text;
            if (text.Length <= 20) return text;
            return text.Substring(0, 19) + "…";
        }

        // pushes the whole opening state in one command.
        public void send_now()
        {
            send_layers();
            send_settings();
            send_mix();
        }

        // the post processing state goes out as one character per layer, in
        // the order the player expects: 0 leaves the song's own choice
        // alone, 1 silences the layer, 2 forces it on.
        void send_layers()
        {
            var chars = new char[LayerOrder.Length];
            for (int i = 0; i < LayerOrder.Length; i++)
                chars[i] = layers[i].Choice.SelectedIndex switch
                {
                    1 => '1',
                    2 => '2',
                    _ => '0',
                };
            owner.Send(
                layers: new string(chars),
                raw: raw.Checked ? 1 : 0,
                tonemap: tone_map.Checked ? 1 : 0);
        }

        void send_settings()
        {
            owner.Send(light_freeze: freeze_light.Checked ? 1 : 0, light_yaw: yaw.Value);
        }

        void send_mix()
        {
            var parts = new List<string>();
            foreach (var row in stems)
                parts.Add($"{row.Group}:{row.CharaId}:{row.Bar.Value / 100f:0.00}");
            owner.Send(bgm_gain: bgm.Value / 100f, stems: string.Join(";", parts));
        }
    }
}
