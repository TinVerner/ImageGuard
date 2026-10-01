using Microsoft.Win32;

namespace ImageGuard.Services;

public interface IFileDialogService
{
    string? OpenImage();
    string? OpenSignature();
    string? OpenPem();
    string? SaveImage(string suggestedName, bool jpeg);
    string? SaveCsv(string suggestedName);
    string? SelectFolder();
}

public sealed class FileDialogService : IFileDialogService
{
    private const string ImageFilter = "Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

    public string? OpenImage() => ShowOpen(ImageFilter);

    public string? OpenSignature() =>
        ShowOpen("Подпись ImageGuard (*.igsig)|*.igsig|JSON (*.json)|*.json");

    public string? OpenPem() => ShowOpen("PEM-ключи (*.pem)|*.pem");

    public string? SaveImage(string suggestedName, bool jpeg)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = jpeg ? ".jpg" : ".png",
            Filter = jpeg
                ? "JPEG (*.jpg)|*.jpg;*.jpeg"
                : "PNG (*.png)|*.png"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveCsv(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку"
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? ShowOpen(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
