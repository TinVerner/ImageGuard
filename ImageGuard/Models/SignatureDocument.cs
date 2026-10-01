using System.Text.Json.Serialization;

namespace ImageGuard.Models;

public sealed class SignatureDocument
{
    public const int CurrentVersion = 3;
    public const string CurrentAlgorithm = "RSA-SHA256-PKCS1-v1_5";

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = CurrentAlgorithm;

    [JsonPropertyName("hashAlgorithm")]
    public string HashAlgorithm { get; set; } = "SHA-256";

    [JsonPropertyName("keySize")]
    public int KeySize { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }

    [JsonPropertyName("protectedFileName")]
    public string ProtectedFileName { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; set; } = string.Empty;

    [JsonPropertyName("publicKeyFingerprint")]
    public string PublicKeyFingerprint { get; set; } = string.Empty;

    [JsonPropertyName("watermarkDelta")]
    public double WatermarkDelta { get; set; } = 10;
}
