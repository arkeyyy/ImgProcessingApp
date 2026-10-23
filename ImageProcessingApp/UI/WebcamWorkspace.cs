using System.ComponentModel;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ImageProcessingApp;

/// <summary>Owns capture resources and the two displayed frames; preview controls only borrow them.</summary>
internal sealed class WebcamWorkspace : WorkspaceControl, IThemeAware
{
    private enum CameraState { Unavailable, Stopped, Starting, Running, Stopping, Error }

    private sealed record CameraChoice(string Name, string Moniker)
    {
        public override string ToString() => Name;
    }

    private sealed class PreviewFrame(Bitmap original, Bitmap result, object ticket, int generation, string effect) : IDisposable
    {
        public Bitmap Original { get; } = original;
        public Bitmap Result { get; } = result;
        public object Ticket { get; } = ticket;
        public int Generation { get; } = generation;
        public string Effect { get; } = effect;
        public void Dispose() { Original.Dispose(); Result.Dispose(); }
    }

    private readonly ComboBox _cameras = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 270,
        AccessibleName = "Camera",
        AccessibleDescription = "Choose a camera before starting capture.",
        Margin = new Padding(8, 4, 16, 4)
    };
    private readonly Button _refresh = Ui.Button("Refresh Cameras");
    private readonly Button _toggle = Ui.Button("Start Camera", ButtonRole.Primary);
    private readonly Label _connection = Ui.Label("Camera: Not checked", muted: true);
    private readonly ImagePreviewControl _original = new("Camera", "Choose a camera and select Start Camera.");
    private readonly ImagePreviewControl _result = new("Live Result", "Choose an effect to preview it live.");
    private readonly List<RoundedRadioButton> _effects = [];
    private readonly object _frameLock = new();
    private readonly System.Windows.Forms.Timer _startupTimer = new() { Interval = 15000 };
    private VideoCaptureDevice? _source;
    private PreviewFrame? _pendingFrame;
    private PreviewFrame? _displayedFrame;
    private object? _frameTicket;
    private volatile string _effect = "None";
    private CameraState _state = CameraState.Unavailable;
    private Task? _stopTask;
    private bool _initialized;
    private bool _enumerating;
    private bool _shuttingDown;
    private bool _acceptFrames;
    private int _generation;
    private string _cameraName = "Camera";

    public WebcamWorkspace()
    {
        Dock = DockStyle.Fill;
        var layout = new ImageWorkspaceLayout(2, rowCount: 5, previewRow: 4);

        var heading = Ui.Label("Webcam", heading: true);
        heading.Margin = new Padding(0, 0, 0, 8);
        layout.AddSection(heading, 0);
        var actions = Ui.Flow();
        var cameraLabel = Ui.Label("&Camera:");
        cameraLabel.UseMnemonic = true;
        cameraLabel.Margin = new Padding(0, 10, 0, 0);
        actions.Controls.AddRange([cameraLabel, _cameras, _refresh, _toggle, _connection]);
        _connection.Margin = new Padding(12, 10, 0, 0);
        layout.AddSection(actions, 1);

        var effectRow = Ui.Flow();
        effectRow.AccessibleName = "Camera effects";
        var effectLabel = Ui.Label("Effect:");
        effectLabel.Margin = new Padding(0, 10, 8, 0);
        effectRow.Controls.Add(effectLabel);
        foreach (var effect in new[] { "None", "Grayscale", "Invert", "Sepia", "Subtract" })
        {
            var option = new RoundedRadioButton
            {
                Text = effect,
                AccessibleName = $"{effect} camera effect",
                AccessibleDescription = effect == "Subtract"
                    ? "Replaces pixels close to the brightness of green with black."
                    : $"Apply {effect.ToLowerInvariant()} to each new camera frame.",
                Appearance = Appearance.Button,
                AutoSize = true,
                MinimumSize = new Size(72, 36),
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 0, 8, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Checked = effect == "None"
            };
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                _effect = effect;
                ApplyTheme(ThemeManager.Current);
                if (_state == CameraState.Running)
                    ReportStatus($"Camera running · {effect} effect");
            };
            _effects.Add(option);
            effectRow.Controls.Add(option);
        }
        layout.AddSection(effectRow, 2);
        var explanation = Ui.Label("Effects apply to each original frame. Subtract uses brightness-based keying to black. Leaving this tab stops the camera.", muted: true);
        explanation.Dock = DockStyle.Fill;
        explanation.Margin = new Padding(0, 0, 0, 12);
        explanation.MaximumSize = new Size(1100, 0);
        layout.AddSection(explanation, 3);
        layout.AddSection(new PreviewLayout(_original, _result), 4);
        Controls.Add(layout);

        _refresh.Click += async (_, _) => await RefreshCamerasAsync();
        _toggle.Click += async (_, _) =>
        {
            if (_source is null) await StartCameraAsync();
            else await StopCameraAsync();
        };
        _cameras.SelectedIndexChanged += (_, _) => UpdateActions();
        _startupTimer.Tick += async (_, _) =>
        {
            _startupTimer.Stop();
            if (_state == CameraState.Starting)
                await StopCameraAsync("The camera did not deliver a frame. Check its connection and whether another app is using it, then try again.");
        };
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateActions();
        ReportStatus("Open the Webcam tab to check available cameras.");
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        InitializeWhenVisible();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeWhenVisible();
    }

    private async void InitializeWhenVisible()
    {
        if (_initialized || !Visible || !IsHandleCreated || DesignMode ||
            LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;
        _initialized = true;
        await RefreshCamerasAsync();
    }

    private async Task RefreshCamerasAsync()
    {
        if (_enumerating || _source is not null || _shuttingDown) return;
        _enumerating = true;
        var previous = (_cameras.SelectedItem as CameraChoice)?.Moniker;
        UpdateActions();
        ReportStatus("Checking available cameras…", busy: true);
        try
        {
            var choices = await Task.Run(() => new FilterInfoCollection(FilterCategory.VideoInputDevice)
                .Cast<FilterInfo>().Select(device => new CameraChoice(device.Name, device.MonikerString)).ToArray());
            if (_shuttingDown || IsDisposed) return;
            _cameras.Items.Clear();
            _cameras.Items.AddRange(choices);
            if (choices.Length == 0)
            {
                SetState(CameraState.Unavailable, "No camera found. Connect a camera, then select Refresh Cameras.");
            }
            else
            {
                var selected = Array.FindIndex(choices, choice => choice.Moniker == previous);
                _cameras.SelectedIndex = selected < 0 ? 0 : selected;
                SetState(CameraState.Stopped, "Camera ready. Select Start Camera to begin.");
            }
        }
        catch (Exception ex)
        {
            if (!_shuttingDown && !IsDisposed)
                SetState(CameraState.Error, $"Could not list cameras. Check the connection and try Refresh Cameras. {ex.Message}");
        }
        finally
        {
            _enumerating = false;
            if (!IsDisposed) UpdateActions();
        }
    }

    private async Task StartCameraAsync()
    {
        if (_shuttingDown || _source is not null || _enumerating || _stopTask is { IsCompleted: false } ||
            _cameras.SelectedItem is not CameraChoice camera) return;
        _cameraName = camera.Name;
        var source = new VideoCaptureDevice(camera.Moniker);
        source.NewFrame += OnNewFrame;
        source.VideoSourceError += OnVideoSourceError;
        source.PlayingFinished += OnPlayingFinished;
        lock (_frameLock)
        {
            _generation++;
            _source = source;
            _acceptFrames = true;
        }
        SetState(CameraState.Starting, $"Starting {camera.Name}…");
        try
        {
            // AForge Start creates its worker and returns; waiting for capture is handled by frame events.
            source.Start();
            _startupTimer.Start();
        }
        catch (Exception ex)
        {
            await StopCameraAsync($"Could not start the camera. Check its connection and whether another app is using it. {ex.Message}");
        }
    }

    private void OnNewFrame(object sender, NewFrameEventArgs args)
    {
        object ticket;
        int generation;
        lock (_frameLock)
        {
            if (!_acceptFrames || _shuttingDown || !ReferenceEquals(sender, _source) || _frameTicket is not null) return;
            ticket = new object();
            _frameTicket = ticket;
            generation = _generation;
        }

        Bitmap? original = null;
        Bitmap? result = null;
        PreviewFrame? frame = null;
        try
        {
            var effect = _effect;
            original = (Bitmap)args.Frame.Clone();
            result = ImageOperations.ApplyBasic(original, effect == "None" ? "Copy Original" : effect);
            frame = new PreviewFrame(original, result, ticket, generation, effect);
            original = null;
            result = null;
            lock (_frameLock)
            {
                if (!_acceptFrames || _shuttingDown || generation != _generation)
                {
                    ReleaseTicket(ticket);
                    frame.Dispose();
                    return;
                }
                _pendingFrame = frame;
            }
            var queuedFrame = frame;
            BeginInvoke((Action)(() => PublishFrame(queuedFrame)));
        }
        catch (Exception ex)
        {
            original?.Dispose();
            result?.Dispose();
            lock (_frameLock)
            {
                // Stop may already have detached and disposed this pending frame.
                if (frame is not null && ReferenceEquals(_pendingFrame, frame))
                {
                    _pendingFrame = null;
                    frame.Dispose();
                }
                ReleaseTicket(ticket);
            }
            QueueFailure(sender, generation, $"Could not process a camera frame. Try restarting the camera. {ex.Message}");
        }
    }

    private void PublishFrame(PreviewFrame frame)
    {
        lock (_frameLock)
        {
            if (!ReferenceEquals(_pendingFrame, frame)) return;
            _pendingFrame = null;
            ReleaseTicket(frame.Ticket);
            if (_shuttingDown || !_acceptFrames || frame.Generation != _generation || IsDisposed)
            {
                frame.Dispose();
                return;
            }
        }
        var old = _displayedFrame;
        _displayedFrame = frame;
        _original.SetImage(frame.Original, _cameraName);
        _result.SetImage(frame.Result, frame.Effect);
        old?.Dispose();
        if (_state == CameraState.Starting)
        {
            _startupTimer.Stop();
            SetState(CameraState.Running, $"Camera running · {_effect} effect");
        }
    }

    private void ReleaseTicket(object ticket)
    {
        if (ReferenceEquals(_frameTicket, ticket)) _frameTicket = null;
    }

    private void OnVideoSourceError(object sender, VideoSourceErrorEventArgs args)
    {
        int generation;
        lock (_frameLock) generation = _generation;
        QueueFailure(sender, generation, $"Camera error: {args.Description}. Check the connection and restart the camera.");
    }

    private void OnPlayingFinished(object sender, ReasonToFinishPlaying reason)
    {
        int generation;
        lock (_frameLock) generation = _generation;
        QueueFailure(sender, generation, reason == ReasonToFinishPlaying.DeviceLost
            ? "The camera was disconnected. Reconnect it, then select Refresh Cameras."
            : "The camera stopped sending frames. Check its connection and restart it.");
    }

    private void QueueFailure(object sender, int generation, string message)
    {
        lock (_frameLock)
        {
            if (_shuttingDown || !_acceptFrames || generation != _generation || !ReferenceEquals(sender, _source)) return;
        }
        try
        {
            BeginInvoke((Action)(async () =>
            {
                lock (_frameLock)
                {
                    if (_shuttingDown || !_acceptFrames || generation != _generation || !ReferenceEquals(sender, _source)) return;
                }
                await StopCameraAsync(message);
            }));
        }
        catch (InvalidOperationException) { /* The form handle has already been destroyed. */ }
    }

    private Task StopCameraAsync(string? failure = null)
    {
        if (_stopTask is { IsCompleted: false }) return _stopTask;
        _stopTask = StopCoreAsync(failure);
        return _stopTask;
    }

    private async Task StopCoreAsync(string? failure)
    {
        _startupTimer.Stop();
        VideoCaptureDevice? source;
        lock (_frameLock)
        {
            _acceptFrames = false;
            _generation++;
            source = _source;
            if (_pendingFrame is not null)
            {
                ReleaseTicket(_pendingFrame.Ticket);
                _pendingFrame.Dispose();
                _pendingFrame = null;
            }
        }
        ClearPreviews();
        if (source is null) return;
        source.NewFrame -= OnNewFrame;
        source.VideoSourceError -= OnVideoSourceError;
        source.PlayingFinished -= OnPlayingFinished;
        SetState(CameraState.Stopping, "Stopping camera…");
        var stopped = false;
        string? stopError = null;
        await Task.Run(() =>
        {
            try
            {
                source.SignalToStop();
                source.WaitForStop();
                stopped = true;
            }
            catch (Exception ex)
            {
                stopError = ex.Message;
                // The driver may throw after its thread has already ended. Only release the
                // source when termination is confirmed; otherwise keep it available for retry.
                try { stopped = !source.IsRunning; }
                catch { stopped = false; }
            }
        });
        if (stopped)
        {
            lock (_frameLock)
            {
                if (ReferenceEquals(_source, source)) _source = null;
            }
            if (stopError is not null)
                failure ??= $"The camera stopped, but reported an error: {stopError}";
        }
        else
            failure = $"Could not confirm that the camera stopped. Select Stop Camera to retry. {stopError}";
        if (!IsDisposed)
            SetState(failure is null ? CameraState.Stopped : CameraState.Error, failure ?? "Camera stopped.");
    }

    private void ClearPreviews()
    {
        _original.SetImage(null);
        _result.SetImage(null);
        _displayedFrame?.Dispose();
        _displayedFrame = null;
    }

    private void SetState(CameraState state, string message)
    {
        _state = state;
        _connection.Text = $"Camera: {state}";
        _connection.AccessibleName = _connection.Text;
        UpdateActions();
        ReportStatus(message, busy: state is CameraState.Starting or CameraState.Stopping, error: state == CameraState.Error);
    }

    private void UpdateActions()
    {
        var active = _source is not null;
        _cameras.Enabled = !active && !_enumerating && !_shuttingDown && _cameras.Items.Count > 0;
        _refresh.Enabled = !active && !_enumerating && !_shuttingDown;
        _toggle.Text = active ? "Stop Camera" : "Start Camera";
        _toggle.AccessibleName = _toggle.Text;
        _toggle.Enabled = !_shuttingDown && !_enumerating && _state != CameraState.Stopping &&
            (active || _cameras.SelectedItem is CameraChoice);
    }

    public override Task DeactivateAsync() => StopCameraAsync();

    public override async Task ShutdownAsync()
    {
        _shuttingDown = true;
        await StopCameraAsync();
        if (_source is not null)
        {
            _shuttingDown = false;
            UpdateActions();
            throw new InvalidOperationException(StatusText);
        }
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme(ThemeManager.Current);

    public void ApplyTheme(ThemePalette palette)
    {
        foreach (var option in _effects)
            option.ApplyTheme(palette);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shuttingDown = true;
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _startupTimer.Dispose();
            VideoCaptureDevice? source;
            lock (_frameLock)
            {
                _acceptFrames = false;
                _generation++;
                source = _source;
                _source = null;
                _pendingFrame?.Dispose();
                _pendingFrame = null;
            }
            if (source is not null && _stopTask is not { IsCompleted: false })
            {
                source.NewFrame -= OnNewFrame;
                source.VideoSourceError -= OnVideoSourceError;
                source.PlayingFinished -= OnPlayingFinished;
                // Fallback for disposal outside the shell's awaited ShutdownAsync path.
                _ = Task.Run(() =>
                {
                    try { source.SignalToStop(); source.WaitForStop(); }
                    catch { /* There is no live UI on which to report disposal failures. */ }
                });
            }
            ClearPreviews();
        }
        base.Dispose(disposing);
    }
}
