using System.Drawing.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public sealed class ImageOperationsTests
{
    [DataTestMethod]
    [DataRow("Copy Original", 30, 90, 150)]
    [DataRow("Grayscale", 90, 90, 90)]
    [DataRow("Invert", 225, 165, 105)]
    [DataRow("Sepia", 109, 97, 75)]
    public void BasicEffectsPreserveExistingFormulasAndSource(string effect, int red, int green, int blue)
    {
        using var source = new Bitmap(1, 1);
        source.SetPixel(0, 0, Color.FromArgb(30, 90, 150));
        using var result = ImageOperations.ApplyBasic(source, effect);
        Assert.AreNotSame(source, result);
        Assert.AreEqual(Color.FromArgb(red, green, blue).ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(Color.FromArgb(30, 90, 150).ToArgb(), source.GetPixel(0, 0).ToArgb());
    }

    [TestMethod]
    public void SubtractionUsesBrightnessThresholdAndOverlappingBounds()
    {
        using var foreground = new Bitmap(4, 2);
        using var background = new Bitmap(3, 3);
        using (var graphics = Graphics.FromImage(background)) graphics.Clear(Color.Blue);
        foreground.SetPixel(0, 0, Color.FromArgb(80, 80, 80));
        foreground.SetPixel(1, 0, Color.FromArgb(90, 90, 90));
        foreground.SetPixel(2, 0, Color.FromArgb(91, 91, 91));
        foreground.SetPixel(0, 1, Color.Lime);
        using var result = ImageOperations.Subtract(foreground, background);
        using var webcam = ImageOperations.ApplyBasic(foreground, "Subtract");
        Assert.AreEqual(new Size(3, 2), result.Size);
        Assert.AreEqual(Color.Blue.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), result.GetPixel(1, 0).ToArgb());
        Assert.AreEqual(Color.FromArgb(91, 91, 91).ToArgb(), result.GetPixel(2, 0).ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), result.GetPixel(0, 1).ToArgb());
        Assert.AreEqual(Color.Black.ToArgb(), webcam.GetPixel(0, 1).ToArgb());
        Assert.AreEqual(Color.Lime.ToArgb(), foreground.GetPixel(0, 1).ToArgb());
    }

    [TestMethod]
    public void ConvolutionNormalizesTransparencyToWhiteRgbAndPreservesOriginal()
    {
        using var source = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        using var result = ImageOperations.ApplyConvolution(source, "Smooth");
        Assert.AreEqual(PixelFormat.Format24bppRgb, result.PixelFormat);
        Assert.AreEqual(Color.White.ToArgb(), result.GetPixel(1, 1).ToArgb());
        Assert.AreEqual(Color.White.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(0, source.GetPixel(1, 1).A);
    }

    [DataTestMethod]
    [DataRow("Smooth")]
    [DataRow("Gaussian Blur")]
    [DataRow("Sharpen")]
    [DataRow("Mean Removal")]
    [DataRow("Laplacian")]
    [DataRow("Horizontal + Vertical")]
    [DataRow("All Directions")]
    [DataRow("Lossy")]
    [DataRow("Horizontal Only")]
    [DataRow("Vertical Only")]
    public void EveryConvolutionReturnsIndependentResultAndKeepsBorders(string effect)
    {
        using var source = Pattern(7, 6);
        using var result = ImageOperations.ApplyConvolution(source, effect);
        using var repeated = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreNotSame(source, result);
        Assert.AreEqual(source.Size, result.Size);
        Assert.AreEqual(source.GetPixel(0, 3), result.GetPixel(0, 3));
        Assert.AreEqual(source.GetPixel(6, 3), result.GetPixel(6, 3));
        Assert.AreEqual(source.GetPixel(3, 0), result.GetPixel(3, 0));
        Assert.AreEqual(source.GetPixel(3, 5), result.GetPixel(3, 5));
        Assert.AreEqual(PatternColor(3, 3), source.GetPixel(3, 3));
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                Assert.AreEqual(result.GetPixel(x, y), repeated.GetPixel(x, y));
    }

    [TestMethod]
    public void ConvolutionUsesCorrectRowsIncludingPaddedStride()
    {
        using var source = Pattern(7, 6);
        using var result = ImageOperations.ApplyConvolution(source, "Smooth");
        for (int y = 1; y < 5; y++)
        {
            for (int x = 1; x < 6; x++)
            {
                int red = 0, green = 0, blue = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var pixel = source.GetPixel(x + dx, y + dy);
                        red += pixel.R;
                        green += pixel.G;
                        blue += pixel.B;
                    }
                }
                Assert.AreEqual(Color.FromArgb(red / 9, green / 9, blue / 9), result.GetPixel(x, y), $"Pixel {x}, {y}");
            }
        }
    }

    [TestMethod]
    public void ConvolutionRejectsTinyImagesAndCancellationLeavesInputUsable()
    {
        using var tiny = new Bitmap(2, 3);
        Assert.ThrowsException<ArgumentException>(() => ImageOperations.ApplyConvolution(tiny, "Smooth"));
        using var source = Pattern(7, 6);
        var cancelled = new CancellationToken(true);
        Assert.ThrowsException<OperationCanceledException>(() => ImageOperations.ApplyConvolution(source, "Smooth", cancelled));
        Assert.ThrowsException<OperationCanceledException>(() => ImageOperations.ApplyBasic(source, "Invert", cancelled));
        Assert.ThrowsException<OperationCanceledException>(() => ImageOperations.Histogram(source, cancelled));
        Assert.ThrowsException<OperationCanceledException>(() => ImageOperations.Subtract(source, source, cancelled));
        Assert.AreEqual(PatternColor(3, 3), source.GetPixel(3, 3));
    }

    [TestMethod]
    public void HistogramCountsEachPixelUsingAverageBrightness()
    {
        using var source = new Bitmap(3, 1);
        source.SetPixel(0, 0, Color.Black);
        source.SetPixel(1, 0, Color.Red);
        source.SetPixel(2, 0, Color.White);
        var bins = ImageOperations.Histogram(source);
        Assert.AreEqual(256, bins.Length);
        Assert.AreEqual(3, bins.Sum());
        Assert.AreEqual(1, bins[0]);
        Assert.AreEqual(1, bins[85]);
        Assert.AreEqual(1, bins[255]);
    }

    [DataTestMethod]
    [DataRow(1, "89504E470D0A1A0A")]
    [DataRow(2, "FFD8FF")]
    [DataRow(3, "424D")]
    public void SaveUsesSelectedEncoderRegardlessOfExtension(int filterIndex, string expectedSignature)
    {
        string path = Path.Combine(Path.GetTempPath(), $"image-processing-{Guid.NewGuid():N}.data");
        try
        {
            using var source = Pattern(7, 6);
            ImageOperations.Save(source, path, filterIndex);
            byte[] bytes = File.ReadAllBytes(path);
            StringAssert.StartsWith(Convert.ToHexString(bytes), expectedSignature);
            using var loaded = ImageOperations.LoadDetached(path);
            Assert.AreEqual(source.Size, loaded.Size);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void LoadReleasesSourceFileWhileImageRemainsUsable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"image-processing-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = Pattern(7, 6)) source.Save(path, ImageFormat.Png);
            using var loaded = ImageOperations.LoadDetached(path);
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.IsTrue(exclusive.Length > 0);
            File.Delete(path);
            Assert.AreEqual(PatternColor(3, 3), loaded.GetPixel(3, 3));
        }
        finally { File.Delete(path); }
    }

    private static Color PatternColor(int x, int y) =>
        Color.FromArgb((x * 37 + y * 19) % 256, (x * 11 + y * 53) % 256, (x * 71 + y * 7) % 256);

    private static Bitmap Pattern(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) bitmap.SetPixel(x, y, PatternColor(x, y));
        return bitmap;
    }
}
