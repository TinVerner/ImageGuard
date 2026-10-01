using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ImageQualityMetricsTests
{
    [Fact]
    public void Metrics_KnownRgbVector_ProducesExpectedMseAndPsnr()
    {
        var services = new TestServices();
        var original = new ImagePixelData(1, 1, 96, 96, [0, 0, 0, 10]);
        var modified = new ImagePixelData(1, 1, 96, 96, [3, 4, 0, 240]);

        var result = services.Metrics.Calculate(original, modified);

        var expectedMse = 25.0 / 3;
        var expectedPsnr = 10 * Math.Log10(255 * 255 / expectedMse);
        Assert.Equal(expectedMse, result.Mse, precision: 10);
        Assert.Equal(expectedPsnr, result.Psnr, precision: 10);
    }

    [Fact]
    public void Metrics_IdenticalRgbWithDifferentAlpha_ProducesInfinity()
    {
        var services = new TestServices();
        var original = new ImagePixelData(1, 1, 96, 96, [10, 20, 30, 0]);
        var modified = new ImagePixelData(1, 1, 96, 96, [10, 20, 30, 255]);

        var result = services.Metrics.Calculate(original, modified);

        Assert.Equal(0, result.Mse);
        Assert.Equal(double.PositiveInfinity, result.Psnr);
    }
}
