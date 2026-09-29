namespace ImageGuard.Enums;

public enum ImageOutputFormat
{
    Png,
    Jpeg
}

public enum SignatureStatus
{
    Valid,
    Invalid,
    Missing,
    NotChecked,
    FormatError,
    Error
}

public enum WatermarkStatus
{
    Valid,
    NotFound,
    Error
}

public enum OverallVerificationStatus
{
    Authentic,
    ModifiedWatermarkPreserved,
    ModifiedWatermarkNotFound,
    Inconsistent,
    Error
}

public enum ImageTransformationType
{
    JpegCompression,
    Brightness,
    Contrast,
    GaussianNoise,
    Resize,
    Crop,
    RegionModification
}
