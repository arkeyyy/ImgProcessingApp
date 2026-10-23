using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

internal enum ButtonRole { Secondary, Primary, Save, Filter }

// Keep the native control types so dialog buttons, mnemonics, accessibility and
// radio-button grouping continue to use the standard Windows Forms behavior.
internal sealed class RoundedButton : Button, IThemeAware
{
    private ThemePalette _palette = ThemeManager.Current;
    private readonly RoundedControlInteraction _interaction;
    private ButtonRole _role;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _interaction = new RoundedControlInteraction(this);
        ApplyTheme(_palette);
    }

    public ButtonRole Role
    {
        get => _role;
        set
        {
            if (_role == value) return;
            _role = value;
            ApplyTheme(_palette);
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        var colors = RoundedControlRenderer.Colors(palette, Role);
        BackColor = colors.Fill;
        ForeColor = colors.Text;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) => RoundedControlRenderer.Draw(
        e.Graphics, this, _palette, Role, _interaction, Focused && ShowFocusCues,
        ShowKeyboardCues, IsDefault);

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyTheme(ThemeManager.Current);
    }
}

internal sealed class RoundedRadioButton : RadioButton, IThemeAware
{
    private ThemePalette _palette = ThemeManager.Current;
    private readonly RoundedControlInteraction _interaction;

    public RoundedRadioButton()
    {
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        TextAlign = ContentAlignment.MiddleCenter;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _interaction = new RoundedControlInteraction(this);
        ApplyTheme(_palette);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        var colors = RoundedControlRenderer.Colors(palette, CurrentRole);
        BackColor = colors.Fill;
        ForeColor = colors.Text;
        Invalidate();
    }

    private ButtonRole CurrentRole => Checked ? ButtonRole.Primary : ButtonRole.Secondary;

    protected override void OnCheckedChanged(EventArgs e)
    {
        ApplyTheme(_palette);
        base.OnCheckedChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) => RoundedControlRenderer.Draw(
        e.Graphics, this, _palette, CurrentRole, _interaction, Focused && ShowFocusCues,
        ShowKeyboardCues, false);

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyTheme(ThemeManager.Current);
    }
}

// This observes input without replacing native click, capture or key handling.
// All event subscriptions belong to the control itself; no static subscriptions
// or drawing resources outlive a control.
internal sealed class RoundedControlInteraction
{
    private bool _mouseDown;
    private bool _spaceDown;

    public bool Hovered { get; private set; }
    public bool Pressed => _spaceDown || (_mouseDown && Hovered);

    public RoundedControlInteraction(Control owner)
    {
        owner.MouseEnter += (_, _) => { Hovered = true; owner.Invalidate(); };
        owner.MouseLeave += (_, _) => { Hovered = false; owner.Invalidate(); };
        owner.MouseMove += (_, e) =>
        {
            bool inside = owner.ClientRectangle.Contains(e.Location);
            if (inside == Hovered) return;
            Hovered = inside;
            owner.Invalidate();
        };
        owner.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = true;
            Hovered = owner.ClientRectangle.Contains(e.Location);
            owner.Invalidate();
        };
        owner.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = false;
            owner.Invalidate();
        };
        owner.MouseCaptureChanged += (_, _) =>
        {
            if (owner.Capture) return;
            _mouseDown = false;
            owner.Invalidate();
        };
        owner.KeyDown += (_, e) =>
        {
            if (e.KeyData != Keys.Space) return;
            _spaceDown = true;
            owner.Invalidate();
        };
        owner.KeyUp += (_, _) =>
        {
            _spaceDown = false;
            owner.Invalidate();
        };
        owner.LostFocus += (_, _) =>
        {
            _spaceDown = false;
            _mouseDown = false;
            owner.Invalidate();
        };
        owner.EnabledChanged += (_, _) =>
        {
            _spaceDown = false;
            _mouseDown = false;
            Hovered = false;
            owner.Invalidate();
        };
        owner.GotFocus += (_, _) => owner.Invalidate();
        owner.ChangeUICues += (_, _) => owner.Invalidate();
        owner.DpiChangedAfterParent += (_, _) => owner.Invalidate();
        owner.ParentChanged += (_, _) => owner.Invalidate();
    }
}

internal static class RoundedControlRenderer
{
    internal readonly record struct ControlColors(Color Fill, Color Text, Color Border);

