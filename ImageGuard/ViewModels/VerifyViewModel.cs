using ImageGuard.Enums;
using ImageGuard.Models;
using ImageGuard.Services;

namespace ImageGuard.ViewModels;

public sealed class VerifyViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly IVerificationService _verification;
    private CancellationTokenSource? _cancellationTokenSource;
    private string _imagePath = string.Empty;
    private string _signaturePath = string.Empty;
    private string _publicKeyPath = string.Empty;
    private string _statusText = "Выберите изображение, подпись и открытый ключ.";
    private string _signatureText = "Не проверена";
    private string _watermarkText = "Не проверен";
    private string _overallText = string.Empty;
    private string _extractedWatermark = string.Empty;
    private string _currentSha256 = string.Empty;
    private string _storedSha256 = string.Empty;
    private string _actualFingerprint = string.Empty;
    private string _expectedFingerprint = string.Empty;
    private string _fingerprintMatchText = string.Empty;
    private string _details = string.Empty;
    private string _processingTime = string.Empty;
    private double _progressValue;
    private bool _isBusy;
    private OverallVerificationStatus _overallStatus = OverallVerificationStatus.Error;

    public VerifyViewModel(IFileDialogService dialogs, IVerificationService verification)
    {
        _dialogs = dialogs;
        _verification = verification;
        SelectImageCommand = new RelayCommand(
            () => SetSelectedPath(_dialogs.OpenImage(), value => ImagePath = value),
            () => !IsBusy);
        SelectSignatureCommand = new RelayCommand(
            () => SetSelectedPath(_dialogs.OpenSignature(), value => SignaturePath = value),
            () => !IsBusy);
        SelectPublicKeyCommand = new RelayCommand(
            () => SetSelectedPath(_dialogs.OpenPem(), value => PublicKeyPath = value),
            () => !IsBusy);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, CanVerify, SetUnexpectedError);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public RelayCommand SelectImageCommand { get; }
    public RelayCommand SelectSignatureCommand { get; }
    public RelayCommand SelectPublicKeyCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }
    public RelayCommand CancelCommand { get; }
    public WatermarkSettings WatermarkSettings { get; } = new();

    public string ImagePath
    {
        get => _imagePath;
        private set
        {
            if (SetProperty(ref _imagePath, value))
            {
                ResetResult();
                RefreshCommands();
            }
        }
    }

    public string SignaturePath
    {
        get => _signaturePath;
        private set
        {
            if (SetProperty(ref _signaturePath, value))
            {
                ResetResult();
                RefreshCommands();
            }
        }
    }

    public string PublicKeyPath
    {
        get => _publicKeyPath;
        private set
        {
            if (SetProperty(ref _publicKeyPath, value))
            {
                ResetResult();
                RefreshCommands();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SignatureText
    {
        get => _signatureText;
        private set => SetProperty(ref _signatureText, value);
    }

    public string WatermarkText
    {
        get => _watermarkText;
        private set => SetProperty(ref _watermarkText, value);
    }

    public string OverallText
    {
        get => _overallText;
        private set => SetProperty(ref _overallText, value);
    }

    public string ExtractedWatermark
    {
        get => _extractedWatermark;
        private set => SetProperty(ref _extractedWatermark, value);
    }

    public string CurrentSha256
    {
        get => _currentSha256;
        private set => SetProperty(ref _currentSha256, value);
    }

    public string StoredSha256
    {
        get => _storedSha256;
        private set => SetProperty(ref _storedSha256, value);
    }

    public string ActualFingerprint
    {
        get => _actualFingerprint;
        private set => SetProperty(ref _actualFingerprint, value);
    }

    public string ExpectedFingerprint
    {
        get => _expectedFingerprint;
        private set => SetProperty(ref _expectedFingerprint, value);
    }

    public string Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    public string FingerprintMatchText
    {
        get => _fingerprintMatchText;
        private set => SetProperty(ref _fingerprintMatchText, value);
    }

    public string ProcessingTime
    {
        get => _processingTime;
        private set => SetProperty(ref _processingTime, value);
    }

    public OverallVerificationStatus OverallStatus
    {
        get => _overallStatus;
        private set => SetProperty(ref _overallStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    private async Task VerifyAsync()
    {
        IsBusy = true;
        Details = string.Empty;
        ProgressValue = 0;
        _cancellationTokenSource?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellationTokenSource = cancellation;
        var statusProgress = new Progress<string>(message => StatusText = message);
        var operationProgress = new Progress<double>(
            value => ProgressValue = Math.Clamp(value, 0, 1) * 100);
        try
        {
            var result = await _verification.VerifyAsync(
                new(ImagePath, SignaturePath, PublicKeyPath, WatermarkSettings),
                statusProgress,
                operationProgress,
                cancellation.Token);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Операция отменена.";
            Details = "Операция отменена пользователем.";
        }
        finally
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellation))
            {
                _cancellationTokenSource = null;
            }

            cancellation.Dispose();
            IsBusy = false;
        }
    }

    private void ApplyResult(VerificationResult result)
    {
        SignatureText = result.SignatureStatus switch
        {
            SignatureStatus.Valid => "✓ Действительна",
            SignatureStatus.Invalid => "✕ Недействительна",
            SignatureStatus.Missing => "✕ Отсутствует",
            SignatureStatus.NotChecked => "— Не проверена",
            SignatureStatus.FormatError => "⚠ Ошибка формата",
            _ => "⚠ Ошибка проверки"
        };
        WatermarkText = result.WatermarkStatus switch
        {
            WatermarkStatus.Valid => "✓ Обнаружен",
            WatermarkStatus.NotFound => "✕ Не обнаружен",
            _ => "⚠ Ошибка извлечения"
        };
        OverallStatus = result.OverallStatus;
        OverallText = result.OverallMessage;
        ExtractedWatermark = result.ExtractedWatermark ?? "—";
        CurrentSha256 = result.CurrentSha256 ?? "—";
        StoredSha256 = result.StoredSha256 ?? "—";
        ActualFingerprint = result.PublicKeyFingerprint ?? "—";
        ExpectedFingerprint = result.ExpectedKeyFingerprint ?? "—";
        FingerprintMatchText =
            result.PublicKeyFingerprint is null || result.ExpectedKeyFingerprint is null
                ? "N/A"
                : result.PublicKeyFingerprint.Equals(
                    result.ExpectedKeyFingerprint,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Совпадает"
                    : "Не совпадает";
        ProcessingTime = $"{result.ProcessingTime.TotalMilliseconds:F0} мс";
        Details = result.ErrorMessage ?? string.Empty;
    }

    private bool CanVerify() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ImagePath) &&
        !string.IsNullOrWhiteSpace(SignaturePath) &&
        !string.IsNullOrWhiteSpace(PublicKeyPath);

    private void RefreshCommands()
    {
        SelectImageCommand.NotifyCanExecuteChanged();
        SelectSignatureCommand.NotifyCanExecuteChanged();
        SelectPublicKeyCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        StatusText = "Отмена операции...";
        _cancellationTokenSource?.Cancel();
    }

    private void ResetResult()
    {
        SignatureText = "Не проверена";
        WatermarkText = "Не проверен";
        OverallText = string.Empty;
        ExtractedWatermark = string.Empty;
        CurrentSha256 = string.Empty;
        StoredSha256 = string.Empty;
        ActualFingerprint = string.Empty;
        ExpectedFingerprint = string.Empty;
        FingerprintMatchText = string.Empty;
        ProcessingTime = string.Empty;
        Details = string.Empty;
        StatusText = "Файлы изменены. Запустите проверку повторно.";
        OverallStatus = OverallVerificationStatus.Error;
    }

    private static void SetSelectedPath(string? path, Action<string> setter)
    {
        if (path is not null)
        {
            setter(path);
        }
    }

    private void SetUnexpectedError(Exception exception)
    {
        IsBusy = false;
        Details = exception.Message;
    }
}
