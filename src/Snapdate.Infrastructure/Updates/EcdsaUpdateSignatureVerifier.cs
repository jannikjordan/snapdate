using System.Security.Cryptography;
using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// Verifies detached update signatures: ECDSA P-256 over SHA-256 of the file bytes, stored as base64 of the
/// 64-byte IEEE P1363 (r || s) value. Several public keys may be trusted at once so keys can be rotated
/// (see ADR 0005).
/// </summary>
public sealed class EcdsaUpdateSignatureVerifier : IUpdateSignatureVerifier, IDisposable
{
    /// <summary>Length of the P1363 signature of a P-256 key (r and s, 32 bytes each).</summary>
    public const int SignatureLength = 64;

    /// <summary>Base64 length of <see cref="SignatureLength"/> bytes, including padding.</summary>
    private const int EncodedLength = 88;

    private const string KeyResourceSuffix = ".pub.pem";

    private readonly ECDsa[] _keys;

    /// <summary>Creates a verifier that trusts the given SubjectPublicKeyInfo PEM public keys.</summary>
    /// <exception cref="ArgumentException">No key given, or a key is not a valid P-256 public key.</exception>
    public EcdsaUpdateSignatureVerifier(IEnumerable<string> publicKeyPems)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPems);
        var keys = new List<ECDsa>();
        try
        {
            foreach (var pem in publicKeyPems)
            {
                var key = ECDsa.Create();
                keys.Add(key);
                try
                {
                    key.ImportFromPem(pem);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    throw new ArgumentException("A trusted update key is not a valid public key.", nameof(publicKeyPems), ex);
                }

                if (key.KeySize != 256)
                {
                    throw new ArgumentException("A trusted update key must be an ECDSA P-256 key.", nameof(publicKeyPems));
                }
            }

            if (keys.Count == 0)
            {
                throw new ArgumentException("At least one trusted update key is required.", nameof(publicKeyPems));
            }
        }
        catch
        {
            foreach (var key in keys)
            {
                key.Dispose();
            }

            throw;
        }

        _keys = [.. keys];
    }

    /// <summary>Creates a verifier with the public keys embedded in the app (<c>Updates/Keys/*.pub.pem</c>).</summary>
    public static EcdsaUpdateSignatureVerifier CreateDefault() => new(LoadEmbeddedKeys());

    /// <summary>The PEM text of every public key embedded in the app.</summary>
    public static IReadOnlyList<string> LoadEmbeddedKeys()
    {
        var assembly = typeof(EcdsaUpdateSignatureVerifier).Assembly;
        var pems = new List<string>();
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(KeyResourceSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded key '{name}' is missing.");
            using var reader = new StreamReader(stream);
            pems.Add(reader.ReadToEnd());
        }

        return pems;
    }

    /// <inheritdoc />
    public bool Verify(Stream content, string? signatureText)
    {
        ArgumentNullException.ThrowIfNull(content);

        var signature = DecodeSignature(signatureText);
        if (signature is null)
        {
            return false;
        }

        // Hash once, then try every trusted key.
        var hash = SHA256.HashData(content);
        foreach (var key in _keys)
        {
            try
            {
                if (key.VerifyHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // Treat a malformed value like a failed check.
            }
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var key in _keys)
        {
            key.Dispose();
        }
    }

    /// <summary>Strict decoding: one base64 line of exactly 88 characters (64 bytes); surrounding whitespace is ignored.</summary>
    private static byte[]? DecodeSignature(string? text)
    {
        if (text is null || text.Length > EncodedLength + 16)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Length != EncodedLength)
        {
            return null;
        }

        var buffer = new byte[EncodedLength];
        if (!Convert.TryFromBase64String(trimmed, buffer, out var written) || written != SignatureLength)
        {
            return null;
        }

        // Rejects non-canonical encodings (e.g. non-zero trailing bits) so there is one valid text per signature.
        return Convert.ToBase64String(buffer, 0, written) == trimmed ? buffer[..written] : null;
    }
}
