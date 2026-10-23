using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

/// <summary>Displays a borrowed image. The document/camera owns and disposes the bitmap.</summary>
internal sealed class ImagePreviewControl : UserControl, IThemeAware
{
    private readonly Label _heading;
    private readonly Label _metadata;
    private readonly PreviewCanvas _canvas;
    private ThemePalette _palette = ThemeManager.Current;

    public ImagePreviewControl() : this("Image", "Load an image to get started.") { }

    public ImagePreviewControl(string title, string emptyMessage)
    {
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(1);
        Margin = new Padding(6);
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = title;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _heading = Ui.Label(title);
        _heading.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _heading.Padding = new Padding(16, 12, 16, 12);
        _heading.Margin = Padding.Empty;
        _heading.Dock = DockStyle.Fill;
        _canvas = new PreviewCanvas(emptyMessage) { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = title + " preview" };
        _metadata = Ui.Label("No image loaded", muted: true);
        _metadata.AutoSize = false;
        _metadata.Height = 38;
        _metadata.Dock = DockStyle.Fill;
        _metadata.TextAlign = ContentAlignment.MiddleLeft;
        _metadata.AutoEllipsis = true;
        _metadata.Padding = new Padding(16, 0, 16, 0);
        _metadata.Margin = Padding.Empty;
        layout.Controls.Add(_heading, 0, 0);
        layout.Controls.Add(_canvas, 0, 1);
        layout.Controls.Add(_metadata, 0, 2);
        Controls.Add(layout);
    }

    public string Heading
    {
        get => _heading.Text;
        set { _heading.Text = value; AccessibleName = value; _canvas.AccessibleName = value + " preview"; }
    }

    public void SetImage(Image? image, string? detail = null)
    {
        _canvas.Image = image;
        _metadata.Text = image is null ? detail ?? "No image loaded"
            : $"{(string.IsNullOrWhiteSpace(detail) ? "Image" : detail)}  ·  {image.Width:N0} × {image.Height:N0} px";
        _metadata.AccessibleName = _metadata.Text;
        _canvas.AccessibleDescription = image is null ? _canvas.EmptyMessage : _metadata.Text;
        _canvas.Invalidate();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.Border;
        _heading.BackColor = _metadata.BackColor = palette.Surface;
        _heading.ForeColor = palette.Text;
        _metadata.ForeColor = palette.Muted;
        _canvas.Palette = palette;
        _canvas.BackColor = palette.Canvas;
        Invalidate(true);
    }

    private sealed class PreviewCanvas(string emptyMessage) : Control
    {
        public Image? Image { get; set; }
        public string EmptyMessage { get; } = emptyMessage;
        public ThemePalette Palette { get; set; } = ThemeManager.Current;

        protected override void OnCreateControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Graphic;
            TabStop = false;
            base.OnCreateControl();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Palette;
            e.Graphics.Clear(p.Canvas);
            if (Image is null)
            {
                float scale = DeviceDpi / 96f;
                int iconSize = (int)(44 * scale);
                var icon = new Rectangle((Width - iconSize) / 2, Math.Max(8, Height / 2 - (int)(50 * scale)), iconSize, (int)(34 * scale));
                using var pen = new Pen(p.Muted, Math.Max(1, scale));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(pen, icon);
                e.Graphics.DrawEllipse(pen, icon.X + 8 * scale, icon.Y + 7 * scale, 5 * scale, 5 * scale);
                e.Graphics.DrawLines(pen, [new PointF(icon.Left + 4 * scale, icon.Bottom - 5 * scale),
                    new PointF(icon.Left + 17 * scale, icon.Top + 15 * scale),
                    new PointF(icon.Left + 26 * scale, icon.Top + 23 * scale),
                    new PointF(icon.Right - 4 * scale, icon.Top + 12 * scale)]);
                var textArea = new Rectangle((int)(20 * scale), icon.Bottom + (int)(18 * scale),
                    Math.Max(1, Width - (int)(40 * scale)), Math.Max(1, Height - icon.Bottom - (int)(22 * scale)));
                TextRenderer.DrawText(e.Graphics, EmptyMessage, Font, textArea, p.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            float ratio = Math.Min((float)Math.Max(1, Width - 24) / Image.Width, (float)Math.Max(1, Height - 24) / Image.Height);
            int width = Math.Max(1, (int)(Image.Width * ratio));
            int height = Math.Max(1, (int)(Image.Height * ratio));
            var bounds = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
            using var tile = new SolidBrush(ThemeManager.Blend(p.Canvas, p.Muted, .14f));
            int tileSize = Math.Max(8, (int)(10 * DeviceDpi / 96f));
            var saved = e.Graphics.Save();
            e.Graphics.SetClip(bounds);
            for (int y = bounds.Top; y < bounds.Bottom; y += tileSize)
                for (int x = bounds.Left; x < bounds.Right; x += tileSize)
                    if (((x - bounds.Left) / tileSize + (y - bounds.Top) / tileSize) % 2 == 0)
                        e.Graphics.FillRectangle(tile, x, y, tileSize, tileSize);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Image, bounds);
            e.Graphics.Restore(saved);
        }
    }
}

internal sealed class PreviewLayout : Panel
{
    private readonly TableLayoutPanel _grid = new() { Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly ImagePreviewControl[] _previews;
    private bool _arranging;
    private bool? _stacked;

    public PreviewLayout(params ImagePreviewControl[] previews)
    {
        _previews = previews;
        Dock = DockStyle.Fill;
        AutoScroll = false;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        Controls.Add(_grid);
        SizeChanged += (_, _) => Arrange();
        DpiChangedAfterParent += (_, _) => Arrange();
    }

    private void Arrange()
    {
        if (_arranging || IsDisposed || _previews.Length == 0) return;
        _arranging = true;
        try
        {
            float scale = DeviceDpi / 96f;
            bool stacked = Width / scale < 900;
            if (_stacked != stacked)
            {
                _stacked = stacked;
                _grid.SuspendLayout();
                _grid.Controls.Clear();
                _grid.ColumnStyles.Clear();
                _grid.RowStyles.Clear();
                _grid.ColumnCount = stacked ? 1 : _previews.Length;
                _grid.RowCount = stacked ? _previews.Length : 1;
                for (int x = 0; x < _grid.ColumnCount; x++) _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _grid.ColumnCount));
                for (int y = 0; y < _grid.RowCount; y++) _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / _grid.RowCount));
                for (int i = 0; i < _previews.Length; i++) _grid.Controls.Add(_previews[i], stacked ? 0 : i, stacked ? i : 0);
                _grid.ResumeLayout();
            }
            _grid.SetBounds(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        }
        finally { _arranging = false; }
    }
}
