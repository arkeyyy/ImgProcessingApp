using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
[DoNotParallelize]
public class StudioWindowTests
{
    [TestMethod]
    public void CaptionHitTestsAndSystemCommandsUseTheTopLevelWindow() => RunSta(async () =>
    {
        using var form = new Form1 { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        await Task.Delay(30);
        var buttons = Descendants(form).OfType<StudioCaptionButton>().ToArray();
        Assert.AreEqual(3, buttons.Length);
        foreach (var button in buttons)
        {
            var point = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
            int expected = button.Action == CaptionAction.Maximize ? 9 : 1;
            Assert.AreEqual(expected, HitTest(form, point), "Minimize and Close use client input; maximize retains native snap hit-testing.");
            Assert.AreEqual(button.Action == CaptionAction.Maximize ? -1 : 1, HitTest(button, point),
                "Only idle Maximize forwards hover hit testing to the top-level window.");
            Assert.IsFalse(string.IsNullOrEmpty(button.AccessibilityObject.Name));
        }
        var appearance = Descendants(form).OfType<CheckBox>().Single();
        Assert.AreEqual(1, HitTest(form, appearance.PointToScreen(new Point(4, 4))), "Appearance controls remain ordinary client controls.");
        var title = Descendants(form).OfType<Label>().Single(c => c.Text == form.Text);
        Assert.AreEqual(2, HitTest(form, title.PointToScreen(new Point(12, title.Height / 2))), "The title must be a native draggable caption.");

        var maximize = buttons.Single(b => b.Action == CaptionAction.Maximize);
        var restored = form.Bounds;
        maximize.PerformClick();
        Assert.AreEqual(FormWindowState.Maximized, form.WindowState);
        Assert.AreEqual(Screen.FromControl(form).WorkingArea, form.Bounds, "Maximize must respect the taskbar working area.");
        maximize.PerformClick();
        Assert.AreEqual(FormWindowState.Normal, form.WindowState);
        Assert.AreEqual(restored, form.Bounds);
        var titlePoint = title.PointToScreen(new Point(12, title.Height / 2));
        var titleLocation = (IntPtr)((titlePoint.Y << 16) | (titlePoint.X & 0xffff));
        SendMessage(form.Handle, 0xA3, (IntPtr)2, titleLocation);
        Assert.AreEqual(FormWindowState.Maximized, form.WindowState, "Native caption double-click must maximize.");
        SendMessage(form.Handle, 0xA3, (IntPtr)2, titleLocation);
        Assert.AreEqual(FormWindowState.Normal, form.WindowState, "Native caption double-click must restore.");
        SendMessage(form.Handle, 0x112, (IntPtr)0xF020, IntPtr.Zero);
        Assert.AreEqual(FormWindowState.Minimized, form.WindowState);
        SendMessage(form.Handle, 0x112, (IntPtr)0xF120, IntPtr.Zero);
        Assert.AreEqual(FormWindowState.Normal, form.WindowState);
        form.Close();
    });

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CloseOwnsClientHoverAndCancelsAReleaseOutsideItsBounds(bool histogram, bool dark) => RunSta(async () =>
    {
        bool originalDark = ThemeManager.IsDark;
        try
        {
            using StudioForm form = histogram ? new HistogramDialog(new int[256], "Result", "sample.png") : new Form1();
            // Form1 loads saved preferences during construction; choose the test theme afterwards.
            ThemeManager.SetDark(dark, persist: false);
            Assert.AreEqual(dark, ThemeManager.IsDark);
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            await Task.Delay(30);
            var close = Descendants(form).OfType<StudioCaptionButton>().Single(b => b.Action == CaptionAction.Close);
            var interaction = (RoundedControlInteraction)typeof(RoundedButton)
                .GetField("_interaction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(close)!;
            foreach (var state in new[] { FormWindowState.Normal, FormWindowState.Maximized, FormWindowState.Normal })
            {
                form.WindowState = state;
                var point = close.PointToScreen(new Point(close.Width / 2, close.Height / 2));
                Assert.AreEqual(1, HitTest(form, point));
                Assert.AreEqual(1, HitTest(close, point));
            }
            int clicks = 0;
            close.Click += (_, _) => clicks++;
            CaptureCaption(close, "normal", histogram, dark);
            typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(close, [EventArgs.Empty]);
            SendMessage(form.Handle, 0x2A2, IntPtr.Zero, IntPtr.Zero); // leaving the native frame
            Assert.IsTrue(interaction.Hovered, "A delayed frame-leave must not erase client-button hover.");
            interaction.Animate(0, motionAllowed: false);
            CaptureCaption(close, "hover", histogram, dark);
            var inside = (IntPtr)(((close.Height / 2) << 16) | (close.Width / 2));
            SendMessage(close.Handle, 0x201, (IntPtr)1, inside);
            Assert.IsTrue(close.Capture, "The visible Close button must own mouse capture.");
            Assert.IsTrue(interaction.Pressed);
            interaction.Animate(0, motionAllowed: false);
            CaptureCaption(close, "pressed", histogram, dark);
            var outside = (IntPtr)(((close.Height + 20) << 16) | (close.Width + 20));
            SendMessage(close.Handle, 0x200, (IntPtr)1, outside);
            SendMessage(close.Handle, 0x202, IntPtr.Zero, outside);
            Assert.AreEqual(0, clicks, "Dragging away before release must cancel closing.");
            Assert.IsFalse(close.Capture);
            Assert.IsFalse(interaction.Pressed);
            Assert.IsFalse(form.IsDisposed);
            close.Enabled = false;
            CaptureCaption(close, "disabled", histogram, dark);
            close.PerformClick();
            Assert.AreEqual(0, clicks, "Disabled Close must not activate.");
            close.Enabled = true;
            close.Focus();
            SendMessage(close.Handle, 0x100, (IntPtr)Keys.Space, IntPtr.Zero);
            SendMessage(close.Handle, 0x101, (IntPtr)Keys.Space, IntPtr.Zero);
            Assert.AreEqual(1, clicks, "A committed keyboard click must fire once.");
        }
        finally { ThemeManager.SetDark(originalDark, persist: false); }
    });

    [DataTestMethod]
    [DataRow("button")]
    [DataRow("system-command")]
    [DataRow("window-message")]
    public void EveryCloseRouteWaitsForProcessingToFinish(string route) => RunSta(async () =>
    {
        using var form = new Form1 { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        var workspace = Descendants(form).OfType<StudioNavigation>().Single().Pages.OfType<StillImageWorkspace>().First();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.FormClosed += (_, _) => closed.TrySetResult();
        Func<CancellationToken, Task> operation = async token =>
        {
            using var registration = token.Register(() => canceled.TrySetResult());
            await release.Task;
        };
        var pending = (Task)typeof(AsyncImageWorkspace).GetMethod("RunOperationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(workspace, ["Processing test image", operation])!;
        try
        {
            switch (route)
            {
                case "button": Descendants(form).OfType<StudioCaptionButton>().Single(b => b.Action == CaptionAction.Close).PerformClick(); break;
                case "system-command": SendMessage(form.Handle, 0x112, (IntPtr)0xF060, IntPtr.Zero); break;
                default: SendMessage(form.Handle, 0x10, IntPtr.Zero, IntPtr.Zero); break;
            }
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(form.IsDisposed, "A close request must await the active operation.");
            Assert.IsFalse(form.Enabled);
            Assert.IsFalse(pending.IsCompleted);
            release.SetResult();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(pending.IsCompleted);
        }
        finally { release.TrySetResult(); }
    });

    private static void CaptureCaption(Control close, string state, bool histogram, bool dark)
    {
        var folder = Environment.GetEnvironmentVariable("IMAGEPROCESSING_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var strip = close.Parent!;
        using var bitmap = new Bitmap(strip.Width, strip.Height);
        strip.DrawToBitmap(bitmap, strip.ClientRectangle);
        bitmap.Save(Path.Combine(folder, $"caption-{(histogram ? "histogram" : "main")}-{(dark ? "dark" : "light")}-{state}.png"));
    }

    private static int HitTest(Control control, Point screenPoint) => (int)SendMessage(control.Handle, 0x84, IntPtr.Zero,
        (IntPtr)((screenPoint.Y << 16) | (screenPoint.X & 0xffff)));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

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
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Window test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
}
