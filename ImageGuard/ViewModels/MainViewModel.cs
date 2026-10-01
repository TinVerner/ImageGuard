namespace ImageGuard.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _currentViewModel;

    public MainViewModel(
        ProtectViewModel protect,
        VerifyViewModel verify,
        KeysViewModel keys)
    {
        Protect = protect;
        Verify = verify;
        Keys = keys;
        _currentViewModel = Protect;

        ShowProtectCommand = new RelayCommand(() => CurrentViewModel = Protect);
        ShowVerifyCommand = new RelayCommand(() => CurrentViewModel = Verify);
        ShowKeysCommand = new RelayCommand(() => CurrentViewModel = Keys);
    }

    public ProtectViewModel Protect { get; }
    public VerifyViewModel Verify { get; }
    public KeysViewModel Keys { get; }

    public RelayCommand ShowProtectCommand { get; }
    public RelayCommand ShowVerifyCommand { get; }
    public RelayCommand ShowKeysCommand { get; }

    public bool IsProtectActive => ReferenceEquals(CurrentViewModel, Protect);
    public bool IsVerifyActive => ReferenceEquals(CurrentViewModel, Verify);
    public bool IsKeysActive => ReferenceEquals(CurrentViewModel, Keys);

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (SetProperty(ref _currentViewModel, value))
            {
                OnPropertyChanged(nameof(IsProtectActive));
                OnPropertyChanged(nameof(IsVerifyActive));
                OnPropertyChanged(nameof(IsKeysActive));
            }
        }
    }
}
