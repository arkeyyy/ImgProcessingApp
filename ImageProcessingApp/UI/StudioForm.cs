using System.Runtime.InteropServices;

namespace ImageProcessingApp;

// Retain WS_THICKFRAME, WS_SYSMENU and native system commands. The top-level
// window owns caption hit testing even when the pointer is over a child HWND.
public class StudioForm : Form
{
    private const int HtClient = 1, HtCaption = 2, HtMaxButton = 9;
    private const int ScSize = 0xF000, ScMove = 0xF010, ScMinimize = 0xF020,
        ScMaximize = 0xF030, ScClose = 0xF060, ScRestore = 0xF120;
    internal StudioAnimationScheduler Animations { get; }
    private readonly Dictionary<Control, ChromeHitTestWindow> _chromeWindows = [];
    private readonly List<StudioCaptionButton> _captionButtons = [];
    private StudioCaptionButton? _maximize;
    private Control? _titleBar, _appearance;

    public StudioForm()
    {
        Animations = new StudioAnimationScheduler(this);
        Padding = new Padding(6);
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style &= ~0x00C00000; // WS_CAPTION; keep the sizing frame and system menu.
            return cp;
        }
    }

    internal Control CreateTitleBar(Control? appearance = null)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(18, 6, 2, 6), Margin = Padding.Empty, Tag = "surface"
        };
        _titleBar = bar;
        _appearance = appearance;
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = Ui.Label(Text);
        title.AutoSize = false;
        title.AutoEllipsis = true;
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Margin = Padding.Empty;
        title.MinimumSize = new Size(120, 36);
        StudioMetrics.OwnFont(title, StudioMetrics.Heading(11));
        bar.Controls.Add(title, 0, 0);
        TextChanged += (_, _) => title.Text = Text;
        if (appearance is not null) bar.Controls.Add(appearance, 1, 0);
        var buttons = Ui.Flow();
        buttons.WrapContents = false;
        buttons.Dock = DockStyle.Fill;
        if (MinimizeBox) buttons.Controls.Add(CaptionButton(CaptionAction.Minimize, "Minimize window", () => SendSystemCommand(ScMinimize)));
        if (MaximizeBox)
        {
            _maximize = CaptionButton(CaptionAction.Maximize, "Maximize window", ToggleMaximize);
            buttons.Controls.Add(_maximize);
        }
        buttons.Controls.Add(CaptionButton(CaptionAction.Close, "Close window", Close));
        bar.Controls.Add(buttons, 2, 0);
        if (IsHandleCreated) ConnectChromeHitTesting();
        return bar;
    }

    private StudioCaptionButton CaptionButton(CaptionAction captionAction, string name, Action action)
    {
        var button = new StudioCaptionButton(captionAction)
        {
            AccessibleName = name, Size = new Size(40, 36),
            Margin = new Padding(2, 0, 0, 0), TabStop = true
        };
        button.Click += (_, _) =>
        {
            // Button raises Click before its mouse-up handler releases capture.
            // Complete the pointer gesture before issuing a window system command.
            button.Capture = false;
            action();
        };
        _captionButtons.Add(button);
        return button;
    }
    private void ToggleMaximize() => SendSystemCommand(WindowState == FormWindowState.Maximized ? ScRestore : ScMaximize);
    private void SendSystemCommand(int command) => SendMessage(Handle, 0x112, (IntPtr)command, IntPtr.Zero);

    private void ShowSystemMenu(Point point)
    {
        var menu = GetSystemMenu(Handle, false);
        bool normal = WindowState == FormWindowState.Normal;
        EnableMenuItem(menu, ScRestore, normal ? 1u : 0u);
        EnableMenuItem(menu, ScMove, normal ? 0u : 1u);
        EnableMenuItem(menu, ScSize, normal && IsResizable ? 0u : 1u);
        EnableMenuItem(menu, ScMinimize, MinimizeBox && WindowState != FormWindowState.Minimized ? 0u : 1u);
        EnableMenuItem(menu, ScMaximize, MaximizeBox && WindowState != FormWindowState.Maximized ? 0u : 1u);
        EnableMenuItem(menu, ScClose, Enabled && ControlBox ? 0u : 1u);
        int command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, Handle, IntPtr.Zero);
        if (command != 0) SendSystemCommand(command);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Space))
        {
            ShowSystemMenu(PointToScreen(new Point(8 * DeviceDpi / 96, 42 * DeviceDpi / 96)));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_maximize is null) return;
        _maximize.Invalidate();
        _maximize.AccessibleName = WindowState == FormWindowState.Maximized ? "Restore window" : "Maximize window";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ConnectChromeHitTesting();
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27); // NOMOVE | NOSIZE | NOZORDER | FRAMECHANGED
    }

    private void ConnectChromeHitTesting()
    {
        if (_titleBar is null) return;
        void Attach(Control control)
        {
            if (!_chromeWindows.ContainsKey(control)) _chromeWindows.Add(control, new ChromeHitTestWindow(this, control));
        }
        void AttachTree(Control control)
        {
            Attach(control);
            foreach (Control child in control.Controls) AttachTree(child);
        }
        AttachTree(_titleBar);
        for (var parent = _titleBar.Parent; parent is not null && parent != this; parent = parent.Parent) Attach(parent);
    }

    private bool IsResizable => FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
    private static bool ContainsScreenPoint(Control? control, Point point) =>
        control is { IsHandleCreated: true, Visible: true } && control.RectangleToScreen(control.ClientRectangle).Contains(point);

    private int HitTestChrome(Point screenPoint)
    {
        var point = PointToClient(screenPoint);
        if (IsResizable && WindowState == FormWindowState.Normal)
        {
            int edge = ResizeHitTest(point, ClientSize, Math.Max(6, 6 * DeviceDpi / 96));
            if (edge != HtClient) return edge;
        }
        if (!ContainsScreenPoint(_titleBar, screenPoint)) return HtClient;
        foreach (var button in _captionButtons)
            if (ContainsScreenPoint(button, screenPoint))
                // Maximize reports HTMAXBUTTON for Windows Snap Layouts. Its
                // press is redirected to the visible button below. Other buttons
                // receive ordinary client input throughout.
                return button.Enabled && button.Action == CaptionAction.Maximize ? HtMaxButton : HtClient;
        if (ContainsScreenPoint(_appearance, screenPoint)) return HtClient;
        return HtCaption;
    }

    internal static int ResizeHitTest(Point p, Size size, int grip)
    {
        bool left = p.X < grip, right = p.X >= size.Width - grip;
        bool top = p.Y < grip, bottom = p.Y >= size.Height - grip;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => 13, (_, true, true, _) => 14,
            (true, _, _, true) => 16, (_, true, _, true) => 17,
            (true, _, _, _) => 10, (_, true, _, _) => 11,
            (_, _, true, _) => 12, (_, _, _, true) => 15, _ => HtClient
        };
    }

    private static Point ScreenPoint(IntPtr lparam)
    {
        long packed = lparam.ToInt64();
        // Signed words are essential for monitors left of or above the primary one.
        return new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
    }

    private void UpdateCaptionPointer(int hit)
    {
        // During a press the child owns capture and tracks client mouse events.
        // A delayed NC leave must not reset that press or client-button hover.
        if (_maximize is { IsDisposed: false, Capture: false } button)
            button.SetChromeInteraction(button.Enabled && hit == HtMaxButton, false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x85) { m.Result = IntPtr.Zero; return; } // WM_NCPAINT: all frame drawing belongs to controls.
        if (m.Msg == 0x86) { m.Result = (IntPtr)1; return; }
        if (m.Msg == 0x83 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == 0x84)
        {
            // All caption geometry belongs to our controls. A DWM fallback here
            // can expose hit targets for native buttons we do not display.
            m.Result = (IntPtr)HitTestChrome(ScreenPoint(m.LParam));
            return;
        }
        if (m.Msg == 0xA0)
        {
            UpdateCaptionPointer((int)m.WParam);
            var track = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 0x12, Hwnd = Handle };
            TrackMouseEvent(ref track);
            // Notify the compositor for Snap Layouts without letting USER32
            // draw or track its own caption buttons over our custom controls.
            DwmDefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam, out var result);
            m.Result = result;
            return;
        }
        if (m.Msg == 0x2A2)
        {
            UpdateCaptionPointer(HtClient);
            DwmDefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam, out var result);
            m.Result = result;
            return;
        }
        if (m.Msg is 0xA1 or 0xA3 && (int)m.WParam == HtMaxButton)
        {
            // Native caption tracking uses system geometry, not this button's
            // bounds. Translate both NC down and double-click to a normal press;
            // Button then owns capture, drag-away cancellation and one Click.
            var screenPoint = ScreenPoint(m.LParam);
            if (_maximize is { Enabled: true } button && ContainsScreenPoint(button, screenPoint))
            {
                Activate();
                var point = button.PointToClient(screenPoint);
                var position = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
                SendMessage(button.Handle, 0x201, (IntPtr)1, position); // WM_LBUTTONDOWN, MK_LBUTTON
            }
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == 0xA5 && (int)m.WParam == HtCaption)
        {
            ShowSystemMenu(ScreenPoint(m.LParam));
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == 0x24)
        {
            base.WndProc(ref m);
            var info = Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
            var screen = Screen.FromHandle(Handle);
            info.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
            info.MaxSize = screen.WorkingArea.Size;
            Marshal.StructureToPtr(info, m.LParam, false);
            return;
        }
        if (m.Msg == 0x6 || m.Msg == 0x320)
        {
            var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var window in _chromeWindows.Values) window.Dispose();
            _chromeWindows.Clear();
            Animations.Dispose();
        }
        base.Dispose(disposing);
    }

    // HTTRANSPARENT continues hit testing through same-thread child windows.
    // Only chrome regions forward to the top-level HWND; appearance controls and
    // workspace controls keep normal native mouse and keyboard behavior.
    private sealed class ChromeHitTestWindow : NativeWindow, IDisposable
    {
        private readonly StudioForm _owner;
        private readonly Control _control;
        public ChromeHitTestWindow(StudioForm owner, Control control)
        {
            _owner = owner;
            _control = control;
            control.HandleCreated += Created;
            control.HandleDestroyed += Destroyed;
            if (control.IsHandleCreated) AssignHandle(control.Handle);
        }
        private void Created(object? sender, EventArgs e) => AssignHandle(_control.Handle);
        private void Destroyed(object? sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }
        protected override void WndProc(ref Message m)
        {
            // Button.OnMouseUp validates WindowFromPoint before releasing capture.
            // Let that lookup find the pressed button instead of skipping it as
            // HTTRANSPARENT, even though its idle hover uses HTMAXBUTTON.
            if (m.Msg == 0x84 && _control is StudioCaptionButton { Capture: true })
            {
                m.Result = (IntPtr)HtClient;
                return;
            }
            if (m.Msg == 0x84 && !_owner.IsDisposed && _owner.HitTestChrome(ScreenPoint(m.LParam)) != HtClient)
            {
                m.Result = (IntPtr)(-1);
                return;
            }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            _control.HandleCreated -= Created;
            _control.HandleDestroyed -= Destroyed;
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSizePoint, MaxPosition, MinTrackSize, MaxTrackSize;
        public Size MaxSize { readonly get => new(MaxSizePoint.X, MaxSizePoint.Y); set => MaxSizePoint = new Point(value.Width, value.Height); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouse { public uint Size, Flags; public IntPtr Hwnd; public uint HoverTime; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] private static extern uint EnableMenuItem(IntPtr menu, int item, uint enable);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouse track);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern bool DwmDefWindowProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, out IntPtr result);
}
