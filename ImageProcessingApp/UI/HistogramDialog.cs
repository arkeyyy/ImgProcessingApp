using System.Drawing.Drawing2D;

namespace ImageProcessingApp;

internal sealed class HistogramDialog : Form, IThemeAware
{
    private readonly DataGridView _values;

    public HistogramDialog(int[] values, string sourceName, string? filename)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != 256) throw new ArgumentException("A histogram must have 256 intensity bins.", nameof(values));
        Text = $"Histogram · {sourceName}";
        AccessibleName = $"Intensity histogram for {sourceName.ToLowerInvariant()} image";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
            RowStyles =
            {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize),
                new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.AutoSize)
            }
        };
        var heading = Ui.Label($"Source: {sourceName} · {filename ?? "Image"}", heading: true);
        ImageWorkspaceDialogs.WrapLabel(heading);
        layout.Controls.Add(heading, 0, 0);
        long count = values.Sum(value => (long)value);
        var info = Ui.Label($"{count:N0} pixels · Intensity is the average of red, green, and blue. Use the Values tab to read exact frequencies.", muted: true);
        ImageWorkspaceDialogs.WrapLabel(info);
        layout.Controls.Add(info, 0, 1);

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill, AccessibleName = "Histogram views" };
        var chartPage = new TabPage("Chart") { Padding = new Padding(8) };
        chartPage.Controls.Add(new HistogramChart(values) { Dock = DockStyle.Fill });
        var valuesPage = new TabPage("Values") { Padding = new Padding(8) };
        _values = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            AccessibleName = "Intensity frequencies",
            AccessibleDescription = "256 rows from intensity zero, black, to intensity 255, white. Arrow keys navigate the frequency values."
        };
        _values.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Intensity (0–255)", Name = "Intensity", SortMode = DataGridViewColumnSortMode.NotSortable });
        _values.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Frequency (pixels)", Name = "Frequency", SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0" } });
        for (var intensity = 0; intensity < values.Length; intensity++) _values.Rows.Add(intensity, values[intensity]);
        valuesPage.Controls.Add(_values);
        tabs.TabPages.AddRange([chartPage, valuesPage]);
        layout.Controls.Add(tabs, 0, 2);

        var footer = Ui.Flow();
        footer.Dock = DockStyle.Fill;
        footer.FlowDirection = FlowDirection.RightToLeft;
        footer.Margin = new Padding(0, 12, 0, 0);
        var close = Ui.Button("Close");
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        footer.Controls.Add(close);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        ThemeManager.ThemeChanged += OnThemeChanged;
        ThemeManager.Apply(this);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Background;
        ForeColor = palette.Text;
        _values.BackgroundColor = palette.Surface;
        _values.GridColor = palette.Border;
        _values.DefaultCellStyle.BackColor = palette.Surface;
        _values.DefaultCellStyle.ForeColor = palette.Text;
        _values.DefaultCellStyle.SelectionBackColor = palette.Accent;
        _values.DefaultCellStyle.SelectionForeColor = palette.OnAccent;
        _values.ColumnHeadersDefaultCellStyle.BackColor = palette.Background;
        _values.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        _values.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Background;
        _values.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ThemeManager.Apply(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing) ThemeManager.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

internal sealed class HistogramChart : Control, IThemeAware
{
    private readonly int[] _values;
    private ThemePalette _palette = ThemeManager.Current;

    public HistogramChart(int[] values)
    {
        _values = (int[])values.Clone();
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = "Intensity histogram chart";
        AccessibleDescription = "Horizontal axis: intensity from zero to 255. Vertical axis: pixel frequency. Exact values are available on the Values tab.";
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var scale = DeviceDpi / 96f;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        using var textBrush = new SolidBrush(_palette.Text);
        using var mutedBrush = new SolidBrush(_palette.Muted);
        using var barBrush = new SolidBrush(_palette.Accent);
        using var borderPen = new Pen(_palette.Border);
        var max = Math.Max(1, _values.Max());
        var maxText = max.ToString("N0");
        var left = Math.Max(52 * scale, graphics.MeasureString(maxText, Font).Width + 12 * scale);
        var plot = new RectangleF(left, 30 * scale, Math.Max(1, Width - left - 18 * scale), Math.Max(1, Height - 88 * scale));
        if (plot.Width < 30 || plot.Height < 20) return;
        graphics.DrawString("Frequency (pixels)", Font, textBrush, 0, 0);
        for (var step = 0; step <= 4; step++)
        {
            float fraction = step / 4f;
            float y = plot.Bottom - fraction * plot.Height;
            graphics.DrawLine(borderPen, plot.Left, y, plot.Right, y);
            string label = Math.Round(max * (double)fraction).ToString("N0");
            var measured = graphics.MeasureString(label, Font);
            graphics.DrawString(label, Font, mutedBrush, plot.Left - measured.Width - 6 * scale, y - measured.Height / 2);
        }
        var barWidth = plot.Width / _values.Length;
        for (var intensity = 0; intensity < _values.Length; intensity++)
        {
            var barHeight = _values[intensity] / (float)max * plot.Height;
            if (barHeight > 0)
                graphics.FillRectangle(barBrush, plot.Left + intensity * barWidth, plot.Bottom - barHeight, Math.Max(1, barWidth), barHeight);
        }
        graphics.DrawLine(borderPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        foreach (var intensity in new[] { 0, 64, 128, 192, 255 })
        {
            var label = intensity.ToString();
            var measured = graphics.MeasureString(label, Font);
            var x = plot.Left + intensity / 255f * plot.Width;
            graphics.DrawString(label, Font, mutedBrush, x - measured.Width / 2, plot.Bottom + 4 * scale);
        }
        var caption = "Intensity (0 = black, 255 = white)";
        var captionSize = graphics.MeasureString(caption, Font);
        graphics.DrawString(caption, Font, textBrush, plot.Left + (plot.Width - captionSize.Width) / 2, plot.Bottom + 27 * scale);
    }
}
