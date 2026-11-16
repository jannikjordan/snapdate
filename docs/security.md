# 🔒 Security

How Snapdate protects users, their photos and the release supply chain.
To report a vulnerability, see [SECURITY.md](../SECURITY.md).

## Threat model

| Asset | What could go wrong | Who/what could cause it |
|---|---|---|
| The user's photos | Lost, overwritten, half-copied | Bugs, crashes, power loss, disk full, name collisions |
| The user's file system | Files written outside the chosen folder | Hostile metadata (e.g. a camera model `..\..\Windows`) |
| The application process | Crash or code execution while parsing | Malformed or hostile image/video files |
| The user's privacy | Data leaving the computer | Telemetry, crash reporters; the update check is the only network access and sends no identifiers |
| The update check | Spoofed or hostile "latest release" answer, redirect to another host, oversized response, tracking | Network attacker, compromised redirect, hostile server |
| The release binary | Tampered or vulnerable exe | Compromised dependency, CI or download; a replaced file that the updater would run (update signatures) |

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
- Network use is limited to (a) opening our own **https** documentation links in
  the browser when the user clicks *Help* or *Project website* and (b) the
  **update check** described below.
- No telemetry, no crash upload. Settings stay in
  `%APPDATA%\Snapdate\settings.json`.

### Update check (network surface)
Decision and rationale: [ADR 0004](adr/0004-update-check-and-in-app-update.md).
- **Only one request**: `GET https://api.github.com/repos/jannikjordan/snapdate/releases/latest`,
  made from `Snapdate.Infrastructure` (`GitHubReleaseFeed`) only. Scheme, host and port are
  hard-checked; redirects are followed manually and only to the same host (max. 3).
- **Nothing identifying is sent**: `User-Agent: Snapdate/<version>` and `Accept`; no cookies,
  tokens, machine or user ids. GitHub sees the IP address like for any web request.
- **Opt-out, rare**: on by default, can be switched off; automatic checks at most once per day.
- **The response is untrusted**: 10 s timeout, 1 MB cap, source-generated JSON parsing, version
  must be valid SemVer, page links must be `https://github.com/jannikjordan/snapdate/…` (no `.`/`..` path segments), asset
  links must be exactly `…/releases/download/<tag>/<name>` with `<name>` equal to the asset name, and
  asset names are restricted to safe characters and exclude Windows reserved device names. Drafts, pre-releases, equal or older versions are
  ignored. Any failure (offline, 403/429 rate limit, malformed data) is silent.
- **No code is run or downloaded by the check.** Portable builds only link to the release page.
  The installer download of Installed builds (issue #3) requires signature verification
  (issue #4) and will be documented here when it exists.
- Threats considered: spoofed/man-in-the-middle answer (TLS, host allow-list (`api.github.com` only),
  link validation, no automatic execution), malicious redirect (rejected), oversized or hostile JSON
  (cap, source-generated parser, size/time limits), tracking (no identifiers), API rate limiting (once a day).

### Process hardening
- Runs as the normal user (`asInvoker` manifest), never asks for admin rights.
- Self-contained single-file build: the .NET runtime and the native graphics
  libraries are bundled and loaded only from the app's own per-user extraction
  folder, never from `PATH` or the working directory (no DLL planting;
  see [ADR 0003](adr/0003-single-file-instead-of-native-aot.md)).
- Every release candidate is started with `--self-test` from an empty folder
  before it is packaged.
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
  a **CycloneDX SBOM** and — once the repository is public — **build provenance attestations**
  (`gh attestation verify <file> --repo jannikjordan/snapdate`).
- **Update signatures**: every release file gets a detached ECDSA P-256 / SHA-256
  signature (`<file>.sig`) made in the release workflow with a private key that
  only the signing step can read (secret `SNAPDATE_UPDATE_SIGNING_KEY`). The
  matching public key is compiled into the app, so a future in-app updater only
  runs files that we signed. The build verifies every signature right after
  signing. Details and key rotation: [ADR 0005](adr/0005-update-signatures.md).
- Authenticode (code signing certificate) is **not** used yet; optional signing
  is wired into the build script, and SignPath Foundation is planned once the
  repository is public.
## Verifying a download

```powershell
Get-FileHash .\Snapdate-v0.1.0-win-x64.exe -Algorithm SHA256
```

Compare the result with `SHA256SUMS.txt` from the same release, or verify the
provenance with the GitHub CLI:

```bash
gh attestation verify Snapdate-v0.1.0-win-x64.exe --repo jannikjordan/snapdate
```
