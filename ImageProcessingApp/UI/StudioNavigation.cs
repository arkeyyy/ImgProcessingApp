namespace ImageProcessingApp;

internal sealed class StudioNavigation : UserControl, IThemeAware
{
    private readonly TableLayoutPanel _layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
    private readonly TableLayoutPanel _strip = new() { Dock = DockStyle.Fill, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly List<Control> _pages = [];
    private readonly List<RoundedRadioButton> _buttons = [];
    private int _selectedIndex = -1;
    public event EventHandler? SelectedIndexChanged;
    public int PageCount => _pages.Count;
    public Control? SelectedPage => _selectedIndex < 0 ? null : _pages[_selectedIndex];
    public IReadOnlyList<Control> Pages => _pages;

    public StudioNavigation()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        TabStop = false;
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "Image processing workspaces";
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.Controls.Add(_strip, 0, 0);
        _layout.Controls.Add(_content, 0, 1);
        Controls.Add(_layout);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateMetrics(); }

    private void UpdateMetrics()
    {
        if (_layout.RowStyles.Count == 0) return;
        float scale = DeviceDpi / 96f;
        int gap = (int)Math.Ceiling(8 * scale);
        int height = Math.Max((int)Math.Ceiling(48 * scale), Font.Height * 2 + gap) + gap;
        if (_layout.RowStyles[0].Height == height && _strip.Margin.Bottom == gap) return;
        _layout.SuspendLayout();
        _strip.Margin = new Padding(0, 0, 0, gap);
        _layout.RowStyles[0].Height = height;
        _layout.ResumeLayout(true);
    }

    public void AddPage(string title, Control control)
    {
        int index = _pages.Count;
        _pages.Add(control);
        control.Dock = DockStyle.Fill;
        control.Visible = false;
        _content.Controls.Add(control);
        var button = new RoundedRadioButton
        {
            Text = title, AccessibleName = title, AccessibleRole = AccessibleRole.PageTab,
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), TabStop = index == 0,
            AutoCheck = false, IsNavigation = true
        };
        button.Click += (_, _) => SelectedIndex = index;
        _buttons.Add(button);
        _strip.ColumnCount = _buttons.Count;
        _strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _strip.Controls.Add(button, index, 0);
        if (_selectedIndex < 0) SelectedIndex = 0;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _pages.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value) return;
            _content.SuspendLayout();
            try
            {
                if (_selectedIndex >= 0) _pages[_selectedIndex].Visible = false;
                _selectedIndex = value;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    _buttons[i].Checked = i == value;
                    _buttons[i].TabStop = i == value;
                }
                _pages[value].Visible = true;
                _pages[value].BringToFront();
            }
            finally { _content.ResumeLayout(true); }
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void FocusNavigation() { if (_selectedIndex >= 0) _buttons[_selectedIndex].Focus(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (PageCount == 0) return base.ProcessCmdKey(ref msg, keyData);
        if (_buttons.Any(b => b.Focused) && keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)
        {
            SelectedIndex = keyData switch
            {
                Keys.Home => 0, Keys.End => PageCount - 1,
                Keys.Left or Keys.Up => (SelectedIndex + PageCount - 1) % PageCount,
                _ => (SelectedIndex + 1) % PageCount
            };
            FocusNavigation();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SelectedIndex = (SelectedIndex + (keyData.HasFlag(Keys.Shift) ? PageCount - 1 : 1)) % PageCount;
            FocusNavigation();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void ApplyTheme(ThemePalette palette) => BackColor = palette.Background;
}
