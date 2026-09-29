using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
            throw new ArgumentOutOfRangeException(nameof(parameters.Value),
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
