using System.Diagnostics;
using System.Text;

namespace UmaLauncher
{
    // the front end: song list on the left, mirrored cast slots on the right, launch at the bottom.
    partial class MainForm : Form
    {
        private readonly Dictionary<int, LiveData> songs = [];
        private readonly Dictionary<int, string> titles = [];
        private readonly Dictionary<int, string> charaNames = [];
        private readonly Dictionary<int, string> dressNames = [];
        private readonly List<DressData> liveDresses = [];

        private readonly ListBox songList = new();
        private readonly TextBox searchBox = new();
        private readonly FlowLayoutPanel slotPanel = new();
        private readonly Button launchButton = new() { Text = "Launch concert", Enabled = false, Height = 40, Dock = DockStyle.Fill };
        private readonly Label songInfo = new();

        private LiveData? selectedSong;
        private readonly List<SlotPick> slots = [];

        public MainForm()
        {
            Text = "UmaLauncher";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 720);
            KeyPreview = true;

            try
            {
                Db.Open(Config.MainPath);
                foreach (var s in Db.Songs()) songs[s.MusicId] = s;
                titles = Db.Titles();
                charaNames = Db.CharaNames();
                dressNames = Db.DressNames();
                liveDresses = Db.LiveDresses();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read the game database at " + Config.MainPath +
                    "\n\n" + ex.Message, "Game data", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            BuildLayout();
            RefreshSongs("");
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));

            TableLayoutPanel left = new() { Dock = DockStyle.Fill, RowCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            searchBox.Dock = DockStyle.Fill;
            searchBox.TextChanged += (s, e) => RefreshSongs(searchBox.Text);
            left.Controls.Add(searchBox, 0, 0);
            songList.Dock = DockStyle.Fill;
            songList.IntegralHeight = false;
            songList.SelectedIndexChanged += (s, e) => SelectSong(songList.SelectedItem as LiveData);
            left.Controls.Add(songList, 0, 1);
            root.Controls.Add(left, 0, 0);

            TableLayoutPanel right = new() { Dock = DockStyle.Fill, RowCount = 3 };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            songInfo.Dock = DockStyle.Fill;
            right.Controls.Add(songInfo, 0, 0);
            slotPanel.Dock = DockStyle.Fill;
            slotPanel.WrapContents = true;
            slotPanel.AutoScroll = true;
            right.Controls.Add(slotPanel, 0, 1);
            TableLayoutPanel bottom = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            launchButton.Dock = DockStyle.Fill;
            launchButton.Click += (s, e) => Launch();
            Button updateButton = new() { Text = "Check for update", Dock = DockStyle.Fill };
            updateButton.Click += (s, e) => Updater.Check(this);
            bottom.Controls.Add(launchButton, 0, 0);
            bottom.Controls.Add(updateButton, 1, 0);
            right.Controls.Add(bottom, 0, 2);
            root.Controls.Add(right, 1, 0);

            Controls.Add(root);
        }

