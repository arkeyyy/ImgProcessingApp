using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
[DoNotParallelize]
public class StudioInteractionTests
{
    [TestMethod]
    public void InterruptedHoverAndPressContinueFromTheVisibleValue()
    {
        var transition = new StudioTransition();
        transition.Set(1, 0, 160, animate: true);
        transition.Advance(40);
        float beforeReverse = transition.Value;
        Assert.IsTrue(beforeReverse > 0 && beforeReverse < 1);

        transition.Set(0, 40, 160, animate: true);
        Assert.AreEqual(beforeReverse, transition.Value, "Reversing hover must not jump to an endpoint.");
        transition.Advance(120);
        Assert.IsTrue(transition.Value > 0 && transition.Value < beforeReverse);

        float beforePress = transition.Value;
        transition.Set(1, 120, 90, animate: true);
        Assert.AreEqual(beforePress, transition.Value, "Pressing during a hover transition must remain continuous.");
        transition.Advance(210);
        Assert.AreEqual(1f, transition.Value);
        Assert.IsFalse(transition.Running);
    }

    [DataTestMethod]
    [DataRow(90)]
    [DataRow(160)]
    [DataRow(180)]
    public void LateAnimationTickCompletesAtTheExactTarget(int duration)
    {
        var transition = new StudioTransition();
        transition.Set(1, 1000, duration, animate: true);
        Assert.IsTrue(transition.Advance(1000 + duration / 2.0));
        Assert.IsTrue(transition.Value > 0 && transition.Value < 1);
        Assert.IsFalse(transition.Advance(1000 + duration * 20));
        Assert.AreEqual(1f, transition.Value, "Elapsed time, rather than frame count, determines completion.");
        Assert.IsFalse(transition.Advance(1000 + duration * 21));
        Assert.AreEqual(1f, transition.Value);
    }

    [TestMethod]
    public void DisablingMotionFinishesAnInFlightTransitionImmediately()
    {
        var transition = new StudioTransition();
        transition.Set(1, 0, 180, animate: true);
        transition.Advance(90);
        Assert.IsTrue(transition.Running);
        transition.Set(1, 90, 180, animate: false);
        Assert.AreEqual(1f, transition.Value);
        Assert.IsFalse(transition.Running);
        transition.Set(0, 90, 0, animate: true);
        Assert.AreEqual(0f, transition.Value);
        Assert.IsFalse(transition.Running);
    }

    [DataTestMethod]
    [DataRow(false, false, true, true)]
    [DataRow(true, false, true, false)]
    [DataRow(false, true, true, false)]
    [DataRow(false, false, false, false)]
    [DataRow(true, true, true, false)]
    [DataRow(true, false, false, false)]
    [DataRow(false, true, false, false)]
    [DataRow(true, true, false, false)]
    public void SystemMotionPreferencesAlwaysOverrideTheAppToggle(bool reduceMotion, bool highContrast,
        bool systemAnimations, bool expected)
    {
        Assert.AreEqual(expected, StudioAnimationScheduler.AllowsMotion(reduceMotion, highContrast, systemAnimations));
    }

    [TestMethod]
    public void OlderThemeSettingsKeepTheirThemeAndDefaultToFullMotion()
    {
        var light = JsonSerializer.Deserialize<ThemeManager.ThemeSettings>("{}");
        var dark = JsonSerializer.Deserialize<ThemeManager.ThemeSettings>("{\"DarkMode\":true}");
        Assert.IsNotNull(light);
        Assert.IsNotNull(dark);
        Assert.IsFalse(light.DarkMode);
        Assert.IsFalse(light.ReduceMotion);
        Assert.IsTrue(dark.DarkMode);
        Assert.IsFalse(dark.ReduceMotion);

        var original = new ThemeManager.ThemeSettings(true, true);
        var restored = JsonSerializer.Deserialize<ThemeManager.ThemeSettings>(JsonSerializer.Serialize(original));
        Assert.AreEqual(original, restored, "Both preferences must survive the existing settings format.");
    }

