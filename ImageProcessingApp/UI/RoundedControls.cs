using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ImageProcessingApp;

internal enum ButtonRole { Secondary, Primary, Save, Filter }

// Keep the native control types so dialog buttons, mnemonics, accessibility and
// radio-button grouping continue to use the standard Windows Forms behavior.
internal class RoundedButton : Button, IThemeAware
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

    internal void SetChromeInteraction(bool hovered, bool pressed) => _interaction.SetExternalState(hovered, pressed);

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

    protected Color GlyphColor => RoundedControlRenderer.ResolveColors(this, _palette, Role, _interaction).Text;

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

    public bool IsNavigation { get; set; }
    private ButtonRole CurrentRole => Checked ? ButtonRole.Primary : ButtonRole.Secondary;

    protected override AccessibleObject CreateAccessibilityInstance() => new StudioRadioAccessibleObject(this);
    private sealed class StudioRadioAccessibleObject(RoundedRadioButton owner) : RadioButtonAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State |
            (owner.IsNavigation && owner.Checked ? AccessibleStates.Selected : AccessibleStates.None);
    }


    protected override void OnCheckedChanged(EventArgs e)
    {
        _interaction?.SelectionChanged(Checked);
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
internal sealed class RoundedControlInteraction : IStudioAnimation
{
    private readonly Control _owner;
    private StudioAnimationScheduler? _scheduler;
    private readonly StudioTransition _hover = new(), _press = new(), _selection = new();
    public float HoverAmount => _hover.Value;
    public float PressAmount => _press.Value;
    public float SelectionAmount => _selection.Value;
    public bool IsAmbient => false;
    private Bitmap? _shadow;
    private ThemePalette? _shadowPalette;
    private int _shadowDpi;
    private bool _mouseDown;
    private bool _spaceDown;

    public bool Hovered { get; private set; }
    public bool Pressed => _spaceDown || (_mouseDown && Hovered);

    public RoundedControlInteraction(Control owner)
    {
        _owner = owner;
        owner.Disposed += (_, _) => { _scheduler?.Remove(this); _shadow?.Dispose(); };
        owner.MouseEnter += (_, _) => { Hovered = true; Update(); };
        owner.MouseLeave += (_, _) => { Hovered = false; Update(); };
        owner.MouseMove += (_, e) =>
        {
            bool inside = owner.ClientRectangle.Contains(e.Location);
            if (inside == Hovered) return;
            Hovered = inside;
            Update();
        };
        owner.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = true;
            Hovered = owner.ClientRectangle.Contains(e.Location);
            Update();
        };
        owner.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = false;
            Update();
        };
        owner.MouseCaptureChanged += (_, _) =>
        {
            if (owner.Capture) return;
            _mouseDown = false;
            Update();
        };
        owner.KeyDown += (_, e) =>
        {
            if (e.KeyData != Keys.Space) return;
            _spaceDown = true;
            Update();
        };
        owner.KeyUp += (_, _) =>
        {
            _spaceDown = false;
            Update();
        };
        owner.LostFocus += (_, _) =>
        {
            _spaceDown = false;
            _mouseDown = false;
            Update();
        };
        owner.EnabledChanged += (_, _) =>
        {
            _spaceDown = false;
            _mouseDown = false;
            Hovered = false;
            Update();
        };
        owner.GotFocus += (_, _) => Update();
        owner.ChangeUICues += (_, _) => Update();
        owner.DpiChangedAfterParent += (_, _) => Update();
        owner.ParentChanged += (_, _) => Update();
    }

    internal void SetExternalState(bool hovered, bool pressed)
    {
        Hovered = hovered;
        _mouseDown = pressed;
        _spaceDown = false;
        Update();
    }

    public void SelectionChanged(bool selected)
    {
        ResolveScheduler();
        _selection.Set(selected ? 1 : 0, _scheduler?.Now ?? 0, 180, _scheduler?.MotionAllowed == true);
        RefreshScheduling();
        _owner.Invalidate();
    }
    private void RefreshScheduling()
    {
        if (_hover.Running || _press.Running || _selection.Running) _scheduler?.Request(this);
        else _scheduler?.Remove(this);
    }
    private void ResolveScheduler()
    {
        var scheduler = (_owner.FindForm() as StudioForm)?.Animations;
        if (ReferenceEquals(_scheduler, scheduler)) return;
        _scheduler?.Remove(this);
        _scheduler = scheduler;
    }
    private void Update()
    {
        ResolveScheduler();
        bool animate = _owner.Enabled && _owner.Visible && _scheduler?.MotionAllowed == true;
        double now = _scheduler?.Now ?? 0;
        _hover.Set(_owner.Enabled && Hovered ? 1 : 0, now, 160, animate);
        _press.Set(_owner.Enabled && Pressed ? 1 : 0, now, 90, animate);
        if (!_owner.Enabled) _selection.Set(_selection.Target, now, 0, false);
        RefreshScheduling();
        _owner.Invalidate();
    }
    public void DrawShadow(Graphics graphics, ThemePalette palette, bool pill)
    {
        if (!_owner.Enabled || SystemInformation.HighContrast || HoverAmount <= 0) return;
        if (_shadow is null || _shadow.Size != _owner.Size || _shadowPalette != palette || _shadowDpi != _owner.DeviceDpi)
        {
            _shadow?.Dispose();
            _shadow = new Bitmap(_owner.Width, _owner.Height);
            _shadowPalette = palette;
            _shadowDpi = _owner.DeviceDpi;
            using var g = Graphics.FromImage(_shadow);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = _owner.DeviceDpi / 96f;
            using var outline = RoundedControlRenderer.RoundedRectangle(new RectangleF(scale, 3 * scale,
                Math.Max(1, _owner.Width - 2 * scale), Math.Max(1, _owner.Height - 4 * scale)), (pill ? 24 : StudioMetrics.ControlRadius) * scale);
            using var pen = new Pen(Color.FromArgb(32, palette.Accent), 3 * scale);
            g.DrawPath(pen, outline);
        }
        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = HoverAmount };
        attributes.SetColorMatrix(matrix);
        graphics.DrawImage(_shadow, _owner.ClientRectangle, 0, 0, _shadow.Width, _shadow.Height, GraphicsUnit.Pixel, attributes);
    }

    public bool Animate(double milliseconds, bool motionAllowed)
    {
        if (_owner.IsDisposed) return false;
        foreach (var value in new[] { _hover, _press, _selection })
        {
            if (!motionAllowed || !_owner.Enabled || !_owner.Visible) value.Set(value.Target, milliseconds, 0, false);
            else value.Advance(milliseconds);
        }
        _owner.Invalidate(false);
        return _hover.Running || _press.Running || _selection.Running;
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

    internal static ControlColors ResolveColors(ButtonBase control, ThemePalette palette,
        ButtonRole role, RoundedControlInteraction interaction)
    {
        var colors = Colors(palette, role);
        if (control is RoundedRadioButton)
        {
            var idle = Colors(palette, ButtonRole.Secondary);
            var selected = Colors(palette, ButtonRole.Primary);
            float amount = interaction.SelectionAmount;
            var fill = ThemeManager.Blend(idle.Fill, selected.Fill, amount);
            colors = new(fill, Contrast(fill, idle.Text) >= Contrast(fill, selected.Text) ? idle.Text : selected.Text,
                ThemeManager.Blend(idle.Border, selected.Border, amount));
        }
        bool highContrast = SystemInformation.HighContrast;
        if (!control.Enabled)
        {
            colors = highContrast
                ? new(SystemColors.Control, SystemColors.GrayText, SystemColors.GrayText)
                : new(palette.Canvas, palette.Muted, palette.Border);
        }
        else if (interaction.PressAmount > 0 || interaction.HoverAmount > 0)
        {
            if (highContrast)
                colors = new(SystemColors.Highlight, SystemColors.HighlightText, SystemColors.HighlightText);
            else
            {
                bool filled = role is ButtonRole.Primary or ButtonRole.Save;
                Color tint = filled
                    ? (colors.Text.GetBrightness() > .5f ? Color.Black : Color.White)
                    : palette.Accent;
                // Keep feedback subtle; text contrast is checked after tinting.
                float amount = .06f * interaction.HoverAmount + (filled ? .10f : .06f) * interaction.PressAmount;
                Color fill = ThemeManager.Blend(colors.Fill, tint, amount);
                colors = colors with { Fill = fill, Border = filled ? fill : ThemeManager.Blend(colors.Border, palette.Accent, Math.Max(interaction.HoverAmount, interaction.PressAmount)) };
            }
        }

        if (control.Enabled && !highContrast && Contrast(colors.Fill, colors.Text) < 4.5)
        {
            var text = Contrast(colors.Fill, Color.Black) >= Contrast(colors.Fill, Color.White) ? Color.Black : Color.White;
            colors = colors with { Text = text };
        }

        return colors;
    }

    internal static void Draw(Graphics graphics, ButtonBase control, ThemePalette palette,
        ButtonRole role, RoundedControlInteraction interaction, bool focused,
        bool showKeyboardCues, bool isDefault)
    {
        graphics.Clear(control.Parent?.BackColor ?? palette.Background);
        if (control.Width < 2 || control.Height < 2) return;

        var colors = ResolveColors(control, palette, role, interaction);

        float scale = control.DeviceDpi / 96f;
        float lineWidth = Math.Max(1f, (isDefault && control.Enabled ? 2f : 1f) * scale);
        interaction.DrawShadow(graphics, palette, control is RoundedRadioButton { IsNavigation: true });
        float inset = lineWidth / 2f + 2 * scale;
        var bounds = new RectangleF(inset, inset,
            control.Width - 2 * inset, control.Height - 2 * inset);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = RoundedRectangle(bounds, (control is RoundedRadioButton { IsNavigation: true } ? 24f : StudioMetrics.ControlRadius) * scale);
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
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
            flags |= control is RoundedRadioButton { IsNavigation: true } ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine;
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

    internal static double Contrast(Color a, Color b)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel) { double c = channel / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
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
