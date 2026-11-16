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
| The in-app updater | Running a tampered, wrong-architecture, older or attacker-supplied installer; redirect to another host; partial or oversized download; forged install marker; file swapped after verification | Network attacker, compromised CDN link, local process writing next to the exe or into the update folder |
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
- **Opt-out, rare**: on by default, can be switched off (About); automatic checks at most once per day.
- **Informed first**: on the very first start Snapdate only shows a one-time notice ("checks GitHub
  once a day … turn this off in About") and makes **no request** in that session; the first check
  runs from the next start, so users can opt out before anything is sent. The notice is remembered
  in the settings file the moment it is shown. A result that arrives after the user switched the
  check off is discarded.
- **The response is untrusted**: 10 s timeout, 1 MB cap, source-generated JSON parsing, version
  must be valid SemVer, page links must be `https://github.com/jannikjordan/snapdate/…` (no `.`/`..` path segments), asset
  links must be exactly `…/releases/download/<tag>/<name>` with `<name>` equal to the asset name, and
  asset names are restricted to safe characters and exclude Windows reserved device names. Drafts, pre-releases, equal or older versions are
  ignored. Any failure (offline, 403/429 rate limit, malformed data) is silent.
- **No code is run or downloaded by the check.** Portable builds only link to the release page.
  The installer download of Installed builds is described in the next section.
- Threats considered: spoofed/man-in-the-middle answer (TLS, host allow-list (`api.github.com` only),
  link validation, no automatic execution), malicious redirect (rejected), oversized or hostile JSON
  (cap, source-generated parser, size/time limits), tracking (no identifiers), API rate limiting (once a day).

### In-app updater (installed builds only)
Decision and details: [ADR 0004](adr/0004-update-check-and-in-app-update.md), signatures:
[ADR 0005](adr/0005-update-signatures.md). Started by the user (the banner's *Update now*), never automatically.
- **Download hosts** (https, default port, no user info; checked for the start URL and every redirect hop, max. 5):
  `github.com`, `objects.githubusercontent.com`, `release-assets.githubusercontent.com`. The start URL must be exactly
  `https://github.com/jannikjordan/snapdate/releases/download/<tag>/<name>`. Redirects are never followed automatically.
- **No identifiers**: only `User-Agent: Snapdate/<version>` and `Accept`; no cookies or credentials.
- **Size**: must equal the size announced by the release and is enforced on the bytes read; caps 300 MB (installer),
  1 KB (`.sig`); timeouts and cancellation.
- **Signature first**: the installer is verified (ECDSA P-256, key compiled into the app) from the open read handle; a
  failure deletes the files and nothing is run. The same handle is held (no writers, renamers or deleters) until the
  installer process has exited (also covering Setup relaunching itself elevated for all-users updates). The app does
  not exit by itself while it waits; Setup closes it through the Restart Manager.
- **Private folder**: a random sub-folder of `%LOCALAPPDATA%\Snapdate\updates`; the file name is the validated
  asset name and the path is checked to stay inside the folder. The update root, its parent and the run folder must not
  be junctions or symbolic links, and the held file's final path (`GetFinalPathNameByHandle`) must equal the expected
  path. Stale folders are removed at every start (in the background, best effort).
- **No self-elevation**: Snapdate runs as the normal user and starts only the verified installer (`UseShellExecute`, no `runas`);
  for all-users installs Setup asks for administrator rights itself and relaunches itself elevated. A declined prompt
  ends Setup with a non-zero exit code, which the app reports (to be verified manually on an all-users install:
  not testable unattended).
- **Redirected profiles**: if `%LOCALAPPDATA%` itself sits behind a `subst` drive, junction or symbolic link, the
  update folder checks fail and the in-app update reports a failed download. That is by design (the release page is the
  fallback).
- **Install kind is cross-checked**: the marker file must be confirmed by the installer's uninstall registry entry
  (`InstallLocation` equals the exe folder); otherwise the silent path is not offered.

| Threat | Mitigation |
|---|---|
| Tampered installer or `.sig` (CDN, MITM, compromised release) | Signature check against the key in the app; failure deletes the file and never runs it |
| MITM, redirect to another host, downgrade to http | TLS, host allow-list on every hop, https and default port only, max. 5 hops, manual redirects |
| Downgrade to an older release | Only strictly newer stable versions are accepted and the asset name and tag folder embed the version; Setup itself refuses to install over a newer installed version (silent: error exit code). See the residual risk below |
| Wrong architecture | Exact `win-x64` / `win-arm64` asset for the process architecture; no fallback |
| Forged install marker (portable copy claims to be installed) | Registry `InstallLocation` must match the exe folder; otherwise release page only |
| Disk full, partial or oversized download | Exact size match on received bytes, hard caps, write errors mean failure; folder deleted |
| File swapped between check and run | Verified from a held read handle that denies writers, renamers and deleters until the installer (including its elevated relaunch) has exited |
| Junction or symlink redirecting the update folder | Root, parent and run folder must not be reparse points; the held handle's final path must equal the expected path; stale cleanup ignores reparse points |
| Declined administrator prompt | Setup exits non-zero, the app stays open and reports it (`InstallerFailed`); it does not vanish |
| Installer reaches another tag folder | The `<tag>` segment of both download URLs must be `v<Version>` of the release |
| Hostile asset name / path traversal | Safe-character names unchanged by `FileNameSanitizer`; `PathGuard` keeps the path in the random folder |
| Leftovers of crashed runs | Stale-folder cleanup at startup |

Residual risks, stated honestly:
- **Replayed older installer.** The signature covers the bytes only, not the version or the file name. Someone who
  can upload release assets could publish an older, genuinely signed installer under a newer name. The release
  account is therefore a trusted component (protect it, see ADR 0005 and the release process). The checks on version,
  asset name and tag folder do not stop that; the cheap extra defence is Setup refusing to install over a newer
  installed version (per-user and all-users entries are both checked). That check compares x.y.z only and ignores a
  `-suffix`, so a release-candidate installer can replace the final release of the same number.
- **Same-user attacker.** A process that already runs as the same user can do anything the user can, including
  replacing the Snapdate exe itself; the updater does not claim to defend against that. For **per-user** installs this
  is also the limit of the install-marker/registry cross-check. For **all-users** installs the exe and the registry
  entry sit in places a normal user cannot change, and the verified installer is protected by the held handle during
  the whole run including the elevated relaunch (while Snapdate is alive the lock is held; when the Restart Manager
  closes Snapdate, the elevated Setup is already running from the verified file, which cannot be written meanwhile). The junction and
  final-path checks cover the update folder in the user's profile.

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
Get-FileHash .\Snapdate-Setup-v0.2.0-win-x64.exe -Algorithm SHA256
```

Compare the result with `SHA256SUMS.txt` from the same release, or verify the
provenance with the GitHub CLI:

```bash
gh attestation verify Snapdate-Setup-v0.2.0-win-x64.exe --repo jannikjordan/snapdate
```
