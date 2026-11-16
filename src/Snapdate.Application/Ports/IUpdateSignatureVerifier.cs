namespace Snapdate.Application.Ports;

/// <summary>
/// Checks that a downloaded release file was signed by us. A file that fails the check must never
/// be run or installed.
/// </summary>
public interface IUpdateSignatureVerifier
{
    /// <summary>
    /// Verifies <paramref name="content"/> against the text of its detached <c>.sig</c> file.
    /// </summary>
    /// <param name="content">The release file, read from its current position to the end.</param>
    /// <param name="signatureText">
    /// The content of the <c>.sig</c> file (untrusted): base64 of a 64-byte ECDSA P-256 signature.
    /// </param>
    /// <returns>
    /// <c>true</c> only for a valid signature by one of the trusted keys over exactly these bytes (an empty
    /// file verifies if, and only if, it was signed as empty). Malformed, truncated, oversized or null
    /// signature text, a signature over other content and a signature by an untrusted key all yield
    /// <c>false</c>; no exception is thrown for bad input (an I/O error while reading
    /// <paramref name="content"/> still propagates).
    /// </returns>
    bool Verify(Stream content, string? signatureText);
}
