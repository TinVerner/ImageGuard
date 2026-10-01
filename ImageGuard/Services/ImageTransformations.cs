using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed record ImageTransformationOutput(
    ImagePixelData Image,
    ImageOutputFormat OutputFormat = ImageOutputFormat.Png,
    int JpegQuality = 95);

public interface IImageTransformation
{
    ImageTransformationType Type { get; }
    string DisplayName { get; }

    ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default);

    string Describe(ImageTransformationParameters parameters);
}

public static class ImageTransformations
{
    public static IReadOnlyList<IImageTransformation> CreateAll() =>
    [
        new JpegCompressionTransformation(),
        new BrightnessTransformation(),
        new ContrastTransformation(),
        new GaussianNoiseTransformation(),
        new ResizeTransformation(),
        new CropTransformation(),
        new RegionModificationTransformation()
    ];
}

public sealed class JpegCompressionTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.JpegCompression;
    public string DisplayName => "JPEG-сжатие";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var quality = (int)Math.Round(parameters.Value);
        if (quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Качество JPEG должно быть от 1 до 100.");
        }

        // Само преобразование выполняется штатным JpegBitmapEncoder при сохранении
        // результата ExperimentService. Повторная загрузка отражает реальные потери.
        return new(source.Clone(), ImageOutputFormat.Jpeg, quality);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"quality={(int)Math.Round(parameters.Value)}";
}

public sealed class BrightnessTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.Brightness;
    public string DisplayName => "Яркость";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Value is < -255 or > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Смещение яркости должно быть от -255 до 255.");
        }

        var result = source.Clone();
        for (var index = 0; index < result.Pixels.Length; index += 4)
        {
            if (index % (source.Stride * 16) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var component = 0; component < 3; component++)
            {
                result.Pixels[index + component] =
                    PixelMath.ClampToByte(result.Pixels[index + component] + parameters.Value);
            }
        }

        return new(result);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"смещение={parameters.Value:+0.##;-0.##;0}";
}

public sealed class ContrastTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.Contrast;
    public string DisplayName => "Контрастность";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Value is <= 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Множитель контрастности должен быть больше 0 и не превышать 4.");
        }

        var result = source.Clone();
        for (var index = 0; index < result.Pixels.Length; index += 4)
        {
            if (index % (source.Stride * 16) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var component = 0; component < 3; component++)
            {
                result.Pixels[index + component] = PixelMath.ClampToByte(
                    (result.Pixels[index + component] - 128) * parameters.Value + 128);
            }
        }

        return new(result);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"множитель={parameters.Value:0.##}";
}

public sealed class GaussianNoiseTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.GaussianNoise;
    public string DisplayName => "Гауссов шум";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Sigma шума должна быть от 0 до 100.");
        }

        var result = source.Clone();
        var random = new Random(parameters.Seed);
        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width + x) * 4;
                for (var component = 0; component < 3; component++)
                {
                    result.Pixels[index + component] = PixelMath.ClampToByte(
                        result.Pixels[index + component] +
                        NextGaussian(random) * parameters.Value);
                }
            }
        }

        return new(result);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"sigma={parameters.Value:0.##}; seed={parameters.Seed}";

    private static double NextGaussian(Random random)
    {
        // Box–Muller превращает две равномерные величины в N(0, 1).
        var u1 = Math.Max(random.NextDouble(), double.Epsilon);
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

public sealed class ResizeTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.Resize;
    public string DisplayName => "Изменение размера";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Value is < 10 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Масштаб должен быть от 10% до 200%.");
        }

        var scale = parameters.Value / 100.0;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = PixelMath.ResizeBilinear(source, width, height, cancellationToken);
        if (parameters.RestoreOriginalSize && (width != source.Width || height != source.Height))
        {
            resized = PixelMath.ResizeBilinear(
                resized,
                source.Width,
                source.Height,
                cancellationToken);
        }

        return new(resized);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"масштаб={parameters.Value:0.##}%; возврат к исходному размеру=" +
        (parameters.RestoreOriginalSize ? "да" : "нет");
}

