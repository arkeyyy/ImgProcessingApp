using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImageProcessingApp;

// A clock-driven value: reversing a transition starts at the current value.
internal sealed class StudioTransition
{
    private double _start, _duration;
    private float _from;
    public float Value { get; private set; }
    public float Target { get; private set; }
    public bool Running { get; private set; }

    public void Set(float target, double now, double duration, bool animate)
    {
        Advance(now);
        if (Target == target && animate) return;
        Target = target;
        _from = Value;
        _start = now;
        _duration = duration;
        Running = animate && duration > 0 && Value != target;
        if (!Running) Value = target;
    }

    public bool Advance(double now)
    {
        if (!Running) return false;
        double t = Math.Clamp((now - _start) / _duration, 0, 1);
        Value = _from + (Target - _from) * (float)(t * t * (3 - 2 * t));
        Running = t < 1;
        return Running;
    }
}

internal interface IStudioAnimation
{
    bool IsAmbient { get; }
    bool Animate(double milliseconds, bool motionAllowed);
}

internal sealed class StudioAnimationScheduler : IDisposable
{
    private readonly Form _window;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Func<double> _now;
    private readonly Func<bool> _systemAnimations;
    private readonly HashSet<IStudioAnimation> _clients = [];
    private bool _active, _disposed;
    private double _lastAmbientFrame;
    public double Now => _now();
    internal bool IsRunning => _timer.Enabled;
    internal int SubscriptionCount => _clients.Count;
    internal bool MotionAllowed { get; private set; }
    public static bool AllowsMotion(bool reduceMotion, bool highContrast, bool systemAnimations) =>
        !reduceMotion && !highContrast && systemAnimations;

    public StudioAnimationScheduler(Form window, Func<bool>? systemAnimations = null, Func<double>? clock = null)
    {
        _window = window;
        _now = clock ?? (() => _clock.Elapsed.TotalMilliseconds);
        _systemAnimations = systemAnimations ?? (() => SystemParametersInfo(0x1042, 0, out bool enabled, 0) && enabled);
        _timer.Tick += Tick;
        window.Activated += Activated;
        window.Deactivate += Deactivated;
        window.VisibleChanged += StateChanged;
        window.Resize += StateChanged;
        ThemeManager.ThemeChanged += PreferencesChanged;
        RefreshPreferences();
    }

    private void RefreshPreferences()
    {
        MotionAllowed = AllowsMotion(ThemeManager.ReduceMotion, SystemInformation.HighContrast, _systemAnimations());
    }

    public void Request(IStudioAnimation client)
    {
        if (_disposed) return;
        if (_clients.Add(client) && client.IsAmbient) client.Animate(Now, false);
        Wake();
    }

    public void Remove(IStudioAnimation client)
    {
        _clients.Remove(client);
        Wake();
    }

    private bool CanRun => _active && _window.Visible && _window.WindowState != FormWindowState.Minimized;
    private void Activated(object? sender, EventArgs e) { _active = true; ResetAmbientClock(); Wake(); }
    private void Deactivated(object? sender, EventArgs e) { _active = false; Settle(); }
    private void StateChanged(object? sender, EventArgs e) { if (CanRun) { ResetAmbientClock(); Wake(); } else Settle(); }
    private void PreferencesChanged(object? sender, EventArgs e) { RefreshPreferences(); Settle(); Wake(); }

    private void ResetAmbientClock()
    {
        _lastAmbientFrame = Now;
        foreach (var client in _clients.Where(c => c.IsAmbient).ToArray()) client.Animate(Now, false);
    }

    private void Settle()
    {
        foreach (var client in _clients.ToArray())
        {
            client.Animate(Now, false);
            if (!client.IsAmbient) _clients.Remove(client);
        }
        _timer.Stop();
    }

    private void Wake()
    {
        if (_disposed) return;
        if (!MotionAllowed || !CanRun) { Settle(); return; }
        _timer.Interval = _clients.Any(c => !c.IsAmbient) ? 16 : 33;
        _timer.Enabled = _clients.Count > 0;
    }

    private void Tick(object? sender, EventArgs e) => Advance(Now);
    internal void Advance(double now)
    {
        bool ambientDue = now - _lastAmbientFrame >= 33;
        foreach (var client in _clients.ToArray())
        {
            if (client.IsAmbient && !ambientDue) continue;
            if (!client.Animate(now, MotionAllowed && CanRun)) _clients.Remove(client);
        }
        if (ambientDue) _lastAmbientFrame = now;
        Wake();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _clients.Clear();
        _clock.Stop();
        _window.Activated -= Activated;
        _window.Deactivate -= Deactivated;
        _window.VisibleChanged -= StateChanged;
        _window.Resize -= StateChanged;
        ThemeManager.ThemeChanged -= PreferencesChanged;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
