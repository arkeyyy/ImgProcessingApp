using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

internal static class StudioMetrics
{
    public const int Gutter = 24, CardRadius = 16, ControlRadius = 10;
    public static Font Heading(float size = 16)
    {
        var font = new Font("Bahnschrift", size, FontStyle.Regular);
        if (font.Name == "Bahnschrift") return font;
        font.Dispose();
        return new Font("Segoe UI", size, FontStyle.Bold);
    }
    public static void OwnFont(Control control, Font font)
    {
        control.Font = font;
        control.Disposed += (_, _) => font.Dispose();
    }
}

// This panel alone repaints during ambient ticks. Opaque child HWNDs (especially
// image canvases) are excluded by Windows; ticks never invalidate descendants.
internal sealed class StudioBackdrop : TableLayoutPanel, IThemeAware, IStudioAnimation
{
    private Bitmap? _lavender, _teal;
    private ThemePalette _palette = ThemeManager.Current;
    private StudioAnimationScheduler? _scheduler;
    private double _phase, _lastTick;
    public bool IsAmbient => true;

    public StudioBackdrop()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _scheduler = (FindForm() as StudioForm)?.Animations;
        _scheduler?.Request(this);
    }
    protected override void OnSizeChanged(EventArgs e) { ClearCache(); base.OnSizeChanged(e); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { ClearCache(); base.OnDpiChangedAfterParent(e); }
    public void ApplyTheme(ThemePalette palette)
    {
        if (_palette != palette) ClearCache();
        _palette = palette;
        BackColor = palette.Background;
        Invalidate();
    }
    public bool Animate(double milliseconds, bool motionAllowed)
    {
        if (motionAllowed && Visible)
        {
            _phase += Math.Max(0, milliseconds - _lastTick) / 24000 * Math.PI * 2;
            Invalidate(false);
        }
        _lastTick = milliseconds;
        return !IsDisposed;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 1 || Height < 1 || SystemInformation.HighContrast) return;
        int size = Math.Max(1, (int)(480 * DeviceDpi / 96f));
        _lavender ??= Glow(size, _palette.Accent);
        _teal ??= Glow(size, _palette.Success);
        e.Graphics.DrawImageUnscaled(_lavender, (int)(Width * .18 + Math.Sin(_phase) * size * .16), -size / 2);
        e.Graphics.DrawImageUnscaled(_teal, (int)(Width * .72 + Math.Cos(_phase) * size * .12), (int)(Height * .55));
    }
    private static Bitmap Glow(int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, size - 1, size - 1);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(24, color), SurroundColors = [Color.FromArgb(0, color)]
        };
        g.FillPath(brush, path);
        return bitmap;
    }
    private void ClearCache() { _lavender?.Dispose(); _teal?.Dispose(); _lavender = _teal = null; }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _scheduler?.Remove(this); ClearCache(); }
        base.Dispose(disposing);
    }
}

internal class StudioSurface : UserControl, IThemeAware
{
    private Bitmap? _frame;
    private ThemePalette _palette = ThemeManager.Current;
    public StudioSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(4);
    }
    public virtual void ApplyTheme(ThemePalette palette)
    {
        if (_palette != palette) ClearCache();
        _palette = palette;
        BackColor = palette.Background;
        Invalidate();
    }
    protected override void OnSizeChanged(EventArgs e) { ClearCache(); base.OnSizeChanged(e); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { ClearCache(); base.OnDpiChangedAfterParent(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 12 || Height < 12) return;
        if (_frame is null)
        {
            _frame = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(_frame);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            var bounds = new RectangleF(2 * scale, scale, Width - 4 * scale, Height - 4 * scale);
            using var shadowPath = RoundedControlRenderer.RoundedRectangle(new RectangleF(bounds.X, bounds.Y + 2 * scale, bounds.Width, bounds.Height), StudioMetrics.CardRadius * scale);
            using var shadow = new SolidBrush(Color.FromArgb(18, Color.Black));
            if (!SystemInformation.HighContrast) g.FillPath(shadow, shadowPath);
            using var outline = RoundedControlRenderer.RoundedRectangle(bounds, StudioMetrics.CardRadius * scale);
            using var fill = new SolidBrush(_palette.Surface);
            using var border = new Pen(_palette.Border, scale);
            g.FillPath(fill, outline);
            g.DrawPath(border, outline);
        }
        e.Graphics.DrawImageUnscaled(_frame, 0, 0);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // Round the content without clipping the cached outer shadow.
        foreach (Control child in Controls)
        {
            if (child.Width < 2 || child.Height < 2) continue;
            using var path = RoundedControlRenderer.RoundedRectangle(child.ClientRectangle, 13 * DeviceDpi / 96f);
            var old = child.Region;
            child.Region = new Region(path);
            old?.Dispose();
        }
    }
    private void ClearCache() { _frame?.Dispose(); _frame = null; }
    protected override void Dispose(bool disposing) { if (disposing) ClearCache(); base.Dispose(disposing); }
}
