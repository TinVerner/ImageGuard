using System.Security.Cryptography;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ExactByteSigningTests
{
    [Fact]
    public async Task Protect_SignsExactFinalEncodedBytes()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, "protected.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(256, 256),
                inputPath);
            var keys = services.Crypto.GenerateKeyPair(directory, "exact-bytes");
            var settings = TestServices.ReliableSettings();

            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                outputPath,
                "EXACT-BYTES",
                settings,
                keys.PrivateKeyPath,
                signaturePath));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.WatermarkValidAfterSave, result.ErrorMessage);

            var finalBytes = File.ReadAllBytes(outputPath);
            var document = services.Crypto.Load(signaturePath);
            using var publicKey = services.Crypto.LoadPublicKey(keys.PublicKeyPath);
            Assert.Equal(services.Crypto.ComputeSha256Hex(finalBytes), document.Sha256);
            Assert.Equal(SignatureDocument.CurrentAlgorithm, document.Algorithm);
            Assert.True(publicKey.VerifyData(
                finalBytes,
                Convert.FromBase64String(document.SignatureBase64),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
            Assert.True(services.Crypto.Verify(
                finalBytes,
                Convert.FromBase64String(document.SignatureBase64),
                publicKey));

            var reloaded = services.Images.Load(outputPath);
            var extracted = services.Watermarks.Extract(
                reloaded,
                new WatermarkSettings { Delta = document.WatermarkDelta });
            Assert.Equal(WatermarkStatus.Valid, extracted.Status);
            Assert.Equal("EXACT-BYTES", extracted.Text);

            Assert.Equal(0x89, finalBytes[0]);
            Assert.Equal(0x50, finalBytes[1]);
            Assert.Equal(0x4E, finalBytes[2]);
            Assert.Equal(0x47, finalBytes[3]);
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
                inputPath);
            var keys = services.Crypto.GenerateKeyPair(directory, "modified-bytes");
            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                outputPath,
                "MODIFIED-BYTES",
                TestServices.ReliableSettings(),
                keys.PrivateKeyPath,
                signaturePath));
            Assert.True(result.Success, result.ErrorMessage);

            var bytes = File.ReadAllBytes(outputPath);
            var document = services.Crypto.Load(signaturePath);
            using var publicKey = services.Crypto.LoadPublicKey(keys.PublicKeyPath);
            bytes[^1] ^= 0x01;

            Assert.False(services.Crypto.Verify(
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
    [InlineData("wrong.jpg")]
    [InlineData("wrong.jpeg")]
    [InlineData("missing-extension")]
    public void Save_RejectsNonPngExtension(string fileName)
    {
        var services = new TestServices();
        var path = Path.Combine(Path.GetTempPath(), fileName);

        Assert.Throws<ArgumentException>(() =>
            services.Images.Save(
                TestServices.CreateTexturedImage(32, 32),
                path));
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
