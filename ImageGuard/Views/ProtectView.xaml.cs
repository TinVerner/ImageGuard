using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ImageGuard.Models;
using ImageGuard.Services;
using Microsoft.Win32;

namespace ImageGuard.Views;

public partial class ProtectView : UserControl
{
    private readonly ImageService _images;
    private readonly WatermarkService _watermarks;
    private readonly ProtectionService _protection;
    private string _inputImagePath = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _outputImagePath = string.Empty;
    private string _signatureOutputPath = string.Empty;
    private bool _watermarkFits;
    private bool _isBusy;

    public ProtectView(
        ImageService images,
        WatermarkService watermarks,
        ProtectionService protection)
    {
        InitializeComponent();
        _images = images;
        _watermarks = watermarks;
        _protection = protection;
        UpdateProtectEnabled();
    }

    private void SelectImage_Click(object sender, RoutedEventArgs e)
    {
        var path = OpenFile("Изображения PNG (*.png)|*.png");
        if (path is null)
        {
            return;
        }

        try
        {
            var image = _images.Load(path);
            var info = _images.GetImageInfo(path);
            _inputImagePath = path;
            SourcePreviewImage.Source = image.ToBitmapSource();
            InputImagePathText.Text = path;
            ImageInfoText.Text =
                $"{info.FileName} · {info.Width}×{info.Height} · {info.Format} · {FormatSize(info.FileSize)}";
            ClearResult();
            UpdateCapacity();
        }
        catch (Exception exception)
        {
            ResultText.Text = exception.Message;
        }
    }

    private void SelectPrivateKey_Click(object sender, RoutedEventArgs e)
    {
        var path = OpenFile("PEM-ключи (*.pem)|*.pem");
        if (path is null)
        {
            return;
        }

        _privateKeyPath = path;
        PrivateKeyPathText.Text = path;
        UpdateProtectEnabled();
    }

    private void SelectOutput_Click(object sender, RoutedEventArgs e)
    {
        var baseName = string.IsNullOrWhiteSpace(_inputImagePath)
            ? "protected"
            : $"{Path.GetFileNameWithoutExtension(_inputImagePath)}_protected";
        var dialog = new SaveFileDialog
        {
            FileName = baseName + ".png",
            AddExtension = true,
            DefaultExt = ".png",
            Filter = "PNG (*.png)|*.png"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _outputImagePath = dialog.FileName;
        _signatureOutputPath = Path.ChangeExtension(dialog.FileName, ".igsig");
        OutputImagePathText.Text = _outputImagePath;
        OutputPathShortText.Text = _outputImagePath;
        SignatureOutputPathText.Text = _signatureOutputPath;
        UpdateProtectEnabled();
    }

    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        WatermarkByteCountText.Text = $"UTF-8: {Encoding.UTF8.GetByteCount(WatermarkTextBox.Text)} байт";
        UpdateCapacity();
    }

    private async void Protect_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings, out var error))
        {
            ResultText.Text = error;
            return;
        }

        SetBusy(true);
        ResultText.Text = string.Empty;
        Sha256Text.Text = string.Empty;
        OperationProgressBar.Value = 0;
        var statusProgress = new Progress<string>(message => StatusText.Text = message);
        var operationProgress = new Progress<double>(
            value => OperationProgressBar.Value = Math.Clamp(value, 0, 1) * 100);
        try
        {
            var result = await _protection.ProtectAsync(
                new(
                    _inputImagePath,
                    _outputImagePath,
                    WatermarkTextBox.Text,
                    settings,
                    _privateKeyPath,
                    _signatureOutputPath),
                statusProgress,
                operationProgress);

            ProcessingTimeText.Text = $"{result.ProcessingTime.TotalMilliseconds:F0} мс";
            if (!result.Success)
            {
                ResultText.Text = result.ErrorMessage ?? "Не удалось защитить изображение.";
                return;
            }

            ProtectedPreviewImage.Source = _images.Load(_outputImagePath).ToBitmapSource();
            Sha256Text.Text = result.Sha256 ?? string.Empty;
            ResultText.Text = result.WatermarkValidAfterSave
                ? "Защищенное изображение и подпись сохранены.\nWatermark после сохранения извлекается корректно."
                : result.ErrorMessage ?? "Контрольное извлечение watermark не удалось.";
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

    private void UpdateCapacity()
    {
        if (string.IsNullOrWhiteSpace(_inputImagePath) ||
            string.IsNullOrWhiteSpace(WatermarkTextBox.Text))
        {
            _watermarkFits = false;
            CapacityInfoText.Text = "Выберите изображение и введите текст.";
            UpdateProtectEnabled();
            return;
        }

        try
        {
            var image = _images.Load(_inputImagePath);
            var capacity = _watermarks.CalculateCapacity(image, WatermarkTextBox.Text);
            _watermarkFits = capacity.Fits;
            CapacityInfoText.Text =
                $"Доступно блоков: {capacity.CapacityBits:N0}; требуется: {capacity.RequiredBits:N0}; " +
                $"полезная емкость: {capacity.CapacityPayloadBytes:N0} байт. " +
                (capacity.Fits ? "Текст помещается." : "Изображение недостаточно большое.");
        }
        catch (Exception exception)
        {
            _watermarkFits = false;
            CapacityInfoText.Text = exception.Message;
        }

        UpdateProtectEnabled();
    }

    private void UpdateProtectEnabled()
    {
        if (ProtectButton is null || WatermarkTextBox is null)
        {
            return;
        }

        ProtectButton.IsEnabled =
            !_isBusy &&
            !string.IsNullOrWhiteSpace(_inputImagePath) &&
            !string.IsNullOrWhiteSpace(WatermarkTextBox.Text) &&
            !string.IsNullOrWhiteSpace(_privateKeyPath) &&
            !string.IsNullOrWhiteSpace(_outputImagePath) &&
            !string.IsNullOrWhiteSpace(_signatureOutputPath) &&
            _watermarkFits;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        OperationProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProtectEnabled();
    }

    private void ClearResult()
    {
        ProtectedPreviewImage.Source = null;
        ResultText.Text = string.Empty;
        Sha256Text.Text = string.Empty;
        ProcessingTimeText.Text = string.Empty;
    }

    private bool TryReadSettings(out WatermarkSettings settings, out string error)
    {
        settings = new WatermarkSettings();
        error = string.Empty;
        if (!double.TryParse(DeltaTextBox.Text, out var delta) || !double.IsFinite(delta) || delta <= 0)
        {
            error = "Delta должна быть конечным положительным числом.";
            return false;
        }

        settings.Delta = delta;
        return true;
    }

    private static string? OpenFile(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F2} МБ"
            : $"{bytes / 1024d:F1} КБ";
}
