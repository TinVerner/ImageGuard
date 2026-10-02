using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageGuard.Models;

public sealed class ImagePixelData
{
    public ImagePixelData(int width, int height, double dpiX, double dpiY, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException("Размер BGRA32-буфера не соответствует размерам изображения.", nameof(pixels));
        }

        Width = width;
        Height = height;
        DpiX = dpiX > 0 ? dpiX : 96;
        DpiY = dpiY > 0 ? dpiY : 96;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public double DpiX { get; }
    public double DpiY { get; }
    public int Stride => Width * 4;
    public byte[] Pixels { get; }

    public ImagePixelData Clone() =>
        new(Width, Height, DpiX, DpiY, (byte[])Pixels.Clone());

    public BitmapSource ToBitmapSource()
    {
        var bitmap = BitmapSource.Create(
            Width,
            Height,
            DpiX,
            DpiY,
            PixelFormats.Bgra32,
            null,
            Pixels,
            Stride);
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record ImageInfo(
    string FileName,
    int Width,
    int Height,
    string Format,
    long FileSize);

public sealed record YCbCrPlanes(
    double[,] Y,
    double[,] Cb,
    double[,] Cr,
    byte[] Alpha,
    int Width,
    int Height);
