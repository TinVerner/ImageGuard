using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
                "Качество JPEG должно быть от 1 до 100.");
        }

        // Само преобразование выполняется штатным JpegBitmapEncoder при сохранении
        // результата ExperimentService. Повторная загрузка отражает реальные потери.
        return new(source.Clone(), ImageOutputFormat.Jpeg, quality);
    }

    public string Describe(ImageTransformationParameters parameters) =>
        $"quality={(int)Math.Round(parameters.Value)}";
}
