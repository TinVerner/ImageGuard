using System.Security.Cryptography;

namespace ImageGuard.Services.Cryptography;

public interface IHashService
{
    byte[] ComputeSha256(ReadOnlySpan<byte> data);
    string ComputeSha256Hex(ReadOnlySpan<byte> data);
}

public sealed class HashService : IHashService
{
    public byte[] ComputeSha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public string ComputeSha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(ComputeSha256(data));
}
