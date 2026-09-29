using System.Security.Cryptography;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class CryptographyTests
{
    [Fact]
    public void Sha256_KnownVector_MatchesStandardDigest()
    {
        var hash = new TestServices().Hash.ComputeSha256Hex("abc"u8);

        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223" +
            "B00361A396177A9CB410FF61F20015AD",
            hash);
    }

    [Fact]
    public void SignAndVerify_DetectsChangedData()
    {
        var services = new TestServices();
        using var rsa = RSA.Create(2048);
        var data = "signed image bytes"u8.ToArray();
        var signature = services.Signatures.Sign(data, rsa);

        Assert.True(services.Signatures.Verify(data, signature, rsa));

        data[0] ^= 0x01;
        Assert.False(services.Signatures.Verify(data, signature, rsa));
    }

    [Fact]
    public void SignatureDocument_SerializeDeserialize_PreservesFields()
    {
        var services = new TestServices();
        var expected = new SignatureDocument
        {
            KeySize = 2048,
            CreatedUtc = DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
            ProtectedFileName = "protected.png",
            Sha256 = "AABB",
            SignatureBase64 = "AQID",
            PublicKeyFingerprint = "AA:BB",
            WatermarkDelta = 20
        };

        var actual = services.Documents.Deserialize(services.Documents.Serialize(expected));

        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Algorithm, actual.Algorithm);
        Assert.Equal(expected.HashAlgorithm, actual.HashAlgorithm);
        Assert.Equal(expected.KeySize, actual.KeySize);
        Assert.Equal(expected.ProtectedFileName, actual.ProtectedFileName);
        Assert.Equal(expected.SignatureBase64, actual.SignatureBase64);
        Assert.Equal(expected.PublicKeyFingerprint, actual.PublicKeyFingerprint);
        Assert.Equal(expected.WatermarkDelta, actual.WatermarkDelta);
    }

    [Fact]
    public void SignatureDocument_ObsoleteVersion_IsRejected()
    {
        var services = new TestServices();
        var legacy = new SignatureDocument
        {
            Version = 1,
            KeySize = 2048,
            CreatedUtc = DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
            ProtectedFileName = "protected.png",
            Sha256 = "AABB",
            SignatureBase64 = "AQID",
            PublicKeyFingerprint = "AA:BB",
            WatermarkDelta = 20
        };

        Assert.Throws<NotSupportedException>(() =>
            services.Documents.Deserialize(services.Documents.Serialize(legacy)));
    }

    [Fact]
    public void Generate3072BitKeyPair_LoadsAndHasStableFingerprint()
    {
        var services = new TestServices();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ImageGuardKeyTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var generated = services.Keys.GenerateKeyPair(
                directory,
                "rsa-3072",
                "password",
                3072);
            using var privateKey = services.Keys.LoadPrivateKey(
                generated.PrivateKeyPath,
                "password");
            using var publicKey = services.Keys.LoadPublicKey(generated.PublicKeyPath);

            Assert.Equal(3072, generated.KeySize);
            Assert.Equal(3072, privateKey.KeySize);
            Assert.Equal(3072, publicKey.KeySize);
            Assert.Equal(generated.Fingerprint, services.Keys.GetFingerprint(privateKey));
            Assert.Equal(generated.Fingerprint, services.Keys.GetFingerprint(publicKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
