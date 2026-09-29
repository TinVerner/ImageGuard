using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using ImageGuard.Enums;
using ImageGuard.Models;
using ImageGuard.Services;
using ImageGuard.Services.Imaging;
using ImageGuard.Services.Verification;
using ImageGuard.Services.Watermarking;

namespace ImageGuard.ViewModels;

public sealed class ProtectViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly IImageFileService _images;
    private readonly IWatermarkService _watermarks;
    private readonly IProtectionService _protection;
    private CancellationTokenSource? _cancellationTokenSource;
    private string _inputImagePath = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _privateKeyPassword = string.Empty;
    private string _outputImagePath = string.Empty;
    private string _signatureOutputPath = string.Empty;
    private string _watermarkText = string.Empty;
    private ImageOutputFormat _outputFormat = ImageOutputFormat.Png;
    private int _jpegQuality = 95;
    private BitmapSource? _sourcePreview;
    private BitmapSource? _protectedPreview;
    private string _imageInfo = "Изображение не выбрано";
    private string _capacityInfo = "Выберите изображение и введите текст.";
    private string _statusText = "Готово к работе";
    private string _resultText = string.Empty;
    private string _sha256 = string.Empty;
    private string _mseText = string.Empty;
    private string _psnrText = string.Empty;
    private string _processingTime = string.Empty;
    private double _progressValue;
    private bool _watermarkFits;
    private bool _isBusy;

    public ProtectViewModel(
        IFileDialogService dialogs,
        IImageFileService images,
        IWatermarkService watermarks,
        IProtectionService protection)
    {
        _dialogs = dialogs;
        _images = images;
        _watermarks = watermarks;
        _protection = protection;

        SelectImageCommand = new RelayCommand(SelectImage, () => !IsBusy);
        SelectPrivateKeyCommand = new RelayCommand(SelectPrivateKey, () => !IsBusy);
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !IsBusy);
        ProtectCommand = new AsyncRelayCommand(ProtectAsync, CanProtect, SetUnexpectedError);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public WatermarkSettings WatermarkSettings { get; } = new();
    public Array OutputFormats { get; } = Enum.GetValues<ImageOutputFormat>();

    public RelayCommand SelectImageCommand { get; }
    public RelayCommand SelectPrivateKeyCommand { get; }
    public RelayCommand SelectOutputCommand { get; }
    public AsyncRelayCommand ProtectCommand { get; }
    public RelayCommand CancelCommand { get; }

    public string InputImagePath
    {
        get => _inputImagePath;
        private set => SetProperty(ref _inputImagePath, value);
    }

    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        private set
        {
            if (SetProperty(ref _privateKeyPath, value))
            {
                RefreshCommands();
            }
        }
    }

    public string PrivateKeyPassword
    {
        get => _privateKeyPassword;
        set => SetProperty(ref _privateKeyPassword, value);
    }

    public string OutputImagePath
    {
        get => _outputImagePath;
        private set
        {
            if (SetProperty(ref _outputImagePath, value))
            {
                RefreshCommands();
            }
        }
    }

    public string SignatureOutputPath
    {
        get => _signatureOutputPath;
        private set
        {
            if (SetProperty(ref _signatureOutputPath, value))
            {
                RefreshCommands();
            }
        }
    }

    public string WatermarkText
    {
        get => _watermarkText;
        set
        {
            if (SetProperty(ref _watermarkText, value))
            {
                UpdateCapacity();
                OnPropertyChanged(nameof(WatermarkByteCount));
                RefreshCommands();
            }
        }
    }

    public int WatermarkByteCount => Encoding.UTF8.GetByteCount(WatermarkText);

    public ImageOutputFormat OutputFormat
    {
        get => _outputFormat;
        set
        {
            if (SetProperty(ref _outputFormat, value))
            {
                OnPropertyChanged(nameof(IsJpeg));
                OutputImagePath = string.Empty;
                SignatureOutputPath = string.Empty;
                ResetResult();
                RefreshCommands();
            }
        }
    }

    public bool IsJpeg => OutputFormat == ImageOutputFormat.Jpeg;

    public int JpegQuality
    {
        get => _jpegQuality;
        set
        {
            if (SetProperty(ref _jpegQuality, value))
            {
                RefreshCommands();
            }
        }
    }

    public BitmapSource? SourcePreview
    {
        get => _sourcePreview;
        private set => SetProperty(ref _sourcePreview, value);
    }

    public BitmapSource? ProtectedPreview
    {
        get => _protectedPreview;
        private set => SetProperty(ref _protectedPreview, value);
    }

    public string ImageInfo
    {
        get => _imageInfo;
        private set => SetProperty(ref _imageInfo, value);
    }

    public string CapacityInfo
    {
        get => _capacityInfo;
        private set => SetProperty(ref _capacityInfo, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string Sha256
    {
        get => _sha256;
        private set => SetProperty(ref _sha256, value);
    }

    public string PsnrText
    {
        get => _psnrText;
        private set => SetProperty(ref _psnrText, value);
    }

    public string MseText
    {
        get => _mseText;
        private set => SetProperty(ref _mseText, value);
    }

    public string ProcessingTime
    {
        get => _processingTime;
        private set => SetProperty(ref _processingTime, value);
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

    private void SelectImage()
    {
        var path = _dialogs.OpenImage();
        if (path is null)
        {
            return;
        }

        try
        {
            var image = _images.Load(path);
            var info = _images.GetImageInfo(path);
            InputImagePath = path;
            SourcePreview = image.ToBitmapSource();
            ResetResult();
            ImageInfo =
                $"{info.FileName} · {info.Width}×{info.Height} · {info.Format} · {FormatSize(info.FileSize)}";
            UpdateCapacity();
            RefreshCommands();
        }
        catch (Exception exception)
        {
            ResultText = exception.Message;
        }
    }

    private void SelectPrivateKey()
    {
        var path = _dialogs.OpenPem();
        if (path is not null)
        {
            PrivateKeyPath = path;
        }
    }

    private void SelectOutput()
    {
        var baseName = string.IsNullOrWhiteSpace(InputImagePath)
            ? "protected"
            : $"{Path.GetFileNameWithoutExtension(InputImagePath)}_protected";
        var imagePath = _dialogs.SaveImage(
            baseName + (IsJpeg ? ".jpg" : ".png"),
            IsJpeg);
        if (imagePath is null)
        {
            return;
        }

        OutputImagePath = imagePath;
        SignatureOutputPath = Path.ChangeExtension(imagePath, ".igsig");
    }

    private async Task ProtectAsync()
    {
        IsBusy = true;
        ResultText = string.Empty;
        Sha256 = string.Empty;
        MseText = string.Empty;
        PsnrText = string.Empty;
        ProgressValue = 0;
        _cancellationTokenSource?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellationTokenSource = cancellation;
        var statusProgress = new Progress<string>(message => StatusText = message);
        var operationProgress = new Progress<double>(
            value => ProgressValue = Math.Clamp(value, 0, 1) * 100);
        try
        {
            var result = await _protection.ProtectAsync(
                new(
                    InputImagePath,
                    OutputImagePath,
                    WatermarkText,
                    WatermarkSettings,
                    PrivateKeyPath,
                    PrivateKeyPassword,
                    SignatureOutputPath,
                    OutputFormat,
                    JpegQuality),
                statusProgress,
                operationProgress,
                cancellation.Token);

            ProcessingTime = $"{result.ProcessingTime.TotalMilliseconds:F0} мс";
            if (!result.Success)
            {
                ResultText = result.ErrorMessage ?? "Не удалось защитить изображение.";
                return;
            }

            ProtectedPreview = _images.Load(OutputImagePath).ToBitmapSource();
            Sha256 = result.Sha256 ?? string.Empty;
            MseText = result.Mse is null ? "N/A" : $"{result.Mse:F4}";
            PsnrText = result.Psnr is double.PositiveInfinity
                ? "Без искажений (∞ dB)"
                : $"{result.Psnr:F2} dB";
            ResultText = result.WatermarkValidAfterSave
                ? $"Защищенное изображение и подпись сохранены.\nWatermark после сохранения извлекается корректно."
                : result.ErrorMessage ?? "Контрольное извлечение watermark не удалось.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Операция отменена.";
            ResultText = "Операция отменена пользователем.";
        }
        finally
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellation))
            {
                _cancellationTokenSource = null;
            }

            cancellation.Dispose();
            PrivateKeyPassword = string.Empty;
            IsBusy = false;
        }
    }

    private bool CanProtect() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(InputImagePath) &&
        !string.IsNullOrWhiteSpace(WatermarkText) &&
        !string.IsNullOrWhiteSpace(PrivateKeyPath) &&
        !string.IsNullOrWhiteSpace(OutputImagePath) &&
        !string.IsNullOrWhiteSpace(SignatureOutputPath) &&
        _watermarkFits &&
        (!IsJpeg || JpegQuality is >= 1 and <= 100);

    private void UpdateCapacity()
    {
        if (string.IsNullOrWhiteSpace(InputImagePath) || string.IsNullOrWhiteSpace(WatermarkText))
        {
            _watermarkFits = false;
            CapacityInfo = "Выберите изображение и введите текст.";
            RefreshCommands();
            return;
        }

        try
        {
            var image = _images.Load(InputImagePath);
            var capacity = _watermarks.CalculateCapacity(image, WatermarkText);
            _watermarkFits = capacity.Fits;
            CapacityInfo =
                $"Доступно блоков: {capacity.CapacityBits:N0}; требуется: {capacity.RequiredBits:N0}; " +
                $"полезная емкость: {capacity.CapacityPayloadBytes:N0} байт. " +
                (capacity.Fits ? "Текст помещается." : "Изображение недостаточно большое.");
        }
        catch (Exception exception)
        {
            _watermarkFits = false;
            CapacityInfo = exception.Message;
        }

        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectImageCommand.NotifyCanExecuteChanged();
        SelectPrivateKeyCommand.NotifyCanExecuteChanged();
        SelectOutputCommand.NotifyCanExecuteChanged();
        ProtectCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        StatusText = "Отмена операции...";
        _cancellationTokenSource?.Cancel();
    }

    private void ResetResult()
    {
        ProtectedPreview = null;
        ResultText = string.Empty;
        Sha256 = string.Empty;
        MseText = string.Empty;
        PsnrText = string.Empty;
        ProcessingTime = string.Empty;
    }

    private void SetUnexpectedError(Exception exception)
    {
        IsBusy = false;
        ResultText = exception.Message;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F2} МБ"
            : $"{bytes / 1024d:F1} КБ";
}
