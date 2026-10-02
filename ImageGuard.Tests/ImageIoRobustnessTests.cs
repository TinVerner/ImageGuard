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
                originalPath);

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
            services.Images.Save(embedded.Image, path);
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
    public void Load_RejectsNonPngExtension()
    {
        var services = new TestServices();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        try
        {
            Assert.Throws<NotSupportedException>(() => services.Images.Load(path));
        }
        finally
        {
            File.Delete(path);
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
                inputPath);
            var originalBytes = File.ReadAllBytes(inputPath);
            var keys = services.Crypto.GenerateKeyPair(directory, "no-overwrite");

            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                inputPath,
                "NO-OVERWRITE",
                TestServices.ReliableSettings(),
                keys.PrivateKeyPath,
                signaturePath));

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
