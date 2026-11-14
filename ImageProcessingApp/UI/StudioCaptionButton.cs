namespace ImageProcessingApp;

internal enum CaptionAction { Minimize, Maximize, Close }

internal sealed class StudioCaptionButton(CaptionAction action) : RoundedButton
{
    public CaptionAction Action { get; } = action;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = DeviceDpi / 96f;
        float size = 10 * scale;
        float x = (Width - size) / 2, y = (Height - size) / 2;
        using var pen = new Pen(GlyphColor, Math.Max(1, scale));
        if (Action == CaptionAction.Minimize) e.Graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
        else if (Action == CaptionAction.Close)
        {
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            pen.Width = Math.Max(1, 1.5f * scale);
            e.Graphics.DrawLine(pen, x, y, x + size, y + size);
            e.Graphics.DrawLine(pen, x + size, y, x, y + size);
            e.Graphics.Restore(state);
        }
        else if (FindForm()?.WindowState == FormWindowState.Maximized)
        {
            // Draw only the exposed rear edges; an opaque fill would erase the
            // hover/pressed background inside the restore glyph.
            e.Graphics.DrawLines(pen, new PointF[]
            {
                new(x + 3 * scale, y + 3 * scale), new(x + 3 * scale, y),
                new(x + size, y), new(x + size, y + size - 3 * scale),
                new(x + size - 3 * scale, y + size - 3 * scale)
            });
            e.Graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
        }
        else e.Graphics.DrawRectangle(pen, x, y, size, size);
    }

}
