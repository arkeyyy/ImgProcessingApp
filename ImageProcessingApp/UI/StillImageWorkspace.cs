namespace ImageProcessingApp;

/// <summary>Owns the lifecycle of a single asynchronous workspace operation.</summary>
internal abstract class AsyncImageWorkspace : WorkspaceControl
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task _operation = Task.CompletedTask;
    private bool _tokenDisposed;
    protected bool IsWorking { get; private set; }
    protected bool IsStopping { get; private set; }

    protected abstract void UpdateActions();

    protected Task RunOperationAsync(string message, Func<CancellationToken, Task> action)
    {
        if (IsWorking || IsStopping || IsDisposed) return Task.CompletedTask;
        IsWorking = true;
        ReportStatus(message, busy: true);
        UpdateActions();
        _operation = ExecuteAsync(action);
        return _operation;
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsStopping && !IsDisposed)
                ReportStatus($"Could not complete the operation. {ex.Message} Check the image or location and try again.", error: true);
        }
        finally
        {
            IsWorking = false;
            if (!IsStopping && !IsDisposed) UpdateActions();
        }
    }

    public override async Task ShutdownAsync()
    {
        IsStopping = true;
        if (!_tokenDisposed) _lifetime.Cancel();
        await _operation;
        if (!_tokenDisposed)
        {
            _lifetime.Dispose();
            _tokenDisposed = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_tokenDisposed)
        {
            IsStopping = true;
            _lifetime.Cancel();
            if (_operation.IsCompleted)
                _lifetime.Dispose();
            else
                _ = _operation.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
            _tokenDisposed = true;
        }
        base.Dispose(disposing);
    }
}

internal static class ImageWorkspaceDialogs
{
    public static OpenFileDialog Open(string title) => new()
    {
        Title = title,
        Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
        CheckFileExists = true,
        Multiselect = false,
        RestoreDirectory = true
    };

    public static SaveFileDialog Save(string? filename) => new()
    {
        Title = "Save Result",
        Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap image (*.bmp)|*.bmp",
        DefaultExt = "png",
        AddExtension = true,
        OverwritePrompt = true,
        FileName = string.IsNullOrEmpty(filename) ? "result" : $"{Path.GetFileNameWithoutExtension(filename)}-result",
        RestoreDirectory = true
    };

    public static ImageWorkspaceLayout Layout(int previewCount = 2) => new(previewCount);

    public static void AddAutoRow(TableLayoutPanel panel, Control control)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, row);
    }

    public static void WrapLabel(Label label)
    {
        label.AutoSize = true;
        label.Dock = DockStyle.Top;
        label.Margin = new Padding(0, 4, 0, 12);
        label.MaximumSize = new Size(1600, 0);
    }
}

/// <summary>One scrolling viewport; its table always reserves usable image space.</summary>
internal sealed class ImageWorkspaceLayout : Panel
{
    private readonly TableLayoutPanel _table = new()
    {
        ColumnCount = 1,
        Margin = Padding.Empty,
        Padding = new Padding(16),
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
    };
    private readonly Control?[] _sections;
    private readonly int _previewCount;
    private readonly int _previewRow;
    private bool _arranging;

    public ImageWorkspaceLayout(int previewCount, int rowCount = 4, int previewRow = 2)
    {
        _previewCount = previewCount;
        _previewRow = previewRow;
        _sections = new Control?[rowCount];
        _table.RowCount = rowCount;
        for (int row = 0; row < rowCount; row++)
            _table.RowStyles.Add(new RowStyle(row == previewRow ? SizeType.Percent : SizeType.Absolute, row == previewRow ? 100 : 0));
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        Controls.Add(_table);
        _table.Layout += (_, _) => ArrangeSections();
        DpiChangedAfterParent += (_, _) => ArrangeSections();
    }

    public void AddSection(Control control, int row)
    {
        _sections[row] = control;
        _table.Controls.Add(control, 0, row);
        ArrangeSections();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ArrangeSections();
    }

    private void ArrangeSections()
    {
        if (_arranging || IsDisposed || _sections.Any(section => section is null)) return;
        _arranging = true;
        try
        {
            float scale = DeviceDpi / 96f;
            // ClientSize excludes an existing scrollbar. Restore that width before deciding
            // whether the new layout needs one, avoiding a width/scrollbar feedback loop.
            int fullWidth = ClientSize.Width + (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            int tableWidth = Math.Max(1, fullWidth);
            int minimumHeight = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int contentWidth = Math.Max(1, tableWidth - _table.Padding.Horizontal);
                int controlsHeight = 0;
                for (int row = 0; row < _sections.Length; row++)
                {
                    if (row == _previewRow) continue;
                    int rowHeight = Measure(_sections[row]!, contentWidth);
                    _table.RowStyles[row].Height = rowHeight;
                    controlsHeight += rowHeight;
                }
                bool stacked = contentWidth / scale < 900;
                int previewHeight = (int)Math.Ceiling((stacked ? 250 * _previewCount : 280) * scale);
                minimumHeight = _table.Padding.Vertical + controlsHeight + previewHeight;
                int desiredWidth = Math.Max(1, fullWidth - (minimumHeight > ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0));
                if (tableWidth == desiredWidth) break;
                tableWidth = desiredWidth;
            }
            int tableHeight = Math.Max(ClientSize.Height, minimumHeight);
            AutoScrollMinSize = new Size(0, minimumHeight);
            _table.SetBounds(AutoScrollPosition.X, AutoScrollPosition.Y, tableWidth, tableHeight);
        }
        finally { _arranging = false; }
    }

