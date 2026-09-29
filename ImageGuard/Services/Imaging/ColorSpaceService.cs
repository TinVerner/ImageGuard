using ImageGuard.Models;

namespace ImageGuard.Services.Imaging;

public interface IColorSpaceService
{
    YCbCrPlanes ToYCbCr(ImagePixelData image);
    ImagePixelData FromYCbCr(YCbCrPlanes planes, double dpiX, double dpiY);
}

public sealed class ColorSpaceService : IColorSpaceService
{
    public YCbCrPlanes ToYCbCr(ImagePixelData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var yPlane = new double[image.Height, image.Width];
        var cbPlane = new double[image.Height, image.Width];
        var crPlane = new double[image.Height, image.Width];
        var alpha = new byte[image.Width * image.Height];

        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                var pixelIndex = row * image.Stride + column * 4;
                var planeIndex = row * image.Width + column;
                var b = image.Pixels[pixelIndex];
                var g = image.Pixels[pixelIndex + 1];
                var r = image.Pixels[pixelIndex + 2];

                // BT.601: watermarking uses Y; Cb and Cr preserve color.
                yPlane[row, column] = 0.299 * r + 0.587 * g + 0.114 * b;
                cbPlane[row, column] =
                    -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
                crPlane[row, column] =
                    0.5 * r - 0.418688 * g - 0.081312 * b + 128;
                alpha[planeIndex] = image.Pixels[pixelIndex + 3];
            }
        }

        return new(yPlane, cbPlane, crPlane, alpha, image.Width, image.Height);
    }

    public ImagePixelData FromYCbCr(YCbCrPlanes planes, double dpiX, double dpiY)
    {
        ArgumentNullException.ThrowIfNull(planes);
        var pixels = new byte[planes.Width * planes.Height * 4];

        for (var row = 0; row < planes.Height; row++)
        {
            for (var column = 0; column < planes.Width; column++)
            {
                var pixelIndex = (row * planes.Width + column) * 4;
                var y = planes.Y[row, column];
                var cbOffset = planes.Cb[row, column] - 128;
                var crOffset = planes.Cr[row, column] - 128;

                var r = y + 1.402 * crOffset;
                var g = y - 0.34414 * cbOffset - 0.71414 * crOffset;
                var b = y + 1.772 * cbOffset;

                pixels[pixelIndex] = ClampToByte(b);
                pixels[pixelIndex + 1] = ClampToByte(g);
                pixels[pixelIndex + 2] = ClampToByte(r);
                pixels[pixelIndex + 3] = planes.Alpha[row * planes.Width + column];
            }
        }

        return new(planes.Width, planes.Height, dpiX, dpiY, pixels);
    }

    private static byte ClampToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
