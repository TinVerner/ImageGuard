using ImageGuard.Models;
using ImageGuard.Services.Cryptography;
using ImageGuard.Services.Experiments;
using ImageGuard.Services.Imaging;
using ImageGuard.Services.Transformations;
using ImageGuard.Services.Verification;
using ImageGuard.Services.Watermarking;

namespace ImageGuard.Tests;

internal sealed class TestServices
{
    public TestServices()
    {
        Hash = new HashService();
        Keys = new KeyService(Hash);
        Signatures = new DigitalSignatureService();
        Documents = new SignatureDocumentService();
        Images = new ImageFileService();
        Colors = new ColorSpaceService();
        TextCodec = new WatermarkTextCodec();
        Watermarks = new WatermarkService(
            new DctService(),
            TextCodec,
            Colors,
            new SequentialBlockSelector());
        Metrics = new ImageQualityMetricsService();
        Protection = new ProtectionService(
            Images,
            Watermarks,
            Metrics,
            Hash,
            Keys,
            Signatures,
            Documents);
        Verification = new VerificationService(
            Images,
            Watermarks,
            Hash,
            Keys,
            Signatures,
            Documents);
        Transformations =
        [
            new JpegCompressionTransformation(),
            new BrightnessTransformation(),
            new ContrastTransformation(),
            new GaussianNoiseTransformation(),
            new ResizeTransformation(),
            new CropTransformation(),
            new RegionModificationTransformation()
        ];
        Experiments = new ExperimentService(
            Images,
            Metrics,
            Verification,
            TextCodec,
            Watermarks,
            Documents,
            Transformations);
    }

    public IHashService Hash { get; }
    public IKeyService Keys { get; }
    public IDigitalSignatureService Signatures { get; }
    public ISignatureDocumentService Documents { get; }
    public IImageFileService Images { get; }
    public IColorSpaceService Colors { get; }
    public IWatermarkTextCodec TextCodec { get; }
    public IWatermarkService Watermarks { get; }
    public IImageQualityMetricsService Metrics { get; }
    public IProtectionService Protection { get; }
    public IVerificationService Verification { get; }
    public IReadOnlyList<IImageTransformation> Transformations { get; }
    public IExperimentService Experiments { get; }

    public static ImagePixelData CreateTexturedImage(int width = 192, int height = 192)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index] = (byte)((x * 3 + y * 5) % 256);
                pixels[index + 1] = (byte)((x * 7 + y * 2 + 40) % 256);
                pixels[index + 2] = (byte)((x * 2 + y * 9 + 80) % 256);
                pixels[index + 3] = 255;
            }
        }

        return new(width, height, 96, 96, pixels);
    }

    public static WatermarkSettings ReliableSettings() => new() { Delta = 20 };
}
