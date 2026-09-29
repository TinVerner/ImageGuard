using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ExactByteSigningTests
{
    [Theory]
    [InlineData(ImageOutputFormat.Png, "protected.png", 95)]
    [InlineData(ImageOutputFormat.Jpeg, "protected.jpg", 100)]
    public async Task Protect_SignsExactFinalEncodedBytes(
        ImageOutputFormat format,
        string outputName,
        int jpegQuality)
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, outputName);
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(256, 256),
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "exact-bytes", "password");
            var settings = TestServices.ReliableSettings();

            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                outputPath,
                "EXACT-BYTES",
                settings,
                keys.PrivateKeyPath,
                "password",
                signaturePath,
                format,
                jpegQuality));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.WatermarkValidAfterSave, result.ErrorMessage);

            var finalBytes = File.ReadAllBytes(outputPath);
            var document = services.Documents.Load(signaturePath);
            using var publicKey = services.Keys.LoadPublicKey(keys.PublicKeyPath);
            Assert.Equal(services.Hash.ComputeSha256Hex(finalBytes), document.Sha256);
            Assert.True(services.Signatures.Verify(
                finalBytes,
                Convert.FromBase64String(document.SignatureBase64),
                publicKey));

            var reloaded = services.Images.Load(outputPath);
            var extracted = services.Watermarks.Extract(
                reloaded,
                new WatermarkSettings { Delta = document.WatermarkDelta });
            Assert.Equal(WatermarkStatus.Valid, extracted.Status);
            Assert.Equal("EXACT-BYTES", extracted.Text);

            if (format == ImageOutputFormat.Jpeg)
            {
                Assert.Equal(0xFF, finalBytes[0]);
                Assert.Equal(0xD8, finalBytes[1]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Protect_ModifiedFinalBytes_DoNotVerify()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, "protected.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "modified-bytes", "password");
            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                outputPath,
                "MODIFIED-BYTES",
                TestServices.ReliableSettings(),
                keys.PrivateKeyPath,
                "password",
                signaturePath,
                ImageOutputFormat.Png));
            Assert.True(result.Success, result.ErrorMessage);

            var bytes = File.ReadAllBytes(outputPath);
            var document = services.Documents.Load(signaturePath);
            using var publicKey = services.Keys.LoadPublicKey(keys.PublicKeyPath);
            bytes[^1] ^= 0x01;

            Assert.False(services.Signatures.Verify(
                bytes,
                Convert.FromBase64String(document.SignatureBase64),
                publicKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(ImageOutputFormat.Png, "wrong.jpg")]
    [InlineData(ImageOutputFormat.Jpeg, "wrong.png")]
    [InlineData(ImageOutputFormat.Jpeg, "missing-extension")]
    public void Save_RejectsExtensionThatDoesNotMatchEncoder(
        ImageOutputFormat format,
        string fileName)
    {
        var services = new TestServices();
        var path = Path.Combine(Path.GetTempPath(), fileName);

        Assert.Throws<ArgumentException>(() =>
            services.Images.Save(
                TestServices.CreateTexturedImage(32, 32),
                path,
                format));
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ImageGuardExactBytes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
