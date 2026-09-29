using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services.Transformations;

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
