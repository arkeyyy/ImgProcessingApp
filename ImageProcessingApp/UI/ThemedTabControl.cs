namespace ImageProcessingApp;

internal sealed class ThemedTabControl : TabControl, IThemeAware
{
    private ThemePalette _palette = ThemeManager.Current;
    private bool _updatingMetrics;
    public ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        Multiline = false;
        Padding = new Point(18, 10);
        ItemSize = new Size(150, 42);
        AccessibleName = "Image processing workspaces";
        DpiChangedAfterParent += (_, _) => UpdateMetrics();
        SizeChanged += (_, _) => UpdateMetrics();
    }
    protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); UpdateMetrics(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateMetrics(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    private void UpdateMetrics()
    {
        if (_updatingMetrics || !IsHandleCreated || TabCount == 0) return;
        _updatingMetrics = true;
        try
        {
            using var graphics = CreateGraphics();
            int padding = (int)(32 * DeviceDpi / 96f);
            int preferredWidth = TabPages.Cast<TabPage>().Max(page => TextRenderer.MeasureText(graphics, page.Text, Font).Width) + padding;
            int width = Math.Min(preferredWidth, Math.Max(80, (Width - 12) / TabCount));
            int height = (int)(40 * DeviceDpi / 96f);
            if (width < preferredWidth) height = Math.Max(height, Font.Height * 2 + (int)(16 * DeviceDpi / 96f));
            var size = new Size(width, height);
            if (ItemSize != size) ItemSize = size;
        }
        finally { _updatingMetrics = false; }
    }
    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.Background;
        Invalidate();
    }
    protected override void OnDrawItem(DrawItemEventArgs e) => DrawTab(e.Graphics, e.Bounds, e.Index);

    private void DrawTab(Graphics graphics, Rectangle bounds, int index)
    {
        if (index < 0 || index >= TabCount) return;
        bool selected = index == SelectedIndex;
        Color selectedBackground = SystemInformation.HighContrast ? _palette.Accent : _palette.AccentSurface;
        Color selectedText = SystemInformation.HighContrast ? _palette.OnAccent : _palette.Accent;
        using var background = new SolidBrush(selected ? selectedBackground : _palette.Background);
        graphics.FillRectangle(background, bounds);
        TextRenderer.DrawText(graphics, TabPages[index].Text, Font, Rectangle.Inflate(bounds, -6, -4),
            selected ? selectedText : _palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (selected)
        {
            using var accent = new SolidBrush(_palette.Accent);
            int thickness = Math.Max(2, (int)(3 * DeviceDpi / 96f));
            graphics.FillRectangle(accent, bounds.Left + 8, bounds.Bottom - thickness, bounds.Width - 16, thickness);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -5, -5), selectedText, selectedBackground);
        }
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated || TabCount == 0) return;
        using var graphics = m.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
        using var brush = new SolidBrush(_palette.Background);
        var page = DisplayRectangle;
        // Native tab borders are drawn outside the owner-draw bounds. Repaint the
        // whole strip so their system-colored edges do not leak into dark mode.
        graphics.FillRectangle(brush, 0, 0, Width, page.Top);
        for (int index = 0; index < TabCount; index++) DrawTab(graphics, GetTabRect(index), index);
        graphics.FillRectangle(brush, 0, page.Top, Math.Max(0, page.Left), Math.Max(0, Height - page.Top));
        graphics.FillRectangle(brush, page.Right, page.Top, Math.Max(0, Width - page.Right), Math.Max(0, Height - page.Top));
        graphics.FillRectangle(brush, 0, page.Bottom, Width, Math.Max(0, Height - page.Bottom));
    }
}
