using ImageGuard.Models;
using ImageGuard.Services;

namespace ImageGuard.Tests;

public sealed class TransformationTests
{
    [Fact]
    public void Brightness_ChangesRgbAndPreservesAlpha()
    {
        var source = CreateSolidImage(2, 2, b: 10, g: 100, r: 250, a: 77);
        var transformation = new BrightnessTransformation();

        var result = transformation.Apply(
            source,
            new ImageTransformationParameters { Value = 20 }).Image;

        Assert.Equal(30, result.Pixels[0]);
        Assert.Equal(120, result.Pixels[1]);
        Assert.Equal(255, result.Pixels[2]);
        Assert.Equal(77, result.Pixels[3]);
        Assert.Equal(10, source.Pixels[0]);
    }

    [Fact]
    public void GaussianNoise_WithSameSeed_IsReproducible()
    {
        var source = TestServices.CreateTexturedImage(32, 32);
        var transformation = new GaussianNoiseTransformation();
        var parameters = new ImageTransformationParameters { Value = 10, Seed = 42 };

        var first = transformation.Apply(source, parameters).Image;
        var second = transformation.Apply(source, parameters).Image;

        Assert.Equal(first.Pixels, second.Pixels);
        Assert.NotEqual(source.Pixels, first.Pixels);
    }

    [Fact]
    public void Contrast_UsesMidpointAndDoesNotActAsBrightness()
    {
        var source = CreateSolidImage(1, 1, b: 64, g: 128, r: 192, a: 200);

        var result = new ContrastTransformation().Apply(
            source,
            new ImageTransformationParameters { Value = 0.5 }).Image;

        Assert.Equal(96, result.Pixels[0]);
        Assert.Equal(128, result.Pixels[1]);
        Assert.Equal(160, result.Pixels[2]);
        Assert.Equal(200, result.Pixels[3]);
    }

    [Fact]
    public void Resize_CanRestoreOriginalDimensions()
    {
        var source = TestServices.CreateTexturedImage(80, 64);
        var transformation = new ResizeTransformation();

        var reduced = transformation.Apply(
            source,
            new ImageTransformationParameters
            {
                Value = 50,
                RestoreOriginalSize = false
            }).Image;
        var restored = transformation.Apply(
            source,
            new ImageTransformationParameters
            {
                Value = 50,
                RestoreOriginalSize = true
            }).Image;

        Assert.Equal(40, reduced.Width);
        Assert.Equal(32, reduced.Height);
        Assert.Equal(source.Width, restored.Width);
        Assert.Equal(source.Height, restored.Height);
    }

    [Fact]
    public void CropAndRegionModification_UseRequestedGeometry()
    {
        var source = CreateSolidImage(100, 80, b: 10, g: 20, r: 30, a: 255);
        var cropped = new CropTransformation().Apply(
            source,
            new ImageTransformationParameters
            {
                Value = 20,
                RestoreOriginalSize = false
            }).Image;

        Assert.Equal(80, cropped.Width);
        Assert.Equal(64, cropped.Height);

        var modified = new RegionModificationTransformation().Apply(
            source,
            new ImageTransformationParameters
            {
                X = 10,
                Y = 5,
                Width = 2,
                Height = 3,
                RegionRed = 200,
                RegionGreen = 150,
                RegionBlue = 100
            }).Image;
        var changedIndex = (5 * source.Width + 10) * 4;
        var untouchedIndex = 0;
        Assert.Equal(100, modified.Pixels[changedIndex]);
        Assert.Equal(150, modified.Pixels[changedIndex + 1]);
        Assert.Equal(200, modified.Pixels[changedIndex + 2]);
        Assert.Equal(10, modified.Pixels[untouchedIndex]);
        Assert.Equal(20, modified.Pixels[untouchedIndex + 1]);
        Assert.Equal(30, modified.Pixels[untouchedIndex + 2]);
    }

    private static ImagePixelData CreateSolidImage(
        int width,
        int height,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = a;
        }

        return new(width, height, 96, 96, pixels);
    }
}
