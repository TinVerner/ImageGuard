using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed class ImageQualityMetricsService
{
    public ImageQualityMetrics Calculate(ImagePixelData original, ImagePixelData modified)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modified);
        if (original.Width != modified.Width || original.Height != modified.Height)
        {
            throw new ArgumentException("Для расчета PSNR изображения должны иметь одинаковые размеры.");
        }

        double squaredError = 0;
        var componentCount = original.Width * original.Height * 3L;
        // Методика использует только RGB-компоненты; alpha не входит в MSE/PSNR.
        for (var index = 0; index < original.Pixels.Length; index += 4)
        {
            for (var component = 0; component < 3; component++)
            {
                var difference = original.Pixels[index + component] - modified.Pixels[index + component];
                squaredError += difference * difference;
            }
        }

        var mse = squaredError / componentCount;
        var psnr = mse == 0
            ? double.PositiveInfinity
            : 10 * Math.Log10(255 * 255 / mse);
        return new(mse, psnr);
    }
}
