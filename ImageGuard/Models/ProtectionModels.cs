using ImageGuard.Enums;

namespace ImageGuard.Models;

public sealed record ProtectionRequest(
    string InputImagePath,
    string OutputImagePath,
    string WatermarkText,
    WatermarkSettings WatermarkSettings,
    string PrivateKeyPath,
    string? PrivateKeyPassword,
    string SignatureOutputPath,
    ImageOutputFormat ImageOutputFormat,
    int JpegQuality = 95);

public sealed record ProtectionResult(
    bool Success,
    string? OutputImagePath,
    string? SignaturePath,
    string? Sha256,
    bool WatermarkValidAfterSave,
    double? Mse,
    double? Psnr,
    TimeSpan ProcessingTime,
    string? ErrorMessage)
{
    public static ProtectionResult Failure(TimeSpan elapsed, string error) =>
        new(false, null, null, null, false, null, null, elapsed, error);
}