        private void RefreshSongs(string filter)
        {
            songList.Items.Clear();
            foreach (var s in songs.Values.OrderBy(x => x.Sort))
            {
                string title = titles.GetValueOrDefault(s.MusicId, "music " + s.MusicId);
                if (filter.Length > 0 &&
                    !title.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !s.MusicId.ToString().Contains(filter)) continue;
                songList.Items.Add(s);
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
        }

        // the list shows "1006 Make debut!" via the item's ToString.
        private void SelectSong(LiveData? song)
        {
            selectedSong = song;
            slots.Clear();
            slotPanel.Controls.Clear();
            launchButton.Enabled = song is not null;

            if (song is null) return;

            var allowed = Db.AllowedCharas(song.MusicId);
            var recommended = Db.RecommendedCast(song.MusicId);
            int count = song.LiveMemberNumber;
            int pivot = count / 2;

            List<(int index, int position)> order = [];
            for (int pos = 0; pos < count; pos++)
            {
                int slotIndex = PositionToIndex(pos, pivot);
                order.Add((slotIndex, pos));
            }

            foreach (var (index, pos) in order)
            {
                int charaId = recommended.GetValueOrDefault(pos + 1, (0, -1)).Item1;
                int dressId = recommended.GetValueOrDefault(pos + 1, (0, -1)).Item2;
                if (charaId == 0)
                {
                    charaId = allowed.Count > 0 ? allowed[pos % allowed.Count] : 0;
                    dressId = DefaultDress(song, charaId);
                }

                SlotPick pick = new() { Position = pos, CharaId = charaId, DressId = dressId };
                slots.Insert(Math.Min(index, slots.Count), pick);

                Button slotButton = new()
                {
                    Size = new Size(96, 96),
                    Text = SlotLabel(pick),
                    Tag = pick
                };
                slotButton.Click += (s, e) => OpenCharaPicker(pick);
                slotPanel.Controls.Add(slotButton);
            }

            songInfo.Text = $"{titles.GetValueOrDefault(song.MusicId, "music " + song.MusicId)}  |  {count} members  |  id {song.MusicId}";
        }

        private string SlotLabel(SlotPick pick)
        {
            string who = pick.CharaId == 0 ? "empty" : charaNames.GetValueOrDefault(pick.CharaId, "chara " + pick.CharaId);
            string dress = pick.DressId > 0 ? "\n" + dressNames.GetValueOrDefault(pick.DressId, "dress " + pick.DressId) : "";
            return $"pos {pick.Position + 1}\n{who}{dress}";
        }

        private int DefaultDress(LiveData song, int charaId)
        {
            int songDefault = charaId != 0 && song.DefaultMainDress != 0 ? song.DefaultMainDress : song.BackdancerDress;
            if (liveDresses.Any(d => d.Id == songDefault)) return songDefault;
            return liveDresses.Where(d => d.CharaId == charaId).OrderBy(d => d.Id).FirstOrDefault()?.Id ?? -1;
        }

        private void OpenCharaPicker(SlotPick pick)
        {
            if (selectedSong is null) return;
            using CharaPickerForm picker = new(selectedSong.MusicId, charaNames, dressNames, liveDresses, pick.CharaId, Db.AllowedCharas(selectedSong.MusicId));
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            pick.CharaId = picker.SelectedChara;
            pick.DressId = picker.SelectedDress;

            foreach (Control c in slotPanel.Controls)
            {
                if (c is Button b && b.Tag is SlotPick p && p == pick) b.Text = SlotLabel(pick);
            }
        }

        private void Launch()
        {
            if (selectedSong is null) return;

            StringBuilder json = new();
            json.AppendLine("{");
            json.AppendLine($"    \"music_id\": {selectedSong.MusicId},");
            json.AppendLine($"    \"title\": \"{EscapeJson(titles.GetValueOrDefault(selectedSong.MusicId, ""))}\",");
            json.AppendLine($"    \"member_count\": {slots.Count},");
            json.AppendLine("    \"stage_id\": -1,");
            json.AppendLine("    \"slots\": [");
            for (int i = 0; i < slots.Count; i++)
            {
                string comma = i < slots.Count - 1 ? "," : "";
                json.AppendLine($"        {{\"position\": {slots[i].Position + 1}, \"chara_id\": {slots[i].CharaId}, \"dress_id\": {slots[i].DressId}}}{comma}");
            }
            json.AppendLine("    ]");
            json.AppendLine("}");

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            File.WriteAllText(Path.Combine(dir, "selection.json"), json.ToString());

            string exe = Path.Combine(dir, "UV2.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show("UV2.exe not found next to the launcher.", "Concert player", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, UseShellExecute = false });
        }

        private static string EscapeJson(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        // explorer's center-out mirror: position 0 sits at the pivot, even steps right, odd steps left.
        private static int PositionToIndex(int position, int pivot)
        {
            if (position == 0) return pivot;
            if (position % 2 == 0) return pivot + position / 2;
            return pivot - (position + 1) / 2;
        }
    }

    class SlotPick
    {
        public int Position;
        public int CharaId;
        public int DressId;
    }
}
