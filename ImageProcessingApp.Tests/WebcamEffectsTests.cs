using System.Drawing.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public sealed class WebcamEffectsTests
{
    public static IEnumerable<object[]> Presets => WebcamEffects.All.Select(effect => new object[] { effect.Name });

    [TestMethod]
    public void CatalogContainsAllFifteenChoicesWithUniqueNamesAndDescriptions()
    {
        string[] expected =
        [
            "None", "Grayscale", "Invert", "Sepia", "Subtract", "Gaussian Blur", "Sharpen",
            "Motion Blur Horizontal", "Motion Blur Vertical", "Motion Blur Diagonal",
            "Sobel Edges", "Prewitt Edges", "Scharr Edges", "Emboss NW–SE", "Emboss NE–SW"
        ];
        CollectionAssert.AreEqual(expected, WebcamEffects.All.Select(effect => effect.Name).ToArray());
        Assert.AreEqual(expected.Length, WebcamEffects.All.Select(effect => effect.Name).Distinct().Count());
        Assert.IsTrue(WebcamEffects.All.All(effect => !string.IsNullOrWhiteSpace(effect.Description)));
    }

    [DataTestMethod]
    [DynamicData(nameof(Presets))]
    public void EachLiveEffectMatchesItsStillImageOperationWithoutChangingTheSource(string effect)
    {
        using var source = Frame();
        using var sourceBefore = (Bitmap)source.Clone();
        using var expected = effect is "None" or "Grayscale" or "Invert" or "Sepia" or "Subtract"
            ? ImageOperations.ApplyBasic(source, effect == "None" ? "Copy Original" : effect)
            : ImageOperations.ApplyConvolution(source, effect);
        using var result = WebcamEffects.Apply(source, effect);
        using var repeated = WebcamEffects.Apply(source, effect);
        Assert.AreNotSame(source, result);
        Assert.AreNotSame(result, repeated);
        Assert.AreEqual(source.Size, result.Size);
        Assert.AreEqual(expected.PixelFormat, result.PixelFormat);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Assert.AreEqual(sourceBefore.GetPixel(x, y), source.GetPixel(x, y));
                Assert.AreEqual(expected.GetPixel(x, y), result.GetPixel(x, y));
                Assert.AreEqual(result.GetPixel(x, y), repeated.GetPixel(x, y));
            }

        result.SetPixel(0, 0, Color.Magenta);
        Assert.AreEqual(sourceBefore.GetPixel(0, 0), source.GetPixel(0, 0));
        Assert.AreEqual(expected.GetPixel(0, 0), repeated.GetPixel(0, 0));
    }

    [DataTestMethod]
    [DynamicData(nameof(Presets))]
    public void CancellationKeepsTheCameraFrameUsable(string effect)
    {
        using var source = Frame();
        var original = source.GetPixel(3, 3);
        Assert.ThrowsException<OperationCanceledException>(() => WebcamEffects.Apply(source, effect, new CancellationToken(true)));
        Assert.AreEqual(original, source.GetPixel(3, 3));
        using var result = WebcamEffects.Apply(source, effect);
        Assert.AreEqual(source.Size, result.Size);
    }

    [TestMethod]
    public void UnsupportedEffectsAndNullFramesAreRejected()
    {
        using var source = Frame();
        var original = source.GetPixel(3, 3);
        Assert.ThrowsException<ArgumentException>(() => WebcamEffects.Apply(source, "Not a webcam effect"));
        Assert.ThrowsException<ArgumentException>(() => WebcamEffects.Apply(source, "Copy Original"));
        Assert.ThrowsException<ArgumentNullException>(() => WebcamEffects.Apply(null!, "None"));
        Assert.ThrowsException<ArgumentNullException>(() => WebcamEffects.Apply(source, null!));
        Assert.AreEqual(original, source.GetPixel(3, 3));
    }

    private static Bitmap Frame()
    {
        var image = new Bitmap(7, 6, PixelFormat.Format24bppRgb);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, Color.FromArgb((x * 37 + y * 19) % 256, (x * 11 + y * 53) % 256, (x * 71 + y * 7) % 256));
        return image;
    }
}