    private static int Measure(Control control, int width)
    {
        int contentWidth = Math.Max(1, width - control.Margin.Horizontal);
        if (control is Label label) label.MaximumSize = new Size(contentWidth, 0);
        if (control is TableLayoutPanel { ColumnCount: 1 } table)
        {
            int height = table.Padding.Vertical;
            foreach (Control child in table.Controls)
                height += Measure(child, Math.Max(1, contentWidth - table.Padding.Horizontal));
            return height + control.Margin.Vertical;
        }
        return control.GetPreferredSize(new Size(contentWidth, 0)).Height + control.Margin.Vertical;
    }
}

internal sealed class StillImageWorkspace : AsyncImageWorkspace
{
    private readonly bool _convolution;
    private readonly ImageDocument _document = new();
    private readonly ImagePreviewControl _original = new("Original", "Load an image to begin.");
    private readonly ImagePreviewControl _result = new("Result", "Choose a filter to compare the result.");
    private readonly Button _load = Ui.Button("Load Image", ButtonRole.Primary);
    private readonly Button _save = Ui.Button("Save Result", ButtonRole.Save);
    private readonly Button _clear = Ui.Button("Clear Images");
    private readonly Button _histogram = Ui.Button("Histogram");
    private readonly Label _guidance = Ui.Label("Every filter starts from the original image.", muted: true);
    private readonly Label _description = Ui.Label("Choose a filter. Focus a button to read how it works.", muted: true);
    private readonly List<Button> _filters = new();
    private readonly ToolTip _tips = new();
    private string? _resultEffect;

