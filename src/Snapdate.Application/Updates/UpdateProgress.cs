namespace Snapdate.Application.Updates;

/// <summary>Download progress of the installer.</summary>
/// <param name="BytesReceived">Bytes written so far.</param>
/// <param name="TotalBytes">Size announced by the release (already cross-checked), always greater than zero.</param>
public readonly record struct UpdateProgress(long BytesReceived, long TotalBytes);
