using System.IO;
using System.Security.Cryptography;
using ImageGuard.Models;

namespace ImageGuard.Services;

public interface IKeyService
{
    KeyGenerationResult GenerateKeyPair(
        string directory,
        string name,
        string? password,
        int keySize = 2048);

    RSA LoadPrivateKey(string path, string? password);
    RSA LoadPublicKey(string path);
    string GetFingerprint(RSA rsa);
    KeyInspectionResult Inspect(string path, string? password = null);
}

public sealed class KeyService(CryptoService cryptoService) : IKeyService
{
    public KeyGenerationResult GenerateKeyPair(
        string directory,
        string name,
        string? password,
        int keySize = 2048)
    {
        if (keySize is not (2048 or 3072))
        {
            throw new ArgumentOutOfRangeException(nameof(keySize), "Поддерживаются ключи RSA 2048 и 3072 бит.");
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Не выбрана папка сохранения ключей.", nameof(directory));
        }

        name = SanitizeFileName(name);
        Directory.CreateDirectory(directory);
        var privatePath = Path.Combine(directory, $"{name}.private.pem");
        var publicPath = Path.Combine(directory, $"{name}.public.pem");

        using var rsa = RSA.Create(keySize);
        var privatePem = string.IsNullOrEmpty(password)
            ? rsa.ExportPkcs8PrivateKeyPem()
            : rsa.ExportEncryptedPkcs8PrivateKeyPem(
                password,
                new PbeParameters(
                    PbeEncryptionAlgorithm.Aes256Cbc,
                    HashAlgorithmName.SHA256,
                    100_000));

        File.WriteAllText(privatePath, privatePem);
        File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
        return new(privatePath, publicPath, rsa.KeySize, GetFingerprint(rsa));
    }

    public RSA LoadPrivateKey(string path, string? password)
    {
        var pem = ReadPem(path);
        var rsa = RSA.Create();
        try
        {
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new CryptographicException("Для закрытого ключа требуется пароль.");
                }

                rsa.ImportFromEncryptedPem(pem, password);
            }
            else
            {
                rsa.ImportFromPem(pem);
            }

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
        var digest = cryptoService.ComputeSha256(rsa.ExportSubjectPublicKeyInfo());
        return string.Join(':', Convert.ToHexString(digest).Chunk(2).Select(chars => new string(chars)));
    }

    public KeyInspectionResult Inspect(string path, string? password = null)
    {
        var pem = ReadPem(path);
        var isEncrypted = pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        var isPrivate = isEncrypted ||
                        pem.Contains("PRIVATE KEY", StringComparison.Ordinal);

        using var rsa = isPrivate
            ? LoadPrivateKey(path, password)
            : LoadPublicKey(path);
        return new(isPrivate, isEncrypted, rsa.KeySize, GetFingerprint(rsa));
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
