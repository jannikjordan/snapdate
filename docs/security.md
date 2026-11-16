# 🔒 Security

How Snapdate protects users, their photos and the release supply chain.
To report a vulnerability, see [SECURITY.md](../SECURITY.md).

## Threat model

| Asset | What could go wrong | Who/what could cause it |
|---|---|---|
| The user's photos | Lost, overwritten, half-copied | Bugs, crashes, power loss, disk full, name collisions |
| The user's file system | Files written outside the chosen folder | Hostile metadata (e.g. a camera model `..\..\Windows`) |
| The application process | Crash or code execution while parsing | Malformed or hostile image/video files |
| The user's privacy | Data leaving the computer | Telemetry, update checks, crash reporters |
| The release binary | Tampered or vulnerable exe | Compromised dependency, CI or download |

## Mitigations

### Never lose a photo
- **Copy is the default**; move asks for confirmation.
- **Never overwrite**: every target name is checked against the plan *and* the
  disk; existing names get ` (2)`, ` (3)` … Copies use `FileMode.CreateNew` and
  `File.Move(…, overwrite: false)`, so a file that appears at the last moment is
  still never replaced.
- **Atomic copies**: data is written to a temporary file in the target folder,
  flushed, size-verified and only then renamed into place. A cancelled or
  failed copy leaves no partial file.
- **Safe moves**: on the same drive a move is an atomic rename; across drives
  the original is deleted only after a verified copy.
- **Audit log**: every run writes a CSV log (old → new) into the target folder.
  Values starting with `= + - @` are prefixed to prevent spreadsheet formula
  injection.

### Untrusted input
- Image and video files are parsed by
  [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet),
  **metadata only** — no pixel decoding. Files are opened read-only and every
  parser exception is caught (fallback: file date).
- Thumbnails are decoded only for common formats (JPEG, PNG, WebP, GIF, BMP)
  up to 40 MB, off the UI thread, with limited parallelism.
- Every placeholder value is sanitised (`FileNameSanitizer`): path separators,
  control and reserved characters are replaced; `.`/`..` segments, trailing
  dots/spaces and reserved device names (`CON`, `NUL`, `COM1` …) are
  neutralised; segment length is capped.
- Every final target path goes through `PathGuard.CombineInside`, which rejects
  anything that would resolve outside the chosen target folder.
- Scanning skips hidden/system files and **does not follow symbolic links or
  junctions**.
- Implausible dates (before 1900 or in the future) are ignored.
- The settings file is size-limited and parsed with source-generated
  `System.Text.Json`; corrupt settings fall back to defaults.

### Privacy
- No network code apart from opening our own **https** documentation links in
  the browser when the user clicks *Help* or *Project website*.
- No telemetry, no update checks, no crash upload. Settings stay in
  `%APPDATA%\Snapdate\settings.json`.

### Process hardening
- Runs as the normal user (`asInvoker` manifest), never asks for admin rights.
- Native AOT build: no JIT, no runtime code generation, smaller attack surface;
  ASLR/DEP/CET compatible by default.
- Trim/AOT analysers are enabled and warnings are errors.

### Supply chain
- Few dependencies: Avalonia, CommunityToolkit.Mvvm, MetadataExtractor (+ their
  transitive packages). Versions are **pinned centrally** in
  `Directory.Packages.props`, with transitive pinning.
- `packages.lock.json` for every project; CI restores with `--locked-mode`.
- **NuGet audit** fails the build on any known vulnerability, including
  transitive and SDK-provided packages (this caught a vulnerable ILCompiler
  shipped with an older SDK — see [ADR 0002](adr/0002-native-aot-and-locked-restore.md)).
- The .NET SDK is pinned in `global.json`.
- GitHub Actions are pinned to **commit SHAs**; workflows use least-privilege
  `permissions`; Dependabot keeps NuGet packages and actions current.
- Releases are built only in GitHub Actions and ship with **SHA-256 checksums**,
  a **CycloneDX SBOM** and **build provenance attestations**
  (`gh attestation verify <file> --repo jannikjordan/snapdate`).
- Optional Authenticode signing is wired into the build script.

## Verifying a download

```powershell
Get-FileHash .\Snapdate-v0.1.0-win-x64.exe -Algorithm SHA256
```

Compare the result with `SHA256SUMS.txt` from the same release, or verify the
provenance with the GitHub CLI:

```bash
gh attestation verify Snapdate-v0.1.0-win-x64.exe --repo jannikjordan/snapdate
```
