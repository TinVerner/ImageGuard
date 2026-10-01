using ImageGuard.Enums;
using ImageGuard.Models;
using ImageGuard.Services;

namespace ImageGuard.Tests;

public sealed class VerificationSemanticsTests
{
    [Fact]
    public async Task Verify_WrongPublicKey_DoesNotReportImageAsModified()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var otherKeys = fixture.Services.Keys.GenerateKeyPair(
            fixture.Directory,
            "other",
            "other-password");

        var result = await fixture.Services.Verification.VerifyAsync(new(
            fixture.ProtectedImagePath,
            fixture.SignaturePath,
            otherKeys.PublicKeyPath,
            fixture.Settings));

        Assert.Equal(SignatureStatus.NotChecked, result.SignatureStatus);
        Assert.False(result.SignatureValid);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(OverallVerificationStatus.Inconsistent, result.OverallStatus);
        Assert.Contains("fingerprint", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_MissingSignature_StillExtractsWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var missingSignaturePath = Path.Combine(fixture.Directory, "missing.igsig");

        var result = await fixture.Services.Verification.VerifyAsync(new(
            fixture.ProtectedImagePath,
            missingSignaturePath,
            fixture.PublicKeyPath,
            fixture.Settings));

        Assert.Equal(SignatureStatus.Missing, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(fixture.WatermarkText, result.ExtractedWatermark);
        Assert.Equal(OverallVerificationStatus.Inconsistent, result.OverallStatus);
    }

    [Fact]
    public async Task Verify_MalformedSignatureJson_StillExtractsWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        File.WriteAllText(fixture.SignaturePath, "{ malformed json");

        var result = await fixture.VerifyAsync();

        Assert.Equal(SignatureStatus.FormatError, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(fixture.WatermarkText, result.ExtractedWatermark);
    }

    [Fact]
    public async Task Verify_InvalidSignatureBase64_StillExtractsWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var document = fixture.Services.Documents.Load(fixture.SignaturePath);
        document.SignatureBase64 = "not-valid-base64%%%";
        fixture.Services.Documents.Save(document, fixture.SignaturePath);

        var result = await fixture.VerifyAsync();

        Assert.Equal(SignatureStatus.FormatError, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(fixture.WatermarkText, result.ExtractedWatermark);
    }

    [Fact]
    public async Task Verify_UnsupportedSignatureVersion_StillExtractsWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var document = fixture.Services.Documents.Load(fixture.SignaturePath);
        document.Version = SignatureDocument.CurrentVersion + 1;
        File.WriteAllText(
            fixture.SignaturePath,
            fixture.Services.Documents.Serialize(document));

        var result = await fixture.VerifyAsync();

        Assert.Equal(SignatureStatus.FormatError, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(fixture.WatermarkText, result.ExtractedWatermark);
    }

    [Fact]
    public async Task Verify_InvalidPublicKey_StillExtractsWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var invalidKeyPath = Path.Combine(fixture.Directory, "invalid.public.pem");
        File.WriteAllText(invalidKeyPath, "not a PEM key");

        var result = await fixture.Services.Verification.VerifyAsync(new(
            fixture.ProtectedImagePath,
            fixture.SignaturePath,
            invalidKeyPath,
            fixture.Settings));

        Assert.Equal(SignatureStatus.Error, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Valid, result.WatermarkStatus);
        Assert.Equal(fixture.WatermarkText, result.ExtractedWatermark);
        Assert.Equal(OverallVerificationStatus.Inconsistent, result.OverallStatus);
    }

    [Fact]
    public async Task Verify_WatermarkExtractionError_IsNotReportedAsDamaged()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var verification = CreateVerificationWithThrowingWatermark(fixture.Services);

        var result = await verification.VerifyAsync(new(
            fixture.ProtectedImagePath,
            fixture.SignaturePath,
            fixture.PublicKeyPath,
            fixture.Settings));

