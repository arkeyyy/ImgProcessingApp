using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
[DoNotParallelize]
public class CaptionMouseTests
{
    [DataTestMethod]
    [DataRow(0xA1)]
    [DataRow(0xA3)]
    public void MaximizePressUsesChildCaptureAndNeverNativeCaptionTracking(int message) => RunSta(async () =>
    {
        using var form = new TrackingProbe { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        await Task.Delay(30);
        var button = Buttons(form).Single(b => b.Action == CaptionAction.Maximize);
        var point = button.PointToScreen(Center(button));
        SendMessage(form.Handle, message, (IntPtr)9, Pack(point));
        Assert.AreEqual(0, form.NativeCaptionCalls, "Default caption tracking must never receive the press.");
        Assert.IsTrue(button.Capture, "The visible button must capture the mouse.");
        Assert.AreEqual((IntPtr)1, SendMessage(button.Handle, 0x84, IntPtr.Zero, Pack(point)),
            "During capture WindowFromPoint must find the child for release validation.");
        SendMessage(form.Handle, 0x2A2, IntPtr.Zero, IntPtr.Zero);
        Assert.IsTrue(Interaction(button).Pressed, "A delayed NC leave must not cancel client press feedback.");
        var outside = new Point(-20, button.Height + 20);
        SendMessage(button.Handle, 0x200, (IntPtr)1, Pack(outside));
        SendMessage(button.Handle, 0x202, IntPtr.Zero, Pack(outside));
        Assert.IsFalse(button.Capture);
        Assert.IsFalse(Interaction(button).Pressed);
        Assert.AreEqual(FormWindowState.Normal, form.WindowState, "Release outside must cancel maximize.");
        SendMessage(form.Handle, message, (IntPtr)9, Pack(point));
        Assert.IsTrue(button.Capture);
        form.Capture = true;
        Assert.IsFalse(Interaction(button).Pressed, "Losing capture must cancel the pressed appearance.");
        form.Capture = false;
        Assert.AreEqual(FormWindowState.Normal, form.WindowState);
    });

    [TestMethod]
    public void CaptionHoverDoesNotAskUser32ToPaintNativeButtons() => RunSta(async () =>
    {
        using var form = new TrackingProbe { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        await Task.Delay(30);
        var maximize = Buttons(form).Single(b => b.Action == CaptionAction.Maximize);
        SendMessage(form.Handle, 0xA0, (IntPtr)9, Pack(maximize.PointToScreen(Center(maximize))));
        Assert.IsTrue(Interaction(maximize).Hovered);
        SendMessage(form.Handle, 0x2A2, IntPtr.Zero, IntPtr.Zero);
        Assert.IsFalse(Interaction(maximize).Hovered);
        Assert.AreEqual(0, form.NativeCaptionCalls, "Native painting must not draw a second set of buttons.");
    });

    [TestMethod]
    public void CaptionCommandsReleaseCaptureBeforeChangingWindowState() => RunSta(async () =>
    {
        using var form = new TrackingProbe { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        await Task.Delay(30);
        var maximize = Buttons(form).Single(b => b.Action == CaptionAction.Maximize);
        maximize.Capture = true;
        maximize.PerformClick();
        Assert.IsFalse(maximize.Capture);
        Assert.AreEqual(FormWindowState.Maximized, form.WindowState);
        maximize.Capture = true;
        maximize.PerformClick();
        Assert.AreEqual(FormWindowState.Normal, form.WindowState);
        var minimize = Buttons(form).Single(b => b.Action == CaptionAction.Minimize);
        minimize.Capture = true;
        minimize.PerformClick();
        Assert.IsFalse(minimize.Capture);
        Assert.AreEqual(FormWindowState.Minimized, form.WindowState);
    });

    [DataTestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void VisibleCaptionMouseMessagesRunEachWindowCommand(bool histogram, bool dark)
    {
        if (Environment.GetEnvironmentVariable("IMAGEPROCESSING_CAPTION_MOUSE_TESTS") != "1")
            Assert.Inconclusive("Set IMAGEPROCESSING_CAPTION_MOUSE_TESTS=1 to exercise mouse messages in visible test windows.");
        RunSta(async () =>
        {
            var savedForeground = GetForegroundWindow();
            bool savedTheme = ThemeManager.IsDark;
            try
            {
                using StudioForm form = histogram ? new HistogramDialog(new int[256], "Result", "test.png") : new Form1();
                form.ShowInTaskbar = false;
                form.TopMost = true;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = Screen.PrimaryScreen!.WorkingArea.Location + new Size(30, 30);
                form.Size = new Size(1000, 650);
                ThemeManager.SetDark(dark, persist: false);
                form.Show();
                form.Activate();
                await Task.Delay(100);
                form.Bounds = new Rectangle(30, 30, 1000, 650);
                SetWindowPos(form.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x3);
                SetForegroundWindow(form.Handle);
                await Task.Delay(100);
                string prefix = $"{(histogram ? "histogram" : "main")}-{(dark ? "dark" : "light")}";
                var maximize = Buttons(form).Single(b => b.Action == CaptionAction.Maximize);
                await PressAndRelease(form, maximize, prefix + "-cancel", cancel: true);
                Assert.AreEqual(FormWindowState.Normal, form.WindowState);
                var restored = form.Bounds;
                await PressAndRelease(form, maximize, prefix + "-maximize");
                Assert.AreEqual(FormWindowState.Maximized, form.WindowState);
                Assert.AreEqual(Screen.FromControl(form).WorkingArea, form.Bounds);
                await PressAndRelease(form, maximize, prefix + "-restore");
                Assert.AreEqual(FormWindowState.Normal, form.WindowState);
                Assert.AreEqual(restored, form.Bounds);
                var minimize = Buttons(form).SingleOrDefault(b => b.Action == CaptionAction.Minimize);
                if (minimize is not null)
                {
                    await PressAndRelease(form, minimize, prefix + "-minimize-cancel", cancel: true);
                    Assert.AreEqual(FormWindowState.Normal, form.WindowState);
                    await PressAndRelease(form, minimize, prefix + "-minimize");
                    Assert.AreEqual(FormWindowState.Minimized, form.WindowState);
                    SendMessage(form.Handle, 0x112, (IntPtr)0xF120, IntPtr.Zero);
                    await Task.Delay(100);
                    form.Activate();
                }
                var close = Buttons(form).Single(b => b.Action == CaptionAction.Close);
                await PressAndRelease(form, close, prefix + "-close-cancel", cancel: true);
                Assert.IsFalse(form.IsDisposed);
                await PressAndRelease(form, close, prefix + "-close");
                Assert.IsTrue(form.IsDisposed, "Mouse release must close the actual window.");
            }
            finally
            {
                if (savedForeground != IntPtr.Zero) SetForegroundWindow(savedForeground);
                ThemeManager.SetDark(savedTheme, persist: false);
            }
        });
    }

    private static async Task PressAndRelease(StudioForm form, StudioCaptionButton button, string name, bool cancel = false)
    {
        int clicks = 0;
        EventHandler clicked = (_, _) => clicks++;
        button.Click += clicked;
        var point = button.PointToScreen(Center(button));
        int hit = (int)SendMessage(form.Handle, 0x84, IntPtr.Zero, Pack(point));
        Assert.AreEqual(button.Action == CaptionAction.Maximize ? 9 : 1, hit);
        // Exercise the OS message path synchronously so an unrelated desktop
        // pointer movement cannot cancel the synthetic gesture between messages.
        if (hit == 9) SendMessage(form.Handle, 0xA1, (IntPtr)9, Pack(point));
        else SendMessage(button.Handle, 0x201, (IntPtr)1, Pack(Center(button)));
        Assert.IsTrue(button.Capture, name + ": visible button must capture the press.");
        Assert.AreEqual(button.Handle, WindowFromPoint(point), "Mouse-up validation must find the captured child.");
        Assert.IsTrue(Interaction(button).Pressed);
        Interaction(button).Animate(0, motionAllowed: false);
        CaptureTitle(form, button, name);
        var release = cancel ? new Point(-20, button.Height + 20) : Center(button);
        SendMessage(button.Handle, 0x200, (IntPtr)1, Pack(release));
        SendMessage(button.Handle, 0x202, IntPtr.Zero, Pack(release));
        await Task.Delay(100);
        Assert.AreEqual(cancel ? 0 : 1, clicks, "A valid release clicks exactly once; dragging away cancels.");
        if (!button.IsDisposed)
        {
            Assert.IsFalse(button.Capture);
            button.Click -= clicked;
        }
    }

    private static void CaptureTitle(Form form, Control button, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("IMAGEPROCESSING_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        form.Update();
        var rectangle = new Rectangle(form.Location, new Size(form.Width, button.PointToScreen(new Point(0, button.Height)).Y - form.Top + 12));
        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rectangle.Location, Point.Empty, rectangle.Size);
        bitmap.Save(Path.Combine(folder, "mouse-" + name + ".png"));
    }

    private sealed class TrackingProbe : StudioForm
    {
        public int NativeCaptionCalls { get; private set; }
        public TrackingProbe()
        {
            ClientSize = new Size(600, 350);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.Controls.Add(CreateTitleBar(), 0, 0);
            Controls.Add(shell);
        }
        protected override void DefWndProc(ref Message m)
        {
            if ((m.Msg is 0xA0 or 0xA1 or 0xA3 && (int)m.WParam is 8 or 9 or 20) || m.Msg == 0x2A2)
            {
                NativeCaptionCalls++;
                m.Result = IntPtr.Zero; // fail safely instead of entering the native modal tracking loop
                return;
            }
            base.DefWndProc(ref m);
        }
    }
    private static IEnumerable<StudioCaptionButton> Buttons(Control control) => Descendants(control).OfType<StudioCaptionButton>();
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    private static RoundedControlInteraction Interaction(RoundedButton button) =>
        (RoundedControlInteraction)typeof(RoundedButton).GetField("_interaction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!;
    private static Point Center(Control control) => new(control.Width / 2, control.Height / 2);
    private static IntPtr Pack(Point point) => (IntPtr)((point.Y << 16) | (point.X & 0xffff));
    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var host = new Form { Opacity = 0, ShowInTaskbar = false };
            host.Shown += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { failure = ex; }
                finally { host.Close(); }
            };
            Application.Run(host);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Caption mouse test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, int flags);
}
