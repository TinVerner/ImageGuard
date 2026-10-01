using ImageGuard.Models;
using ImageGuard.Services;

namespace ImageGuard.Tests;

internal sealed class TestServices
{
    public TestServices()
    {
        Crypto = new CryptoService();
        Keys = new KeyService(Crypto);
        Documents = new SignatureDocumentService();
        Images = new ImageFileService();
        Colors = new ColorSpaceService();
        WatermarkCodec = new WatermarkCodec();
        Watermarks = new WatermarkService(
            new DctService(),
            WatermarkCodec,
            Colors);
        Metrics = new ImageQualityMetricsService();
        Protection = new ProtectionService(
            Images,
            Watermarks,
            Metrics,
            Crypto,
            Keys,
            Documents);
        Verification = new VerificationService(
            Images,
            Watermarks,
            Crypto,
            Keys,
            Documents);
    }

    public CryptoService Crypto { get; }
    public IKeyService Keys { get; }
    public ISignatureDocumentService Documents { get; }
    public IImageFileService Images { get; }
    public ColorSpaceService Colors { get; }
    public WatermarkCodec WatermarkCodec { get; }
    public IWatermarkService Watermarks { get; }
    public ImageQualityMetricsService Metrics { get; }
    public IProtectionService Protection { get; }
    public IVerificationService Verification { get; }

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
