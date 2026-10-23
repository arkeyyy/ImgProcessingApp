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
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(45)), "UI smoke test timed out.");
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
        await (Task)Invoke(basic, "ApplyFilterAsync", "Invert")!;
        using var first = (Bitmap)basicDoc.Result!.Clone();
        await (Task)Invoke(basic, "ApplyFilterAsync", "Invert")!;
        Assert.AreEqual(first.GetPixel(20, 20), basicDoc.Result!.GetPixel(20, 20), "Repeated filters must start from the original.");
        Assert.IsNull(convolutionDoc.Result, "Another workspace must not inherit the first result.");
        Assert.IsTrue(FindButton(basic, "Save Result").Enabled);
        await (Task)Invoke(convolution, "ApplyFilterAsync", "Gaussian Blur")!;
        var savedResult = basicDoc.Result;
        var folder = Environment.GetEnvironmentVariable("IMAGEPROCESSING_UI_ARTIFACTS");
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        foreach (bool dark in new[] { false, true })
        {
            ThemeManager.SetDark(dark, persist: false);
            Assert.AreSame(savedResult, basicDoc.Result, "Theme switches must retain image state.");
            foreach (var size in new[] { new Size(1200, 760), new Size(780, 640) })
            {
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
        ThemeManager.SetDark(false, persist: false);
    }

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
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments);
}