    [TestMethod]
    public void SchedulerStopsWhenIdleAndPausesWhenInactiveHiddenOrMinimized() => RunSta(() =>
    {
        bool originalReduceMotion = ThemeManager.ReduceMotion;
        try
        {
            ThemeManager.SetReduceMotion(false, persist: false);
            using var window = new TestWindow();
            double now = 100;
            using var scheduler = new StudioAnimationScheduler(window, () => true, () => now);
            window.Show();
            window.NotifyActive();
            Assert.IsFalse(scheduler.IsRunning, "An idle window must not run an animation timer.");
            Assert.AreEqual(now, scheduler.Now);

            var ambient = new AnimationProbe(ambient: true);
            var interaction = new AnimationProbe(ambient: false) { KeepRunning = true };
            scheduler.Request(ambient);
            Assert.AreEqual(!SystemInformation.HighContrast, scheduler.IsRunning);
            scheduler.Request(interaction);
            now = 150;
            scheduler.Advance(now);
            Assert.AreEqual(now, ambient.LastTime);
            Assert.AreEqual(!SystemInformation.HighContrast, ambient.LastMotionAllowed);
            if (!SystemInformation.HighContrast)
            {
                Assert.AreEqual(now, interaction.LastTime);
                Assert.IsTrue(interaction.LastMotionAllowed);
            }

            window.NotifyInactive();
            Assert.IsFalse(scheduler.IsRunning);
            Assert.AreEqual(1, scheduler.SubscriptionCount, "Paused interaction transitions must settle and unsubscribe.");
            Assert.IsFalse(interaction.LastMotionAllowed);
            var whileInactive = new AnimationProbe(ambient: false);
            scheduler.Request(whileInactive);
            Assert.IsFalse(whileInactive.LastMotionAllowed);
            Assert.AreEqual(1, scheduler.SubscriptionCount);

            window.NotifyActive();
            Assert.AreEqual(!SystemInformation.HighContrast, scheduler.IsRunning);
            window.WindowState = FormWindowState.Minimized;
            Assert.IsFalse(scheduler.IsRunning);
            window.WindowState = FormWindowState.Normal;
            window.NotifyActive();
            Assert.AreEqual(!SystemInformation.HighContrast, scheduler.IsRunning);
            window.Hide();
            Assert.IsFalse(scheduler.IsRunning);
            window.Show();
            window.NotifyActive();
            Assert.AreEqual(!SystemInformation.HighContrast, scheduler.IsRunning);
            scheduler.Remove(ambient);
            Assert.AreEqual(0, scheduler.SubscriptionCount);
            Assert.IsFalse(scheduler.IsRunning);

            var finalFrame = new AnimationProbe(ambient: false);
            scheduler.Request(finalFrame);
            now = 200;
            scheduler.Advance(now);
            Assert.AreEqual(0, scheduler.SubscriptionCount, "Completed transitions must release the timer.");
            Assert.IsFalse(scheduler.IsRunning);
        }
        finally { ThemeManager.SetReduceMotion(originalReduceMotion, persist: false); }
    });

    [TestMethod]
    public void MotionPreferenceChangesSettleTransitionsAndSchedulerDisposalUnsubscribes() => RunSta(() =>
    {
        bool originalReduceMotion = ThemeManager.ReduceMotion;
        try
        {
            ThemeManager.SetReduceMotion(false, persist: false);
            using var window = new TestWindow();
            bool systemAnimations = true;
            int preferenceReads = 0;
            using var scheduler = new StudioAnimationScheduler(window,
                () => { preferenceReads++; return systemAnimations; }, () => 42);
            window.Show();
            window.NotifyActive();
            var ambient = new AnimationProbe(ambient: true);
            var interaction = new AnimationProbe(ambient: false) { KeepRunning = true };
            scheduler.Request(ambient);
            scheduler.Request(interaction);

            systemAnimations = false;
            ThemeManager.RefreshSystemColors();
            Assert.IsFalse(scheduler.MotionAllowed);
            Assert.IsFalse(scheduler.IsRunning);
            Assert.IsFalse(interaction.LastMotionAllowed);
            Assert.AreEqual(1, scheduler.SubscriptionCount);
            systemAnimations = true;
            ThemeManager.RefreshSystemColors();
            Assert.AreEqual(!SystemInformation.HighContrast, scheduler.IsRunning);
            ThemeManager.SetReduceMotion(true, persist: false);
            Assert.IsFalse(scheduler.MotionAllowed);
            Assert.IsFalse(scheduler.IsRunning);

            scheduler.Dispose();
            Assert.AreEqual(0, scheduler.SubscriptionCount);
            Assert.IsFalse(scheduler.IsRunning);
            int readsAtDisposal = preferenceReads;
            ThemeManager.RefreshSystemColors();
            window.NotifyActive();
            scheduler.Request(new AnimationProbe(ambient: true));
            Assert.AreEqual(readsAtDisposal, preferenceReads, "Disposed schedulers must release the static preference event.");
            Assert.AreEqual(0, scheduler.SubscriptionCount);
            Assert.IsFalse(scheduler.IsRunning);
        }
        finally { ThemeManager.SetReduceMotion(originalReduceMotion, persist: false); }
    });

