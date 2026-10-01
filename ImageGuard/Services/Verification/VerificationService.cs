using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using ImageGuard.Enums;
using ImageGuard.Models;
using ImageGuard.Services.Cryptography;
using ImageGuard.Services.Imaging;
using ImageGuard.Services.Watermarking;

namespace ImageGuard.Services.Verification;

public interface IVerificationService
{
    Task<VerificationResult> VerifyAsync(
        VerificationRequest request,
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class VerificationService(
    IImageFileService imageFileService,
    IWatermarkService watermarkService,
    IHashService hashService,
    IKeyService keyService,
    IDigitalSignatureService signatureService,
    ISignatureDocumentService documentService) : IVerificationService
{
    public Task<VerificationResult> VerifyAsync(
        VerificationRequest request,
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => Verify(request, status, progress, cancellationToken),
            cancellationToken);
    }

    private VerificationResult Verify(
        VerificationRequest request,
        IProgress<string>? status,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            progress?.Report(0);
            cancellationToken.ThrowIfCancellationRequested();

            byte[]? fileBytes = null;
            string? currentHash = null;
            string? imageReadError = null;
            try
            {
                if (string.IsNullOrWhiteSpace(request.ImagePath) ||
                    !File.Exists(request.ImagePath))
                {
                    throw new FileNotFoundException(
                        "Проверяемое изображение не найдено.",
                        request.ImagePath);
                }

                fileBytes = File.ReadAllBytes(request.ImagePath);
                currentHash = hashService.ComputeSha256Hex(fileBytes);
                progress?.Report(0.15);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Debug.WriteLine(exception);
                imageReadError = ToUserMessage(exception);
            }

            // RSA и watermark — независимые доказательства: ошибка одной ветви
            // не должна запрещать выполнение и отображение результата другой.
            status?.Report("Проверка цифровой подписи...");
            var signature = VerifySignature(request, fileBytes, cancellationToken);
            progress?.Report(0.45);

            status?.Report("Извлечение цифрового водяного знака...");
            var extractionSettings = new WatermarkSettings
            {
                Delta = signature.WatermarkDelta ?? request.WatermarkSettings.Delta
            };
            var extractionProgress = progress is null
                ? null
                : new Progress<double>(value => progress.Report(0.45 + value * 0.5));
            var extraction = ExtractWatermark(
                request,
                extractionSettings,
                extractionProgress,
                cancellationToken);
            var overall = DetermineOverallStatus(signature.Status, extraction.Status);
            stopwatch.Stop();

            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(imageReadError))
            {
                notes.Add(imageReadError);
            }

            if (!string.IsNullOrWhiteSpace(signature.Message))
            {
                notes.Add(signature.Message);
            }

            if (currentHash is not null &&
                signature.StoredSha256 is not null &&
                !string.Equals(currentHash, signature.StoredSha256, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("Текущий SHA-256 отличается от диагностического значения в .igsig.");
            }

            if (!string.IsNullOrWhiteSpace(extraction.ErrorMessage))
            {
                notes.Add(extraction.ErrorMessage);
            }

            status?.Report("Проверка завершена.");
            progress?.Report(1);
            return new(
                signature.Status,
                extraction.Status,
                overall,
                currentHash,
                signature.StoredSha256,
                signature.ActualFingerprint,
                signature.ExpectedFingerprint,
                extraction.Text,
                signature.IsValid,
                extraction.Status == WatermarkStatus.Valid,
                stopwatch.Elapsed,
                notes.Count == 0 ? null : string.Join(Environment.NewLine, notes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            stopwatch.Stop();
            return Error(stopwatch.Elapsed, ToUserMessage(exception));
        }
    }

    private SignatureBranchResult VerifySignature(
        VerificationRequest request,
        byte[]? fileBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.SignaturePath) ||
            !File.Exists(request.SignaturePath))
        {
            return new(
                SignatureStatus.Missing,
                false,
                null,
                null,
                null,
                null,
                "Файл цифровой подписи не найден.");
        }

        SignatureDocument document;
        try
        {
            document = documentService.Load(request.SignaturePath);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or FormatException)
        {
            Debug.WriteLine(exception);
            return new(
                SignatureStatus.FormatError,
                false,
                null,
                null,
                null,
                null,
                ToUserMessage(exception));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Debug.WriteLine(exception);
            return new(
                SignatureStatus.Error,
                false,
                null,
                null,
                null,
                null,
                ToUserMessage(exception));
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(document.SignatureBase64);
        }
        catch (FormatException exception)
        {
            Debug.WriteLine(exception);
            return new(
                SignatureStatus.FormatError,
                false,
                document.Sha256,
                null,
                document.PublicKeyFingerprint,
                document.WatermarkDelta,
                "Файл .igsig содержит некорректную подпись Base64.");
        }

        if (string.IsNullOrWhiteSpace(request.PublicKeyPath) ||
            !File.Exists(request.PublicKeyPath))
        {
            return new(
                SignatureStatus.NotChecked,
                false,
                document.Sha256,
                null,
                document.PublicKeyFingerprint,
                document.WatermarkDelta,
                "Цифровая подпись не проверена: открытый RSA-ключ не найден.");
        }

        try
        {
            using var publicKey = keyService.LoadPublicKey(request.PublicKeyPath);
            var actualFingerprint = keyService.GetFingerprint(publicKey);
            if (!actualFingerprint.Equals(
                    document.PublicKeyFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    SignatureStatus.NotChecked,
                    false,
                    document.Sha256,
                    actualFingerprint,
                    document.PublicKeyFingerprint,
                    document.WatermarkDelta,
                    "Цифровая подпись не проверена: fingerprint выбранного открытого ключа не совпадает с fingerprint в .igsig.");
            }

            if (fileBytes is null)
            {
                return new(
                    SignatureStatus.Error,
                    false,
                    document.Sha256,
                    actualFingerprint,
                    document.PublicKeyFingerprint,
                    document.WatermarkDelta,
                    "Не удалось прочитать точные байты проверяемого изображения.");
            }

            var isValid = signatureService.Verify(fileBytes, signatureBytes, publicKey);
            return new(
                isValid ? SignatureStatus.Valid : SignatureStatus.Invalid,
                isValid,
                document.Sha256,
                actualFingerprint,
                document.PublicKeyFingerprint,
                document.WatermarkDelta,
                isValid
                    ? null
                    : "RSA PKCS#1 v1.5 подпись проверяемых байтов недействительна.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Debug.WriteLine(exception);
            return new(
                SignatureStatus.Error,
                false,
                document.Sha256,
                null,
                document.PublicKeyFingerprint,
                document.WatermarkDelta,
                ToUserMessage(exception));
        }
    }

    private WatermarkExtractionResult ExtractWatermark(
        VerificationRequest request,
        WatermarkSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = imageFileService.Load(request.ImagePath);
            return watermarkService.Extract(
                image,
                settings,
                progress,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Debug.WriteLine(exception);
            return new(
                WatermarkStatus.Error,
                null,
                $"Не удалось извлечь цифровой водяной знак: {ToUserMessage(exception)}");
        }
    }

    private static OverallVerificationStatus DetermineOverallStatus(
        SignatureStatus signature,
        WatermarkStatus watermark)
    {
        if (signature == SignatureStatus.Valid)
        {
            return watermark == WatermarkStatus.Valid
                ? OverallVerificationStatus.Authentic
                : OverallVerificationStatus.Inconsistent;
        }

        if (signature == SignatureStatus.Invalid)
        {
            return watermark switch
            {
                WatermarkStatus.Valid => OverallVerificationStatus.ModifiedWatermarkPreserved,
                WatermarkStatus.NotFound =>
                    OverallVerificationStatus.ModifiedWatermarkNotFound,
                _ => OverallVerificationStatus.Inconsistent
            };
        }

        return signature == SignatureStatus.Error && watermark == WatermarkStatus.Error
            ? OverallVerificationStatus.Error
            : OverallVerificationStatus.Inconsistent;
    }

    private static VerificationResult Error(TimeSpan elapsed, string message) =>
        new(
            SignatureStatus.Error,
            WatermarkStatus.Error,
            OverallVerificationStatus.Error,
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            elapsed,
            message);

    private static string ToUserMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Нет доступа к выбранному файлу.",
        FormatException => "Файл .igsig содержит некорректную подпись Base64.",
        InvalidDataException => exception.Message,
        NotSupportedException => exception.Message,
        CryptographicException => "Не удалось прочитать открытый RSA-ключ.",
        IOException => $"Ошибка чтения файла: {exception.Message}",
        _ => exception.Message
    };

    private sealed record SignatureBranchResult(
        SignatureStatus Status,
        bool IsValid,
        string? StoredSha256,
        string? ActualFingerprint,
        string? ExpectedFingerprint,
        double? WatermarkDelta,
        string? Message);
}
