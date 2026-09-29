using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ProtectionIntegrationTests
{
    [Fact]
    public async Task ProtectThenVerify_ValidAndModifiedScenariosWork()
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
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "integration", "test-password");
            var settings = TestServices.ReliableSettings();

            var protection = await services.Protection.ProtectAsync(new(
                inputPath,
                protectedPath,
                "BSTU-TEST-001",
                settings,
                keys.PrivateKeyPath,
                "test-password",
                signaturePath,
                ImageOutputFormat.Png));

            Assert.True(protection.Success, protection.ErrorMessage);
            Assert.True(protection.WatermarkValidAfterSave, protection.ErrorMessage);

            var valid = await services.Verification.VerifyAsync(new(
                protectedPath,
                signaturePath,
                keys.PublicKeyPath,
                settings));

            Assert.True(valid.SignatureValid, valid.ErrorMessage);
            Assert.Equal(WatermarkStatus.Valid, valid.WatermarkStatus);
            Assert.Equal(OverallVerificationStatus.Authentic, valid.OverallStatus);
            Assert.Equal("BSTU-TEST-001", valid.ExtractedWatermark);

            var modifiedImage = services.Images.Load(protectedPath).Clone();
            var lastPixel = modifiedImage.Pixels.Length - 4;
            modifiedImage.Pixels[lastPixel] ^= 0x01;
            services.Images.Save(modifiedImage, modifiedPath, ImageOutputFormat.Png);

            var modified = await services.Verification.VerifyAsync(new(
                modifiedPath,
                signaturePath,
                keys.PublicKeyPath,
                settings));

            Assert.False(modified.SignatureValid);
            Assert.Equal(SignatureStatus.Invalid, modified.SignatureStatus);
            Assert.Equal(WatermarkStatus.Valid, modified.WatermarkStatus);
            Assert.Equal(
                OverallVerificationStatus.ModifiedWatermarkPreserved,
                modified.OverallStatus);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
