using ImageGuard.Enums;

namespace ImageGuard.Models;

public sealed record VerificationRequest(
    string ImagePath,
    string SignaturePath,
    string PublicKeyPath,
    WatermarkSettings WatermarkSettings);

public sealed record VerificationResult(
    SignatureStatus SignatureStatus,
    WatermarkStatus WatermarkStatus,
    OverallVerificationStatus OverallStatus,
    string? CurrentSha256,
    string? StoredSha256,
    string? PublicKeyFingerprint,
    string? ExpectedKeyFingerprint,
    string? ExtractedWatermark,
    bool SignatureValid,
    bool WatermarkDetected,
    TimeSpan ProcessingTime,
    string? ErrorMessage)
{
    public string OverallMessage => OverallStatus switch
    {
        OverallVerificationStatus.Authentic => "Изображение подлинно и не изменено.",
        OverallVerificationStatus.ModifiedWatermarkPreserved =>
            "Изображение изменено, однако цифровой водяной знак сохранился.",
        OverallVerificationStatus.ModifiedWatermarkNotFound =>
            "Изображение изменено, цифровой водяной знак не найден.",
        OverallVerificationStatus.Inconsistent when SignatureStatus == Enums.SignatureStatus.Valid =>
            "Подпись действительна, но водяной знак недоступен или некорректен. Результаты проверки противоречат друг другу.",
        OverallVerificationStatus.Inconsistent when SignatureStatus == Enums.SignatureStatus.Invalid =>
            "Подпись недействительна, но состояние водяного знака определить не удалось.",
        OverallVerificationStatus.Inconsistent when SignatureStatus == Enums.SignatureStatus.Missing =>
            "Файл подписи отсутствует. Результат водяного знака показан отдельно, подлинность не установлена.",
        OverallVerificationStatus.Inconsistent when SignatureStatus == Enums.SignatureStatus.NotChecked =>
            "Подпись не проверена. Результат водяного знака показан отдельно, подлинность не установлена.",
        OverallVerificationStatus.Inconsistent when SignatureStatus == Enums.SignatureStatus.FormatError =>
            "Файл подписи имеет неподдерживаемый или некорректный формат. Водяной знак проверен независимо.",
        OverallVerificationStatus.Inconsistent =>
            "Одна из независимых проверок завершилась ошибкой. Доступный результат показан отдельно.",
        _ => "Проверка не завершена из-за ошибки."
    };
}

public sealed record KeyGenerationResult(
    string PrivateKeyPath,
    string PublicKeyPath,
    int KeySize,
    string Fingerprint);

public sealed record KeyInspectionResult(
    bool IsPrivate,
    bool IsEncrypted,
    int KeySize,
    string Fingerprint);