        Assert.Equal(SignatureStatus.Valid, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Error, result.WatermarkStatus);
        Assert.Equal(OverallVerificationStatus.Inconsistent, result.OverallStatus);
        Assert.DoesNotContain("изменено", result.OverallMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_InvalidSignatureAndWatermarkError_DoesNotReportDamagedWatermark()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var modifiedImagePath = Path.Combine(fixture.Directory, "modified.png");
        var modifiedImage = fixture.Services.Images.Load(fixture.ProtectedImagePath).Clone();
        modifiedImage.Pixels[^4] ^= 0x01;
        fixture.Services.Images.Save(
            modifiedImage,
            modifiedImagePath,
            ImageOutputFormat.Png);
        var verification = CreateVerificationWithThrowingWatermark(fixture.Services);

        var result = await verification.VerifyAsync(new(
            modifiedImagePath,
            fixture.SignaturePath,
            fixture.PublicKeyPath,
            fixture.Settings));

        Assert.Equal(SignatureStatus.Invalid, result.SignatureStatus);
        Assert.Equal(WatermarkStatus.Error, result.WatermarkStatus);
        Assert.Equal(OverallVerificationStatus.Inconsistent, result.OverallStatus);
        Assert.NotEqual(
            OverallVerificationStatus.ModifiedWatermarkNotFound,
            result.OverallStatus);
    }

    private static IVerificationService CreateVerificationWithThrowingWatermark(
        TestServices services) =>
        new VerificationService(
            services.Images,
            new ThrowingWatermarkService(),
            services.Crypto,
            services.Keys,
            services.Documents);

    private sealed class ThrowingWatermarkService : IWatermarkService
    {
        public WatermarkCapacity CalculateCapacity(ImagePixelData image, string text) =>
            throw new InvalidOperationException("Synthetic watermark failure.");

        public WatermarkEmbedResult Embed(
            ImagePixelData image,
            string text,
            WatermarkSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic watermark failure.");

        public WatermarkExtractionResult Extract(
            ImagePixelData image,
            WatermarkSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic watermark failure.");
    }

    private sealed class VerificationFixture : IDisposable
    {
        private VerificationFixture(
            TestServices services,
            string directory,
            string protectedImagePath,
            string signaturePath,
            string publicKeyPath,
            string watermarkText,
            WatermarkSettings settings)
        {
            Services = services;
            Directory = directory;
            ProtectedImagePath = protectedImagePath;
            SignaturePath = signaturePath;
            PublicKeyPath = publicKeyPath;
            WatermarkText = watermarkText;
            Settings = settings;
        }

        public TestServices Services { get; }
        public string Directory { get; }
        public string ProtectedImagePath { get; }
        public string SignaturePath { get; }
        public string PublicKeyPath { get; }
        public string WatermarkText { get; }
        public WatermarkSettings Settings { get; }

        public static async Task<VerificationFixture> CreateAsync()
        {
            var services = new TestServices();
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"ImageGuardVerificationTests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            try
            {
                var inputPath = Path.Combine(directory, "input.png");
                var protectedImagePath = Path.Combine(directory, "protected.png");
                var signaturePath = Path.Combine(directory, "protected.igsig");
                const string watermarkText = "STAGE-1-TEST";
                const string password = "test-password";
                var settings = TestServices.ReliableSettings();

                services.Images.Save(
                    TestServices.CreateTexturedImage(),
                    inputPath,
                    ImageOutputFormat.Png);
                var keys = services.Keys.GenerateKeyPair(
                    directory,
                    "verification",
                    password);
                var protection = await services.Protection.ProtectAsync(new(
                    inputPath,
                    protectedImagePath,
                    watermarkText,
                    settings,
                    keys.PrivateKeyPath,
                    password,
                    signaturePath,
                    ImageOutputFormat.Png));

                Assert.True(protection.Success, protection.ErrorMessage);
                Assert.True(protection.WatermarkValidAfterSave, protection.ErrorMessage);

                return new(
                    services,
                    directory,
                    protectedImagePath,
                    signaturePath,
                    keys.PublicKeyPath,
                    watermarkText,
                    settings);
            }
            catch
            {
                System.IO.Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public Task<VerificationResult> VerifyAsync() =>
            Services.Verification.VerifyAsync(new(
                ProtectedImagePath,
                SignaturePath,
                PublicKeyPath,
                Settings));

        public void Dispose()
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
