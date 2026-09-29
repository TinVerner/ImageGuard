using ImageGuard.Enums;

namespace ImageGuard.Models;

public sealed class ImageTransformationParameters
{
    /// <summary>
    /// Основной параметр: JPEG quality, brightness offset, contrast multiplier,
    /// noise sigma, resize percent или crop percent — в зависимости от типа.
    /// </summary>
    public double Value { get; set; }

    public int Seed { get; set; } = 42;
    public bool RestoreOriginalSize { get; set; } = true;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 64;
    public int Height { get; set; } = 64;
    public byte RegionRed { get; set; } = 220;
    public byte RegionGreen { get; set; } = 30;
    public byte RegionBlue { get; set; } = 30;
}

public sealed record ExperimentCase(
    ImageTransformationType TransformationType,
    ImageTransformationParameters Parameters,
    string? Label = null);

public sealed record ExperimentRequest(
    string ImagePath,
    string SignaturePath,
    string PublicKeyPath,
    string OutputDirectory,
    string? ExpectedWatermarkText,
    WatermarkSettings WatermarkSettings,
    IReadOnlyList<ExperimentCase> Cases);

public sealed record ExperimentResult(
    string TransformationName,
    string Parameters,
    SignatureStatus SignatureStatus,
    bool SignatureValid,
    WatermarkStatus WatermarkStatus,
    string? ExtractedWatermark,
    double? WatermarkBitErrorRate,
    double? Mse,
    double? Psnr,
    TimeSpan ProcessingTime,
    string OutputFilePath,
    string? ErrorMessage)
{
    public string MseDisplay => Mse is null ? "N/A" : $"{Mse:F4}";

    public string PsnrDisplay => Psnr switch
    {
        null => "N/A",
        double.PositiveInfinity => "∞ dB",
        _ => $"{Psnr:F2} dB"
    };

    public string BerDisplay => WatermarkBitErrorRate is null
        ? "N/A"
        : $"{WatermarkBitErrorRate:P2}";

    public string SignatureDisplay => SignatureStatus switch
    {
        SignatureStatus.Valid => "Действительна",
        SignatureStatus.Invalid => "Недействительна",
        SignatureStatus.Missing => "Отсутствует",
        SignatureStatus.NotChecked => "Не проверена",
        SignatureStatus.FormatError => "Ошибка формата",
        _ => "Ошибка"
    };

    public string WatermarkDisplay => WatermarkStatus switch
    {
        WatermarkStatus.Valid => "Обнаружен",
        WatermarkStatus.NotFound => "Не найден",
        _ => "Ошибка"
    };
}
