namespace ImageGuard.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _currentViewModel;

    public MainViewModel(
        ProtectViewModel protect,
        VerifyViewModel verify,
        KeysViewModel keys,
        ResearchViewModel research)
    {
        Protect = protect;
        Verify = verify;
        Keys = keys;
        Research = research;
        _currentViewModel = Protect;

        ShowProtectCommand = new RelayCommand(() => CurrentViewModel = Protect);
        ShowVerifyCommand = new RelayCommand(() => CurrentViewModel = Verify);
        ShowKeysCommand = new RelayCommand(() => CurrentViewModel = Keys);
        ShowResearchCommand = new RelayCommand(() => CurrentViewModel = Research);
    }

    public ProtectViewModel Protect { get; }
    public VerifyViewModel Verify { get; }
    public KeysViewModel Keys { get; }
    public ResearchViewModel Research { get; }

    public RelayCommand ShowProtectCommand { get; }
    public RelayCommand ShowVerifyCommand { get; }
    public RelayCommand ShowKeysCommand { get; }
    public RelayCommand ShowResearchCommand { get; }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }
}
