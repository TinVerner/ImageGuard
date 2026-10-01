using System.IO;
using System.Text.Json;
using ImageGuard.Models;

namespace ImageGuard.Services.Cryptography;

public interface ISignatureDocumentService
{
    void Save(SignatureDocument document, string path);
    SignatureDocument Load(string path);
    string Serialize(SignatureDocument document);
    SignatureDocument Deserialize(string json);
}

public sealed class SignatureDocumentService : ISignatureDocumentService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void Save(SignatureDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Не указан путь файла подписи.", nameof(path));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(document));
    }

    public SignatureDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Файл цифровой подписи не найден.", path);
        }

        return Deserialize(File.ReadAllText(path));
    }

    public string Serialize(SignatureDocument document) =>
        JsonSerializer.Serialize(document, Options);

    public SignatureDocument Deserialize(string json)
    {
        SignatureDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SignatureDocument>(json, Options)
                       ?? throw new InvalidDataException("Файл подписи пуст.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Файл .igsig содержит некорректный JSON.", exception);
        }

        if (document.Version != SignatureDocument.CurrentVersion)
        {
            throw new NotSupportedException("Версия файла подписи не поддерживается.");
        }

        if (!string.Equals(
                document.Algorithm,
                SignatureDocument.CurrentAlgorithm,
                StringComparison.Ordinal) ||
            !string.Equals(document.HashAlgorithm, "SHA-256", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Алгоритм файла подписи не поддерживается.");
        }

        if (string.IsNullOrWhiteSpace(document.SignatureBase64))
        {
            throw new InvalidDataException("Файл подписи не содержит цифровую подпись.");
        }

        if (!double.IsFinite(document.WatermarkDelta) ||
            document.WatermarkDelta <= 0)
        {
            throw new InvalidDataException(
                "Файл подписи содержит некорректное значение Delta.");
        }

        return document;
    }
}