    internal static ControlColors Colors(ThemePalette palette, ButtonRole role)
    {
        if (SystemInformation.HighContrast)
            return role is ButtonRole.Primary or ButtonRole.Save
                ? new(SystemColors.Highlight, SystemColors.HighlightText, SystemColors.Highlight)
                : new(SystemColors.Control, SystemColors.ControlText, SystemColors.ControlText);

        return role switch
        {
            ButtonRole.Primary => new(palette.Accent, palette.OnAccent, palette.Accent),
            ButtonRole.Save => new(palette.Success, palette.OnSuccess, palette.Success),
            ButtonRole.Filter => new(palette.AccentSurface, palette.Accent, palette.Accent),
            _ => new(palette.Surface, palette.Text, palette.ControlBorder)
        };
    }

    internal static void Draw(Graphics graphics, ButtonBase control, ThemePalette palette,
        ButtonRole role, RoundedControlInteraction interaction, bool focused,
        bool showKeyboardCues, bool isDefault)
    {
        graphics.Clear(control.Parent?.BackColor ?? palette.Background);
        if (control.Width < 2 || control.Height < 2) return;

        var colors = Colors(palette, role);
        bool highContrast = SystemInformation.HighContrast;
        if (!control.Enabled)
        {
            colors = highContrast
                ? new(SystemColors.Control, SystemColors.GrayText, SystemColors.GrayText)
                : new(palette.Canvas, palette.Muted, palette.Border);
        }
        else if (interaction.Pressed || interaction.Hovered)
        {
            if (highContrast)
                colors = new(SystemColors.Highlight, SystemColors.HighlightText, SystemColors.HighlightText);
            else
            {
                bool filled = role is ButtonRole.Primary or ButtonRole.Save;
                Color tint = filled
                    ? (colors.Text.GetBrightness() > .5f ? Color.Black : Color.White)
                    : palette.Accent;
                // A stronger tint would reduce the filter-label contrast below
                // 4.5:1 in the light palette.
                float amount = interaction.Pressed ? (filled ? .16f : .12f) : .06f;
                Color fill = ThemeManager.Blend(colors.Fill, tint, amount);
                colors = colors with { Fill = fill, Border = filled ? fill : palette.Accent };
            }
        }

        float scale = control.DeviceDpi / 96f;
        float lineWidth = Math.Max(1f, (isDefault && control.Enabled ? 2f : 1f) * scale);
        float inset = lineWidth / 2f;
        var bounds = new RectangleF(inset, inset,
            control.Width - lineWidth, control.Height - lineWidth);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = RoundedRectangle(bounds, 10f * scale);
            using var fill = new SolidBrush(colors.Fill);
            using var border = new Pen(isDefault && control.Enabled ? palette.Accent : colors.Border, lineWidth);
            graphics.FillPath(fill, outline);
            graphics.DrawPath(border, outline);

            if (isDefault && control.Enabled)
            {
                var defaultBounds = RectangleF.Inflate(bounds, -2f * scale, -2f * scale);
                if (defaultBounds.Width > 0 && defaultBounds.Height > 0)
                {
                    using var defaultOutline = RoundedRectangle(defaultBounds, 8f * scale);
                    using var defaultPen = new Pen(colors.Text, Math.Max(1f, scale));
                    graphics.DrawPath(defaultPen, defaultOutline);
                }
            }

            if (focused && control.Enabled)
            {
                var focusBounds = RectangleF.Inflate(bounds, -4f * scale, -4f * scale);
                if (focusBounds.Width > 0 && focusBounds.Height > 0)
                {
                    using var focusOutline = RoundedRectangle(focusBounds, 6f * scale);
                    using var focusPen = new Pen(colors.Text, Math.Max(1f, scale)) { DashStyle = DashStyle.Dot };
                    graphics.DrawPath(focusPen, focusOutline);
                }
            }

            var padding = control.Padding;
            int edge = Math.Max(2, (int)Math.Ceiling(2f * scale));
            var textBounds = new Rectangle(padding.Left + edge, padding.Top + edge,
                Math.Max(0, control.Width - padding.Horizontal - 2 * edge),
                Math.Max(0, control.Height - padding.Vertical - 2 * edge));
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping;
            flags |= control.TextAlign switch
            {
                ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };
            flags |= control.TextAlign switch
            {
                ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
                _ => TextFormatFlags.VerticalCenter
            };
            if (!control.UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!showKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (control.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(graphics, control.Text, control.Font, textBounds, colors.Text, flags);
        }
        finally { graphics.Restore(state); }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
