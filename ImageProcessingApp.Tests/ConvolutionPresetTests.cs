using System.Drawing.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public sealed class ConvolutionPresetTests
{
    public static IEnumerable<object[]> NewPresets => new[]
    {
        "Motion Blur Horizontal", "Motion Blur Vertical", "Motion Blur Diagonal",
        "Emboss NW–SE", "Emboss NE–SW", "Sobel Edges", "Prewitt Edges", "Scharr Edges"
    }.Select(effect => new object[] { effect });

    [DataTestMethod]
    [DataRow("Motion Blur Horizontal", 7, 52, 15)]
    [DataRow("Motion Blur Vertical", 8, 51, 17)]
    [DataRow("Motion Blur Diagonal", 9, 51, 18)]
    [DataRow("Emboss NW–SE", 185, 71, 242)]
    [DataRow("Emboss NE–SW", 157, 99, 186)]
    [DataRow("Sobel Edges", 10, 10, 10)]
    [DataRow("Prewitt Edges", 9, 9, 9)]
    [DataRow("Scharr Edges", 10, 10, 10)]
    public void MinimumInputHasExpectedNumericalResult(string effect, int red, int green, int blue)
    {
        using var source = new Bitmap(3, 3);
        int[] values = [1, 2, 3, 5, 7, 11, 13, 17, 19];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                int v = values[y * 3 + x];
                source.SetPixel(x, y, Color.FromArgb(v, 60 - v, 2 * v));
            }
        using var result = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(Color.FromArgb(red, green, blue), result.GetPixel(1, 1));
    }

    [DataTestMethod]
    [DataRow("Motion Blur Horizontal", 1, 0)]
    [DataRow("Motion Blur Vertical", 0, 1)]
    [DataRow("Motion Blur Diagonal", 1, 1)]
    public void MotionBlurSpreadsImpulseOnlyAlongSelectedDirection(string effect, int dx, int dy)
    {
        using var source = Solid(7, 7, Color.Black);
        source.SetPixel(3, 3, Color.FromArgb(255, 120, 30));
        using var result = ImageOperations.ApplyConvolution(source, effect);
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
            {
                bool onLine = Enumerable.Range(-1, 3).Any(i => x == 3 + i * dx && y == 3 + i * dy);
                Assert.AreEqual(onLine ? Color.FromArgb(85, 40, 10) : Color.FromArgb(0, 0, 0), result.GetPixel(x, y));
            }
    }

    [DataTestMethod]
    [DynamicData(nameof(NewPresets))]
    public void ConstantImagesHaveExpectedInteriorAndUnchangedBorder(string effect)
    {
        var color = Color.FromArgb(37, 91, 173);
        using var source = Solid(5, 6, color);
        using var result = ImageOperations.ApplyConvolution(source, effect);
        var expected = effect.StartsWith("Motion") ? color
            : effect.StartsWith("Emboss") ? Color.FromArgb(128, 128, 128) : Color.FromArgb(0, 0, 0);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                Assert.AreEqual(x == 0 || y == 0 || x == 4 || y == 5 ? color : expected, result.GetPixel(x, y));
    }

    [DataTestMethod]
    [DataRow("Sobel Edges", 60, 42)]
    [DataRow("Prewitt Edges", 40, 56)]
    [DataRow("Scharr Edges", 75, 31)]
    public void EdgeWeightsAndCombinedMagnitudeMatchKnownImpulses(string effect, int axial, int diagonal)
    {
        foreach (var sample in new[] { (X: 2, Y: 1, Expected: axial), (X: 2, Y: 0, Expected: diagonal) })
        {
            using var source = Solid(3, 3, Color.Black);
            source.SetPixel(sample.X, sample.Y, Color.FromArgb(120, 120, 120));
            using var result = ImageOperations.ApplyConvolution(source, effect);
            Assert.AreEqual(Color.FromArgb(sample.Expected, sample.Expected, sample.Expected), result.GetPixel(1, 1));
        }
    }

    [DataTestMethod]
    [DataRow("Sobel Edges")]
    [DataRow("Prewitt Edges")]
    [DataRow("Scharr Edges")]
    public void EdgeTransitionsRetainBothSignsAndAxes(string effect)
    {
        foreach (bool vertical in new[] { false, true })
            foreach (bool reverse in new[] { false, true })
                foreach (int contrast in new[] { 120, 255 })
                {
                    using var source = new Bitmap(3, 3);
                    for (int y = 0; y < 3; y++)
                        for (int x = 0; x < 3; x++)
                        {
                            int v = ((vertical ? y : x) == 2) != reverse ? contrast : 0;
                            source.SetPixel(x, y, Color.FromArgb(v, v, v));
                        }
                    using var result = ImageOperations.ApplyConvolution(source, effect);
                    Assert.AreEqual(Color.FromArgb(contrast, contrast, contrast), result.GetPixel(1, 1));
                }
    }

    [DataTestMethod]
    [DataRow("Sobel Edges", 255)]
    [DataRow("Prewitt Edges", 240)]
    [DataRow("Scharr Edges", 255)]
    public void EdgeMagnitudeClampsAfterCombiningGradients(string effect, int expected)
    {
        using var source = Solid(3, 3, Color.Black);
        source.SetPixel(2, 1, Color.White);
        source.SetPixel(1, 2, Color.White);
        source.SetPixel(2, 2, Color.White);
        using var result = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(Color.FromArgb(expected, expected, expected), result.GetPixel(1, 1));
    }

    [DataTestMethod]
    [DataRow("Emboss NW–SE", 2, 2)]
    [DataRow("Emboss NE–SW", 0, 2)]
    public void EmbossAppliesOffsetThenClampsPositiveAndNegativeRelief(string effect, int brightX, int brightY)
    {
        using var source = Solid(3, 3, Color.Black);
        source.SetPixel(brightX, brightY, Color.FromArgb(100, 20, 0));
        using var positive = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(Color.FromArgb(255, 168, 128), positive.GetPixel(1, 1));
        source.SetPixel(brightX, brightY, Color.Black);
        source.SetPixel(2 - brightX, 2 - brightY, Color.FromArgb(100, 20, 0));
        using var negative = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(Color.FromArgb(0, 88, 128), negative.GetPixel(1, 1));
    }

    [DataTestMethod]
    [DynamicData(nameof(NewPresets))]
    public void AllStridePaddingsMatchIsolatedNeighborhoods(string effect)
    {
        // Widths 3..7 cover 0, 1, 2, and 3 bytes of 24-bit row padding.
        for (int width = 3; width <= 7; width++)
        {
            using var source = new Bitmap(width, 6, PixelFormat.Format24bppRgb);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < width; x++)
                    source.SetPixel(x, y, Color.FromArgb((x * 61 + y * 17) % 256, (x * 23 + y * 53) % 256, (x * 47 + y * 31) % 256));
            using var result = ImageOperations.ApplyConvolution(source, effect);
            for (int y = 1; y < source.Height - 1; y++)
                for (int x = 1; x < width - 1; x++)
                {
                    using var neighborhood = source.Clone(new Rectangle(x - 1, y - 1, 3, 3), source.PixelFormat);
                    using var expected = ImageOperations.ApplyConvolution(neighborhood, effect);
                    Assert.AreEqual(expected.GetPixel(1, 1), result.GetPixel(x, y), $"{effect}: width {width}, pixel {x},{y}");
                }
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(NewPresets))]
    public void TransparencyIsFlattenedBeforeProcessing(string effect)
    {
        using var source = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
        using var opaque = Solid(3, 3, Color.White);
        opaque.SetPixel(0, 0, Color.FromArgb(255, 127, 127));
        using var expected = ImageOperations.ApplyConvolution(opaque, effect);
        using var result = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(PixelFormat.Format24bppRgb, result.PixelFormat);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Assert.AreEqual(expected.GetPixel(x, y), result.GetPixel(x, y));
        Assert.AreEqual(128, source.GetPixel(0, 0).A);
        Assert.AreEqual(0, source.GetPixel(1, 1).A);
    }

    [DataTestMethod]
    [DynamicData(nameof(NewPresets))]
    public void TinyInputsAndCancellationLeaveSourceUsable(string effect)
    {
        using var narrow = new Bitmap(2, 3);
        using var shortImage = new Bitmap(3, 2);
        Assert.ThrowsException<ArgumentException>(() => ImageOperations.ApplyConvolution(narrow, effect));
        Assert.ThrowsException<ArgumentException>(() => ImageOperations.ApplyConvolution(shortImage, effect));
        using var source = Solid(3, 3, Color.CornflowerBlue);
        Assert.ThrowsException<OperationCanceledException>(() => ImageOperations.ApplyConvolution(source, effect, new CancellationToken(true)));
        Assert.AreEqual(Color.CornflowerBlue.ToArgb(), source.GetPixel(1, 1).ToArgb());
        using var result = ImageOperations.ApplyConvolution(source, effect);
        Assert.AreEqual(source.Size, result.Size);
    }

    private static Bitmap Solid(int width, int height, Color color)
    {
        var result = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(color);
        return result;
    }
}