    [TestMethod]
    public void AmbientTicksNeverLayoutControlsOrInvalidateImageCanvases() => RunSta(() =>
    {
        using var window = new TestWindow();
        var backdrop = new StudioBackdrop { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(24) };
        backdrop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        backdrop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var preview = new ImagePreviewControl("Original", "Load an image.");
        backdrop.Controls.Add(preview, 0, 0);
        window.Controls.Add(backdrop);
        using var bitmap = new Bitmap(32, 32);
        bitmap.SetPixel(0, 0, Color.Coral);
        preview.SetImage(bitmap, "Borrowed image");
        window.Show();
        window.PerformLayout();
        window.Update();

        int layouts = 0, previewInvalidations = 0, backdropInvalidations = 0;
        foreach (var control in Descendants(window).Prepend(window)) control.Layout += (_, _) => layouts++;
        foreach (var control in Descendants(preview).Prepend(preview)) control.Invalidated += (_, _) => previewInvalidations++;
        backdrop.Invalidated += (_, _) => backdropInvalidations++;
        Rectangle originalBounds = preview.Bounds;
        for (int tick = 1; tick <= 30; tick++) backdrop.Animate(tick * 33, motionAllowed: true);

        Assert.IsTrue(backdropInvalidations > 0, "The backdrop should repaint during decorative motion.");
        Assert.AreEqual(0, previewInvalidations, "Ambient repainting must exclude every preview descendant.");
        Assert.AreEqual(0, layouts, "Animation must never trigger layout.");
        Assert.AreEqual(originalBounds, preview.Bounds);
        Assert.AreEqual(Color.Coral.ToArgb(), bitmap.GetPixel(0, 0).ToArgb(), "The preview only borrows image pixels.");

        backdropInvalidations = 0;
        backdrop.Animate(2000, motionAllowed: false);
        Assert.AreEqual(0, backdropInvalidations, "Reduced motion must not invalidate even the backdrop.");
    });

