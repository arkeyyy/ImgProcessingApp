using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageProcessingApp
{
    public static class BitmapFilter
    {

        public static unsafe bool Conv3x3(Bitmap b, ConvMatrix m, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(b);
            ArgumentNullException.ThrowIfNull(m);
            if (m.Factor == 0) return false;
            if (b.Width < 3 || b.Height < 3)
                throw new ArgumentException("Convolution needs an image at least 3 × 3 pixels.", nameof(b));
            if (b.PixelFormat != PixelFormat.Format24bppRgb)
                throw new ArgumentException("Convolution requires a 24-bit RGB working copy.", nameof(b));
            cancellationToken.ThrowIfCancellationRequested();

            using var source = (Bitmap)b.Clone();
            BitmapData? destinationData = null;
            BitmapData? sourceData = null;
            try
            {
                var bounds = new Rectangle(0, 0, b.Width, b.Height);
                destinationData = b.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                sourceData = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                // Address each row using its own stride. The one-pixel border stays unchanged.
                for (int y = 1; y < b.Height - 1; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte* top = (byte*)sourceData.Scan0 + (y - 1) * sourceData.Stride;
                    byte* middle = top + sourceData.Stride;
                    byte* bottom = middle + sourceData.Stride;
                    byte* destination = (byte*)destinationData.Scan0 + y * destinationData.Stride;
                    for (int x = 1; x < b.Width - 1; x++)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            int position = x * 3 + channel;
                            int value = (top[position - 3] * m.TopLeft +
                                top[position] * m.TopMid + top[position + 3] * m.TopRight +
                                middle[position - 3] * m.MidLeft + middle[position] * m.Pixel +
                                middle[position + 3] * m.MidRight + bottom[position - 3] * m.BottomLeft +
                                bottom[position] * m.BottomMid + bottom[position + 3] * m.BottomRight)
                                / m.Factor + m.Offset;
                            destination[position] = (byte)Math.Clamp(value, 0, 255);
                        }
                    }
                }
                return true;
            }
            finally
            {
                if (sourceData is not null) source.UnlockBits(sourceData);
                if (destinationData is not null) b.UnlockBits(destinationData);
            }
        }


        public static bool Smooth(Bitmap b, int weight = 1, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.SetAll(1);
            m.Pixel = weight;
            m.Factor = weight + 8;
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool GaussianBlur(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();

            m.TopLeft = 1; m.TopMid = 2; m.TopRight = 1;
            m.MidLeft = 2; m.Pixel = 4; m.MidRight = 2;
            m.BottomLeft = 1; m.BottomMid = 2; m.BottomRight = 1;

            m.Factor = 16;
            m.Offset = 0;

            return Conv3x3(b, m, cancellationToken);
        }

        public static bool Sharpen(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.SetAll(0);
            m.Pixel = 11;
            m.TopMid = m.MidLeft = m.MidRight = m.BottomMid = -2;
            m.Factor = 3;
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool MeanRemoval(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();

            m.TopLeft = -1; m.TopMid = -1; m.TopRight = -1;
            m.MidLeft = -1; m.Pixel = 9; m.MidRight = -1;
            m.BottomLeft = -1; m.BottomMid = -1; m.BottomRight = -1;
            m.Factor = 1;
            m.Offset = 0;
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool Emboss(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();

            m.TopLeft = -1; m.TopMid = 0; m.TopRight = -1;
            m.MidLeft = 0; m.Pixel = 4; m.MidRight = 0;
            m.BottomLeft = -1; m.BottomMid = 0; m.BottomRight = -1;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool EmbossHorizontalVertical(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.TopLeft = 0; m.TopMid = -1; m.TopRight = 0;
            m.MidLeft = -1; m.Pixel = 4; m.MidRight = -1;
            m.BottomLeft = 0; m.BottomMid = -1; m.BottomRight = 0;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool EmbossAllDirections(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.TopLeft = -1; m.TopMid = -1; m.TopRight = 1;
            m.MidLeft = -1; m.Pixel = 8; m.MidRight = -1;
            m.BottomLeft = 1; m.BottomMid = -1; m.BottomRight = 1;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool EmbossLossy(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.TopLeft = 1; m.TopMid = -2; m.TopRight = 1;
            m.MidLeft = -2; m.Pixel = 4; m.MidRight = -2;
            m.BottomLeft = -1; m.BottomMid = 1; m.BottomRight = -2;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }

        public static bool EmbossHorizontalOnly(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.TopLeft = 0; m.TopMid = 0; m.TopRight = 0;
            m.MidLeft = -1; m.Pixel = 2; m.MidRight = -1;
            m.BottomLeft = 0; m.BottomMid = 0; m.BottomRight = 0;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }


        public static bool EmbossVerticalOnly(Bitmap b, CancellationToken cancellationToken = default)
        {
            ConvMatrix m = new ConvMatrix();
            m.TopLeft = 0; m.TopMid = -1; m.TopRight = 0;
            m.MidLeft = 0; m.Pixel = 0; m.MidRight = 0;
            m.BottomLeft = 0; m.BottomMid = 1; m.BottomRight = 0;
            m.Factor = 1;
            m.Offset = 127;  // Brighten to avoid clamping to black :)
            return Conv3x3(b, m, cancellationToken);
        }
    }
}
