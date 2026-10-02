using System.Security.Cryptography;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class CryptographyTests
{
    [Fact]
    public void Sha256_KnownVector_MatchesStandardDigest()
    {
        var hash = new TestServices().Crypto.ComputeSha256Hex("abc"u8);

        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223" +
            "B00361A396177A9CB410FF61F20015AD",
            hash);
    }

    [Fact]
    public void SignAndVerify_UsesPkcs1V15AndDetectsChangedData()
    {
        var services = new TestServices();
        using var rsa = RSA.Create(2048);
        var data = "signed image bytes"u8.ToArray();
        var signature = services.Crypto.Sign(data, rsa);

        Assert.True(rsa.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        Assert.False(rsa.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

        var independentlyCreatedPkcs1Signature = rsa.SignData(
            data,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        Assert.True(services.Crypto.Verify(
            data,
            independentlyCreatedPkcs1Signature,
            rsa));
        Assert.True(services.Crypto.Verify(data, signature, rsa));

        data[0] ^= 0x01;
        Assert.False(services.Crypto.Verify(data, signature, rsa));
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

        var actual = services.Crypto.Deserialize(services.Crypto.Serialize(expected));

        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Algorithm, actual.Algorithm);
        Assert.Equal(SignatureDocument.CurrentAlgorithm, actual.Algorithm);
        Assert.Equal(expected.HashAlgorithm, actual.HashAlgorithm);
        Assert.Equal(expected.KeySize, actual.KeySize);
        Assert.Equal(expected.ProtectedFileName, actual.ProtectedFileName);
        Assert.Equal(expected.SignatureBase64, actual.SignatureBase64);
        Assert.Equal(expected.PublicKeyFingerprint, actual.PublicKeyFingerprint);
        Assert.Equal(expected.WatermarkDelta, actual.WatermarkDelta);
    }

    [Fact]
    public void SignatureDocument_OldPssAlgorithm_IsRejectedWithoutVersionChange()
    {
        var services = new TestServices();
        var document = new SignatureDocument
        {
            Algorithm = "RSA-PSS",
            KeySize = 2048,
            CreatedUtc = DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
            ProtectedFileName = "protected.png",
            Sha256 = "AABB",
            SignatureBase64 = "AQID",
            PublicKeyFingerprint = "AA:BB",
            WatermarkDelta = 20
        };

        Assert.Throws<NotSupportedException>(() =>
            services.Crypto.Deserialize(services.Crypto.Serialize(document)));
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
            services.Crypto.Deserialize(services.Crypto.Serialize(legacy)));
    }

    [Fact]
    public void GenerateKeyPair_Uses2048BitPkcs8AndHasStableFingerprint()
    {
        var services = new TestServices();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ImageGuardKeyTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var generated = services.Crypto.GenerateKeyPair(
                directory,
                "rsa-2048");
            using var privateKey = services.Crypto.LoadPrivateKey(generated.PrivateKeyPath);
            using var publicKey = services.Crypto.LoadPublicKey(generated.PublicKeyPath);

            Assert.Equal(2048, generated.KeySize);
            Assert.Equal(2048, privateKey.KeySize);
            Assert.Equal(2048, publicKey.KeySize);
            Assert.Contains(
                "-----BEGIN PRIVATE KEY-----",
                File.ReadAllText(generated.PrivateKeyPath),
                StringComparison.Ordinal);
            Assert.Equal(generated.Fingerprint, services.Crypto.GetFingerprint(privateKey));
            Assert.Equal(generated.Fingerprint, services.Crypto.GetFingerprint(publicKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
