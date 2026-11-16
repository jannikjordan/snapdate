# ADR 0006 — Inno Setup installer next to portable exes

- Status: accepted
- Date: 2026-11-16
- Related: [ADR 0003](0003-single-file-instead-of-native-aot.md) (single-file exe), [ADR 0004](0004-update-check-and-in-app-update.md) (in-app update), [ADR 0005](0005-update-signatures.md) (update signatures). Issue #1.

## Context
Snapdate was only shipped as a portable exe. Non-technical users expect a
program that appears in the Start menu, has an entry in Settings → Apps, can be
uninstalled, and — for the planned in-app updater (#3) — can be replaced by a
newer version without the user doing anything by hand.

## Decision

### Inno Setup 6
Installers are built with [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`installer/Snapdate.iss`). Alternatives considered:

- **MSI (WiX)**: heavier tooling (WiX v4/v5 toolchain, XML authoring, MSI
  upgrade/patch rules) for a one-file app; silent per-user installs are
  possible but fiddly.
- **MSIX**: needs a code-signing certificate that Windows trusts to install at
  all, and changes the file-system and registry semantics of the app.
- **Hand-written installer / ClickOnce**: more code to own and secure.

Inno Setup is a single free compiler, has a per-user/all-users model, a
decent German translation, Restart-Manager based "close the running app",
silent switches and a stable upgrade story keyed on a fixed `AppId`. It is a
build-time tool only: nothing of it enters the app, no new NuGet dependency.
The Inno Setup licence is free of charge; its non-commercial banner applies to
this open source project.

On CI, `scripts/build/Ensure-InnoSetup.ps1` uses an Inno Setup that is already
on the runner; otherwise it downloads the official installer of a pinned version
(URL and SHA256 from the winget manifest of `JRSoftware.InnoSetup`, not taken
from the download itself), runs it only if the hash matches, and installs it per
user. The remaining trust is in JRSoftware (the Inno Setup authors and their
release on GitHub) and in winget's manifest; a compromised upstream release
would still be built into our installers, which is why the installers are
smoke-tested and every release file is signed by us (ADR 0005).

### One installer per CPU, plus portable exes
Release assets: `Snapdate-Setup-vX.Y.Z-win-x64.exe` and `...-win-arm64.exe`
(recommended) and the unchanged portable single exes, now named
`Snapdate-vX.Y.Z-win-x64-portable.exe` / `...-win-arm64-portable.exe`. The zip
packages are dropped: they held only the exe plus README, CHANGELOG and
LICENSE, which the release page and repository already provide (the installer
also installs the LICENSE). The x64 installer uses `x64compatible` (also
installs on ARM PCs through emulation); the arm64 one is `arm64` only.

### Scope and upgrades
`PrivilegesRequired=lowest` with `PrivilegesRequiredOverridesAllowed=dialog
commandline`: the first interactive install asks *only for me* (default, no
UAC, `{autopf}` resolves to `%LOCALAPPDATA%\Programs`) or *for all users*
(UAC, `Program Files`). The `AppId` GUID is fixed forever;
`UsePreviousAppDir` / `UsePreviousPrivileges` make a newer installer upgrade
in the existing scope and folder without asking. Settings in
`%APPDATA%\Snapdate` are never touched by install or upgrade. The uninstaller
asks whether to delete them (default keep) only for per-user installs. An
all-users uninstall runs elevated, and `{userappdata}` may then be another
administrator's profile, so it keeps the settings without asking; silent
uninstalls always keep them.

**Scopes are separate installs.** `UsePreviousPrivileges` means an interactive
Setup that finds an existing installation reuses its scope and folder without
asking. Forcing the other scope on the command line (`/ALLUSERS` over a per-user
install or `/CURRENTUSER` over an all-users one) is possible and creates a
**second** installation, because the uninstall key of the `AppId` lives in
`HKCU` for per-user and in `HKLM` for all-users installs. Setup does not detect
that. The in-app updater therefore always passes the scope from the install
marker, and the unattended smoke test only covers the per-user scope (all-users
needs UAC and is only tested by hand).

### Install marker
The installer writes `snapdate-install.ini` (`[Install]`, `Scope=user|all`)
next to the exe. The app reads it (`IInstallInfo` / `FileInstallInfo`) to tell
installed from portable, strictly: anything unexpected means portable. The
marker only selects UI and update behaviour; it is not trusted for security
(whoever can write next to the exe could forge it, and all downloads are
signature-verified). A registry lookup was rejected: the marker travels with
the folder, needs no registry access from the app and is trivial to test.

### Silent mode for the updater
`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` plus `/CURRENTUSER` or `/ALLUSERS`
(from the marker) keeps scope and folder and shows no dialog; a custom
`/RESTARTAPP` switch starts Snapdate again after a silent install (as the
original, non-elevated user). Exact command lines are in
[build-and-release.md](../build-and-release.md). The installer closes a running
Snapdate through the Restart Manager.

### Authenticode and update signatures
If `SNAPDATE_SIGN_PFX` is set, the installers are signed with `signtool` after
compilation (the exe inside is signed earlier). The uninstaller stub inside the
installer is **not** Authenticode-signed for now: that needs Inno's `SignTool`
integration. Authenticode is not used yet at all, so SignTool support will be
added together with SignPath. Independent of that, every release file including
the installers gets the detached ECDSA signature from ADR 0005, which is what
the updater verifies before it runs an installer.

### Verification
`build.ps1 -Target Installer` smoke-tests the installer of the host CPU:
silent per-user install into a temp folder, registry/Start menu/desktop
checks, `--self-test` of the installed exe, two in-place upgrades (one with the
app running, one with `/RESTARTAPP`), silent uninstall. It refuses to run when
the `AppId` is already registered. CI runs it on `main`, the release workflow
for every tag (arm64 on the native ARM64 runner).

## Consequences
- Releases contain more and larger files (installers about 18 MB each).
- The `AppId` can never change; changing the installer's identity would leave
  old installations orphaned.
- The all-users install path needs UAC and is only covered by manual testing.
- Inno Setup is a new build-time prerequisite for maintainers.
