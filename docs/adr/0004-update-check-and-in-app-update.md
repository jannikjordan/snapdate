# ADR 0004 — Update check and in-app update

- Status: accepted (part 1: the update check, issue #2; part 2: the verified in-app updater, issue #3;
  signatures: [ADR 0005](0005-update-signatures.md)).
- Date: 2026-11-16

## Context
Snapdate has had **no network access at all** (see [security.md](../security.md)). Users of a
desktop tool rarely check for new versions, so fixes (including security fixes) reach them late.
We want to tell them when a new version exists — without telemetry, without surprises and with an
off switch.

## Decision
**Update check (this ADR, part 1)**
- One endpoint only: `https://api.github.com/repos/jannikjordan/snapdate/releases/latest`.
  The code hard-checks scheme `https`, host `api.github.com` and the default port, also for
  redirects (followed manually, same host only, at most 3). Anything else is rejected.
- The request is a plain `GET` with `Accept` and `User-Agent: Snapdate/<version>`. No cookies,
  credentials, machine or user identifiers, no extra headers. (GitHub necessarily sees the IP address,
  as with any web request.)
- Automatic check on start at most **once per 24 h**; **on by default**, switchable off in the
  settings; a manual "check now" ignores the limit and the opt-out. The time is stored only after an
  answer was received, so an offline start does not suppress the next attempt.
- Hard limits: 10 s timeout, 1 MB response cap (declared and actual), cancellation honoured.
- Every failure (offline, DNS/TLS, timeout, HTTP error incl. 403/429 rate limit, oversized or
  malformed JSON, links that do not point into our own repository) yields "no information": the UI shows
  nothing and never an error.
- The answer is **untrusted**. JSON is parsed with a source-generated `System.Text.Json` context
  (trim/AOT safe). The release page and asset URLs must be `https://github.com/jannikjordan/snapdate/…`;
  asset names are restricted to safe characters.
- Drafts and pre-releases are ignored; the same or an older version is never offered
  (SemVer precedence, `0.2.0-rc.1 < 0.2.0`); a version can be "skipped" (hidden for automatic checks only).
