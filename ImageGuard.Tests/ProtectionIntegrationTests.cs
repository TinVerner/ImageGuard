using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ProtectionIntegrationTests
{
    [Fact]
    public async Task ProtectThenSign_ValidAndModifiedScenariosWork()
    {
        var services = new TestServices();
        var directory = Path.Combine(Path.GetTempPath(), $"ImageGuardTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var protectedPath = Path.Combine(directory, "protected.png");
            var modifiedPath = Path.Combine(directory, "modified.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath);
            var keys = services.Crypto.GenerateKeyPair(directory, "integration");
            var settings = TestServices.ReliableSettings();

            var protection = await services.Protection.ProtectAsync(new(
                inputPath,
                protectedPath,
                "BSTU-TEST-001",
                settings,
                keys.PrivateKeyPath,
                signaturePath));

            Assert.True(protection.Success, protection.ErrorMessage);
            Assert.True(protection.WatermarkValidAfterSave, protection.ErrorMessage);

            var protectedBytes = File.ReadAllBytes(protectedPath);
            var document = services.Crypto.Load(signaturePath);
            using var publicKey = services.Crypto.LoadPublicKey(keys.PublicKeyPath);
            Assert.True(services.Crypto.Verify(
                protectedBytes,
                Convert.FromBase64String(document.SignatureBase64),
                publicKey));

            var extracted = services.Watermarks.Extract(
                services.Images.Load(protectedPath),
                settings);
            Assert.Equal(WatermarkStatus.Valid, extracted.Status);
            Assert.Equal("BSTU-TEST-001", extracted.Text);

            var modifiedImage = services.Images.Load(protectedPath).Clone();
            modifiedImage.Pixels[^4] ^= 0x01;
            services.Images.Save(modifiedImage, modifiedPath);

            Assert.False(services.Crypto.Verify(
                File.ReadAllBytes(modifiedPath),
                Convert.FromBase64String(document.SignatureBase64),
                publicKey));
            var modifiedExtracted = services.Watermarks.Extract(
                services.Images.Load(modifiedPath),
                settings);
            Assert.Equal(WatermarkStatus.Valid, modifiedExtracted.Status);
            Assert.Equal("BSTU-TEST-001", modifiedExtracted.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
