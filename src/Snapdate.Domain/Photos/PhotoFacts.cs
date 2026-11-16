namespace Snapdate.Domain.Photos;

/// <summary>Everything a name pattern can refer to for one file.</summary>
/// <param name="OriginalName">File name without extension, e.g. <c>IMG_4711</c>.</param>
/// <param name="Extension">Extension including the dot, e.g. <c>.JPG</c>.</param>
/// <param name="Date">Capture date.</param>
/// <param name="CameraModel">Camera model from metadata, if any.</param>
/// <param name="Counter">1-based running number within the current plan.</param>
public sealed record PhotoFacts(
    string OriginalName,
    string Extension,
    CaptureDate Date,
    string? CameraModel,
    int Counter);
