using System.Drawing;
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
        private readonly Button launchButton = new() { Text = LanguageManager.T("launch"), Enabled = false, Height = 40, Dock = DockStyle.Fill };
        private Button? updateButtonField;
        private readonly Label songInfo = new();

        private LiveData? selectedSong;
        private readonly List<SlotPick> slots = [];
        private readonly Dictionary<int, Image> jacketCache = [];
        private readonly Dictionary<int, Image?> portraitCache = [];
        private readonly ImageList slotImages = new() { ImageSize = new Size(40, 40), ColorDepth = ColorDepth.Depth32Bit };
        private readonly Dictionary<int, int> portraitIndex = [];

        // character portrait from the charicons folder exported by the player.
        internal Image? PortraitFor(int charaId)
        {
            if (portraitCache.TryGetValue(charaId, out Image? cached)) return cached;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charicons", charaId + ".png");
            Image? img = null;
            if (File.Exists(path))
            {
                try { img = Image.FromFile(path); }
                catch { img = null; }
            }
            portraitCache[charaId] = img!;
            return img;
        }

        // scaled portrait index for slot buttons; -1 when the character has no image.
        private int SlotImageIndex(int charaId)
        {
            if (portraitIndex.TryGetValue(charaId, out int idx)) return idx;
            Image? img = PortraitFor(charaId);
            if (img is null)
            {
                portraitIndex[charaId] = -1;
                return -1;
            }
            slotImages.Images.Add(img);
            idx = slotImages.Images.Count - 1;
            portraitIndex[charaId] = idx;
            return idx;
        }

        // jacket thumbnails exported by the player's -dumpjackets mode.
        private Image? JacketFor(int musicId)
        {
            if (jacketCache.TryGetValue(musicId, out Image? cached)) return cached;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jackets", musicId + ".png");
            Image? img = null;
            if (File.Exists(path))
            {
                try { img = Image.FromFile(path); }
                catch { img = null; }
            }
            jacketCache[musicId] = img!;
            return img;
        }

        // the player exports jacket art and character portraits on first run so the ui can show them.
        private void EnsureIcons()
        {
            string base_dir = AppDomain.CurrentDomain.BaseDirectory;
            string jacket_dir = Path.Combine(base_dir, "jackets");
            string icon_dir = Path.Combine(base_dir, "charicons");
            bool jackets_ok = Directory.Exists(jacket_dir) && Directory.EnumerateFiles(jacket_dir, "*.png").Any();
            bool icons_ok = Directory.Exists(icon_dir) && Directory.EnumerateFiles(icon_dir, "*.png").Any();
            if (jackets_ok && icons_ok) return;
            string exe = Path.Combine(base_dir, "UV2.exe");
            if (!File.Exists(exe)) return;
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-dumpicons -batchmode",
                    WorkingDirectory = base_dir,
                    UseShellExecute = false
                });
                proc?.WaitForExit(180000);
            }
            catch { }
        }

        public MainForm()
        {
            Text = "UmaLauncher";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 720);
            KeyPreview = true;

            try
            {
                Db.Open(Config.MainPath);
                foreach (var s in Db.Songs())
                {
                    if (s.HasLive != 1) continue;
                    s.DisplayTitle = "";
                    songs[s.MusicId] = s;
                }
                titles = Db.Titles();
                foreach (var s in songs.Values)
                    s.DisplayTitle = LanguageManager.Name(titles.GetValueOrDefault(s.MusicId, ""));
                charaNames = Db.CharaNames();
                dressNames = Db.DressNames();
                liveDresses = Db.LiveDresses();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read the game database at " + Config.MainPath +
                    "\n\n" + ex.Message, "Game data", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            LanguageManager.Set(Language.Japanese);
            BuildLayout();
            EnsureIcons();
            RefreshSongs("");
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));

            // one top bar across the window: search box, current song, language dropdown.
            TableLayoutPanel topBar = new() { Dock = DockStyle.Fill, ColumnCount = 3 };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F - 36F - 13F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            searchBox.Dock = DockStyle.Fill;
            searchBox.Text = LanguageManager.T("search");
            searchBox.TextChanged += (s, e) => RefreshSongs(searchBox.Text);
            topBar.Controls.Add(searchBox, 0, 0);
            songInfo.Dock = DockStyle.Fill;
            songInfo.TextAlign = ContentAlignment.MiddleCenter;
            songInfo.AutoSize = false;
            topBar.Controls.Add(songInfo, 1, 0);
            ComboBox languageBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            languageBox.Items.AddRange(["日本語", "English", "中文"]);
            languageBox.SelectedIndex = (int)LanguageManager.Current;
            languageBox.SelectedIndexChanged += (s, e) =>
            {
                LanguageManager.Set((Language)languageBox.SelectedIndex);
                ApplyLanguage();
            };
            topBar.Controls.Add(languageBox, 2, 0);
            root.Controls.Add(topBar, 0, 0);
            root.SetColumnSpan(topBar, 2);

            songList.Dock = DockStyle.Fill;
            songList.IntegralHeight = false;
            songList.DrawMode = DrawMode.OwnerDrawFixed;
            songList.ItemHeight = 56;
            songList.DrawItem += (s, e) => DrawSongRow(e);
            songList.SelectedIndexChanged += (s, e) => SelectSong(songList.SelectedItem as LiveData);
            root.Controls.Add(songList, 0, 1);

            slotPanel.Dock = DockStyle.Fill;
            slotPanel.WrapContents = true;
            slotPanel.AutoScroll = true;
            root.Controls.Add(slotPanel, 1, 1);

            TableLayoutPanel bottom = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            launchButton.Dock = DockStyle.Fill;
            launchButton.Click += (s, e) => Launch();
            updateButtonField = new Button() { Text = LanguageManager.T("check_update"), Dock = DockStyle.Fill };
            updateButtonField.Click += (s, e) => Updater.Check(this);
            bottom.Controls.Add(launchButton, 0, 0);
            bottom.Controls.Add(updateButtonField, 1, 0);
            root.Controls.Add(bottom, 0, 2);
            root.SetColumnSpan(bottom, 2);

            Controls.Add(root);
        }

        // re-applies every localized string after a language switch.
        private void ApplyLanguage()
        {
            foreach (var s in songs.Values)
                s.DisplayTitle = LanguageManager.Name(titles.GetValueOrDefault(s.MusicId, ""));
            searchBox.Text = LanguageManager.T("search");
            launchButton.Text = LanguageManager.T("launch");
            if (updateButtonField is not null) updateButtonField.Text = LanguageManager.T("check_update");
            foreach (Control c in slotPanel.Controls)
                if (c is Button b && b.Tag is SlotPick p) b.Text = SlotLabel(p);
            RefreshSongs(searchBox.Text == LanguageManager.T("search") ? "" : searchBox.Text);
            if (selectedSong is not null) SelectSong(selectedSong);
        }

        private void RefreshSongs(string filter)
        {
            songList.Items.Clear();
            foreach (var s in songs.Values.OrderBy(x => x.MusicId))
            {
                string title = titles.GetValueOrDefault(s.MusicId, "music " + s.MusicId);
                if (filter.Length > 0 &&
                    !title.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !s.MusicId.ToString().Contains(filter)) continue;
                songList.Items.Add(s);
            }
        }

        // owner-drawn song row: jacket thumbnail, then title over id.
        private void DrawSongRow(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.State.HasFlag(DrawItemState.Selected)) e.DrawFocusRectangle();
            if (e.Index < 0) return;
            var song = songList.Items[e.Index] as LiveData;
            if (song is null) return;

            Image? jacket = JacketFor(song.MusicId);
            if (jacket is not null)
                e.Graphics.DrawImage(jacket, e.Bounds.Left + 6, e.Bounds.Top + 4, 48, 48);

            bool selected = e.State.HasFlag(DrawItemState.Selected);
            using var titleBrush = new SolidBrush(selected ? SystemColors.HighlightText : SystemColors.ControlText);
            using var dimBrush = new SolidBrush(SystemColors.GrayText);
            string title = string.IsNullOrEmpty(song.DisplayTitle) ? "music " + song.MusicId : song.DisplayTitle;
            e.Graphics.DrawString(title, Font, titleBrush, e.Bounds.Left + 64, e.Bounds.Top + 16);
            e.Graphics.DrawString("id " + song.MusicId + "   " + song.LiveMemberNumber + " members", Font, dimBrush, e.Bounds.Left + 64, e.Bounds.Top + 36);
        }

        // the list shows "1006 Make debut!" via the item's ToString.
        private void SelectSong(LiveData? song)
        {
            selectedSong = song;
            slots.Clear();
            slotPanel.Controls.Clear();
            launchButton.Enabled = song is not null;

            if (song is null)
            {
                songInfo.Text = "";
                return;
            }

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
                    TextAlign = ContentAlignment.BottomCenter,
                    ImageList = slotImages,
                    ImageIndex = SlotImageIndex(pick.CharaId),
                    ImageAlign = ContentAlignment.TopCenter,
                    Tag = pick
                };
                slotButton.Click += (s, e) => OpenCharaPicker(pick);
                slotPanel.Controls.Add(slotButton);
            }

            songInfo.Text = $"{LanguageManager.Name(titles.GetValueOrDefault(song.MusicId, "music " + song.MusicId))}  |  {count} {LanguageManager.T("members")}  |  id {song.MusicId}";
        }

        private string SlotLabel(SlotPick pick)
        {
            string who = pick.CharaId == 0 ? LanguageManager.T("empty")
                : LanguageManager.Name(charaNames.GetValueOrDefault(pick.CharaId, "chara " + pick.CharaId));
            string dress = pick.DressId > 0 ? "\n" + LanguageManager.Name(dressNames.GetValueOrDefault(pick.DressId, "dress " + pick.DressId)) : "";
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
            using CharaPickerForm picker = new(selectedSong.MusicId, charaNames, dressNames, liveDresses, pick.CharaId, Db.AllowedCharas(selectedSong.MusicId), PortraitFor);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            pick.CharaId = picker.SelectedChara;
            pick.DressId = picker.SelectedDress;

            foreach (Control c in slotPanel.Controls)
            {
                if (c is Button b && b.Tag is SlotPick p && p == pick)
                {
                    b.Text = SlotLabel(pick);
                    b.ImageIndex = SlotImageIndex(pick.CharaId);
                }
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
            var ordered_slots = slots.OrderBy(s => s.Position).ToList();
            for (int i = 0; i < ordered_slots.Count; i++)
            {
                string comma = i < ordered_slots.Count - 1 ? "," : "";
                json.AppendLine($"        {{\"position\": {ordered_slots[i].Position + 1}, \"chara_id\": {ordered_slots[i].CharaId}, \"dress_id\": {ordered_slots[i].DressId}}}{comma}");
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
