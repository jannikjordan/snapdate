# ADR 0005 — Detached update signatures (ECDSA P-256)

- Status: accepted
- Date: 2026-11-16
- Related: [ADR 0004](0004-update-check-and-in-app-update.md) (update check and in-app update). The verified updater of issue #3 uses the signature scheme defined here.

## Context
The planned in-app updater (#3) downloads a new release and starts it. A
checksum from the same GitHub release page proves nothing against an attacker who
can replace release files, and GitHub build provenance
(`gh attestation verify`) is not available for private repositories and needs
the GitHub CLI. The app needs a check it can do **offline-capable, without extra
dependencies**: "was this file signed by us?". Only files that pass it may ever
be run.

## Decision

### Algorithm
**ECDSA with curve P-256 and SHA-256**, using `System.Security.Cryptography`
(built into .NET, trim- and AOT-safe, no new dependency). Ed25519 was
considered but is not in .NET's class library; it would need a third-party
package (supply-chain cost) for no practical gain here.

### Signature file
Every release asset `<name>` gets a sibling `<name>.sig` containing, in ASCII on
one line, the **base64 of the 64-byte IEEE P1363 signature** (`r || s`) over the
exact bytes of the file, computed with SHA-256
(`SignData(..., SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`).
Everything users or the updater download is signed, including
`SHA256SUMS.txt` and the SBOM; `.sig` files themselves are not signed.

The verifier is strict: exactly 88 base64 characters (surrounding whitespace is
ignored), canonical encoding, 64 bytes after decoding, input size capped.
Anything else — including DER-encoded signatures — is rejected without an
exception.

### Keys
- The **private key** is a PKCS#8 PEM in the GitHub Actions secret
  `SNAPDATE_UPDATE_SIGNING_KEY`. It is never in the repository and never logged.
  The release workflow hands it only to the one step that signs
  (`build.ps1 -Target SignRelease`), not to the build.
- The **public keys** (SubjectPublicKeyInfo PEM, not secret) live in
  `src/Snapdate.Infrastructure/Updates/Keys/*.pub.pem`. The same files are
  **embedded into the app** (`EcdsaUpdateSignatureVerifier`) and used by the
  build script to verify every signature right after signing, so the repo has a
  single source of truth. A tag build without the secret fails; a local run
  without a key only warns.

### Key rotation
The app trusts **all** embedded public keys.
1. Generate a new key pair; put the new private key into the secret
   (keep the old one until step 3).
2. Release N: add the new public key as an additional `.pub.pem` file. Still
   sign with the old key. After this release, installed apps know both keys.
3. Release N+1 (or later, once users have updated): sign with the new key.
4. Remove the old public key from the app once old installations no longer
   matter. Apps that never updated past release N-1 cannot verify new releases
   and must be replaced manually.

If the private key leaks: publish a release signed with a new key as soon as
possible and announce the manual download; since old apps do not know the new
key yet, this is the one case where users must update by hand.

### What this does and does not protect
Protects: a tampered or replaced release file (download or hosting compromise)
is not run by the updater. Does not protect: a compromise of the GitHub Actions
secret itself, or a malicious change merged into the repository.

### Authenticode
**No Authenticode certificate for now** (v0.2.0); Windows SmartScreen may warn on
first start; the release notes carry a note about it. The existing `Sign` target
(`SNAPDATE_SIGN_PFX`) stays and works as soon as a certificate exists. The plan
is to apply to the **SignPath Foundation** (free code signing for open source)
once the repository is public.

## Consequences
- Signing and verification need no new package; the private key is the new
  secret to protect.
- Releases gain a `.sig` file per asset.
- The verifier lives behind the port `IUpdateSignatureVerifier`, so the
  updater (#3) depends only on the Application layer.
- Verification of the `.sig` is independent of GitHub, so it keeps working if
  releases are mirrored.
