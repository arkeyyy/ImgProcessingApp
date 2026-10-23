namespace ImageProcessingApp;

internal sealed class SubtractionWorkspace : AsyncImageWorkspace
{
    private readonly ImagePreviewControl _foregroundPreview = new("Foreground", "Load the image containing the area to replace.");
    private readonly ImagePreviewControl _backgroundPreview = new("Background", "Load the replacement background image.");
    private readonly ImagePreviewControl _resultPreview = new("Result", "Load both inputs, then choose Subtract.");
    private readonly Button _loadForeground = Ui.Button("Load Foreground", ButtonRole.Primary);
    private readonly Button _loadBackground = Ui.Button("Load Background", ButtonRole.Primary);
    private readonly Button _save = Ui.Button("Save Result", ButtonRole.Save);
    private readonly Button _clear = Ui.Button("Clear Images");
    private readonly Button _subtract = Ui.Button("Subtract", ButtonRole.Primary);
    private readonly Label _dimensions = Ui.Label("Load both images to begin.", muted: true);
    private Bitmap? _foreground;
    private Bitmap? _background;
    private Bitmap? _result;
    private string? _foregroundName;
    private string? _backgroundName;

    public SubtractionWorkspace()
    {
        Dock = DockStyle.Fill;
        AccessibleName = "Background subtraction workspace";
        var layout = ImageWorkspaceDialogs.Layout(previewCount: 3);
        var actions = Ui.Flow();
        actions.Dock = DockStyle.Fill;
        actions.Controls.AddRange([_loadForeground, _loadBackground, _save, _clear]);
        layout.AddSection(actions, 0);
        var description = Ui.Label("Brightness keying replaces foreground pixels with average RGB brightness from 80 to 90. Images align at their top-left corners.", muted: true);
        ImageWorkspaceDialogs.WrapLabel(description);
        layout.AddSection(description, 1);
        layout.AddSection(new PreviewLayout(_foregroundPreview, _backgroundPreview, _resultPreview), 2);

        var controls = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0, 12, 0, 0),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };
        var processActions = Ui.Flow();
        processActions.Dock = DockStyle.Fill;
        processActions.Controls.Add(_subtract);
        ImageWorkspaceDialogs.AddAutoRow(controls, processActions);
        ImageWorkspaceDialogs.WrapLabel(_dimensions);
        _dimensions.Margin = new Padding(0, 4, 0, 0);
        ImageWorkspaceDialogs.AddAutoRow(controls, _dimensions);
        layout.AddSection(controls, 3);
        Controls.Add(layout);

        _loadForeground.Click += (_, _) => OpenImage();
        _loadBackground.Click += async (_, _) => await LoadInputAsync(foreground: false);
        _save.Click += (_, _) => SaveResult();
        _clear.Click += (_, _) => ClearImages();
        _subtract.Click += async (_, _) => await SubtractAsync();
        _subtract.AccessibleDescription = "Replace foreground pixels with average brightness within 5 of 85 using pixels from the background. The result uses the overlapping dimensions.";
        UpdateActions();
        ThemeManager.Apply(this);
    }

    public override async void OpenImage() => await LoadInputAsync(foreground: true);

    private async Task LoadInputAsync(bool foreground)
    {
        if (IsWorking || IsStopping) return;
        using var dialog = ImageWorkspaceDialogs.Open(foreground ? "Load Foreground Image" : "Load Background Image");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.FileName;
        await RunOperationAsync(foreground ? "Loading foreground..." : "Loading background...", async token =>
        {
            var loaded = await Task.Run(() => ImageOperations.LoadDetached(path), token);
            if (IsStopping || IsDisposed) { loaded.Dispose(); return; }
            if (foreground)
            {
                _foregroundPreview.SetImage(null);
                _foreground?.Dispose();
                _foreground = loaded;
                _foregroundName = Path.GetFileName(path);
                _foregroundPreview.SetImage(loaded, _foregroundName);
            }
            else
            {
                _backgroundPreview.SetImage(null);
                _background?.Dispose();
                _background = loaded;
                _backgroundName = Path.GetFileName(path);
                _backgroundPreview.SetImage(loaded, _backgroundName);
            }
            _resultPreview.SetImage(null);
            _result?.Dispose();
            _result = null;
            ReportStatus($"Loaded {(foreground ? "foreground" : "background")}: {Path.GetFileName(path)}.");
        });
    }

    private Task SubtractAsync()
    {
        if (_foreground is null || _background is null) return Task.CompletedTask;
        return RunOperationAsync("Subtracting background...", async token =>
        {
            using var foreground = (Bitmap)_foreground.Clone();
            using var background = (Bitmap)_background.Clone();
            var result = await Task.Run(() => ImageOperations.Subtract(foreground, background, token), token);
            if (IsStopping || IsDisposed) { result.Dispose(); return; }
            _resultPreview.SetImage(null);
            _result?.Dispose();
            _result = result;
            _resultPreview.SetImage(result, $"{_foregroundName} + {_backgroundName}");
            ReportStatus($"Subtraction complete. Result: {result.Width:N0} × {result.Height:N0} pixels.");
        });
    }

    public override async void SaveResult()
    {
        if (IsWorking || IsStopping || _result is null) return;
        using var dialog = ImageWorkspaceDialogs.Save(_foregroundName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.FileName;
        var format = dialog.FilterIndex;
        await RunOperationAsync("Saving result...", async token =>
        {
            using var snapshot = (Bitmap)_result.Clone();
            await Task.Run(() => ImageOperations.Save(snapshot, path, format), token);
            if (!IsStopping && !IsDisposed) ReportStatus($"Saved {Path.GetFileName(path)}.");
        });
    }

    private void ClearImages()
    {
        if (IsWorking || IsStopping) return;
        ReleaseImages();
        UpdateActions();
        ReportStatus("Images cleared. Load a foreground and background to begin.");
    }

    protected override void UpdateActions()
    {
        var ready = !IsWorking && !IsStopping;
        _loadForeground.Enabled = ready;
        _loadBackground.Enabled = ready;
        _save.Enabled = ready && _result is not null;
        _clear.Enabled = ready && (_foreground is not null || _background is not null);
        _subtract.Enabled = ready && _foreground is not null && _background is not null;
        if (_foreground is not null && _background is not null)
        {
            var width = Math.Min(_foreground.Width, _background.Width);
            var height = Math.Min(_foreground.Height, _background.Height);
            _dimensions.Text = _foreground.Size == _background.Size
                ? $"Result size: {width:N0} × {height:N0} pixels. Key brightness: 85, tolerance: ±5."
                : $"Different input sizes: the result uses their top-left overlap ({width:N0} × {height:N0} pixels). Key brightness: 85, tolerance: ±5.";
        }
        else
            _dimensions.Text = _foreground is null && _background is null
                ? "Load both images to begin."
                : _foreground is null ? "Load a foreground image to enable subtraction." : "Load a background image to enable subtraction.";
    }

    private void ReleaseImages()
    {
        _foregroundPreview.SetImage(null);
        _backgroundPreview.SetImage(null);
        _resultPreview.SetImage(null);
        _foreground?.Dispose();
        _background?.Dispose();
        _result?.Dispose();
        _foreground = _background = _result = null;
        _foregroundName = _backgroundName = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseImages();
        base.Dispose(disposing);
    }
}
