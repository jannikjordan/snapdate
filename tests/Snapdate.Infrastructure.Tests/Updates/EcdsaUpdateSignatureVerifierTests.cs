using System.Security.Cryptography;
using System.Text;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.Infrastructure.Tests.Updates;

public sealed class EcdsaUpdateSignatureVerifierTests : IDisposable
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("pretend this is Snapdate.exe");

    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly EcdsaUpdateSignatureVerifier _verifier;

    public EcdsaUpdateSignatureVerifierTests()
    {
        _verifier = new EcdsaUpdateSignatureVerifier([_signer.ExportSubjectPublicKeyInfoPem()]);
    }

    public void Dispose()
    {
        _verifier.Dispose();
        _signer.Dispose();
        _otherSigner.Dispose();
    }

    private static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private bool Verify(byte[] data, string? sig) => _verifier.Verify(new MemoryStream(data), sig);

    [Fact]
    public void Valid_signature_is_accepted()
    {
        Assert.True(Verify(Payload, Sign(_signer, Payload)));
    }

    [Fact]
    public void Signature_file_with_trailing_newline_is_accepted()
    {
        Assert.True(Verify(Payload, Sign(_signer, Payload) + "\r\n"));
        Assert.True(Verify(Payload, Sign(_signer, Payload) + "\n"));
    }

    [Fact]
    public void Tampered_file_is_rejected()
    {
        var sig = Sign(_signer, Payload);
        var tampered = (byte[])Payload.Clone();
        tampered[0] ^= 0x01;
        Assert.False(Verify(tampered, sig));
    }

    [Fact]
    public void Appended_bytes_are_rejected()
    {
        Assert.False(Verify([.. Payload, 0], Sign(_signer, Payload)));
    }

    [Fact]
    public void Signature_by_another_key_is_rejected()
    {
        Assert.False(Verify(Payload, Sign(_otherSigner, Payload)));
    }

    [Fact]
    public void Empty_file_signed_by_us_is_accepted_but_not_with_a_signature_for_other_content()
    {
        Assert.True(Verify([], Sign(_signer, [])));
        Assert.False(Verify([], Sign(_signer, Payload)));
    }

    [Fact]
    public void Any_trusted_key_is_enough_for_key_rotation()
    {
        using var rotating = new EcdsaUpdateSignatureVerifier(
            [_otherSigner.ExportSubjectPublicKeyInfoPem(), _signer.ExportSubjectPublicKeyInfoPem()]);
        Assert.True(rotating.Verify(new MemoryStream(Payload), Sign(_signer, Payload)));
        Assert.True(rotating.Verify(new MemoryStream(Payload), Sign(_otherSigner, Payload)));
    }

    [Fact]
    public void Signature_in_der_format_is_rejected()
    {
        var der = Convert.ToBase64String(_signer.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        Assert.False(Verify(Payload, der));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all!")]
    [InlineData("AAAA")]
    public void Malformed_signature_text_is_rejected_without_exception(string? text)
    {
        Assert.False(Verify(Payload, text));
    }

    [Fact]
    public void Truncated_and_extended_signatures_are_rejected()
    {
        var sig = Sign(_signer, Payload);
        Assert.False(Verify(Payload, sig[..^4]));
        Assert.False(Verify(Payload, sig[..40]));
        var raw = Convert.FromBase64String(sig);
        Assert.False(Verify(Payload, Convert.ToBase64String([.. raw, 0])));
        Assert.False(Verify(Payload, Convert.ToBase64String(raw[..63])));
    }

    [Fact]
    public void Signature_with_embedded_whitespace_or_garbage_is_rejected()
    {
        var sig = Sign(_signer, Payload);
        Assert.False(Verify(Payload, sig.Insert(40, "\n")));
        Assert.False(Verify(Payload, sig + "!!"));
        Assert.False(Verify(Payload, "X" + sig));
    }

    [Fact]
    public void Oversized_signature_text_is_rejected()
    {
        Assert.False(Verify(Payload, new string('A', 10_000_000)));
    }

    [Fact]
    public void All_zero_signature_is_rejected()
    {
        Assert.False(Verify(Payload, Convert.ToBase64String(new byte[64])));
    }

    [Fact]
    public void Invalid_trusted_keys_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new EcdsaUpdateSignatureVerifier([]));
        Assert.Throws<ArgumentException>(() => new EcdsaUpdateSignatureVerifier(["garbage"]));

        var pem = _signer.ExportSubjectPublicKeyInfoPem();
        var mangled = pem.Replace("A", "!", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new EcdsaUpdateSignatureVerifier([mangled]));
        Assert.Throws<ArgumentException>(() => new EcdsaUpdateSignatureVerifier([pem[..^30]]));

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => new EcdsaUpdateSignatureVerifier([p384.ExportSubjectPublicKeyInfoPem()]));
    }

    [Fact]
    public void Embedded_production_key_parses_and_is_a_p256_key()
    {
        var keys = EcdsaUpdateSignatureVerifier.LoadEmbeddedKeys();
        Assert.NotEmpty(keys);
        foreach (var pem in keys)
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            Assert.Equal(256, key.KeySize);
        }

        using var verifier = EcdsaUpdateSignatureVerifier.CreateDefault();
        Assert.False(verifier.Verify(new MemoryStream(Payload), Sign(_signer, Payload)));
    }
}
