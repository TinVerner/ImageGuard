using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
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
