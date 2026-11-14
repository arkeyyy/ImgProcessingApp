using Microsoft.Win32;

namespace ImageProcessingApp;

public partial class Form1 : StudioForm
{
    private readonly StudioNavigation _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false, Width = 100 };
    private readonly List<WorkspaceControl> _workspaces = [];
    private WorkspaceControl? _activeWorkspace;
    private bool _closing;
    private bool _shutdownComplete;
    private RadioButton? _lightTheme;
    private RadioButton? _darkTheme;
    private CheckBox? _reduceMotion;

    public Form1()
    {
        SuspendLayout();
        InitializeComponent();
        ThemeManager.Initialize();
        BuildShell();
        ThemeManager.ThemeChanged += OnThemeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ThemeManager.Apply(this);
        ResumeLayout(true);
    }

    private void BuildShell()
    {
        var shell = new StudioBackdrop { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(StudioMetrics.Gutter, 16, StudioMetrics.Gutter, 16), Margin = Padding.Empty, BackColor = Color.Transparent, Tag = "ambient" };
        var title = Ui.Label("Your creative workspace", heading: true);
        var subtitle = Ui.Label("Explore filters, compare results, and make something new.", muted: true);
        title.Tag = "ambient";
        subtitle.Tag = "ambient-muted";
        header.Controls.AddRange([title, subtitle]);
        header.SizeChanged += (_, _) => subtitle.MaximumSize = new Size(Math.Max(100, header.ClientSize.Width - header.Padding.Horizontal), 0);
        var appearance = Ui.Flow();
        appearance.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        appearance.Dock = DockStyle.None;
        appearance.WrapContents = false;
        appearance.Tag = "surface";
        var light = new RoundedRadioButton { Text = "Light", AccessibleName = "Light theme", AutoSize = true, Padding = new Padding(12, 6, 12, 6), Checked = !ThemeManager.IsDark, Margin = new Padding(0, 0, 4, 0) };
        var dark = new RoundedRadioButton { Text = "Dark", AccessibleName = "Dark theme", AutoSize = true, Padding = new Padding(12, 6, 12, 6), Checked = ThemeManager.IsDark, Margin = Padding.Empty };
        _lightTheme = light;
        _darkTheme = dark;
        light.CheckedChanged += (_, _) => { if (light.Checked) ChangeTheme(false); };
        dark.CheckedChanged += (_, _) => { if (dark.Checked) ChangeTheme(true); };
        _reduceMotion = new CheckBox { Text = "Reduce motion", AccessibleName = "Reduce motion", AutoSize = true,
            Checked = ThemeManager.ReduceMotion, Margin = new Padding(12, 8, 16, 0) };
        _reduceMotion.CheckedChanged += (_, _) =>
        {
            if (_reduceMotion.Checked != ThemeManager.ReduceMotion && !ThemeManager.SetReduceMotion(_reduceMotion.Checked))
                _status.Text = "Motion preference changed, but could not be saved.";
        };
        appearance.Controls.AddRange([light, dark, _reduceMotion]);
        AddWorkspace("Basic Filters", new StillImageWorkspace(false));
        AddWorkspace("Background Subtraction", new SubtractionWorkspace());
        AddWorkspace("Webcam", new WebcamWorkspace());
        AddWorkspace("Convolution", new StillImageWorkspace(true));
        _tabs.Margin = new Padding(StudioMetrics.Gutter, 0, StudioMetrics.Gutter, 8);
        _tabs.SelectedIndexChanged += OnWorkspaceChanged;
        _activeWorkspace = _workspaces[0];
        var statusStrip = new StatusStrip { SizingGrip = false, ShowItemToolTips = true, Dock = DockStyle.Fill, Padding = new Padding(24, 4, 16, 4), AccessibleName = "Workspace status" };
        statusStrip.Items.AddRange([_status, _progress]);
        shell.Controls.Add(CreateTitleBar(appearance), 0, 0);
        shell.Controls.Add(header, 0, 1);
        shell.Controls.Add(_tabs, 0, 2);
        shell.Controls.Add(statusStrip, 0, 3);
        Controls.Add(shell);
        UpdateStatus();
    }

    private void AddWorkspace(string title, WorkspaceControl workspace)
    {
        _tabs.AddPage(title, workspace);
        _workspaces.Add(workspace);
        workspace.StatusChanged += (_, _) => { if (ReferenceEquals(_activeWorkspace, workspace)) UpdateStatus(); };
    }

    private void ChangeTheme(bool dark)
    {
        if (ThemeManager.IsDark == dark) return;
        if (!ThemeManager.SetDark(dark))
            _status.Text = "Theme changed. Your preference could not be saved for the next session.";
    }

    private async void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_tabs.SelectedIndex < 0 || _closing) return;
        var previous = _activeWorkspace;
        _activeWorkspace = _workspaces[_tabs.SelectedIndex];
        UpdateStatus();
        if (previous is not null && previous != _activeWorkspace)
        {
            try { await previous.DeactivateAsync(); }
            catch (Exception ex) { _status.Text = "Could not stop the previous workspace: " + ex.Message; }
        }
    }

    private void UpdateStatus()
    {
        if (_closing) return;
        _status.Text = _activeWorkspace?.StatusText ?? "Ready";
        _progress.Visible = _activeWorkspace?.IsBusy ?? false;
        _status.AccessibleName = _status.Text;
        _status.ToolTipText = _status.Text;
        _status.ForeColor = _activeWorkspace?.StatusIsError == true ? ThemeManager.Current.Error : ThemeManager.Current.Muted;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (_lightTheme is not null) _lightTheme.Checked = !ThemeManager.IsDark;
        if (_darkTheme is not null) _darkTheme.Checked = ThemeManager.IsDark;
        if (_reduceMotion is not null) _reduceMotion.Checked = ThemeManager.ReduceMotion;
        ThemeManager.Apply(this);
        UpdateStatus();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        try { BeginInvoke(new Action(ThemeManager.RefreshSystemColors)); }
        catch (InvalidOperationException) { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_closing && keyData == (Keys.Control | Keys.O)) { _activeWorkspace?.OpenImage(); return true; }
        if (!_closing && keyData == (Keys.Control | Keys.S)) { _activeWorkspace?.SaveResult(); return true; }
        if (!_closing && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)))
        {
            int direction = keyData.HasFlag(Keys.Shift) ? -1 : 1;
            _tabs.SelectedIndex = (_tabs.SelectedIndex + direction + _tabs.PageCount) % _tabs.PageCount;
            _tabs.FocusNavigation();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        var desired = SizeFromClientSize(new Size(1200 * DeviceDpi / 96, 760 * DeviceDpi / 96));
        Size = new Size(Math.Min(desired.Width, area.Width), Math.Min(desired.Height, area.Height));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownComplete) { base.OnFormClosing(e); return; }
        e.Cancel = true;
        base.OnFormClosing(e);
        if (_closing) return;
        _closing = true;
        Enabled = false;
        _status.Text = "Finishing processing and releasing the camera…";
        try
        {
            // Stop capture first, so a failed driver shutdown can be retried without
            // permanently cancelling the still-image workspaces.
            await _workspaces.OfType<WebcamWorkspace>().Single().ShutdownAsync();
            await Task.WhenAll(_workspaces.Where(workspace => workspace is not WebcamWorkspace)
                .Select(workspace => workspace.ShutdownAsync()));
        }
        catch (Exception ex)
        {
            _closing = false;
            Enabled = true;
            _tabs.SelectedIndex = 2;
            _status.Text = "Close paused: " + ex.Message + " Retry Stop Camera, then close the window again.";
            return;
        }
        if (IsDisposed) return;
        _shutdownComplete = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnFormClosed(e);
    }

    private void Form1_Load(object sender, EventArgs e)
    {

    }
}
