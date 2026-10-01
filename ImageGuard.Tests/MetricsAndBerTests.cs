using ImageGuard.Helpers;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class MetricsAndBerTests
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

    [Fact]
    public void RawDctBits_EqualEmbeddedPayload_ProducesZeroBer()
    {
        var services = new TestServices();
        var settings = TestServices.ReliableSettings();
        var image = TestServices.CreateTexturedImage(256, 256);
        var expected = services.WatermarkCodec.EncodeBits("RAW-BER");
        var embedded = services.Watermarks.Embed(image, "RAW-BER", settings);

        var actual = services.Watermarks.ExtractRawBits(
            embedded.Image,
            settings,
            expected.Length);

        Assert.Equal(expected, actual);
        Assert.Equal(0, BitErrorRateCalculator.Calculate(expected, actual));
    }

    [Fact]
    public void ControlledRawBitError_ProducesNonZeroBer()
    {
        var services = new TestServices();
        var expected = services.WatermarkCodec.EncodeBits("BIT-BER");
        var damaged = (bool[])expected.Clone();
        damaged[^1] = !damaged[^1];

        var ber = BitErrorRateCalculator.Calculate(expected, damaged);

        Assert.Equal(1.0 / expected.Length, ber);
    }

    [Fact]
    public void Ber_WhenBitSequencesCannotBeCompared_IsNotAvailable()
    {
        Assert.Null(BitErrorRateCalculator.Calculate(null, [true]));
        Assert.Null(BitErrorRateCalculator.Calculate([], []));
        Assert.Null(BitErrorRateCalculator.Calculate([true], [true, false]));
    }
}
