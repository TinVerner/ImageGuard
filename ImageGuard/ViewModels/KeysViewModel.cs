using ImageGuard.Services;

namespace ImageGuard.ViewModels;

public sealed class KeysViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly IKeyService _keys;
    private string _keyName = "mykey";
    private string _outputDirectory = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private int _keySize = 2048;
    private string _resultText = string.Empty;
    private string _fingerprint = string.Empty;
    private string _publicKeyPath = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _inspectionPath = string.Empty;
    private string _inspectionPassword = string.Empty;
    private string _inspectionResult = string.Empty;
    private bool _isBusy;

    public KeysViewModel(IFileDialogService dialogs, IKeyService keys)
    {
        _dialogs = dialogs;
        _keys = keys;
        SelectFolderCommand = new RelayCommand(SelectFolder, () => !IsBusy);
        GenerateKeysCommand = new AsyncRelayCommand(GenerateAsync, CanGenerate, SetError);
        SelectKeyToInspectCommand = new RelayCommand(SelectKeyToInspect, () => !IsBusy);
        InspectKeyCommand = new AsyncRelayCommand(InspectAsync, CanInspect, SetError);
    }

    public int[] KeySizes { get; } = [2048, 3072];
    public RelayCommand SelectFolderCommand { get; }
    public AsyncRelayCommand GenerateKeysCommand { get; }
    public RelayCommand SelectKeyToInspectCommand { get; }
    public AsyncRelayCommand InspectKeyCommand { get; }

    public string KeyName
    {
        get => _keyName;
        set
        {
            if (SetProperty(ref _keyName, value))
            {
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
                RefreshCommands();
            }
        }
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string PasswordConfirmation
    {
        get => _passwordConfirmation;
        set => SetProperty(ref _passwordConfirmation, value);
    }

    public int KeySize
    {
        get => _keySize;
        set => SetProperty(ref _keySize, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string Fingerprint
    {
        get => _fingerprint;
        private set => SetProperty(ref _fingerprint, value);
    }

    public string PublicKeyPath
    {
        get => _publicKeyPath;
        private set => SetProperty(ref _publicKeyPath, value);
    }

    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        private set => SetProperty(ref _privateKeyPath, value);
    }

    public string InspectionPath
    {
        get => _inspectionPath;
        private set
        {
            if (SetProperty(ref _inspectionPath, value))
            {
                RefreshCommands();
            }
        }
    }

    public string InspectionPassword
    {
        get => _inspectionPassword;
        set => SetProperty(ref _inspectionPassword, value);
    }

    public string InspectionResult
    {
        get => _inspectionResult;
        private set => SetProperty(ref _inspectionResult, value);
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

    private void SelectFolder()
    {
        var path = _dialogs.SelectFolder();
        if (path is not null)
        {
            OutputDirectory = path;
        }
    }

    private async Task GenerateAsync()
    {
        if (Password != PasswordConfirmation)
        {
            ResultText = "Пароль и подтверждение не совпадают.";
            return;
        }

        IsBusy = true;
        ResultText = "Создание RSA-ключей...";
        try
        {
            var result = await Task.Run(() =>
                _keys.GenerateKeyPair(OutputDirectory, KeyName, Password, KeySize));
            PublicKeyPath = result.PublicKeyPath;
            PrivateKeyPath = result.PrivateKeyPath;
            Fingerprint = result.Fingerprint;
            ResultText = $"Пара RSA {result.KeySize} бит успешно создана.";
        }
        catch (Exception exception)
        {
            ResultText = exception.Message;
        }
        finally
        {
            Password = string.Empty;
            PasswordConfirmation = string.Empty;
            IsBusy = false;
        }
    }

    private void SelectKeyToInspect()
    {
        var path = _dialogs.OpenPem();
        if (path is not null)
        {
            InspectionPath = path;
        }
    }

    private async Task InspectAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _keys.Inspect(InspectionPath, InspectionPassword));
            InspectionResult =
                $"Тип: {(result.IsPrivate ? "закрытый" : "открытый")}; " +
                $"размер: {result.KeySize} бит; " +
                $"шифрование: {(result.IsEncrypted ? "да" : "нет")}\n" +
                $"Fingerprint: {result.Fingerprint}";
        }
        catch (Exception exception)
        {
            InspectionResult = exception.Message;
        }
        finally
        {
            InspectionPassword = string.Empty;
            IsBusy = false;
        }
    }

    private bool CanGenerate() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(KeyName) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    private bool CanInspect() => !IsBusy && !string.IsNullOrWhiteSpace(InspectionPath);

    private void RefreshCommands()
    {
        SelectFolderCommand.NotifyCanExecuteChanged();
        GenerateKeysCommand.NotifyCanExecuteChanged();
        SelectKeyToInspectCommand.NotifyCanExecuteChanged();
        InspectKeyCommand.NotifyCanExecuteChanged();
    }

    private void SetError(Exception exception)
    {
        IsBusy = false;
        ResultText = exception.Message;
    }
}
