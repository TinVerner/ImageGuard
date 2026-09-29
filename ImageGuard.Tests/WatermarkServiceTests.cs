using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class WatermarkServiceTests
{
    [Fact]
    public void EmbedSaveReloadExtract_RestoresWatermark()
    {
        var services = new TestServices();
        var original = TestServices.CreateTexturedImage();
        var settings = new WatermarkSettings();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

        try
        {
            var embedded = services.Watermarks.Embed(original, "TEST-123", settings);
            services.Images.Save(embedded.Image, path, ImageOutputFormat.Png);
            var reloaded = services.Images.Load(path);

            var extracted = services.Watermarks.Extract(reloaded, settings);

            Assert.Equal(WatermarkStatus.Valid, extracted.Status);
            Assert.Equal("TEST-123", extracted.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Embed_WhenCapacityIsInsufficient_ThrowsMeaningfulError()
    {
        var services = new TestServices();
        var smallImage = TestServices.CreateTexturedImage(32, 32);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.Watermarks.Embed(
                smallImage,
                "A watermark that cannot fit",
                TestServices.ReliableSettings()));

        Assert.Contains("недостаточно", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("требуется", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void EmbedExtract_WorksWithConfiguredDelta(double delta)
    {
        var services = new TestServices();
        var settings = new WatermarkSettings { Delta = delta };

        var embedded = services.Watermarks.Embed(
            TestServices.CreateTexturedImage(),
            "DELTA",
            settings);
        var extracted = services.Watermarks.Extract(embedded.Image, settings);

        Assert.Equal(WatermarkStatus.Valid, extracted.Status);
        Assert.Equal("DELTA", extracted.Text);
    }

    [Fact]
    public void EmbedExtract_RestoresUtf8CyrillicText()
    {
        var services = new TestServices();
        var settings = TestServices.ReliableSettings();

        var embedded = services.Watermarks.Embed(
            TestServices.CreateTexturedImage(),
            "Привет мир",
            settings);
        var extracted = services.Watermarks.Extract(embedded.Image, settings);

        Assert.Equal(WatermarkStatus.Valid, extracted.Status);
        Assert.Equal("Привет мир", extracted.Text);
    }

    [Fact]
    public void Extract_ImageWithoutWatermark_DoesNotReturnValid()
    {
        var services = new TestServices();

        var result = services.Watermarks.Extract(
            TestServices.CreateTexturedImage(),
            TestServices.ReliableSettings());

        Assert.NotEqual(WatermarkStatus.Valid, result.Status);
    }

    [Fact]
    public void Extract_AmbiguousCoefficientDifference_ReturnsError()
    {
        var services = new TestServices();
        var pixels = Enumerable.Repeat(
                new byte[] { 128, 128, 128, 255 },
                64 * 64)
            .SelectMany(pixel => pixel)
            .ToArray();
        var image = new ImagePixelData(64, 64, 96, 96, pixels);

        var result = services.Watermarks.Extract(
            image,
            new WatermarkSettings { Delta = 10 });

        Assert.Equal(WatermarkStatus.Error, result.Status);
        Assert.Contains("уверенность", result.ErrorMessage);
    }

    [Fact]
    public void DifferentDelta_ChangesEmbeddedPixels()
    {
        var services = new TestServices();
        var image = TestServices.CreateTexturedImage(128, 128);

        var weak = services.Watermarks.Embed(
            image,
            "A",
            new WatermarkSettings { Delta = 5 });
        var strong = services.Watermarks.Embed(
            image,
            "A",
            new WatermarkSettings { Delta = 20 });

        Assert.False(weak.Image.Pixels.SequenceEqual(strong.Image.Pixels));
    }
}