public sealed class CropTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.Crop;
    public string DisplayName => "Обрезка";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Value is <= 0 or >= 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Value),
                "Процент обрезки должен быть больше 0 и меньше 90.");
        }

        var remainingFactor = 1 - parameters.Value / 100.0;
        var cropWidth = Math.Max(1, (int)Math.Round(source.Width * remainingFactor));
        var cropHeight = Math.Max(1, (int)Math.Round(source.Height * remainingFactor));
        var startX = (source.Width - cropWidth) / 2;
        var startY = (source.Height - cropHeight) / 2;
        var pixels = new byte[cropWidth * cropHeight * 4];

        for (var y = 0; y < cropHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(
                source.Pixels,
                ((startY + y) * source.Width + startX) * 4,
                pixels,
                y * cropWidth * 4,
                cropWidth * 4);
        }

        var cropped = new ImagePixelData(
            cropWidth,
            cropHeight,
            source.DpiX,
            source.DpiY,
            pixels);
        if (parameters.RestoreOriginalSize)
        {
            cropped = PixelMath.ResizeBilinear(
                cropped,
                source.Width,
                source.Height,
                cancellationToken);
        }

        return new(cropped);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"центральная обрезка={parameters.Value:0.##}%; возврат к исходному размеру=" +
        (parameters.RestoreOriginalSize ? "да" : "нет");
}

public sealed class RegionModificationTransformation : IImageTransformation
{
    public ImageTransformationType Type => ImageTransformationType.RegionModification;
    public string DisplayName => "Изменение области";

    public ImageTransformationOutput Apply(
        ImagePixelData source,
        ImageTransformationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.Width <= 0 || parameters.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.Width),
                "Ширина и высота области должны быть положительными.");
        }

        if (parameters.X < 0 || parameters.Y < 0 ||
            parameters.X >= source.Width || parameters.Y >= source.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters.X),
                "Начальная точка области находится за пределами изображения.");
        }

        var endX = Math.Min(source.Width, parameters.X + parameters.Width);
        var endY = Math.Min(source.Height, parameters.Y + parameters.Height);
        var result = source.Clone();
        for (var y = parameters.Y; y < endY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = parameters.X; x < endX; x++)
            {
                var index = (y * source.Width + x) * 4;
                result.Pixels[index] = parameters.RegionBlue;
                result.Pixels[index + 1] = parameters.RegionGreen;
                result.Pixels[index + 2] = parameters.RegionRed;
                // Alpha исходного пикселя сохраняется.
            }
        }

        return new(result);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"x={parameters.X}; y={parameters.Y}; {parameters.Width}×{parameters.Height}; " +
        $"RGB=({parameters.RegionRed},{parameters.RegionGreen},{parameters.RegionBlue})";
}

internal static class PixelMath
{
    public static byte ClampToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    public static ImagePixelData ResizeBilinear(
        ImagePixelData source,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var target = new byte[targetWidth * targetHeight * 4];
        var xScale = (double)source.Width / targetWidth;
        var yScale = (double)source.Height / targetHeight;

        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = (targetY + 0.5) * yScale - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Height - 1);
            var y1 = Math.Min(y0 + 1, source.Height - 1);
            var yFraction = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);

            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var sourceX = (targetX + 0.5) * xScale - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Width - 1);
                var x1 = Math.Min(x0 + 1, source.Width - 1);
                var xFraction = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
                var targetIndex = (targetY * targetWidth + targetX) * 4;

                for (var component = 0; component < 4; component++)
                {
                    var topLeft = source.Pixels[(y0 * source.Width + x0) * 4 + component];
                    var topRight = source.Pixels[(y0 * source.Width + x1) * 4 + component];
                    var bottomLeft = source.Pixels[(y1 * source.Width + x0) * 4 + component];
                    var bottomRight = source.Pixels[(y1 * source.Width + x1) * 4 + component];
                    var top = topLeft + (topRight - topLeft) * xFraction;
                    var bottom = bottomLeft + (bottomRight - bottomLeft) * xFraction;
                    target[targetIndex + component] =
                        ClampToByte(top + (bottom - top) * yFraction);
                }
            }
        }

        return new(targetWidth, targetHeight, source.DpiX, source.DpiY, target);
    }
}
