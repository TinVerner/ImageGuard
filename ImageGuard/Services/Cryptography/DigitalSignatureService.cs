using System.Security.Cryptography;

namespace ImageGuard.Services.Cryptography;

public interface IDigitalSignatureService
{
    byte[] Sign(ReadOnlySpan<byte> data, RSA privateKey);
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, RSA publicKey);
}

public sealed class DigitalSignatureService : IDigitalSignatureService
{
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