- Two install kinds will exist. **Portable** builds only show "new version available" with a link to
  the release page. **Installed** builds (Inno Setup, issue #1) may later download and run the
  installer (issue #3, to be added here, including signature verification).
- Layering: `AppVersion` in Domain; `IReleaseFeed`, `IClock` ports and `UpdateCheckService` in
  Application; `GitHubReleaseFeed` in Infrastructure (the only place with `HttpClient`). Settings
  (`AutoUpdateCheck`, `LastUpdateCheckUtc`, `SkippedVersion`) extend `AppSettings`.

**In-app update (issue #3)** — only for *Installed* builds, only for a newer stable release, only the
installer that matches the process architecture. Core (no UI) in `InstallUpdateUseCase`.
- **Applicability.** `IInstallInfo` says Installed (marker file) **and** the marker is confirmed by the registry:
  `...\Uninstall\{D5F11F70-E97E-44D0-8573-E15843FA60B3}_is1`, value `InstallLocation`, in HKCU (per-user) or HKLM
  (all users), 64-bit and WOW6432Node views, must equal the folder of the running exe (normalized,
  case-insensitive). A forged marker without that entry is "not applicable" and the UI falls back to the release page.
  The release must not be a draft or pre-release and must be newer than the running version (no downgrade).
- **Asset selection.** Exactly `Snapdate-Setup-v<Version>-win-<x64|arm64>.exe` and the same name plus `.sig`
  from the feed's assets. Missing means "asset missing"; there is no fallback to another architecture.
- **Download** (`HttpUpdateDownloader`, Infrastructure, the only code that fetches installers):
  - GitHub serves release assets via a redirect from `github.com/.../releases/download/...` to its download CDN. The
    check's allow-list (`api.github.com`) is **not** loosened; the downloader has its own, explicit one.
  - Start URL must be exactly `https://github.com/jannikjordan/snapdate/releases/download/<tag>/<name>`
    (default port, no query, no user info, `<name>` equals the asset name).
  - Redirects are followed **by hand**, at most 5 hops. Every hop (and the start) must be `https`, default port,
    without user info, and its host must be on the **explicit allow-list**:
    `github.com`, `objects.githubusercontent.com`, `release-assets.githubusercontent.com`. Anything else is rejected
    without being requested.
  - No cookies, no credentials, no automatic redirects or decompression, `User-Agent: Snapdate/<version>` and
    `Accept` only; the status must be 200 and there must be no `Content-Encoding`.
  - **Size** must equal the size declared by the release **and** is enforced on the bytes actually read, not
    only on `Content-Length`: hard caps 300 MB (installer) and 1 KB (`.sig`). A truncated, longer or differently
    sized body is a failure. Timeouts: 15 s connect, 30 s without data, 30 min overall; cancellation honoured.
  - **Target.** A fresh random folder (GUID) below `%LOCALAPPDATA%\Snapdate\updates` (default ACL of the user
    profile). The file name is the validated asset name (safe characters, unchanged by `FileNameSanitizer`,
    resolved with `PathGuard.CombineInside`). On any failure the files and the folder are deleted.
  - **No temp-name-then-rename.** The file is written under its final name inside the private folder with
    `FileShare.None`, then re-opened for reading with `FileShare.Read` (see TOCTOU); a rename would need a
    delete-share, which is exactly the opening we want to avoid.
- **Verification.** The `.sig` is downloaded the same way (1 KB cap) and checked against the installer bytes with the
  key(s) compiled into the app (ADR 0005). An installer that fails verification is never executed: its folder is
  deleted and the result is "verification failed".
- **TOCTOU.** The verifier reads the installer from the **same read handle** (`FileAccess.Read`,
  `FileShare.Read`: others may read, nobody may write, rename or delete) that stays open until the installer process
  has exited (see Launch). Once the handle is open nothing can change the bytes between "checked" and "run". (The
  short window between finishing the write and opening the read handle is harmless: whatever is in the file at that
  moment is what gets verified.)
- **Junctions and symbolic links.** The launcher starts the installer by path, so that path must lead to the file we
  verified. `%LOCALAPPDATA%\Snapdate\updates`, its parent `Snapdate` and the per-run folder must not be reparse points
  (checked after creating the folder; otherwise the download fails and what was created is removed). In addition the
  final path of the held handle (`GetFinalPathNameByHandle`, source-generated P/Invoke, `\\?\` and `\\?\UNC\`
  prefixes stripped, 8.3 names expanded) must equal the expected full path, case-insensitively; a mismatch is a
  download failure. `CleanUpStaleDownloads()` does nothing when the root or its parent is a reparse point and never
  follows reparse-point folders inside it.
- **Tag folder.** The `<tag>` segment of the installer's and the `.sig`'s download URL must be exactly `v<Version>`
  of the release, otherwise the asset counts as missing.
- **Launch and waiting.** `ProcessInstallerLauncher` starts the verified installer with
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER|/ALLUSERS /RESTARTAPP` (scope from the marker),
  `UseShellExecute = true`, **no `runas`**. Snapdate never elevates itself and starts nothing but that installer.
  Inno's Setup loader runs as the invoking user; for an all-users update it shows the administrator prompt and
  relaunches itself elevated. So the launcher returns the started process (`IStartedInstaller`) and the use case
  **keeps the verified handle open and waits** until the installer exits or the app is shut down: exit code 0 gives
  `Completed`, a non-zero code (declined administrator prompt, Setup refusing a downgrade) gives `InstallerFailed` and
  the files are removed. Therefore the lock also covers the elevated relaunch. Contract for the UI (part B): **the app
  must not exit by itself after starting the update.** It stays open (progress/"installing" state) because Setup closes
  it through the Restart Manager when it needs the exe. When Snapdate is asked to close, the app signals the
  `appShutdown` token passed to `ExecuteAsync`, the call ends with `InstallerRunning`, the files are kept and the
  process exit releases the lock. The download token cancels only download/verification, never a started installer.
- **Cleanup.** `CleanUpStaleDownloads()` (to be called once at startup by the UI wiring, part B) deletes update
  folders (32 hex characters, no reparse points) whose newest write time is older than one hour; failures are ignored.
- **Installer refuses downgrades.** As cheap defence in depth `installer/Snapdate.iss` (`InitializeSetup`) refuses to
  install over a **newer** installed version (uninstall key `DisplayVersion` in HKCU or HKLM, compared as x.y.z; a
  pre-release suffix is ignored): silent runs exit with Setup's error code (so the updater reports `InstallerFailed`),
  interactive runs show a message (EN/DE).
- **Results, no exceptions** to the UI: `Completed`, `InstallerRunning`, `NotApplicable`, `AssetMissing`,
  `DownloadFailed`, `VerificationFailed`, `LaunchFailed`, `InstallerFailed`, `Cancelled` (cancellation of the download
  phase is caught inside the use case). The banner's *Update now* button is part B (UI wiring).
- Layering: ports `IUpdateDownloader`, `IDownloadedFile`, `IInstallerLauncher`, `IInstallLocationCheck`,
  `IStartedInstaller`, `IProcessArchitecture` and `InstallUpdateUseCase` in Application; `HttpUpdateDownloader`, `ProcessInstallerLauncher`,
  `RegistryInstallLocationCheck`, `ProcessArchitecture` in Infrastructure. No new packages (the registry API is part
  of the framework).

## Consequences
- The "no network" promise becomes "no network except this opt-out check and the user-triggered, verified
  updater". AGENTS.md, security.md and the privacy statements are updated accordingly.
- GitHub's unauthenticated API allows 60 requests/hour/IP; once a day per user is far below that, and
  rate-limit answers are silent.
- Any further network use (another host, another purpose) needs a new ADR.
- Found while implementing: `AppSettings` used `init` properties, and the source-generated JSON
  deserializer then drops property defaults for keys missing in older settings files (e.g.
  `IncludeSubfolders` became false). The properties are now `set`; a regression test covers it. This
  matters here because the update check must default to on.
