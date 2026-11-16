using Snapdate.Domain.Photos;

namespace Snapdate.Application.Scanning;

/// <summary>A file found in the source folder, with its capture date already resolved.</summary>
public sealed record ScannedPhoto(
    string SourcePath,
    string OriginalName,
    string Extension,
    CaptureDate Date,
    string? CameraModel,
    long SizeBytes);