    [TestMethod]
    public void DisabledButtonsClearHoverAndPressedStateWithoutLayout() => RunSta(() =>
    {
        using var window = new TestWindow();
        var button = new InputButton { Text = "Apply", Size = new Size(140, 44) };
        window.Controls.Add(button);
        window.Show();
        window.PerformLayout();
        var interaction = (RoundedControlInteraction)typeof(RoundedButton)
            .GetField("_interaction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!;
        button.EnterMouse();
        button.PressMouse();
        Assert.IsTrue(interaction.Hovered);
        Assert.IsTrue(interaction.Pressed);
        button.Enabled = false;
        int layouts = 0;
        window.Layout += (_, _) => layouts++;
        button.Layout += (_, _) => layouts++;
        interaction.Animate(1000, motionAllowed: true);
        Assert.IsFalse(interaction.Hovered);
        Assert.IsFalse(interaction.Pressed);
        Assert.AreEqual(0f, interaction.HoverAmount);
        Assert.AreEqual(0f, interaction.PressAmount);
        Assert.AreEqual(0, layouts);
        button.Enabled = true;
        Assert.AreEqual(0f, interaction.HoverAmount, "Re-enabling must not resurrect stale hover state.");
    });

    [TestMethod]
    public void NavigationChangesContentImmediatelyAndRetainsPageState() => RunSta(() =>
    {
        using var window = new TestWindow();
        var navigation = new StudioNavigation { Dock = DockStyle.Fill };
        var titles = new[] { "Basic Filters", "Background Subtraction", "Webcam", "Convolution" };
        var pages = titles.Select(title => new TextBox { Text = title + " retained state" }).ToArray();
        for (int i = 0; i < titles.Length; i++) navigation.AddPage(titles[i], pages[i]);
        window.Controls.Add(navigation);
        window.Show();
        window.PerformLayout();
        Assert.AreEqual(AccessibleRole.PageTabList, navigation.AccessibleRole);
        Assert.AreEqual(4, navigation.PageCount);
        Assert.AreSame(pages[0], navigation.SelectedPage);

        int changes = 0;
        navigation.SelectedIndexChanged += (_, _) =>
        {
            changes++;
            Assert.IsTrue(navigation.SelectedPage!.Visible, "Content must switch before the selection event.");
            Assert.AreEqual(1, pages.Count(page => page.Visible));
        };
        navigation.FocusNavigation();
        Assert.IsTrue(NavigationKey(navigation, Keys.End));
        Assert.AreEqual(3, navigation.SelectedIndex);
        Assert.IsTrue(NavigationKey(navigation, Keys.Right));
        Assert.AreEqual(0, navigation.SelectedIndex, "Right arrow wraps around the navigation.");
        Assert.IsTrue(NavigationKey(navigation, Keys.Left));
        Assert.AreEqual(3, navigation.SelectedIndex);
        Assert.IsTrue(NavigationKey(navigation, Keys.Home));
        Assert.AreEqual(0, navigation.SelectedIndex);
        Assert.IsTrue(NavigationKey(navigation, Keys.Control | Keys.Tab));
        Assert.AreEqual(1, navigation.SelectedIndex);
        Assert.IsTrue(NavigationKey(navigation, Keys.Control | Keys.Shift | Keys.Tab));
        Assert.AreEqual(0, navigation.SelectedIndex);
        navigation.SelectedIndex = 0;
        Assert.AreEqual(6, changes, "Re-selecting the current page must not raise another event.");

        for (int i = 0; i < pages.Length; i++)
        {
            Assert.AreSame(pages[i], navigation.Pages[i]);
            Assert.AreEqual(titles[i] + " retained state", pages[i].Text);
            Assert.IsFalse(pages[i].IsDisposed);
        }
        var buttons = Descendants(navigation).OfType<RoundedRadioButton>().ToArray();
        CollectionAssert.AreEqual(titles, buttons.Select(button => button.AccessibleName).ToArray());
        Assert.AreEqual(1, buttons.Count(button => button.Checked));
        Assert.AreEqual(1, buttons.Count(button => button.TabStop));
        Assert.IsTrue(buttons[0].Focused);
        Assert.IsTrue(buttons[0].AccessibilityObject.State.HasFlag(AccessibleStates.Selected));
        Assert.IsTrue(buttons.Skip(1).All(button => !button.AccessibilityObject.State.HasFlag(AccessibleStates.Selected)));
    });

    [DataTestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DataRow(192)]
    public void ResizeHitTestingKeepsAllEightEdgesAtSupportedDpi(int dpi)
    {
        var size = new Size(800 * dpi / 96, 600 * dpi / 96);
        int grip = 6 * dpi / 96;
        (Point Point, int Hit)[] targets =
        [
            (new(0, 0), 13), (new(size.Width - 1, 0), 14),
            (new(0, size.Height - 1), 16), (new(size.Width - 1, size.Height - 1), 17),
            (new(0, size.Height / 2), 10), (new(size.Width - 1, size.Height / 2), 11),
            (new(size.Width / 2, 0), 12), (new(size.Width / 2, size.Height - 1), 15),
            (new(grip, grip), 1), (new(size.Width / 2, size.Height / 2), 1)
        ];
        foreach (var (point, hit) in targets)
            Assert.AreEqual(hit, StudioForm.ResizeHitTest(point, size, grip), $"Hit test at {point}, {dpi} DPI.");
    }

    private static bool NavigationKey(StudioNavigation navigation, Keys key)
    {
        var message = Message.Create(navigation.Handle, 0x100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
        return (bool)typeof(StudioNavigation).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(navigation, [message, key])!;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Studio UI test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class TestWindow : Form
    {
        public TestWindow()
        {
            Opacity = 0;
            ShowInTaskbar = false;
            ClientSize = new Size(800, 600);
        }
        public void NotifyActive() => OnActivated(EventArgs.Empty);
        public void NotifyInactive() => OnDeactivate(EventArgs.Empty);
    }

    private sealed class InputButton : RoundedButton
    {
        public void EnterMouse() => OnMouseEnter(EventArgs.Empty);
        public void PressMouse() => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
    }

    private sealed class AnimationProbe(bool ambient) : IStudioAnimation
    {
        public bool IsAmbient { get; } = ambient;
        public bool KeepRunning { get; set; }
        public bool LastMotionAllowed { get; private set; }
        public double LastTime { get; private set; }
        public bool Animate(double milliseconds, bool motionAllowed)
        {
            LastTime = milliseconds;
            LastMotionAllowed = motionAllowed;
            return IsAmbient || KeepRunning;
        }
    }
}
