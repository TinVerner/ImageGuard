using ImageGuard.Enums;

namespace ImageGuard.Tests;

public sealed class ImageIoRobustnessTests
{
    [Fact]
    public void Load_ReleasesSourceFileImmediately()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var originalPath = Path.Combine(directory, "source.png");
            var movedPath = Path.Combine(directory, "moved.png");
            services.Images.Save(
                TestServices.CreateTexturedImage(32, 32),
                originalPath,
                ImageOutputFormat.Png);

            var loaded = services.Images.Load(originalPath);
            File.Move(originalPath, movedPath);
            File.Delete(movedPath);

            Assert.Equal(32, loaded.Width);
            Assert.False(File.Exists(originalPath));
            Assert.False(File.Exists(movedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PngWatermark_PreservesAlphaChannel()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "alpha.png");
            var original = TestServices.CreateTexturedImage(192, 192);
            for (var index = 3; index < original.Pixels.Length; index += 4)
            {
                original.Pixels[index] = (byte)((index / 4) % 256);
            }

            var embedded = services.Watermarks.Embed(
                original,
                "ALPHA",
                TestServices.ReliableSettings());
            services.Images.Save(embedded.Image, path, ImageOutputFormat.Png);
            var reloaded = services.Images.Load(path);

            for (var index = 3; index < original.Pixels.Length; index += 4)
            {
                Assert.Equal(original.Pixels[index], reloaded.Pixels[index]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JpegSave_ExplicitlyProducesOpaquePixels()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "opaque.jpg");
            var image = TestServices.CreateTexturedImage(32, 32);
            for (var index = 3; index < image.Pixels.Length; index += 4)
            {
                image.Pixels[index] = 0;
            }

            services.Images.Save(image, path, ImageOutputFormat.Jpeg, 100);
            var reloaded = services.Images.Load(path);

            for (var index = 3; index < reloaded.Pixels.Length; index += 4)
            {
                Assert.Equal(255, reloaded.Pixels[index]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Embed_NonMultipleOfEightEdgesRemainByteIdentical()
    {
        var services = new TestServices();
        var original = TestServices.CreateTexturedImage(201, 199);

        var embedded = services.Watermarks.Embed(
            original,
            "PARTIAL-EDGE",
            TestServices.ReliableSettings());

        for (var y = 0; y < original.Height; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                if (x < 200 && y < 192)
                {
                    continue;
                }

                var index = (y * original.Width + x) * 4;
                Assert.Equal(
                    original.Pixels.AsSpan(index, 4).ToArray(),
                    embedded.Image.Pixels.AsSpan(index, 4).ToArray());
            }
        }
    }

    [Fact]
    public async Task Protect_DoesNotOverwriteInputFile()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var signaturePath = Path.Combine(directory, "input.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath,
                ImageOutputFormat.Png);
            var originalBytes = File.ReadAllBytes(inputPath);
            var keys = services.Keys.GenerateKeyPair(directory, "no-overwrite", "password");

            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                inputPath,
                "NO-OVERWRITE",
                TestServices.ReliableSettings(),
                keys.PrivateKeyPath,
                "password",
                signaturePath,
                ImageOutputFormat.Png));

            Assert.False(result.Success);
            Assert.Equal(originalBytes, File.ReadAllBytes(inputPath));
            Assert.False(File.Exists(signaturePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ImageGuardIoTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
