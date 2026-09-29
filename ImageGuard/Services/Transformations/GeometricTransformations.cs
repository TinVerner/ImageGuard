using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
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
