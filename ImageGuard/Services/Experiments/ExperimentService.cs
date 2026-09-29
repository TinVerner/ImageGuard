using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ImageGuard.Enums;
using ImageGuard.Helpers;
using ImageGuard.Models;
using ImageGuard.Services.Cryptography;
using ImageGuard.Services.Imaging;
using ImageGuard.Services.Transformations;
using ImageGuard.Services.Verification;
using ImageGuard.Services.Watermarking;

namespace ImageGuard.Services.Experiments;

public interface IExperimentService
{
    Task<IReadOnlyList<ExperimentResult>> RunAsync(
        ExperimentRequest request,
        IProgress<string>? progress = null,
        IProgress<double>? operationProgress = null,
        CancellationToken cancellationToken = default);

    void ExportCsv(IReadOnlyList<ExperimentResult> results, string path);
}

public sealed class ExperimentService(
    IImageFileService imageFileService,
    IImageQualityMetricsService metricsService,
    IVerificationService verificationService,
    IWatermarkTextCodec textCodec,
    IWatermarkService watermarkService,
    ISignatureDocumentService documentService,
    IEnumerable<IImageTransformation> transformations) : IExperimentService
{
    private readonly IReadOnlyDictionary<ImageTransformationType, IImageTransformation> _transformations =
        transformations.ToDictionary(item => item.Type);

    public Task<IReadOnlyList<ExperimentResult>> RunAsync(
        ExperimentRequest request,
        IProgress<string>? progress = null,
        IProgress<double>? operationProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => Run(request, progress, operationProgress, cancellationToken),
            cancellationToken);
    }

    public void ExportCsv(IReadOnlyList<ExperimentResult> results, string path)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Не указан путь CSV.", nameof(path));
        }

        var builder = new StringBuilder();
        builder.AppendLine(
            "Transformation,Parameters,SignatureStatus,SignatureValid,WatermarkStatus," +
            "ExtractedWatermark,BER,MSE,PSNR,ProcessingMilliseconds,OutputFile,Error");
        foreach (var result in results)
        {
            builder.AppendLine(string.Join(',',
                Csv(result.TransformationName),
                Csv(result.Parameters),
                result.SignatureStatus,
                result.SignatureValid.ToString(CultureInfo.InvariantCulture),
                result.WatermarkStatus,
                Csv(result.ExtractedWatermark),
                result.WatermarkBitErrorRate?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                result.Mse?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                result.Psnr?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                result.ProcessingTime.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                Csv(result.OutputFilePath),
                Csv(result.ErrorMessage)));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private IReadOnlyList<ExperimentResult> Run(
        ExperimentRequest request,
        IProgress<string>? progress,
        IProgress<double>? operationProgress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        operationProgress?.Report(0);
        Directory.CreateDirectory(request.OutputDirectory);
        var source = imageFileService.Load(request.ImagePath);
        var extractionSettings = ResolveExtractionSettings(request);
        var results = new List<ExperimentResult>(request.Cases.Count);

        for (var index = 0; index < request.Cases.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var experimentCase = request.Cases[index];
            var stopwatch = Stopwatch.StartNew();
            var outputPath = string.Empty;

            try
            {
                if (!_transformations.TryGetValue(experimentCase.TransformationType, out var transformation))
                {
                    throw new NotSupportedException(
                        $"Преобразование {experimentCase.TransformationType} не зарегистрировано.");
                }

                progress?.Report(
                    $"Эксперимент {index + 1} из {request.Cases.Count}: {transformation.DisplayName}...");
                var transformed = transformation.Apply(
                    source,
                    experimentCase.Parameters,
                    cancellationToken);
                outputPath = BuildOutputPath(
                    request,
                    experimentCase,
                    transformation,
                    transformed.OutputFormat,
                    index);
                imageFileService.Save(
                    transformed.Image,
                    outputPath,
                    transformed.OutputFormat,
                    transformed.JpegQuality);

                // Повторное чтение гарантирует, что метрики и watermark относятся
                // к фактическим пикселям сохраненного PNG/JPEG.
                var saved = imageFileService.Load(outputPath);
                var quality = source.Width == saved.Width && source.Height == saved.Height
                    ? metricsService.Calculate(source, saved)
                    : null;

                var verification = verificationService.VerifyAsync(
                        new(
                            outputPath,
                            request.SignaturePath,
                            request.PublicKeyPath,
                            request.WatermarkSettings),
                        cancellationToken: cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                var ber = CalculateBer(
                    request.ExpectedWatermarkText,
                    saved,
                    extractionSettings,
                    cancellationToken);
                stopwatch.Stop();

                results.Add(new(
                    experimentCase.Label ?? transformation.DisplayName,
                    transformation.Describe(experimentCase.Parameters),
                    verification.SignatureStatus,
                    verification.SignatureValid,
                    verification.WatermarkStatus,
                    verification.ExtractedWatermark,
                    ber,
                    quality?.Mse,
                    quality?.Psnr,
                    stopwatch.Elapsed,
                    outputPath,
                    verification.ErrorMessage));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                stopwatch.Stop();
                results.Add(new(
                    experimentCase.Label ?? experimentCase.TransformationType.ToString(),
                    DescribeSafely(experimentCase),
                    SignatureStatus.Error,
                    false,
                    WatermarkStatus.Error,
                    null,
                    null,
                    null,
                    null,
                    stopwatch.Elapsed,
                    outputPath,
                    exception.Message));
            }

            operationProgress?.Report((index + 1.0) / request.Cases.Count);
        }

        progress?.Report($"Эксперименты завершены: {results.Count}.");
        operationProgress?.Report(1);
        return results;
    }

    private double? CalculateBer(
        string? expectedText,
        ImagePixelData image,
        WatermarkSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(expectedText))
        {
            return null;
        }

        try
        {
            var expected = BitSequenceConverter.ToBitsMsbFirst(
                textCodec.Encode(expectedText));
            var actual = watermarkService.ExtractRawBits(
                image,
                settings,
                expected.Length,
                cancellationToken);
            return BitErrorRateCalculator.Calculate(expected, actual);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private WatermarkSettings ResolveExtractionSettings(ExperimentRequest request)
    {
        try
        {
            var document = documentService.Load(request.SignaturePath);
            return new WatermarkSettings { Delta = document.WatermarkDelta };
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return request.WatermarkSettings;
        }
    }

    private static string BuildOutputPath(
        ExperimentRequest request,
        ExperimentCase experimentCase,
        IImageTransformation transformation,
        ImageOutputFormat format,
        int index)
    {
        var sourceName = Path.GetFileNameWithoutExtension(request.ImagePath);
        var label = experimentCase.Label ?? transformation.DisplayName;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            label = label.Replace(invalid, '_');
        }

        label = string.Concat(label.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var extension = format == ImageOutputFormat.Jpeg ? ".jpg" : ".png";
        return Path.Combine(
            request.OutputDirectory,
            $"{sourceName}_{index + 1:D2}_{label}{extension}");
    }

    private string DescribeSafely(ExperimentCase experimentCase)
    {
        try
        {
            return _transformations.TryGetValue(experimentCase.TransformationType, out var transformation)
                ? transformation.Describe(experimentCase.Parameters)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static void ValidateRequest(ExperimentRequest request)
    {
        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException("Защищенное изображение не найдено.", request.ImagePath);
        }

        if (!File.Exists(request.SignaturePath))
        {
            throw new FileNotFoundException("Файл .igsig не найден.", request.SignaturePath);
        }

        if (!File.Exists(request.PublicKeyPath))
        {
            throw new FileNotFoundException("Открытый RSA-ключ не найден.", request.PublicKeyPath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new ArgumentException("Не выбрана папка результатов.");
        }

        if (request.Cases.Count == 0)
        {
            throw new ArgumentException("Не задано ни одного эксперимента.");
        }
    }
}
