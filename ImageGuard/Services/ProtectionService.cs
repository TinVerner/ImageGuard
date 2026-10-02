using System.Diagnostics;
using System.IO;
using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed class ProtectionService(
    ImageService imageService,
    WatermarkService watermarkService,
    CryptoService cryptoService)
{
    public Task<ProtectionResult> ProtectAsync(
        ProtectionRequest request,
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => Protect(request, status, progress, cancellationToken),
            cancellationToken);
    }

    private ProtectionResult Protect(
        ProtectionRequest request,
        IProgress<string>? status,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            progress?.Report(0);
            ValidateRequest(request);
            cancellationToken.ThrowIfCancellationRequested();

            status?.Report("Загрузка изображения...");
            var original = imageService.Load(request.InputImagePath);
            progress?.Report(0.1);
            cancellationToken.ThrowIfCancellationRequested();

            status?.Report("Встраивание цифрового водяного знака...");
            var embedProgress = progress is null
                ? null
                : new Progress<double>(value => progress.Report(0.1 + value * 0.45));
            var embedded = watermarkService.Embed(
                original,
                request.WatermarkText,
                request.WatermarkSettings,
                embedProgress,
                cancellationToken: cancellationToken);
            progress?.Report(0.55);
            cancellationToken.ThrowIfCancellationRequested();

            status?.Report("Сохранение защищенного изображения...");
            imageService.Save(embedded.Image, request.OutputImagePath);
            progress?.Report(0.65);
            cancellationToken.ThrowIfCancellationRequested();

            // Контрольное извлечение выполняется после повторного чтения сохраненного PNG.
            status?.Report("Контроль сохраненного изображения...");
            var savedImage = imageService.Load(request.OutputImagePath);
            var extractProgress = progress is null
                ? null
                : new Progress<double>(value => progress.Report(0.65 + value * 0.15));
            var extraction = watermarkService.Extract(
                savedImage,
                request.WatermarkSettings,
                extractProgress,
                cancellationToken: cancellationToken);
            var watermarkMatches =
                extraction.Status == WatermarkStatus.Valid &&
                string.Equals(
                    extraction.Text,
                    request.WatermarkText,
                    StringComparison.Ordinal);
            progress?.Report(0.82);
            cancellationToken.ThrowIfCancellationRequested();

            status?.Report("Формирование цифровой подписи...");
            // Критический порядок: подписываются точные байты уже сохраненного файла,
            // а не BitmapSource или промежуточный пиксельный буфер.
            var savedBytes = File.ReadAllBytes(request.OutputImagePath);
            var sha256 = cryptoService.ComputeSha256Hex(savedBytes);
            using var privateKey = cryptoService.LoadPrivateKey(request.PrivateKeyPath);
            var signature = cryptoService.Sign(savedBytes, privateKey);
            progress?.Report(0.95);
            cancellationToken.ThrowIfCancellationRequested();

            var document = new SignatureDocument
            {
                KeySize = privateKey.KeySize,
                CreatedUtc = DateTimeOffset.UtcNow,
                ProtectedFileName = Path.GetFileName(request.OutputImagePath),
                Sha256 = sha256,
                SignatureBase64 = Convert.ToBase64String(signature),
                PublicKeyFingerprint = cryptoService.GetFingerprint(privateKey),
                WatermarkDelta = request.WatermarkSettings.Delta
            };

            cryptoService.Save(document, request.SignatureOutputPath);
            progress?.Report(1);
            stopwatch.Stop();
            status?.Report("Защита изображения завершена.");
            return new(
                true,
                request.OutputImagePath,
                request.SignatureOutputPath,
                sha256,
                watermarkMatches,
                stopwatch.Elapsed,
                watermarkMatches
                    ? null
                    : "Подпись создана, но контрольное извлечение после сохранения не подтвердило исходный watermark: " +
                      (extraction.ErrorMessage ?? "извлеченный текст не совпадает с исходным."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            stopwatch.Stop();
            return ProtectionResult.Failure(stopwatch.Elapsed, ToUserMessage(exception));
        }
    }

    private static void ValidateRequest(ProtectionRequest request)
    {
        if (!File.Exists(request.InputImagePath))
            throw new FileNotFoundException("Исходное изображение не найдено.", request.InputImagePath);

        if (!File.Exists(request.PrivateKeyPath))
            throw new FileNotFoundException("Закрытый RSA-ключ не найден.", request.PrivateKeyPath);

        if (string.IsNullOrWhiteSpace(request.WatermarkText))
            throw new ArgumentException("Введите текст цифрового водяного знака.");

        if (string.IsNullOrWhiteSpace(request.OutputImagePath) ||
            string.IsNullOrWhiteSpace(request.SignatureOutputPath))
            throw new ArgumentException("Укажите пути защищенного изображения и файла подписи.");

        if (Path.GetFullPath(request.InputImagePath).Equals(
                Path.GetFullPath(request.OutputImagePath),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Нельзя перезаписывать исходное изображение. Выберите другой путь.");

        if (Path.GetFullPath(request.OutputImagePath).Equals(
                Path.GetFullPath(request.SignatureOutputPath),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Изображение и подпись должны сохраняться в разные файлы.");

        request.WatermarkSettings.Validate();
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Нет доступа к выбранному файлу или папке.",
        IOException => $"Ошибка чтения или записи: {exception.Message}",
        System.Security.Cryptography.CryptographicException =>
            "Не удалось открыть закрытый RSA-ключ. Проверьте выбранный файл.",
        _ => exception.Message
    };
}
