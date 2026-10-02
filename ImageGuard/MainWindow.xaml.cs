using System.Windows;
using System.Windows.Controls;
using ImageGuard.Services;
using ImageGuard.Views;

namespace ImageGuard;

public partial class MainWindow : Window
{
    private readonly ProtectView _protectView;
    private readonly KeysView _keysView;

    public MainWindow(
        ImageService images,
        WatermarkService watermarks,
        CryptoService crypto,
        ProtectionService protection)
    {
        InitializeComponent();
        _protectView = new ProtectView(images, watermarks, protection);
        _keysView = new KeysView(crypto);
        ShowProtect();
    }

    private void ProtectNavButton_Click(object sender, RoutedEventArgs e) => ShowProtect();

    private void VerifyNavButton_Click(object sender, RoutedEventArgs e) => ShowEmpty(VerifyNavButton);

    private void ResearchNavButton_Click(object sender, RoutedEventArgs e) => ShowEmpty(ResearchNavButton);

    private void KeysNavButton_Click(object sender, RoutedEventArgs e) => ShowKeys();

    private void ShowProtect()
    {
        MainContent.Content = _protectView;
        SetNav(ProtectNavButton);
    }

    private void ShowKeys()
    {
        MainContent.Content = _keysView;
        SetNav(KeysNavButton);
    }

    private void ShowEmpty(Button selected)
    {
        MainContent.Content = null;
        SetNav(selected);
    }

    private void SetNav(Button selected)
    {
        ProtectNavButton.Tag = ReferenceEquals(selected, ProtectNavButton);
        VerifyNavButton.Tag = ReferenceEquals(selected, VerifyNavButton);
        ResearchNavButton.Tag = ReferenceEquals(selected, ResearchNavButton);
        KeysNavButton.Tag = ReferenceEquals(selected, KeysNavButton);
    }
}
