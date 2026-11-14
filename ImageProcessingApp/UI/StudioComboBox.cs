using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

// The native combo still owns input, selection, accessibility, and its popup.
// Owner-draw rows plus a client overlay replace the system-colored field/arrow.
internal sealed class StudioComboBox : ComboBox, IThemeAware
{
    private ThemePalette _palette = ThemeManager.Current;
    private readonly RoundedControlInteraction _interaction;

    public StudioComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        _interaction = new RoundedControlInteraction(this);
        UpdateMetrics();
        ApplyTheme(_palette);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateMetrics();
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMetrics();
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateMetrics();
    }
    private void UpdateMetrics()
    {
        // The selection field follows ItemHeight; native borders add the rest.
        int desired = Math.Max(Font.Height + 8 * DeviceDpi / 96, 28 * DeviceDpi / 96);
        if (ItemHeight != desired) ItemHeight = desired;
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyTheme(ThemeManager.Current);
    }
    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        _interaction.SetExternalState(true, true);
    }
    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        _interaction.SetExternalState(ClientRectangle.Contains(PointToClient(Cursor.Position)), false);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color fill = selected ? _palette.Accent : _palette.Surface;
        Color text = !Enabled ? _palette.Muted : selected ? _palette.OnAccent : _palette.Text;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -8 * DeviceDpi / 96, 0);
        string label = (e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text) ?? string.Empty;
        TextRenderer.DrawText(e.Graphics, label, Font, bounds, text,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping |
            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
        if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), text, fill);
        base.OnDrawItem(e);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (_interaction is null || !IsHandleCreated || IsDisposed || Width < 8 || Height < 8) return;
        if (m.Msg == 0xF)
        {
            using var graphics = Graphics.FromHwnd(Handle);
            DrawClient(graphics);
        }
        else if (m.Msg == 0x318 || (m.Msg == 0x317 && (m.LParam.ToInt64() & 4) != 0))
        {
            // DrawToBitmap supplies its own HDC through WM_PRINT/WM_PRINTCLIENT.
            if (m.WParam == IntPtr.Zero) return;
            using var graphics = Graphics.FromHdc(m.WParam);
            DrawClient(graphics);
        }
    }

    private void DrawClient(Graphics graphics)
    {
        float scale = DeviceDpi / 96f;
        Color parent = Parent?.BackColor is Color color && color.A == 255 ? color : _palette.Background;
        using (var background = new SolidBrush(parent)) graphics.FillRectangle(background, ClientRectangle);
        _interaction.DrawShadow(graphics, _palette, pill: false);
        var fill = Enabled ? ThemeManager.Blend(_palette.Surface, _palette.Accent,
            .06f * _interaction.HoverAmount + .06f * _interaction.PressAmount) : _palette.Canvas;
        var text = Enabled ? _palette.Text : _palette.Muted;
        var border = Enabled ? ThemeManager.Blend(_palette.ControlBorder, _palette.Accent,
            Math.Max(_interaction.HoverAmount, _interaction.PressAmount)) : _palette.Border;
        if (SystemInformation.HighContrast)
        {
            fill = SystemColors.Window;
            text = Enabled ? SystemColors.WindowText : SystemColors.GrayText;
            border = text;
        }
        float inset = 2.5f * scale;
        var bounds = new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var saved = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = RoundedControlRenderer.RoundedRectangle(bounds, StudioMetrics.ControlRadius * scale);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border, Math.Max(1, scale));
            graphics.FillPath(brush, outline);
            graphics.DrawPath(pen, outline);
            if (Focused && ShowFocusCues && Enabled)
            {
                using var focus = RoundedControlRenderer.RoundedRectangle(RectangleF.Inflate(bounds, -3 * scale, -3 * scale), 7 * scale);
                using var focusPen = new Pen(text, Math.Max(1, scale)) { DashStyle = DashStyle.Dot };
                graphics.DrawPath(focusPen, focus);
            }
            bool rtl = RightToLeft == RightToLeft.Yes;
            int left = (int)((rtl ? 36 : 12) * scale);
            var textBounds = new Rectangle(left, 0, Math.Max(0, Width - (int)(48 * scale)), Height);
            TextRenderer.DrawText(graphics, SelectedItem is null ? Text : GetItemText(SelectedItem), Font, textBounds, text,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping |
                (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
            float x = rtl ? 19 * scale : Width - 19 * scale;
            float y = Height / 2f;
            using var arrow = new Pen(text, Math.Max(1.5f, 1.5f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLines(arrow, [new PointF(x - 4 * scale, y - 2 * scale), new PointF(x, y + 2 * scale), new PointF(x + 4 * scale, y - 2 * scale)]);
        }
        finally { graphics.Restore(saved); }
    }
}
