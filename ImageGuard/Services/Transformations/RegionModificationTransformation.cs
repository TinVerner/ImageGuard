using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
            throw new ArgumentOutOfRangeException(nameof(parameters.Width),
                "Ширина и высота области должны быть положительными.");
        }

        if (parameters.X < 0 || parameters.Y < 0 ||
            parameters.X >= source.Width || parameters.Y >= source.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.X),
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
