using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public sealed class ImageDocumentTests
{
    [TestMethod]
    public void WorkspacesOwnIndependentImagesAndClearOnlyTheirOwnState()
    {
        using var basic = new ImageDocument();
        using var convolution = new ImageDocument();
        var source = Solid(Color.Red);
        var result = Solid(Color.Black);
        basic.SetSource(source, "basic.png");
        basic.SetResult(result);
        convolution.SetSource(Solid(Color.Blue), "convolution.png");
        basic.Clear();

        Assert.IsNull(basic.Source);
        Assert.IsNull(basic.Result);
        Assert.IsNull(basic.FileName);
        Assert.IsNull(basic.HistogramSource);
        AssertDisposed(source);
        AssertDisposed(result);
        Assert.AreEqual(Color.Blue.ToArgb(), convolution.Source!.GetPixel(0, 0).ToArgb());
        Assert.AreEqual("convolution.png", convolution.FileName);
    }

    [TestMethod]
    public void ReplacementInvalidatesAndDisposesPreviousResult()
    {
        using var document = new ImageDocument();
        var oldSource = Solid(Color.Red);
        var oldResult = Solid(Color.Black);
        document.SetSource(oldSource, "old.png");
        document.SetResult(oldResult);
        var replacement = Solid(Color.Green);
        document.SetSource(replacement, "new.png");

        Assert.AreSame(replacement, document.Source);
        Assert.IsNull(document.Result);
        Assert.AreSame(replacement, document.HistogramSource);
        Assert.AreEqual("new.png", document.FileName);
        AssertDisposed(oldSource);
        AssertDisposed(oldResult);
    }

    [TestMethod]
    public void HistogramUsesResultWhenAvailableAndSourceOtherwise()
    {
        using var document = new ImageDocument();
        document.SetSource(Solid(Color.Red), "red.png");
        Assert.AreEqual(1, ImageOperations.Histogram(document.HistogramSource!)[85]);
        document.SetResult(ImageOperations.ApplyBasic(document.Source!, "Invert"));
        Assert.AreSame(document.Result, document.HistogramSource);
        Assert.AreEqual(1, ImageOperations.Histogram(document.HistogramSource!)[170]);
    }

    [TestMethod]
    public void EachEffectStartsFromOriginalAndResultsReplaceSafely()
    {
        using var document = new ImageDocument();
        document.SetSource(Solid(Color.FromArgb(30, 90, 150)), "source.png");
        var grayscale = ImageOperations.ApplyBasic(document.Source!, "Grayscale");
        document.SetResult(grayscale);
        document.SetResult(ImageOperations.ApplyBasic(document.Source!, "Invert"));

        AssertDisposed(grayscale);
        Assert.AreEqual(Color.FromArgb(225, 165, 105).ToArgb(), document.Result!.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(Color.FromArgb(30, 90, 150).ToArgb(), document.Source!.GetPixel(0, 0).ToArgb());
    }

    [TestMethod]
    public void DisposeIsIdempotentAndPreventsNewOwnership()
    {
        var document = new ImageDocument();
        var source = Solid(Color.Red);
        document.SetSource(source, "source.png");
        document.Dispose();
        document.Dispose();
        AssertDisposed(source);
        using var next = Solid(Color.Blue);
        Assert.ThrowsException<ObjectDisposedException>(() => document.SetSource(next, "next.png"));
        Assert.ThrowsException<ObjectDisposedException>(() => document.SetResult(next));
    }

    [TestMethod]
    public void SourceCannotBeAssignedAsResult()
    {
        using var document = new ImageDocument();
        document.SetSource(Solid(Color.Red), null);
        Assert.ThrowsException<ArgumentException>(() => document.SetResult(document.Source!));
        Assert.AreEqual(Color.Red.ToArgb(), document.Source!.GetPixel(0, 0).ToArgb());
    }

    private static Bitmap Solid(Color color)
    {
        var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, color);
        return bitmap;
    }

    private static void AssertDisposed(Bitmap bitmap) =>
        Assert.ThrowsException<ArgumentException>(() => bitmap.GetPixel(0, 0));
}
