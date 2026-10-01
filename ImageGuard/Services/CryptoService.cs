using System.Security.Cryptography;

namespace ImageGuard.Services;

public sealed class CryptoService
{
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
}
