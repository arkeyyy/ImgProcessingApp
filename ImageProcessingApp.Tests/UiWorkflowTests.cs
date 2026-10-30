using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public class UiWorkflowTests
{
    [TestMethod]
    public void WorkspacesRenderAndFilterWithoutSharingOrLosingImages()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetDefaultFont(new Font("Segoe UI", 10));
                using var form = new Form1 { Opacity = 0, ShowInTaskbar = false };
                form.Shown += async (_, _) =>
                {
                    try { await ExerciseAsync(form); }
                    catch (Exception ex) { failure = ex; }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(90)), "UI smoke test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task ExerciseAsync(Form1 form)
    {
        await Task.Delay(50);
        var tabs = Descendants(form).OfType<ThemedTabControl>().Single();
        Assert.AreEqual(4, tabs.TabCount);
        var editors = Descendants(form).OfType<StillImageWorkspace>().ToArray();
        Assert.AreEqual(2, editors.Length);
        var basic = editors[0];
        var convolution = editors[1];
        var convolutionPicker = Picker(convolution);
        Assert.IsNull(convolutionPicker.SelectedFilter);
        StringAssert.Contains(convolutionPicker.Trigger.Text, "Choose filter");
        Assert.IsFalse(convolutionPicker.Enabled);
        convolutionPicker.Trigger.PerformClick();
        Assert.IsFalse(convolutionPicker.Menu.Visible);
        Assert.AreEqual(4, Field<List<Button>>(basic, "_filters").Count);
        Assert.AreEqual(0, Descendants(basic).OfType<CategorizedFilterPicker>().Count());
        var webcam = Descendants(form).OfType<WebcamWorkspace>().Single();
        Assert.AreEqual("None", Picker(webcam).SelectedFilter?.Name);
        VerifyCatalogMenus(convolutionPicker, Picker(webcam));
        VerifyConvolutionNavigation(form, tabs, convolution);
        VerifyConvolutionScrollbarResizing(convolution);
        foreach (var editor in editors)
        {
            Assert.IsFalse(FindButton(editor, "Save Result").Enabled);
            var document = Field<ImageDocument>(editor, "_document");
            document.SetSource(SampleImage(), "color-study.png");
            Invoke(editor, "RefreshPreviews");
            Invoke(editor, "UpdateActions");
        }
        var basicDoc = Field<ImageDocument>(basic, "_document");
        var convolutionDoc = Field<ImageDocument>(convolution, "_document");
        Assert.IsTrue(convolutionPicker.Enabled);
        await (Task)Invoke(basic, "ApplyFilterAsync", "Invert")!;
        using var first = (Bitmap)basicDoc.Result!.Clone();
        await (Task)Invoke(basic, "ApplyFilterAsync", "Invert")!;
        Assert.AreEqual(first.GetPixel(20, 20), basicDoc.Result!.GetPixel(20, 20), "Repeated filters must start from the original.");
        Assert.IsNull(convolutionDoc.Result, "Another workspace must not inherit the first result.");
        Assert.IsTrue(FindButton(basic, "Save Result").Enabled);
        await (Task)Invoke(convolution, "ApplyFilterAsync", "Gaussian Blur")!;
        var successfulSelection = convolutionPicker.SelectedFilter;
        var successfulResult = convolutionDoc.Result;
        await (Task)Invoke(convolution, "ApplyFilterAsync", "Invalid filter")!;
        Assert.IsTrue(convolution.StatusIsError);
        Assert.AreSame(successfulSelection, convolutionPicker.SelectedFilter);
        Assert.AreSame(successfulResult, convolutionDoc.Result);
        Assert.IsTrue(convolutionPicker.Enabled);
        await VerifyPickerKeyboardAsync(convolution, convolutionPicker);
        VerifyScreenEdgePlacement(convolutionPicker);
        var savedResult = basicDoc.Result;
        var folder = Environment.GetEnvironmentVariable("IMAGEPROCESSING_UI_ARTIFACTS");
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        foreach (bool dark in new[] { false, true })
        {
            ThemeManager.SetDark(dark, persist: false);
            Assert.AreSame(savedResult, basicDoc.Result, "Theme switches must retain image state.");
            foreach (var size in new[] { new Size(1200, 760), new Size(780, 640) })
            {
                // The inactive Convolution tab must also adapt when it is next shown.
                tabs.SelectedIndex = 0;
                form.ClientSize = new Size(size.Width * form.DeviceDpi / 96, size.Height * form.DeviceDpi / 96);
                for (int index = 0; index < tabs.TabCount; index++)
                {
                    tabs.SelectedIndex = index;
                    await Task.Delay(40);
                    form.PerformLayout();
                    var workspace = tabs.SelectedTab!.Controls.OfType<WorkspaceControl>().Single();
                    Assert.IsTrue(workspace.Width > 100 && workspace.Height > 100);
                    foreach (var preview in Descendants(workspace).OfType<ImagePreviewControl>())
                        Assert.IsTrue(preview.Width > 100 && preview.Height > 100, "Preview must retain a usable size.");
                    foreach (var previewLayout in Descendants(workspace).OfType<PreviewLayout>())
                        Assert.IsTrue(previewLayout.Height >= 200 * form.DeviceDpi / 96, "The preview container must not collapse.");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Capture(form, Path.Combine(folder, $"{(dark ? "dark" : "light")}-{index}-{size.Width}.png"));
                        File.WriteAllLines(Path.Combine(folder, $"layout-{(dark ? "dark" : "light")}-{index}-{size.Width}.txt"),
                            Descendants(form).Select(c => $"{c.GetType().Name} text={c.Text.Replace(Environment.NewLine, " ")} dpi={c.DeviceDpi} bounds={c.Bounds} client={c.ClientSize} scroll={(c is ScrollableControl sc ? $"horizontal:{sc.HorizontalScroll.Visible} vertical:{sc.VerticalScroll.Visible}" : "")} parent={c.Parent?.GetType().Name} parentSize={c.Parent?.Size} visible={c.Visible} autosize={c.AutoSize}"));
                    }
                    if (workspace == convolution)
                    {
                        await ExerciseConvolutionMenuAsync(convolution, convolutionDoc);
                        VerifyMenuAppearance(convolutionPicker, folder, $"{(dark ? "dark" : "light")}-convolution-{size.Width}");
                        if (!string.IsNullOrEmpty(folder))
                            Capture(form, Path.Combine(folder, $"{(dark ? "dark" : "light")}-convolution-filters-{size.Width}.png"));
                        Descendants(convolution).OfType<ImageWorkspaceLayout>().Single().AutoScrollPosition = Point.Empty;
                    }
                    if (workspace is WebcamWorkspace webcamWorkspace)
                    {
                        string? capturePath = string.IsNullOrEmpty(folder) ? null
                            : Path.Combine(folder, $"{(dark ? "dark" : "light")}-webcam-effects-{size.Width}.png");
                        await ExerciseWebcamEffectsAsync(webcamWorkspace, form, capturePath);
                        VerifyMenuAppearance(Picker(webcamWorkspace), folder, $"{(dark ? "dark" : "light")}-webcam-{size.Width}");
                    }
                }
            }
        }

        var bins = ImageOperations.Histogram(basicDoc.HistogramSource!);
        using (var histogram = new HistogramDialog(bins, "Result", "color-study.png") { Opacity = 0 })
        {
            histogram.Show(form);
            await Task.Delay(40);
            Assert.AreEqual(256, Descendants(histogram).OfType<DataGridView>().Single().Rows.Count);
            if (!string.IsNullOrEmpty(folder)) Capture(histogram, Path.Combine(folder, "histogram-dark.png"));
            histogram.Close();
        }
        Assert.AreSame(savedResult, basicDoc.Result, "Histogram must not replace the exported result.");
        Invoke(basic, "ClearImages");
        Assert.IsNull(basicDoc.Source);
        Assert.IsNull(basicDoc.Result);
        Assert.IsNotNull(convolutionDoc.Source);
        Assert.IsNotNull(convolutionDoc.Result);
        Assert.IsFalse(FindButton(basic, "Save Result").Enabled);
        tabs.SelectedIndex = 3;
        convolutionPicker.Trigger.PerformClick();
        Assert.IsTrue(convolutionPicker.Menu.Visible);
        await convolution.DeactivateAsync();
        Assert.IsFalse(convolutionPicker.Menu.Visible, "Leaving the workspace must close its menu.");
        Invoke(convolution, "ClearImages");
        Assert.IsNull(convolutionPicker.SelectedFilter);
        Assert.IsFalse(convolutionPicker.Enabled);
        StringAssert.Contains(convolutionPicker.Trigger.Text, "Choose filter");
        convolutionDoc.SetSource(new Bitmap(2, 3), "too-small.png");
        Invoke(convolution, "RefreshPreviews");
        Invoke(convolution, "UpdateActions");
        Assert.IsFalse(convolutionPicker.Enabled, "Images smaller than 3 by 3 must disable convolution.");
        Assert.IsNull(convolutionPicker.SelectedFilter);
        ThemeManager.SetDark(false, persist: false);
    }

    private static void VerifyConvolutionNavigation(Form form, ThemedTabControl tabs, StillImageWorkspace workspace)
    {
        var picker = Picker(workspace);
        var filterArea = picker.Parent!;
        var originalMenu = picker.Menu;
        var originalItems = MenuLeaves(picker.Menu.Items).ToArray();
        int layouts = 0;
        LayoutEventHandler countLayout = (_, _) => layouts++;
        filterArea.Layout += countLayout;
        try
        {
            for (int visit = 0; visit < 5; visit++)
            {
                tabs.SelectedIndex = 0;
                layouts = 0;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                tabs.SelectedIndex = 3;
                form.Update();
                timer.Stop();
                Console.WriteLine($"Convolution visit {visit}: {timer.Elapsed.TotalMilliseconds:F1} ms, {layouts} filter-area layouts.");
                // Count work instead of asserting machine-dependent elapsed time.
                // Previously every label measurement caused another table layout
                // (over 160 per revisit); a batched layout needs only a few passes.
                Assert.IsTrue(layouts < 30, $"Tab activation caused {layouts} filter-area layouts.");
                Assert.AreSame(originalMenu, picker.Menu);
                CollectionAssert.AreEqual(originalItems, MenuLeaves(picker.Menu.Items).ToArray(), "Navigation must reuse the menu.");
            }
        }
        finally { filterArea.Layout -= countLayout; }
    }

    private static void VerifyConvolutionScrollbarResizing(StillImageWorkspace workspace)
    {
        var viewport = Descendants(workspace).OfType<ImageWorkspaceLayout>().Single();
        var description = Field<Label>(workspace, "_description");
        var dock = workspace.Dock;
        string originalDescription = description.Text;
        workspace.Dock = DockStyle.None;
        try
        {
            // Size the workspace directly so this check is independent of monitor size.
            workspace.Size = new Size(780 * workspace.DeviceDpi / 96, 640 * workspace.DeviceDpi / 96);
            description.Text = string.Join(" ", Enumerable.Repeat("A longer filter explanation must wrap without hiding controls.", 5));
            workspace.PerformLayout();
            Assert.IsTrue(viewport.VerticalScroll.Visible);
            Assert.IsFalse(viewport.HorizontalScroll.Visible);
            int wrappedHeight = description.Height;

            workspace.Size = new Size(1400 * workspace.DeviceDpi / 96, 1600 * workspace.DeviceDpi / 96);
            workspace.PerformLayout();
            Assert.IsFalse(viewport.VerticalScroll.Visible, "The scrollbar must disappear when the full layout fits.");
            Assert.IsFalse(viewport.HorizontalScroll.Visible);
            Assert.IsTrue(description.Height < wrappedHeight, "Descriptions must reflow when the viewport widens.");
            foreach (var preview in Descendants(workspace).OfType<ImagePreviewControl>())
                Assert.IsTrue(preview.Height > 100 && preview.Width > 100);
        }
        finally
        {
            description.Text = originalDescription;
            workspace.Dock = dock;
        }
    }

    private static async Task ExerciseConvolutionMenuAsync(StillImageWorkspace workspace, ImageDocument document)
    {
        var layout = Descendants(workspace).OfType<ImageWorkspaceLayout>().Single();
        var picker = Picker(workspace);
        var description = Field<Label>(workspace, "_description");
        Assert.IsFalse(layout.HorizontalScroll.Visible);
        layout.AutoScrollPosition = Point.Empty;
        AssertPickerAbovePreviews(workspace, picker, description);
        Assert.IsInstanceOfType<RoundedButton>(picker.Trigger);
        Assert.IsTrue(picker.Trigger.TabStop);
        foreach (var filter in ConvolutionFilters.All)
        {
            string effect = filter.Name;
            var item = MenuLeaves(picker.Menu.Items).Single(item => item.Name == effect);
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.AccessibleDescription));
            StringAssert.Contains(item.ToolTipText, filter.Description);
            using var expected = ImageOperations.ApplyConvolution(document.Source!, effect);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var previousSelection = picker.SelectedFilter;
                var previousResult = document.Result;
                OpenItem(picker, item);
                item.PerformClick();
                Assert.IsTrue(workspace.IsBusy);
                Assert.IsFalse(picker.Enabled);
                Assert.IsFalse(picker.Trigger.Enabled);
                Assert.AreSame(previousSelection, picker.SelectedFilter, "Selection commits only after processing succeeds.");
                Assert.IsFalse(FindButton(workspace, "Save Result").Enabled);
                var operation = (Task)typeof(AsyncImageWorkspace).GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
                await operation;
                Assert.IsFalse(workspace.IsBusy);
                Assert.IsFalse(workspace.StatusIsError, workspace.StatusText);
                Assert.IsTrue(picker.Enabled);
                Assert.IsTrue(FindButton(workspace, "Save Result").Enabled);
                Assert.AreEqual(effect, Field<string>(workspace, "_resultEffect"));
                Assert.AreEqual(effect, picker.SelectedFilter?.Name);
                StringAssert.Contains(picker.Trigger.Text, effect);
                StringAssert.Contains(description.Text, filter.Description);
                Assert.AreEqual(1, MenuLeaves(picker.Menu.Items).Count(candidate => candidate.Checked));
                Assert.IsTrue(item.Checked);
                Assert.AreNotSame(previousResult, document.Result, "Repeated selections must run again.");
                Assert.AreEqual(expected.Size, document.Result!.Size);
                for (int y = 0; y < expected.Height; y += 17)
                    for (int x = 0; x < expected.Width; x += 19)
                        Assert.AreEqual(expected.GetPixel(x, y), document.Result.GetPixel(x, y), $"{effect} must start from the original on every selection.");
            }
        }
        var result = document.Result;
        var selection = picker.SelectedFilter;
        var explanation = description.Text;
        picker.Trigger.PerformClick();
        Assert.IsTrue(picker.Menu.Visible);
        picker.Menu.Close(ToolStripDropDownCloseReason.Keyboard);
        Assert.AreSame(result, document.Result);
        Assert.AreSame(selection, picker.SelectedFilter);
        Assert.AreEqual(explanation, description.Text);
        Assert.IsFalse(workspace.IsBusy);
        Assert.IsTrue(picker.Trigger.Focused, "Dismissing a menu must restore focus to its trigger.");
    }

    private static async Task ExerciseWebcamEffectsAsync(WebcamWorkspace workspace, Form form, string? capturePath)
    {
        // Let device discovery finish; no camera is started by this test.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Field<bool>(workspace, "_enumerating") && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.IsFalse(Field<bool>(workspace, "_enumerating"));
        Assert.IsNull(Field<object?>(workspace, "_source"));
        var picker = Picker(workspace);
        var options = MenuLeaves(picker.Menu.Items).ToArray();
        var description = Field<Label>(workspace, "_effectDescription");
        var viewport = Descendants(workspace).OfType<ImageWorkspaceLayout>().Single();
        Assert.AreEqual(15, options.Length);
        Assert.IsFalse(viewport.HorizontalScroll.Visible);
        object previousState = Field<object>(workspace, "_state");
        string previousStatus = workspace.StatusText;
        // An unstarted source is only an identity for the callback's sender check.
        var source = new AForge.Video.DirectShow.VideoCaptureDevice();
        SetField(workspace, "_source", source);
        SetField(workspace, "_acceptFrames", true);
        SetField(workspace, "_state", Enum.Parse(previousState.GetType(), "Starting"));
        using var frame = SampleImage();
        var args = new AForge.Video.NewFrameEventArgs(frame);
        try
        {
            foreach (var effect in WebcamEffects.All)
            {
                var option = options.Single(item => item.Name == effect.Name);
                OpenItem(picker, option);
                StringAssert.Contains(option.ToolTipText, effect.Description);
                option.PerformClick();
                Assert.IsTrue(option.Checked);
                Assert.AreEqual(1, options.Count(item => item.Checked));
                Assert.AreEqual(effect.Name, Field<string>(workspace, "_effect"));
                Assert.AreEqual(effect.Name, picker.SelectedFilter?.Name);
                StringAssert.Contains(description.Text, effect.Description);
                AssertPickerAbovePreviews(workspace, picker, description);

                // Hold the UI thread briefly so publication stays queued while we
                // verify that a second callback cannot add a backlog of frames.
                Task.Run(() => Invoke(workspace, "OnNewFrame", source, args)).GetAwaiter().GetResult();
                var pending = Field<object?>(workspace, "_pendingFrame");
                Assert.IsNotNull(pending, $"{effect.Name} must process a frame successfully.");
                Task.Run(() => Invoke(workspace, "OnNewFrame", source, args)).GetAwaiter().GetResult();
                Assert.AreSame(pending, Field<object?>(workspace, "_pendingFrame"));
                await DrainUiAsync(workspace);
                var displayed = Field<object?>(workspace, "_displayedFrame");
                Assert.AreSame(pending, displayed);
                Assert.IsNull(Field<object?>(workspace, "_frameTicket"));
                Assert.IsFalse(workspace.StatusIsError, workspace.StatusText);
                var result = (Bitmap)displayed!.GetType().GetProperty("Result")!.GetValue(displayed)!;
                var original = (Bitmap)displayed.GetType().GetProperty("Original")!.GetValue(displayed)!;
                Assert.AreEqual(effect.Name, displayed.GetType().GetProperty("Effect")!.GetValue(displayed));
                using var expected = WebcamEffects.Apply(frame, effect.Name);
                Assert.AreEqual(frame.Size, result.Size);
                for (int y = 0; y < frame.Height; y += 17)
                    for (int x = 0; x < frame.Width; x += 19)
                    {
                        Assert.AreEqual(expected.GetPixel(x, y), result.GetPixel(x, y), effect.Name);
                        Assert.AreEqual(frame.GetPixel(x, y), original.GetPixel(x, y), "Every effect must use the original frame.");
                    }
            }
            var selectedEffect = Field<string>(workspace, "_effect");
            var selectedDescription = description.Text;
            picker.Trigger.PerformClick();
            picker.Menu.Close(ToolStripDropDownCloseReason.Keyboard);
            Assert.AreEqual(selectedEffect, Field<string>(workspace, "_effect"));
            Assert.AreEqual(selectedDescription, description.Text);
            Assert.IsTrue(picker.Trigger.Focused);
            viewport.AutoScrollPosition = Point.Empty;
            if (capturePath is not null) Capture(form, capturePath);

            Task.Run(() => Invoke(workspace, "OnNewFrame", source, args)).GetAwaiter().GetResult();
            Assert.IsNotNull(Field<object?>(workspace, "_pendingFrame"));
            SetField(workspace, "_source", null);
            picker.Trigger.PerformClick();
            Assert.IsTrue(picker.Menu.Visible);
            await workspace.DeactivateAsync();
            Assert.IsFalse(picker.Menu.Visible);
            await DrainUiAsync(workspace);
            Assert.IsNull(Field<object?>(workspace, "_pendingFrame"));
            Assert.IsNull(Field<object?>(workspace, "_displayedFrame"));
            Assert.IsNull(Field<object?>(workspace, "_frameTicket"));
        }
        finally
        {
            SetField(workspace, "_source", null);
            await workspace.DeactivateAsync();
            Invoke(workspace, "SetState", previousState, previousStatus);
        }
    }

    private static CategorizedFilterPicker Picker(Control workspace) =>
        Descendants(workspace).OfType<CategorizedFilterPicker>().Single();

    private static IEnumerable<ToolStripMenuItem> MenuLeaves(ToolStripItemCollection items)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            if (!item.HasDropDownItems) yield return item;
            else foreach (var leaf in MenuLeaves(item.DropDownItems)) yield return leaf;
        }
    }

    private static void VerifyCatalogMenus(CategorizedFilterPicker convolution, CategorizedFilterPicker webcam)
    {
        var convolutionGroups = new Dictionary<string, string[]>
        {
            ["Smooth & sharpen"] = ["Smooth", "Gaussian Blur", "Sharpen", "Mean Removal"],
            ["Motion blur"] = ["Motion Blur Horizontal", "Motion Blur Vertical", "Motion Blur Diagonal"],
            ["Edge detection"] = ["Laplacian", "Sobel Edges", "Prewitt Edges", "Scharr Edges"],
            ["Emboss"] = ["Horizontal + Vertical", "All Directions", "Lossy", "Horizontal Only", "Vertical Only", "Emboss NW\u2013SE", "Emboss NE\u2013SW"]
        };
        var webcamGroups = new Dictionary<string, string[]>
        {
            ["Color & tone"] = ["Grayscale", "Invert", "Sepia"],
            ["Background"] = ["Subtract"],
            ["Smooth & sharpen"] = ["Gaussian Blur", "Sharpen"],
            ["Motion blur"] = ["Motion Blur Horizontal", "Motion Blur Vertical", "Motion Blur Diagonal"],
            ["Edge detection"] = ["Sobel Edges", "Prewitt Edges", "Scharr Edges"],
            ["Emboss"] = ["Emboss NW\u2013SE", "Emboss NE\u2013SW"]
        };
        VerifyGroups(convolution, convolutionGroups, 18);
        VerifyGroups(webcam, webcamGroups, 15);
        var none = webcam.Menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "None");
        Assert.IsFalse(none.HasDropDownItems);
        Assert.IsTrue(none.Checked);
        Assert.IsNull(((FilterChoice)none.Tag!).Category);
        Assert.AreEqual(7, webcam.Menu.Items.OfType<ToolStripMenuItem>().Count());
        Assert.AreEqual(4, convolution.Menu.Items.OfType<ToolStripMenuItem>().Count());
    }

    private static void VerifyGroups(CategorizedFilterPicker picker, Dictionary<string, string[]> expected, int count)
    {
        var leaves = MenuLeaves(picker.Menu.Items).ToArray();
        Assert.AreEqual(count, leaves.Length);
        Assert.AreEqual(count, leaves.Select(item => item.Name).Distinct().Count(), "Every filter must appear once.");
        foreach (var (category, names) in expected)
        {
            var group = picker.Menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == category);
            CollectionAssert.AreEqual(names, group.DropDownItems.OfType<ToolStripMenuItem>().Select(item => item.Name).ToArray());
            foreach (ToolStripMenuItem item in group.DropDownItems)
            {
                var filter = (FilterChoice)item.Tag!;
                Assert.AreEqual(category, filter.Category);
                Assert.AreEqual(item.Name, filter.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.AccessibleName));
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.AccessibleDescription));
            }
        }
    }

    private static void AssertPickerAbovePreviews(Control workspace, CategorizedFilterPicker picker, Label description)
    {
        int pickerBottom = picker.PointToScreen(new Point(0, picker.Height)).Y;
        int descriptionTop = description.PointToScreen(Point.Empty).Y;
        int descriptionBottom = description.PointToScreen(new Point(0, description.Height)).Y;
        Assert.IsTrue(pickerBottom <= descriptionTop, "The selected filter's explanation belongs under the picker.");
        foreach (var preview in Descendants(workspace).OfType<ImagePreviewControl>())
            Assert.IsTrue(descriptionBottom <= preview.PointToScreen(Point.Empty).Y, "The picker and explanation belong above previews.");
    }

    private static void OpenItem(CategorizedFilterPicker picker, ToolStripMenuItem item)
    {
        picker.Trigger.Select();
        picker.Trigger.PerformClick();
        Assert.IsTrue(picker.Menu.Visible);
        if (item.OwnerItem is ToolStripMenuItem category)
        {
            category.ShowDropDown();
            Assert.IsTrue(category.DropDown.Visible);
        }
        item.Select();
    }

    private static void VerifyMenuAppearance(CategorizedFilterPicker picker, string? folder, string prefix)
    {
        var palette = ThemeManager.Current;
        picker.Trigger.PerformClick();
        Assert.IsTrue(picker.Menu.Visible);
        VerifyPopup(picker.Menu, palette);
        if (!string.IsNullOrEmpty(folder)) Capture(picker.Menu, Path.Combine(folder, prefix + "-menu.png"));
        foreach (var category in picker.Menu.Items.OfType<ToolStripMenuItem>().Where(item => item.HasDropDownItems))
        {
            category.ShowDropDown();
            VerifyPopup(category.DropDown, palette);
            if (!string.IsNullOrEmpty(folder))
                Capture(category.DropDown, Path.Combine(folder, prefix + "-" + category.Name!.Replace(" & ", "-") + ".png"));
            category.HideDropDown();
        }
        picker.Menu.Close(ToolStripDropDownCloseReason.Keyboard);
        Assert.IsTrue(picker.Trigger.Focused);
    }

    private static void VerifyPopup(ToolStripDropDown popup, ThemePalette palette)
    {
        Assert.IsTrue(popup.Visible);
        Assert.IsTrue(Screen.FromRectangle(popup.Bounds).WorkingArea.Contains(popup.Bounds), "Native menus must fit the screen working area.");
        if (SystemInformation.HighContrast)
            Assert.IsInstanceOfType<ToolStripSystemRenderer>(popup.Renderer);
        else
        {
            Assert.AreEqual(palette.Surface, popup.BackColor);
            foreach (ToolStripItem item in popup.Items) Assert.AreEqual(palette.Text, item.ForeColor);
        }
    }

    private static async Task VerifyPickerKeyboardAsync(StillImageWorkspace workspace, CategorizedFilterPicker picker)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        picker.Trigger.Select();
        var selection = picker.SelectedFilter;
        var command = Message.Create(picker.Trigger.Handle, 0x104, (IntPtr)Keys.Down, IntPtr.Zero);
        Assert.IsTrue((bool)typeof(CategorizedFilterPicker).GetMethod("ProcessCmdKey", flags)!
            .Invoke(picker, [command, Keys.Alt | Keys.Down])!);
        Assert.IsTrue(picker.Menu.Visible, "Alt+Down must open the picker.");
        MenuKey(picker.Menu, Keys.Down);
        var category = picker.Menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Selected);
        Assert.AreEqual("Motion blur", category.Name, "Arrow keys must move between categories.");
        MenuKey(picker.Menu, Keys.Right);
        Assert.IsTrue(category.DropDown.Visible, "Right arrow must open the category submenu.");
        MenuKey(category.DropDown, Keys.Escape);
        Assert.IsFalse(category.DropDown.Visible);
        MenuKey(picker.Menu, Keys.Escape);
        Assert.IsFalse(picker.Menu.Visible);
        Assert.AreSame(selection, picker.SelectedFilter);
        Assert.IsTrue(picker.Trigger.Focused);

        typeof(Control).GetMethod("ProcessDialogKey", flags)!.Invoke(picker.Trigger, [Keys.Enter]);
        Assert.IsTrue(picker.Menu.Visible, "Enter must open the picker.");
        MenuKey(picker.Menu, Keys.Escape);
        typeof(Control).GetMethod("OnKeyDown", flags)!.Invoke(picker.Trigger, [new KeyEventArgs(Keys.Space)]);
        typeof(Control).GetMethod("OnKeyUp", flags)!.Invoke(picker.Trigger, [new KeyEventArgs(Keys.Space)]);
        Assert.IsTrue(picker.Menu.Visible, "Space must open the picker.");
        MenuKey(picker.Menu, Keys.Right);
        var firstCategory = (ToolStripMenuItem)picker.Menu.Items[0];
        Assert.IsTrue(firstCategory.DropDown.Visible);
        MenuKey(firstCategory.DropDown, Keys.Enter);
        Assert.IsTrue(workspace.IsBusy, "Enter must invoke the highlighted filter.");
        await (Task)typeof(AsyncImageWorkspace).GetField("_operation", flags)!.GetValue(workspace)!;
        Assert.AreEqual("Smooth", picker.SelectedFilter?.Name);
        Assert.IsFalse(picker.Menu.Visible);
        Assert.IsTrue(picker.Trigger.Focused, "Focus must return after asynchronous filtering.");
    }

    private static void MenuKey(ToolStripDropDown menu, Keys key) =>
        typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, [key]);

    private static void VerifyScreenEdgePlacement(CategorizedFilterPicker picker)
    {
        var area = Screen.FromControl(picker).WorkingArea;
        foreach (var point in new[] { area.Location, new Point(area.Right - 1, area.Top),
            new Point(area.Left, area.Bottom - 1), new Point(area.Right - 1, area.Bottom - 1) })
        {
            picker.Menu.Show(picker.Trigger, picker.Trigger.PointToClient(point));
            VerifyPopup(picker.Menu, ThemeManager.Current);
            foreach (var category in picker.Menu.Items.OfType<ToolStripMenuItem>().Where(item => item.HasDropDownItems))
            {
                category.ShowDropDown();
                VerifyPopup(category.DropDown, ThemeManager.Current);
                category.HideDropDown();
            }
            picker.Menu.Close();
        }
    }

    private static async Task DrainUiAsync(Control control)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.BeginInvoke((Action)(() => completion.SetResult()));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void SetField(object instance, string name, object? value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static Bitmap SampleImage()
    {
        var image = new Bitmap(640, 360);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(244, 231, 207));
        using var blue = new SolidBrush(Color.FromArgb(48, 103, 164));
        using var orange = new SolidBrush(Color.FromArgb(228, 123, 68));
        using var green = new SolidBrush(Color.FromArgb(61, 122, 102));
        graphics.FillRectangle(blue, 56, 64, 210, 210);
        graphics.FillEllipse(orange, 216, 95, 220, 220);
        graphics.FillPolygon(green, new Point[] { new(434, 54), new(584, 282), new(354, 282) });
        return image;
    }

    private static void Capture(Control control, string path)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Button FindButton(Control root, string text) => Descendants(root).OfType<Button>().Single(button => button.Text == text);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(instance, arguments);
}
