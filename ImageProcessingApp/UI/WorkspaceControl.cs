namespace ImageProcessingApp;

internal sealed class WorkspaceStatusEventArgs(string message, bool busy, bool error) : EventArgs
{
    public string Message { get; } = message;
    public bool Busy { get; } = busy;
    public bool Error { get; } = error;
}

internal abstract class WorkspaceControl : UserControl
{
    protected WorkspaceControl()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
    }

    public event EventHandler<WorkspaceStatusEventArgs>? StatusChanged;
    public string StatusText { get; private set; } = "Ready";
    public bool IsBusy { get; private set; }
    public bool StatusIsError { get; private set; }

    protected void ReportStatus(string message, bool busy = false, bool error = false)
    {
        if (IsDisposed || Disposing) return;
        StatusText = message;
        IsBusy = busy;
        StatusIsError = error;
        StatusChanged?.Invoke(this, new WorkspaceStatusEventArgs(message, busy, error));
    }

    public virtual void OpenImage() { }
    public virtual void SaveResult() { }
    public virtual Task DeactivateAsync() => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;
}

internal static class Ui
{
    public static Button Button(string text, ButtonRole role = ButtonRole.Secondary) => new RoundedButton
    {
        Text = text, AccessibleName = text.Replace("&", ""), AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 36),
        Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 8, 8),
        FlatStyle = FlatStyle.Flat, Role = role,
        UseVisualStyleBackColor = false
    };

    public static Label Label(string text, bool heading = false, bool muted = false) => new()
    {
        Text = text, AutoSize = true, UseMnemonic = false,
        Font = new Font("Segoe UI", heading ? 16 : 10, heading ? FontStyle.Bold : FontStyle.Regular),
        Tag = muted ? "muted" : null, Margin = new Padding(0, 0, 0, heading ? 8 : 4)
    };

    public static FlowLayoutPanel Flow() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top, WrapContents = true,
        Margin = Padding.Empty, Padding = Padding.Empty
    };
}
