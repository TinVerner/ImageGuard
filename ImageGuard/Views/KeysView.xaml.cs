using System.Windows;
using System.Windows.Controls;
using ImageGuard.Services;
using Microsoft.Win32;

namespace ImageGuard.Views;

public partial class KeysView : UserControl
{
    private readonly CryptoService _crypto;
    private string _outputDirectory = string.Empty;
    private string _inspectionPath = string.Empty;
    private bool _isBusy;

    public KeysView(CryptoService crypto)
    {
        InitializeComponent();
        _crypto = crypto;
        UpdateButtons();
    }

    private void KeyNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _outputDirectory = dialog.FolderName;
        OutputDirectoryText.Text = _outputDirectory;
        UpdateButtons();
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ResultText.Text = "Создание RSA-ключей...";
        try
        {
            var keyName = KeyNameTextBox.Text;
            var outputDirectory = _outputDirectory;
            var result = await Task.Run(() =>
                _crypto.GenerateKeyPair(outputDirectory, keyName));
            PublicKeyPathText.Text = result.PublicKeyPath;
            PrivateKeyPathText.Text = result.PrivateKeyPath;
            FingerprintText.Text = result.Fingerprint;
            ResultText.Text = $"Пара RSA {result.KeySize} бит успешно создана.";
        }
        catch (Exception exception)
        {
            ResultText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectKeyToInspect_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PEM-ключи (*.pem)|*.pem",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _inspectionPath = dialog.FileName;
        InspectionPathText.Text = _inspectionPath;
        UpdateButtons();
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _crypto.Inspect(_inspectionPath));
            InspectionResultText.Text =
                $"Тип: {(result.IsPrivate ? "закрытый" : "открытый")}; " +
                $"размер: {result.KeySize} бит\n" +
                $"Fingerprint: {result.Fingerprint}";
        }
        catch (Exception exception)
        {
            InspectionResultText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateButtons()
    {
        if (GenerateButton is null || InspectButton is null || KeyNameTextBox is null)
        {
            return;
        }

        GenerateButton.IsEnabled =
            !_isBusy &&
            !string.IsNullOrWhiteSpace(KeyNameTextBox.Text) &&
            !string.IsNullOrWhiteSpace(_outputDirectory);
        InspectButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(_inspectionPath);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        BusyProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }
}
