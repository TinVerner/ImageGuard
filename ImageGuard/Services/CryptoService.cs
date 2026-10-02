using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed class CryptoService
{
    private static readonly JsonSerializerOptions SignatureJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public byte[] ComputeSha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public string ComputeSha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(ComputeSha256(data));

    public byte[] Sign(ReadOnlySpan<byte> data, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        // RSA подписывает SHA-256-хеш точных байтов сохраненного файла.
        // Используется схема дополнения PKCS#1 v1.5.
        return privateKey.SignData(
            data,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return publicKey.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    public KeyGenerationResult GenerateKeyPair(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Не выбрана папка сохранения ключей.", nameof(directory));
        }

        name = SanitizeFileName(name);
        Directory.CreateDirectory(directory);
        var privatePath = Path.Combine(directory, $"{name}.private.pem");
        var publicPath = Path.Combine(directory, $"{name}.public.pem");

        using var rsa = RSA.Create(2048);
        File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
        return new(privatePath, publicPath, rsa.KeySize, GetFingerprint(rsa));
    }

    public RSA LoadPrivateKey(string path)
    {
        var pem = ReadPem(path);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            _ = rsa.ExportParameters(true);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public RSA LoadPublicKey(string path)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(ReadPem(path));
            _ = rsa.ExportParameters(false);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public string GetFingerprint(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        var digest = ComputeSha256(rsa.ExportSubjectPublicKeyInfo());
        return string.Join(':', Convert.ToHexString(digest).Chunk(2).Select(chars => new string(chars)));
    }

    public KeyInspectionResult Inspect(string path)
    {
        var pem = ReadPem(path);
        var isPrivate = pem.Contains("PRIVATE KEY", StringComparison.Ordinal);

        using var rsa = isPrivate
            ? LoadPrivateKey(path)
            : LoadPublicKey(path);
        return new(isPrivate, rsa.KeySize, GetFingerprint(rsa));
    }

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
        JsonSerializer.Serialize(document, SignatureJsonOptions);

    public SignatureDocument Deserialize(string json)
    {
        SignatureDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SignatureDocument>(json, SignatureJsonOptions)
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

    private static string ReadPem(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Файл PEM-ключа не найден.", path);
        }

        return File.ReadAllText(path);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Имя пары ключей не может быть пустым.", nameof(name));
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Имя пары ключей содержит недопустимые символы.", nameof(name));
        }

        return name.Trim();
    }
}
