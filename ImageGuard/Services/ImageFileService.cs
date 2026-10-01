using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services;

public interface IImageFileService
{
    ImagePixelData Load(string path);
    void Save(ImagePixelData image, string path, ImageOutputFormat format, int jpegQuality = 95);
    ImageInfo GetImageInfo(string path);
}

public sealed class ImageFileService : IImageFileService
{
    public ImagePixelData Load(string path)
    {
        ValidateExistingPath(path);

        BitmapFrame frame;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
            frame.Freeze();
        }

        BitmapSource source = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return new(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, pixels);
    }

    public void Save(ImagePixelData image, string path, ImageOutputFormat format, int jpegQuality = 95)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Не указан путь сохранения изображения.", nameof(path));
        }

        ValidateOutputExtension(path, format);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        BitmapEncoder encoder;
        BitmapSource source = image.ToBitmapSource();
        if (format == ImageOutputFormat.Jpeg)
        {
            if (jpegQuality is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(jpegQuality), "Качество JPEG должно быть от 1 до 100.");
            }

            source = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
        }
        else
        {
            encoder = new PngBitmapEncoder();
        }

        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    public ImageInfo GetImageInfo(string path)
    {
        var image = Load(path);
        var file = new FileInfo(path);
        var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return new(file.Name, image.Width, image.Height, format, file.Length);
    }

    private static void ValidateExistingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Файл изображения не найден.", path);
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Поддерживаются изображения PNG, JPG и JPEG.");
        }
    }

    private static void ValidateOutputExtension(string path, ImageOutputFormat format)
    {
        var extension = Path.GetExtension(path);
        var valid = format switch
        {
            ImageOutputFormat.Png =>
                extension.Equals(".png", StringComparison.OrdinalIgnoreCase),
            ImageOutputFormat.Jpeg =>
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException(
                format == ImageOutputFormat.Png
                    ? "Для формата PNG требуется расширение .png."
                    : "Для формата JPEG требуется расширение .jpg или .jpeg.",
                nameof(path));
        }
    }
}