    public StillImageWorkspace(bool convolution)
    {
        _convolution = convolution;
        Dock = DockStyle.Fill;
        AccessibleName = convolution ? "Convolution workspace" : "Basic filters workspace";
        var layout = ImageWorkspaceDialogs.Layout();
        var actions = Ui.Flow();
        actions.Dock = DockStyle.Fill;
        actions.Controls.AddRange([_load, _save, _clear]);
        if (!convolution) actions.Controls.Add(_histogram);
        ImageWorkspaceDialogs.WrapLabel(_guidance);
        layout.AddSection(actions, 0);
        layout.AddSection(_guidance, 1);
        layout.AddSection(new PreviewLayout(_original, _result), 2);

        var filterArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0, 12, 0, 0),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };
        if (convolution)
        {
            AddFilterGroup(filterArea, "Smooth & sharpen",
                ("Smooth", "Average neighboring pixels to soften fine detail."),
                ("Gaussian Blur", "Use weighted neighboring pixels for a softer blur."),
                ("Sharpen", "Increase contrast around details and edges."),
                ("Mean Removal", "Emphasize fine details by subtracting nearby values."),
                ("Laplacian", "Highlight sudden changes in intensity to reveal edges."));
            AddFilterGroup(filterArea, "Emboss",
                ("Horizontal + Vertical", "Create an embossed effect from horizontal and vertical edges."),
                ("All Directions", "Reveal edge relief in all directions."),
                ("Lossy", "Apply the original lossy emboss kernel for a stylized relief."),
                ("Horizontal Only", "Emphasize horizontal edge relief."),
                ("Vertical Only", "Emphasize vertical edge relief."));
        }
        else
        {
            AddFilterGroup(filterArea, "Color filters",
                ("Copy Original", "Create a fresh copy of the original image."),
                ("Grayscale", "Average red, green, and blue to show each pixel's intensity."),
                ("Invert", "Replace each color channel with its opposite: 255 minus its value."),
                ("Sepia", "Blend the color channels into warm brown tones."));
        }
        ImageWorkspaceDialogs.WrapLabel(_description);
        _description.Margin = new Padding(0, 4, 0, 0);
        ImageWorkspaceDialogs.AddAutoRow(filterArea, _description);
        layout.AddSection(filterArea, 3);
        Controls.Add(layout);

        _load.Click += (_, _) => OpenImage();
        _save.Click += (_, _) => SaveResult();
        _clear.Click += (_, _) => ClearImages();
        _histogram.Click += async (_, _) => await ShowHistogramAsync();
        _histogram.AccessibleDescription = "Analyze the result if available, otherwise the original. Opens a separate histogram window.";
        _tips.SetToolTip(_histogram, _histogram.AccessibleDescription);
        UpdateActions();
        ThemeManager.Apply(this);
    }

    private void AddFilterGroup(TableLayoutPanel area, string heading, params (string Name, string Description)[] filters)
    {
        var title = Ui.Label(heading);
        title.Margin = new Padding(0, 4, 0, 4);
        ImageWorkspaceDialogs.AddAutoRow(area, title);
        var buttons = Ui.Flow();
        buttons.Dock = DockStyle.Fill;
        foreach (var filter in filters)
        {
            var button = Ui.Button(filter.Name, ButtonRole.Filter);
            button.AccessibleDescription = filter.Description + " Always starts from the original image.";
            _tips.SetToolTip(button, button.AccessibleDescription);
            button.Enter += (_, _) => _description.Text = filter.Description;
            button.MouseEnter += (_, _) => _description.Text = filter.Description;
            button.Click += async (_, _) => await ApplyFilterAsync(filter.Name);
            _filters.Add(button);
            buttons.Controls.Add(button);
        }
        ImageWorkspaceDialogs.AddAutoRow(area, buttons);
    }

    public override async void OpenImage()
    {
        if (IsWorking || IsStopping) return;
        using var dialog = ImageWorkspaceDialogs.Open("Load Original Image");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.FileName;
        await RunOperationAsync("Loading image...", async token =>
        {
            var loaded = await Task.Run(() => ImageOperations.LoadDetached(path), token);
            if (IsStopping || IsDisposed) { loaded.Dispose(); return; }
            _original.SetImage(null);
            _result.SetImage(null);
            _document.SetSource(loaded, Path.GetFileName(path));
            _resultEffect = null;
            RefreshPreviews();
            ReportStatus($"Loaded {Path.GetFileName(path)}. Choose a filter to begin.");
        });
    }

    public override async void SaveResult()
    {
        if (IsWorking || IsStopping || _document.Result is null) return;
        using var dialog = ImageWorkspaceDialogs.Save(_document.FileName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.FileName;
        var format = dialog.FilterIndex;
        await RunOperationAsync("Saving result...", async token =>
        {
            using var snapshot = (Bitmap)_document.Result.Clone();
            await Task.Run(() => ImageOperations.Save(snapshot, path, format), token);
            if (!IsStopping && !IsDisposed) ReportStatus($"Saved {Path.GetFileName(path)}.");
        });
    }

    private Task ApplyFilterAsync(string effect)
    {
        if (_document.Source is null) return Task.CompletedTask;
        return RunOperationAsync($"Applying {effect} to the original...", async token =>
        {
            using var snapshot = (Bitmap)_document.Source.Clone();
            var result = await Task.Run(() => _convolution
                ? ImageOperations.ApplyConvolution(snapshot, effect, token)
                : ImageOperations.ApplyBasic(snapshot, effect, token), token);
            if (IsStopping || IsDisposed) { result.Dispose(); return; }
            _result.SetImage(null);
            _document.SetResult(result);
            _resultEffect = effect;
            RefreshPreviews();
            ReportStatus($"{effect} complete. Result is ready to save.");
        });
    }

    private Task ShowHistogramAsync()
    {
        if (_document.HistogramSource is null) return Task.CompletedTask;
        var sourceName = _document.Result is null ? "Original" : "Result";
        return RunOperationAsync($"Calculating histogram from {sourceName.ToLowerInvariant()}...", async token =>
        {
            using var snapshot = (Bitmap)_document.HistogramSource.Clone();
            var values = await Task.Run(() => ImageOperations.Histogram(snapshot, token), token);
            if (IsStopping || IsDisposed) return;
            ReportStatus($"Histogram calculated from {sourceName.ToLowerInvariant()}.");
            using var histogram = new HistogramDialog(values, sourceName, _document.FileName);
            histogram.ShowDialog(this);
        });
    }

    private void ClearImages()
    {
        if (IsWorking || IsStopping) return;
        _original.SetImage(null);
        _result.SetImage(null);
        _document.Clear();
        _resultEffect = null;
        UpdateActions();
        ReportStatus("Images cleared. Load an image to begin.");
    }

    private void RefreshPreviews()
    {
        _original.SetImage(_document.Source, _document.FileName);
        _result.SetImage(_document.Result, _resultEffect is null ? null : $"{_resultEffect} · {_document.FileName}");
    }

    protected override void UpdateActions()
    {
        var ready = !IsWorking && !IsStopping;
        var source = _document.Source;
        var tooSmall = _convolution && source is not null && (source.Width < 3 || source.Height < 3);
        _load.Enabled = ready;
        _save.Enabled = ready && _document.Result is not null;
        _clear.Enabled = ready && source is not null;
        _histogram.Enabled = ready && _document.HistogramSource is not null;
        foreach (var button in _filters) button.Enabled = ready && source is not null && !tooSmall;
        _guidance.Text = tooSmall
            ? "Convolution requires an image at least 3 × 3 pixels. Load a larger image to use these filters."
            : _convolution
                ? "Every filter starts from the original. Transparent areas are flattened onto white for convolution."
                : "Every filter starts from the original. Histogram analyzes the result when available, otherwise the original.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _original.SetImage(null);
            _result.SetImage(null);
            _document.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
