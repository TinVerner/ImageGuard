using System.Collections.ObjectModel;
using ImageGuard.Enums;
using ImageGuard.Models;
using ImageGuard.Services;
using ImageGuard.Services.Experiments;

namespace ImageGuard.ViewModels;

public sealed record TransformationOption(ImageTransformationType Type, string DisplayName);

public sealed class ResearchViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly IExperimentService _experiments;
    private CancellationTokenSource? _cancellationTokenSource;
    private string _imagePath = string.Empty;
    private string _signaturePath = string.Empty;
    private string _publicKeyPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _expectedWatermark = string.Empty;
    private TransformationOption _selectedTransformation;
    private double _parameterValue;
    private int _seed = 42;
    private bool _restoreOriginalSize = true;
    private int _regionX;
    private int _regionY;
    private int _regionWidth = 64;
    private int _regionHeight = 64;
    private byte _regionRed = 220;
    private byte _regionGreen = 30;
    private byte _regionBlue = 30;
    private string _statusText = "Выберите защищенное изображение и параметры исследования.";
    private double _progressValue;
    private bool _isBusy;

    public ResearchViewModel(IFileDialogService dialogs, IExperimentService experiments)
    {
        _dialogs = dialogs;
        _experiments = experiments;
        Transformations =
        [
            new(ImageTransformationType.JpegCompression, "JPEG-сжатие"),
            new(ImageTransformationType.Brightness, "Яркость"),
            new(ImageTransformationType.Contrast, "Контрастность"),
            new(ImageTransformationType.GaussianNoise, "Гауссов шум"),
            new(ImageTransformationType.Resize, "Изменение размера"),
            new(ImageTransformationType.Crop, "Обрезка"),
            new(ImageTransformationType.RegionModification, "Изменение области")
        ];
        _selectedTransformation = Transformations[0];
        SetDefaultParameter();

        SelectImageCommand = new RelayCommand(
            () => SelectPath(_dialogs.OpenImage(), value => ImagePath = value),
            () => !IsBusy);
        SelectSignatureCommand = new RelayCommand(
            () => SelectPath(_dialogs.OpenSignature(), value => SignaturePath = value),
            () => !IsBusy);
        SelectPublicKeyCommand = new RelayCommand(
            () => SelectPath(_dialogs.OpenPem(), value => PublicKeyPath = value),
            () => !IsBusy);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !IsBusy);
        RunExperimentCommand = new AsyncRelayCommand(
            () => RunAsync(usePresetSeries: false),
            CanRun,
            SetError);
        RunPresetSeriesCommand = new AsyncRelayCommand(
            () => RunAsync(usePresetSeries: true),
            CanRun,
            SetError);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Results.Count > 0 && !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public IReadOnlyList<TransformationOption> Transformations { get; }
    public ObservableCollection<ExperimentResult> Results { get; } = [];
    public WatermarkSettings WatermarkSettings { get; } = new();

    public RelayCommand SelectImageCommand { get; }
    public RelayCommand SelectSignatureCommand { get; }
    public RelayCommand SelectPublicKeyCommand { get; }
    public RelayCommand SelectOutputDirectoryCommand { get; }
    public AsyncRelayCommand RunExperimentCommand { get; }
    public AsyncRelayCommand RunPresetSeriesCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand CancelCommand { get; }

    public string ImagePath
    {
        get => _imagePath;
        private set
        {
            if (SetProperty(ref _imagePath, value))
            {
                ResetResults();
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
                ResetResults();
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
                ResetResults();
                RefreshCommands();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        private set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                ResetResults();
                RefreshCommands();
            }
        }
    }

    public string ExpectedWatermark
    {
        get => _expectedWatermark;
        set => SetProperty(ref _expectedWatermark, value);
    }

    public TransformationOption SelectedTransformation
    {
        get => _selectedTransformation;
        set
        {
            if (SetProperty(ref _selectedTransformation, value))
            {
                SetDefaultParameter();
                OnPropertyChanged(nameof(ParameterLabel));
                OnPropertyChanged(nameof(UsesPrimaryValue));
                OnPropertyChanged(nameof(IsNoise));
                OnPropertyChanged(nameof(IsGeometric));
                OnPropertyChanged(nameof(IsRegion));
            }
        }
    }

    public string ParameterLabel => SelectedTransformation.Type switch
    {
        ImageTransformationType.JpegCompression => "Качество JPEG (1–100)",
        ImageTransformationType.Brightness => "Смещение яркости (-255…255)",
        ImageTransformationType.Contrast => "Множитель контрастности",
        ImageTransformationType.GaussianNoise => "Sigma шума",
        ImageTransformationType.Resize => "Масштаб (%)",
        ImageTransformationType.Crop => "Центральная обрезка (%)",
        _ => "Параметр"
    };

    public bool UsesPrimaryValue => SelectedTransformation.Type != ImageTransformationType.RegionModification;
    public bool IsNoise => SelectedTransformation.Type == ImageTransformationType.GaussianNoise;
    public bool IsGeometric => SelectedTransformation.Type is
        ImageTransformationType.Resize or ImageTransformationType.Crop;
    public bool IsRegion => SelectedTransformation.Type == ImageTransformationType.RegionModification;

    public double ParameterValue
    {
        get => _parameterValue;
        set => SetProperty(ref _parameterValue, value);
    }

    public int Seed
    {
        get => _seed;
        set => SetProperty(ref _seed, value);
    }

    public bool RestoreOriginalSize
    {
        get => _restoreOriginalSize;
        set => SetProperty(ref _restoreOriginalSize, value);
    }

    public int RegionX
    {
        get => _regionX;
        set => SetProperty(ref _regionX, value);
    }

    public int RegionY
    {
        get => _regionY;
        set => SetProperty(ref _regionY, value);
    }

    public int RegionWidth
    {
        get => _regionWidth;
        set => SetProperty(ref _regionWidth, value);
    }

    public int RegionHeight
    {
        get => _regionHeight;
        set => SetProperty(ref _regionHeight, value);
    }

    public byte RegionRed
    {
        get => _regionRed;
        set => SetProperty(ref _regionRed, value);
    }

    public byte RegionGreen
    {
        get => _regionGreen;
        set => SetProperty(ref _regionGreen, value);
    }

    public byte RegionBlue
    {
        get => _regionBlue;
        set => SetProperty(ref _regionBlue, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

    private async Task RunAsync(bool usePresetSeries)
    {
        IsBusy = true;
        Results.Clear();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ProgressValue = 0;
        _cancellationTokenSource?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellationTokenSource = cancellation;
        var statusProgress = new Progress<string>(message => StatusText = message);
        var operationProgress = new Progress<double>(
            value => ProgressValue = Math.Clamp(value, 0, 1) * 100);
        try
        {
            var cases = usePresetSeries ? BuildPresetCases() : [BuildCase(ParameterValue)];
            var results = await _experiments.RunAsync(
                new(
                    ImagePath,
                    SignaturePath,
                    PublicKeyPath,
                    OutputDirectory,
                    string.IsNullOrWhiteSpace(ExpectedWatermark) ? null : ExpectedWatermark,
                    WatermarkSettings,
                    cases),
                statusProgress,
                operationProgress,
                cancellation.Token);

            foreach (var result in results)
            {
                Results.Add(result);
            }

            StatusText = $"Готово. Получено результатов: {Results.Count}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Операция отменена пользователем.";
        }
        finally
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellation))
            {
                _cancellationTokenSource = null;
            }

            cancellation.Dispose();
            IsBusy = false;
            RefreshCommands();
        }
    }

    private IReadOnlyList<ExperimentCase> BuildPresetCases()
    {
        double[] values = SelectedTransformation.Type switch
        {
            ImageTransformationType.JpegCompression => [100, 90, 80, 70, 60, 50, 40, 30],
            ImageTransformationType.Brightness => [-40, -20, 20, 40],
            ImageTransformationType.Contrast => [0.6, 0.8, 1.2, 1.4],
            ImageTransformationType.GaussianNoise => [2, 5, 10, 20],
            ImageTransformationType.Resize => [90, 75, 50],
            ImageTransformationType.Crop => [5, 10, 20],
            _ => [0]
        };

        return values.Select(value => BuildCase(value)).ToArray();
    }

    private ExperimentCase BuildCase(double value)
    {
        var parameters = new ImageTransformationParameters
        {
            Value = value,
            Seed = Seed,
            RestoreOriginalSize = RestoreOriginalSize,
            X = RegionX,
            Y = RegionY,
            Width = RegionWidth,
            Height = RegionHeight,
            RegionRed = RegionRed,
            RegionGreen = RegionGreen,
            RegionBlue = RegionBlue
        };
        var label = SelectedTransformation.Type == ImageTransformationType.RegionModification
            ? SelectedTransformation.DisplayName
            : $"{SelectedTransformation.DisplayName}_{value:0.##}";
        return new(SelectedTransformation.Type, parameters, label);
    }

    private void SetDefaultParameter()
    {
        ParameterValue = SelectedTransformation.Type switch
        {
            ImageTransformationType.JpegCompression => 80,
            ImageTransformationType.Brightness => 20,
            ImageTransformationType.Contrast => 1.2,
            ImageTransformationType.GaussianNoise => 5,
            ImageTransformationType.Resize => 75,
            ImageTransformationType.Crop => 10,
            _ => 0
        };
    }

    private void SelectOutputDirectory()
    {
        var path = _dialogs.SelectFolder();
        if (path is not null)
        {
            OutputDirectory = path;
        }
    }

    private void ExportCsv()
    {
        var path = _dialogs.SaveCsv("experiment-results.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            _experiments.ExportCsv(Results, path);
            StatusText = $"CSV сохранен: {path}";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private bool CanRun() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ImagePath) &&
        !string.IsNullOrWhiteSpace(SignaturePath) &&
        !string.IsNullOrWhiteSpace(PublicKeyPath) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    private void RefreshCommands()
    {
        SelectImageCommand.NotifyCanExecuteChanged();
        SelectSignatureCommand.NotifyCanExecuteChanged();
        SelectPublicKeyCommand.NotifyCanExecuteChanged();
        SelectOutputDirectoryCommand.NotifyCanExecuteChanged();
        RunExperimentCommand.NotifyCanExecuteChanged();
        RunPresetSeriesCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        StatusText = "Отмена операции...";
        _cancellationTokenSource?.Cancel();
    }

    private void ResetResults()
    {
        Results.Clear();
        StatusText = "Исходные данные изменены. Запустите исследование повторно.";
        ExportCsvCommand.NotifyCanExecuteChanged();
    }

    private static void SelectPath(string? path, Action<string> setter)
    {
        if (path is not null)
        {
            setter(path);
        }
    }

    private void SetError(Exception exception)
    {
        IsBusy = false;
        StatusText = exception.Message;
    }
}
