using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

internal sealed record FilterChoice(string Name, string? Category, string Description);

internal sealed class FilterInvokedEventArgs(FilterChoice filter) : EventArgs
{
    public FilterChoice Filter { get; } = filter;
}

/// <summary>A reusable menu whose committed selection is owned by its workspace.</summary>
internal sealed class CategorizedFilterPicker : UserControl, IThemeAware
{
    private readonly RoundedButton _trigger = (RoundedButton)Ui.Button("Choose filter", ButtonRole.Filter);
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false, ShowCheckMargin = true, ShowItemToolTips = true };
    private readonly ToolTip _tips = new();
    private readonly Dictionary<string, ToolStripMenuItem> _items = new(StringComparer.Ordinal);
    private readonly string _accessibleName;
    private FilterChoice? _selectedFilter;
    private bool _closing;
    private bool _restoreFocusAfterEnable;

    public CategorizedFilterPicker(IEnumerable<FilterChoice> filters, string accessibleName)
    {
        _accessibleName = accessibleName;
        AccessibleName = accessibleName;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        TabStop = false;
        _trigger.MinimumSize = new Size(270, 36);
        _trigger.Margin = Padding.Empty;
        _trigger.UseMnemonic = false;
        _trigger.AccessibleRole = AccessibleRole.ButtonDropDown;
        Controls.Add(_trigger);
        _menu.AccessibleName = accessibleName;
        var categories = new Dictionary<string, ToolStripMenuItem>(StringComparer.Ordinal);
        foreach (var filter in filters)
        {
            var item = new ToolStripMenuItem(EscapeMnemonic(filter.Name))
            {
                Name = filter.Name,
                Tag = filter,
                AccessibleName = filter.Name,
                AccessibleDescription = filter.Description,
                ToolTipText = filter.Description,
                CheckOnClick = false
            };
            _items.Add(filter.Name, item);
            item.Click += (_, _) =>
            {
                if (Enabled && Visible && !IsDisposed)
                {
                    FilterInvoked?.Invoke(this, new FilterInvokedEventArgs(filter));
                    // Native keyboard selection can close the menu before Click. The
                    // workspace then disables us, so restore focus once it enables us again.
                    if (Visible && !IsDisposed)
                    {
                        if (_trigger.CanFocus) _trigger.Focus();
                        else _restoreFocusAfterEnable = true;
                    }
                }
            };
            if (string.IsNullOrEmpty(filter.Category))
                _menu.Items.Add(item);
            else
            {
                if (!categories.TryGetValue(filter.Category, out var category))
                {
                    category = new ToolStripMenuItem(EscapeMnemonic(filter.Category))
                    {
                        Name = filter.Category,
                        AccessibleName = filter.Category,
                        AccessibleDescription = $"Open {filter.Category} filters.",
                        ToolTipText = $"Open {filter.Category} filters."
                    };
                    categories.Add(filter.Category, category);
                    _menu.Items.Add(category);
                    if (category.DropDown is ToolStripDropDownMenu submenu)
                    {
                        submenu.ShowImageMargin = false;
                        submenu.ShowCheckMargin = true;
                        submenu.ShowItemToolTips = true;
                    }
                }
                category.DropDownItems.Add(item);
            }
        }
        _trigger.Click += (_, _) => OpenMenu();
        _menu.Closed += (_, _) =>
        {
            if (_closing || IsDisposed || Disposing || !Visible) return;
            if (_trigger.CanFocus) _trigger.Focus();
            else _restoreFocusAfterEnable = true;
        };
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateSelection();
        ApplyTheme(ThemeManager.Current);
    }

    public Button Trigger => _trigger;
    public ContextMenuStrip Menu => _menu;
    public event EventHandler<FilterInvokedEventArgs>? FilterInvoked;

    public FilterChoice? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is not null && !_items.ContainsKey(value.Name))
                throw new ArgumentException("The filter does not belong to this picker.", nameof(value));
            _selectedFilter = value is null ? null : (FilterChoice)_items[value.Name].Tag!;
            UpdateSelection();
        }
    }

    private static string EscapeMnemonic(string value) => value.Replace("&", "&&");

    private void UpdateSelection()
    {
        string name = _selectedFilter?.Name ?? "Choose filter";
        _trigger.Text = name + "  ▾";
        _trigger.AccessibleName = $"{_accessibleName}: {name}";
        _trigger.AccessibleDescription = (_selectedFilter?.Description ?? "Choose a filter from a category.") +
            " Press Enter, Space, or Alt+Down to open the filter menu.";
        _tips.SetToolTip(_trigger, _trigger.AccessibleDescription);
        foreach (var (filterName, item) in _items)
            item.Checked = filterName == _selectedFilter?.Name;
    }

    private void OpenMenu()
    {
        if (!Enabled || !Visible || IsDisposed || _menu.Visible) return;
        _restoreFocusAfterEnable = false;
        // Refresh here as well so a system High Contrast change is honored immediately.
        ApplyTheme(ThemeManager.Current);
        _menu.Show(_trigger, new Point(0, _trigger.Height));
        if (_menu.Items.Count > 0) _menu.Items[0].Select();
    }

    public void CloseMenu()
    {
        _restoreFocusAfterEnable = false;
        if (_menu.IsDisposed) return;
        _closing = true;
        try { _menu.Close(); }
        finally { _closing = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Down) && ContainsFocus)
        {
            OpenMenu();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) CloseMenu();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled) _menu.Close();
        else if (_restoreFocusAfterEnable && Visible && _trigger.CanFocus)
        {
            _restoreFocusAfterEnable = false;
            _trigger.Focus();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme(ThemeManager.Current);

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyTheme(ThemeManager.Current);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _trigger.ApplyTheme(palette);
        ToolStripRenderer renderer = SystemInformation.HighContrast
            ? new ToolStripSystemRenderer() : new FilterMenuRenderer(palette);
        ApplyMenuTheme(_menu, palette, renderer);
    }

    private static void ApplyMenuTheme(ToolStripDropDown menu, ThemePalette palette, ToolStripRenderer renderer)
    {
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Renderer = renderer;
        foreach (ToolStripMenuItem item in menu.Items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = palette.Text;
            if (item.HasDropDownItems) ApplyMenuTheme(item.DropDown, palette, renderer);
        }
        menu.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            CloseMenu();
            _menu.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class FilterMenuRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new FilterMenuColors(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? palette.Muted : e.Item.Selected || e.Item.Pressed ? palette.OnAccent : palette.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item is { Enabled: false } ? palette.Muted :
                e.Item is { Selected: true } or { Pressed: true } ? palette.OnAccent : palette.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Draw the check ourselves; the system bitmap otherwise remains black in dark mode.
            var color = e.Item.Selected ? palette.OnAccent : palette.Text;
            var bounds = e.ImageRectangle;
            float scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(color, Math.Max(1.5f, 1.8f * scale));
                float left = bounds.Left + bounds.Width * .18f;
                float top = bounds.Top + bounds.Height * .25f;
                e.Graphics.DrawLines(pen,
                [
                    new PointF(left, top + bounds.Height * .28f),
                    new PointF(left + bounds.Width * .23f, top + bounds.Height * .5f),
                    new PointF(left + bounds.Width * .66f, top)
                ]);
            }
            finally { e.Graphics.Restore(state); }
        }
    }

    private sealed class FilterMenuColors(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color MenuBorder => palette.ControlBorder;
        public override Color MenuItemBorder => palette.Accent;
        public override Color MenuItemSelected => palette.Accent;
        public override Color MenuItemSelectedGradientBegin => palette.Accent;
        public override Color MenuItemSelectedGradientEnd => palette.Accent;
        public override Color MenuItemPressedGradientBegin => palette.Accent;
        public override Color MenuItemPressedGradientMiddle => palette.Accent;
        public override Color MenuItemPressedGradientEnd => palette.Accent;
        public override Color ImageMarginGradientBegin => palette.Surface;
        public override Color ImageMarginGradientMiddle => palette.Surface;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color CheckBackground => palette.Surface;
        public override Color CheckSelectedBackground => palette.Accent;
        public override Color CheckPressedBackground => palette.Accent;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Surface;
    }
}
