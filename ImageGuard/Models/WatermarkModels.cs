namespace ImageGuard.Models;

public enum WatermarkStatus
{
    Valid,
    NotFound,
    Error
}

public sealed class WatermarkSettings
{
    public const int BlockSize = 8;
    public const int Coefficient1Row = 2;
    public const int Coefficient1Column = 3;
    public const int Coefficient2Row = 3;
    public const int Coefficient2Column = 2;

    public double Delta { get; set; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(Delta) || Delta <= 0)
        {
            throw new ArgumentException("Delta должна быть конечным положительным числом.");
        }
    }
}

public sealed record WatermarkTextDecodeResult(
    bool IsValid,
    string? Text,
    string? ErrorMessage);

public sealed record WatermarkCapacity(
    int CapacityBits,
    int RequiredBits,
    int CapacityPayloadBytes,
    bool Fits);

public sealed record WatermarkEmbedResult(
    ImagePixelData Image,
    WatermarkCapacity Capacity);

public sealed record WatermarkExtractionResult(
    WatermarkStatus Status,
    string? Text,
    string? ErrorMessage);
