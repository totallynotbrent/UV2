namespace UmaLauncher
{
    // the explorer's character select dialog skeleton: search, list, dress dropdown, ok.
    partial class CharaPickerForm : Form
    {
        private readonly ListBox charaList = new();
        private readonly TextBox searchBox = new();
        private readonly ComboBox dressBox = new();
        private readonly Button okButton = new();
        private readonly Button cancelButton = new();

        private readonly Dictionary<int, string> charaNames;
        private readonly Dictionary<int, string> dressNames;
        private readonly List<DressData> liveDresses;
        private readonly List<int> allowed;
        private readonly Func<int, Image?> portraitLookup;

        public int SelectedChara { get; private set; }
        public int SelectedDress { get; private set; } = -1;

        public CharaPickerForm(int musicId,
            Dictionary<int, string> charaNames,
            Dictionary<int, string> dressNames,
            List<DressData> liveDresses,
            int currentChara,
            List<int> allowed,
            Func<int, Image?>? portraitLookup = null)
        {
            okButton.Text = LanguageManager.T("ok");
            okButton.DialogResult = DialogResult.OK;
            cancelButton.Text = LanguageManager.T("cancel");
            cancelButton.DialogResult = DialogResult.Cancel;

            this.portraitLookup = portraitLookup ?? (id => null);
            this.charaNames = charaNames;
            this.dressNames = dressNames;
            this.liveDresses = liveDresses;
            this.allowed = allowed;

            Text = LanguageManager.T("select_member");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(420, 620);

            TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            searchBox.Dock = DockStyle.Fill;
            searchBox.TextChanged += (s, e) => RefreshList(searchBox.Text);
            root.Controls.Add(searchBox, 0, 0);

            charaList.Dock = DockStyle.Fill;
            charaList.IntegralHeight = false;
            charaList.DrawMode = DrawMode.OwnerDrawFixed;
            charaList.ItemHeight = 52;
            charaList.DrawItem += (s, e) => DrawCharaRow(e);
            charaList.SelectedIndexChanged += (s, e) => RefreshDresses();
            root.Controls.Add(charaList, 0, 1);

            dressBox.Dock = DockStyle.Fill;
            root.Controls.Add(dressBox, 0, 2);

            TableLayoutPanel buttons = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            okButton.Dock = DockStyle.Fill;
            okButton.Click += (s, e) => Confirm();
            cancelButton.Dock = DockStyle.Fill;
            buttons.Controls.Add(okButton, 0, 0);
            buttons.Controls.Add(cancelButton, 1, 0);
            root.Controls.Add(buttons, 0, 3);

            Controls.Add(root);
            RefreshList("");
            if (currentChara != 0)
            {
                for (int i = 0; i < charaList.Items.Count; i++)
                {
                    if (charaList.Items[i] is CharaEntry entry && entry.Id == currentChara)
                    {
                        charaList.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // character portrait from the shared cache.
        private Image? PortraitFor(int charaId) => portraitLookup(charaId);

        // owner-drawn member row: portrait on the left, name over id.
        private void DrawCharaRow(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            if (charaList.Items[e.Index] is not CharaEntry entry) return;

            Image? portrait = PortraitFor(entry.Id);
            if (portrait is not null)
                e.Graphics.DrawImage(portrait, e.Bounds.Left + 6, e.Bounds.Top + 2, 48, 48);

            bool selected = e.State.HasFlag(DrawItemState.Selected);
            using var nameBrush = new SolidBrush(selected ? SystemColors.HighlightText : SystemColors.ControlText);
            using var dimBrush = new SolidBrush(SystemColors.GrayText);
            e.Graphics.DrawString(entry.Name, Font, nameBrush, e.Bounds.Left + 62, e.Bounds.Top + 14);
            e.Graphics.DrawString(entry.Id.ToString(), Font, dimBrush, e.Bounds.Left + 62, e.Bounds.Top + 32);
            if (selected) e.DrawFocusRectangle();
        }

        private void RefreshList(string filter)
        {
            charaList.Items.Clear();
            List<CharaEntry> pool = [];
            if (allowed.Count == 0)
            {
                pool.AddRange(charaNames.Keys.Select(id => new CharaEntry(id, LanguageManager.Name(charaNames[id]))));
            }
            else
            {
                pool.AddRange(allowed.Select(id => new CharaEntry(id, LanguageManager.Name(charaNames.GetValueOrDefault(id, "chara " + id)))));
            }
            foreach (var entry in pool.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            {
                if (filter.Length > 0 &&
                    !entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Id.ToString().Contains(filter)) continue;
                charaList.Items.Add(entry);
            }
        }

        private void RefreshDresses()
        {
            dressBox.Items.Clear();
            if (charaList.SelectedItem is not CharaEntry entry) return;
            foreach (var dress in liveDresses.Where(d => d.CharaId == entry.Id).OrderBy(d => d.Id))
            {
                dressBox.Items.Add(new DressEntry(dress.Id, LanguageManager.Name(dressNames.GetValueOrDefault(dress.Id, "dress " + dress.Id))));
            }
            if (dressBox.Items.Count > 0) dressBox.SelectedIndex = 0;
        }

        private void Confirm()
        {
            if (charaList.SelectedItem is CharaEntry entry)
            {
                SelectedChara = entry.Id;
                SelectedDress = dressBox.SelectedItem is DressEntry dress ? dress.Id : -1;
                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
            Close();
        }

        record CharaEntry(int Id, string Name)
        {
            public override string ToString() => $"{Name} ({Id})";
        }

        record DressEntry(int Id, string Name)
        {
            public override string ToString() => Name;
        }
    }
}
