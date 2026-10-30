using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ImageProcessingApp;

/// <summary>Image operations return owned bitmaps and never modify their inputs.</summary>
internal static class ImageOperations
{
    private const int KeyBrightness = 85;
    private const int KeyThreshold = 5;

    public static Bitmap LoadDetached(string path)
    {
        using var loaded = new Bitmap(path);
        return CopyArgb(loaded);
    }

    public static void Save(Bitmap image, string path, int filterIndex)
    {
        ArgumentNullException.ThrowIfNull(image);
        switch (filterIndex)
        {
            case 1:
                image.Save(path, ImageFormat.Png);
                break;
            case 2:
                using (var opaque = CopyRgb(image)) opaque.Save(path, ImageFormat.Jpeg);
                break;
            case 3:
                image.Save(path, ImageFormat.Bmp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filterIndex), "Choose PNG, JPEG, or Bitmap.");
        }
    }

    public static unsafe Bitmap ApplyBasic(Bitmap source, string effect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (effect is not ("Copy Original" or "Grayscale" or "Invert" or "Sepia" or "Subtract"))
            throw new ArgumentException("Unknown image effect.", nameof(effect));
        cancellationToken.ThrowIfCancellationRequested();
        var result = CopyArgb(source);
        BitmapData? data = null;
        try
        {
            if (effect == "Copy Original") return result;
            data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            for (int y = 0; y < result.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < result.Width; x++)
                {
                    byte* pixel = row + x * 4;
                    int blue = pixel[0], green = pixel[1], red = pixel[2];
                    switch (effect)
                    {
                        case "Grayscale":
                            pixel[0] = pixel[1] = pixel[2] = (byte)((red + green + blue) / 3);
                            pixel[3] = 255;
                            break;
                        case "Invert":
                            pixel[0] = (byte)(255 - blue);
                            pixel[1] = (byte)(255 - green);
                            pixel[2] = (byte)(255 - red);
                            pixel[3] = 255;
                            break;
                        case "Sepia":
                            pixel[2] = (byte)Math.Min(255, (int)(.393 * red + .769 * green + .189 * blue));
                            pixel[1] = (byte)Math.Min(255, (int)(.349 * red + .686 * green + .168 * blue));
                            pixel[0] = (byte)Math.Min(255, (int)(.272 * red + .534 * green + .131 * blue));
                            pixel[3] = 255;
                            break;
                        case "Subtract":
                            if (Math.Abs((red + green + blue) / 3 - KeyBrightness) <= KeyThreshold)
                            {
                                pixel[0] = pixel[1] = pixel[2] = 0;
                                pixel[3] = 255;
                            }
                            break;
                    }
                }
            }
            return result;
        }
        catch
        {
            if (data is not null)
            {
                result.UnlockBits(data);
                data = null;
            }
            result.Dispose();
            throw;
        }
        finally
        {
            if (data is not null) result.UnlockBits(data);
        }
    }

    public static Bitmap ApplyConvolution(Bitmap source, string effect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width < 3 || source.Height < 3)
            throw new ArgumentException("Convolution needs an image at least 3 × 3 pixels.", nameof(source));
        cancellationToken.ThrowIfCancellationRequested();
        var result = CopyRgb(source);
        try
        {
            bool applied = effect switch
            {
                "Smooth" => BitmapFilter.Smooth(result, cancellationToken: cancellationToken),
                "Gaussian Blur" => BitmapFilter.GaussianBlur(result, cancellationToken),
                "Sharpen" => BitmapFilter.Sharpen(result, cancellationToken),
                "Mean Removal" => BitmapFilter.MeanRemoval(result, cancellationToken),
                "Laplacian" => BitmapFilter.Emboss(result, cancellationToken),
                "Horizontal + Vertical" => BitmapFilter.EmbossHorizontalVertical(result, cancellationToken),
                "All Directions" => BitmapFilter.EmbossAllDirections(result, cancellationToken),
                "Lossy" => BitmapFilter.EmbossLossy(result, cancellationToken),
                "Horizontal Only" => BitmapFilter.EmbossHorizontalOnly(result, cancellationToken),
                "Vertical Only" => BitmapFilter.EmbossVerticalOnly(result, cancellationToken),
                "Motion Blur Horizontal" => BitmapFilter.Conv3x3(result, new ConvMatrix
                {
                    MidLeft = 1, Pixel = 1, MidRight = 1, Factor = 3
                }, cancellationToken),
                "Motion Blur Vertical" => BitmapFilter.Conv3x3(result, new ConvMatrix
                {
                    TopMid = 1, Pixel = 1, BottomMid = 1, Factor = 3
                }, cancellationToken),
                "Motion Blur Diagonal" => BitmapFilter.Conv3x3(result, new ConvMatrix
                {
                    TopLeft = 1, Pixel = 1, BottomRight = 1, Factor = 3
                }, cancellationToken),
                "Emboss NW–SE" => BitmapFilter.Conv3x3(result, new ConvMatrix
                {
                    TopLeft = -2, TopMid = -1, TopRight = 0,
                    MidLeft = -1, Pixel = 0, MidRight = 1,
                    BottomLeft = 0, BottomMid = 1, BottomRight = 2,
                    Offset = 128
                }, cancellationToken),
                "Emboss NE–SW" => BitmapFilter.Conv3x3(result, new ConvMatrix
                {
                    TopLeft = 0, TopMid = -1, TopRight = -2,
                    MidLeft = 1, Pixel = 0, MidRight = -1,
                    BottomLeft = 2, BottomMid = 1, BottomRight = 0,
                    Offset = 128
                }, cancellationToken),
                "Sobel Edges" => ApplyGradientEdges(result, 1, 2, cancellationToken),
                "Prewitt Edges" => ApplyGradientEdges(result, 1, 1, cancellationToken),
                "Scharr Edges" => ApplyGradientEdges(result, 3, 10, cancellationToken),
                _ => throw new ArgumentException("Unknown convolution effect.", nameof(effect))
            };
            if (!applied) throw new InvalidOperationException("The convolution kernel could not be applied.");
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static unsafe bool ApplyGradientEdges(Bitmap result, int a, int b, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = (Bitmap)result.Clone();
        BitmapData? sourceData = null, resultData = null;
        try
        {
            var bounds = new Rectangle(0, 0, result.Width, result.Height);
            sourceData = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            resultData = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            // Leave the one-pixel border in the flattened RGB copy unchanged.
            for (int y = 1; y < result.Height - 1; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte* top = (byte*)sourceData.Scan0 + (y - 1) * sourceData.Stride;
                byte* middle = top + sourceData.Stride;
                byte* bottom = middle + sourceData.Stride;
                byte* destination = (byte*)resultData.Scan0 + y * resultData.Stride;
                for (int x = 1; x < result.Width - 1; x++)
                {
                    int position = x * 3;
                    int tl = Intensity(top + position - 3), tm = Intensity(top + position), tr = Intensity(top + position + 3);
                    int ml = Intensity(middle + position - 3), mr = Intensity(middle + position + 3);
                    int bl = Intensity(bottom + position - 3), bm = Intensity(bottom + position), br = Intensity(bottom + position + 3);
                    // Signed gradients from [-a,0,a; -b,0,b; -a,0,a] and its transpose.
                    int gx = a * (tr - tl + br - bl) + b * (mr - ml);
                    int gy = a * (bl - tl + br - tr) + b * (bm - tm);
                    int magnitude = (int)(Math.Sqrt((double)gx * gx + (double)gy * gy) / (2 * a + b));
                    byte value = (byte)Math.Clamp(magnitude, 0, 255);
                    destination[position] = destination[position + 1] = destination[position + 2] = value;
                }
            }
            return true;
        }
        finally
        {
            if (resultData is not null) result.UnlockBits(resultData);
            if (sourceData is not null) source.UnlockBits(sourceData);
        }
    }

    private static unsafe int Intensity(byte* pixel) => (pixel[0] + pixel[1] + pixel[2]) / 3;

    public static unsafe Bitmap Subtract(Bitmap foreground, Bitmap background, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(background);
        cancellationToken.ThrowIfCancellationRequested();
        int width = Math.Min(foreground.Width, background.Width);
        int height = Math.Min(foreground.Height, background.Height);
        using var front = CopyArgb(foreground);
        using var back = CopyArgb(background);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData? frontData = null, backData = null, resultData = null;
        bool succeeded = false;
        try
        {
            var bounds = new Rectangle(0, 0, width, height);
            frontData = front.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            backData = back.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            resultData = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte* frontRow = (byte*)frontData.Scan0 + y * frontData.Stride;
                byte* backRow = (byte*)backData.Scan0 + y * backData.Stride;
                byte* resultRow = (byte*)resultData.Scan0 + y * resultData.Stride;
                for (int x = 0; x < width; x++)
                {
                    byte* pixel = frontRow + x * 4;
                    int brightness = (pixel[0] + pixel[1] + pixel[2]) / 3;
                    byte* selected = Math.Abs(brightness - KeyBrightness) <= KeyThreshold ? backRow + x * 4 : pixel;
                    *(uint*)(resultRow + x * 4) = *(uint*)selected;
                }
            }
            succeeded = true;
            return result;
        }
        finally
        {
            if (resultData is not null) result.UnlockBits(resultData);
            if (backData is not null) back.UnlockBits(backData);
            if (frontData is not null) front.UnlockBits(frontData);
            if (!succeeded) result.Dispose();
        }
    }

    public static unsafe int[] Histogram(Bitmap source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        using var copy = CopyArgb(source);
        var bins = new int[256];
        var data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < copy.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < copy.Width; x++)
                {
                    byte* pixel = row + x * 4;
                    bins[(pixel[0] + pixel[1] + pixel[2]) / 3]++;
                }
            }
            return bins;
        }
        finally
        {
            copy.UnlockBits(data);
        }
    }

    private static Bitmap CopyArgb(Bitmap source) => Copy(source, PixelFormat.Format32bppArgb, false);
    private static Bitmap CopyRgb(Bitmap source) => Copy(source, PixelFormat.Format24bppRgb, true);

    private static Bitmap Copy(Bitmap source, PixelFormat format, bool flatten)
    {
        var result = new Bitmap(source.Width, source.Height, format);
        try
        {
            using var graphics = Graphics.FromImage(result);
            if (flatten)
                graphics.Clear(Color.White);
            else
                graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
