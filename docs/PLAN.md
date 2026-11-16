# Snapdate — Implementation Plan

> Status: **Draft for approval** · Target version: **v0.1.0** · Last updated: 2026-11-16

Snapdate is a Windows desktop app that copies (or moves) photos from a source
folder into a destination folder and renames them by the date the photo was
taken, using a filename pattern the user builds from placeholders.

Audience: a **non-technical person**. Every decision below favours clarity,
safety (never lose a photo) and a "just double-click it" experience.

---

## 1. Goals & non-goals

**Goals (v0.1.0)**
- Single portable `Snapdate.exe` — no installer, no .NET/Python runtime needed.
- Pick source + destination folder (button or drag & drop).
- Read capture date from metadata (EXIF / XMP / QuickTime / PNG text chunks),
  fall back to file timestamps.
- Visual filename pattern builder with placeholders and a live example.
- Preview of every rename *before* anything is written.
- Copy (default, safe) or move. Never overwrite existing files.
- UI in English and German, auto-selected from the Windows display language,
  fallback English, switchable at runtime.
- Custom, polished design (see [DESIGN.md](../DESIGN.md)).
- Reproducible release build via `scripts/build/` → GitHub Release artefacts.

**Non-goals (v0.1.0)**
- Editing metadata, image conversion, cloud upload, auto-update, network access
  of any kind, macOS/Linux builds (the stack allows them later).

---

## 2. Technology decisions

| Concern | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14, SDK pinned to 10.0.404 (runtime 10.0.15) in `global.json` | Current LTS (support until Nov 2028). .NET 11 (GA 2026-11-10) is STS with the *same* EOL; we stay on LTS until Avalonia officially validates 11. Re-evaluate for v0.2. |
| UI framework | **Avalonia 12.x** (latest 12.1.x at time of writing) | Fully stylable (no "grey Win32" look), compiled XAML bindings, supports trimming / Native AOT, MIT. WinUI 3 single-file deployment is fragile; WPF looks dated without heavy theming and cannot AOT. |
| MVVM | **CommunityToolkit.Mvvm** (source generators) | AOT-friendly, no reflection. |
| Metadata | **MetadataExtractor 2.9.x** | Mature, pure managed, reads EXIF/XMP/IPTC from JPEG, PNG, HEIC/HEIF, WebP, TIFF, GIF, BMP, many RAW (CR2/CR3/NEF/ARW/DNG/ORF/RW2), MP4/MOV. |
| Packaging | `dotnet publish` **Native AOT**, `win-x64` (+ `win-arm64`) | One native exe (~20–30 MB), fast start, nothing to install. Fallback: self-contained single-file + trimming if an AOT blocker appears. |
| Tests | xUnit v3 + FluentAssertions-free assertions (`Assert`), Avalonia.Headless for UI smoke tests | Keep dependency surface small. |
| Package management | Central Package Management (`Directory.Packages.props`) + `packages.lock.json` | Pinned, auditable versions. |

All versions are pinned exactly in `Directory.Packages.props`; the exact numbers
are verified against nuget.org at implementation time (no older majors).

---

## 3. Architecture

Clean/hexagonal layering — the domain knows nothing about Avalonia, the file
system, or MetadataExtractor.

```
            ┌──────────────────────────┐
            │  Snapdate.App (Avalonia) │  Views, ViewModels, Localization, Theme
            └────────────┬─────────────┘
                         │ uses
            ┌────────────▼─────────────┐
            │  Snapdate.Application    │  Use cases: ScanSource, BuildPlan,
            │                          │  ExecutePlan · Ports (interfaces)
            └────────────┬─────────────┘
                         │ uses
            ┌────────────▼─────────────┐
            │  Snapdate.Domain         │  Pure model: NamePattern, Token,
            │  (no dependencies)       │  CaptureDate, RenamePlan, policies
            └──────────────────────────┘
            ┌──────────────────────────┐
            │ Snapdate.Infrastructure  │  Implements ports: MetadataExtractor
            │                          │  adapter, FileSystem, Settings store
            └──────────────────────────┘
```

### 3.1 Domain (`Snapdate.Domain`)
- `NamePattern` — parsed pattern, e.g. `{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}`.
  Parser produces `Literal` and `Token` segments; validation errors are values,
  not exceptions.
- `Token` catalogue (v0.1.0):

  | Token | Meaning | Example |
  |---|---|---|
  | `{YYYY}` `{YY}` | Year | 2024 / 24 |
  | `{MM}` `{M}` | Month with / without leading zero | 07 / 7 |
  | `{MMM}` `{MMMM}` | Month name short / long (UI language) | Jul / July · Juli |
  | `{DD}` `{D}` | Day with / without leading zero | 04 / 4 |
  | `{ddd}` `{dddd}` | Weekday short / long | Sun / Sunday · So / Sonntag |
  | `{HH}` `{H}` | Hour 24h with / without leading zero | 09 / 9 |
  | `{mm}` `{m}` | Minute with / without leading zero | 05 / 5 |
  | `{ss}` `{s}` | Second with / without leading zero | 08 / 8 |
  | `{name}` | Original file name without extension | IMG_4711 |
  | `{camera}` | Camera model (sanitised, may be empty) | iPhone 15 |
  | `{counter}` | Running number, 3 digits | 001 |

  Tokens are **case-sensitive** (`MM` = month, `mm` = minute, as in .NET/Unicode
  date formats). **Everything outside `{…}` is literal text** — spaces, dashes,
  words, any character valid in a Windows file name — so patterns like
  `{YYYY}-{MM}-{DD} {HH}{mm}` → `2024-07-14 1532.jpg` or
  `Holiday {D}.{M}.{YYYY}` → `Holiday 4.7.2024.jpg` work. `{{` / `}}` escape a
  literal brace. Unknown tokens are a validation error shown inline, never
  silently dropped.

  The **extension is never part of the pattern**; it is always appended from
  the original file (lower-cased: `.JPG` → `.jpg`, configurable later).
  `/` inside a pattern creates sub-folders (e.g. `{YYYY}/{MM}/{YYYY}-{MM}-{DD}`).
- `CaptureDate` — value + `DateSource` (`ExifOriginal`, `ExifDigitized`,
  `Xmp`, `QuickTime`, `PngText`, `FileCreated`, `FileModified`).
  Fallback order: EXIF DateTimeOriginal → DateTimeDigitized → EXIF DateTime →
  XMP CreateDate → QuickTime CreationDate → earliest of file created/modified.
- `RenamePlan` / `RenameItem` — source path, target relative path, status
  (`Ready`, `Duplicate`, `Skipped`, `Error`), reason.
- `CollisionPolicy` — `AppendCounter` (default: `name (2).jpg`), `Skip`.
  **Overwrite is not offered.**
- `FileNameSanitizer` — see §5.

### 3.2 Application (`Snapdate.Application`)
- Ports: `IMetadataReader`, `IFileSystem`, `IClock`, `ISettingsStore`.
- `ScanSourceUseCase` — enumerate supported files (optionally recursive),
  streaming results (`IAsyncEnumerable`) so the UI fills progressively.
- `BuildPlanUseCase` — apply pattern, sanitise, resolve collisions against both
  the destination and other items in the same plan.
- `ExecutePlanUseCase` — copy/move with progress + cancellation, writes a
  CSV log (`snapdate-log-YYYYMMDD-HHMMSS.csv`) into the destination for
  traceability/undo.

### 3.3 Infrastructure (`Snapdate.Infrastructure`)
- `MetadataExtractorReader` — opens files read-only, `FileShare.Read`,
  size/time guards, catches all parser exceptions → falls back to file date.
- `LocalFileSystem` — copy via temp file in destination + atomic rename,
  preserves timestamps, verifies size (optional SHA-256 verify flag).
- `JsonSettingsStore` — `%APPDATA%\Snapdate\settings.json` (last folders,
  pattern, language, copy/move). Uses `System.Text.Json` source generation.

### 3.4 App (`Snapdate.App`)
- Avalonia 12, single `MainWindow` with custom title bar, MVVM.
- `Localization/Strings.resx` + `Strings.de.resx`, strongly-typed accessor,
  runtime switch via an observable `Loc` service.
- Language detection: `CultureInfo.CurrentUICulture` (Windows display language)
  → `de-*` ⇒ German, anything else ⇒ English. User choice persisted.
- Theme: custom `Snapdate.Theme` resource dictionaries (light + dark, follows
  Windows setting), tokens from DESIGN.md.

---

## 4. Repository layout

```
image-tool/
├─ AGENTS.md                  # Single source of truth for AI/dev agents
├─ DESIGN.md                  # Design system, layout, components
├─ CHANGELOG.md               # Keep a Changelog + SemVer
├─ README.md
├─ LICENSE
├─ SECURITY.md                # Vulnerability reporting, threat model summary
├─ global.json                # Pins .NET SDK
├─ Directory.Build.props      # Common props: nullable, warnings-as-errors, analyzers, version
├─ Directory.Packages.props   # Central package versions
├─ Snapdate.slnx
├─ .claude/CLAUDE.md          # → points to AGENTS.md
├─ .github/
│  ├─ workflows/ci.yml        # build + test on PR
│  ├─ workflows/release.yml   # tag v* → scripts/build → GitHub Release
│  └─ dependabot.yml
├─ assets/branding/           # logo.svg, icon.svg, app.ico (multi-size)
├─ design/mockup/             # Approved HTML mockup (reference only)
├─ docs/
│  ├─ PLAN.md                 # this file
│  ├─ architecture.md
│  ├─ user-guide.en.md / user-guide.de.md
│  ├─ placeholders.md
│  ├─ security.md             # threat model & mitigations
│  ├─ build-and-release.md
│  └─ adr/                    # Architecture Decision Records (0001-avalonia.md, …)
├─ scripts/build/
│  ├─ build.ps1               # dev/CI entry point (restore, test, publish, package)
│  └─ README.md
├─ src/
│  ├─ Snapdate.Domain/
│  ├─ Snapdate.Application/
│  ├─ Snapdate.Infrastructure/
│  └─ Snapdate.App/
└─ tests/
   ├─ Snapdate.Domain.Tests/
   ├─ Snapdate.Application.Tests/
   ├─ Snapdate.Infrastructure.Tests/  # sample images with known metadata
   └─ Snapdate.App.Tests/             # headless UI smoke tests
```

---

## 5. Security considerations

Images are **untrusted input**; folder names and metadata end up in paths.

| Risk | Mitigation |
|---|---|
| Path traversal via metadata (`{camera}` = `..\..\Windows`) | Every token value sanitised: strip `\ / : * ? " < > \|`, control chars, leading/trailing dots & spaces; collapse `..`. Final path is `Path.GetFullPath`-checked to be **inside** the destination root. |
| Reserved device names (`CON`, `NUL`, `COM1`, …) | Detected per path segment → prefixed with `_`. |
| Overlong paths | Long-path aware (`\\?\` not needed on .NET, but manifest `longPathAware`); segments capped at 200 chars. |
| Data loss | Copy is default; never overwrite; write to temp + atomic rename; move only after successful copy + size check; CSV log for audit/undo. |
| Destination inside source (recursive loop) | Blocked with a friendly message. |
| Symlinks / junctions | Not followed during scan (`AttributesToSkip = ReparsePoint`). |
| Malformed / hostile image files (parser bugs, decompression bombs) | MetadataExtractor reads metadata only (no pixel decoding); per-file size cap and timeout; all exceptions caught → file-date fallback. Thumbnails decoded at reduced size via Avalonia `DecodeToWidth`. |
| Supply chain | Few dependencies, exact pins, `packages.lock.json` with `--locked-mode` in CI, NuGet audit (`NuGetAudit=true`, `NuGetAuditMode=all`, fail on high), Dependabot, GitHub Actions pinned by SHA, SBOM (CycloneDX) per release, SHA-256 checksums published. |
| Tampered binaries | Release built only in GitHub Actions; artefact attestation (`actions/attest-build-provenance`); Authenticode signing hook in build script (cert supplied via secret, optional). |
| Privacy | No network, no telemetry, no crash upload. Settings stored locally only. |
| Hardening | Native AOT + default CET/ASLR/DEP; `TreatWarningsAsErrors`, .NET analyzers at `latest-recommended`. |

---

## 6. Build & release (`scripts/build/`)

`scripts/build/build.ps1` (PowerShell 7 — developer/CI tooling only; the shipped
product is the exe) with steps selectable via `-Target`:

1. `Clean` → `Restore` (locked mode) → `Audit` (fail on vulnerable packages)
2. `Test` (all test projects, TRX + coverage)
3. `Publish` — Native AOT for `win-x64` and `win-arm64`, version from
   `Directory.Build.props` (or `-Version` / git tag)
4. `Package` — into `artifacts/release/`:
   - `Snapdate-v0.1.0-win-x64.exe`, `Snapdate-v0.1.0-win-arm64.exe`
   - `Snapdate-v0.1.0-win-x64.zip` (exe + README + LICENSE + CHANGELOG)
   - `SHA256SUMS.txt`, `sbom.cdx.json`
5. `Sign` (optional, skipped without certificate)

`.github/workflows/release.yml`: on tag `v*.*.*` → run `build.ps1 -Target All`
→ attest → create GitHub Release with the files above and the matching
CHANGELOG section as release notes.

Versioning: SemVer, start at **0.1.0**; CHANGELOG follows *Keep a Changelog*;
commits follow *Conventional Commits*.

---

## 7. Milestones

| # | Milestone | Deliverable |
|---|---|---|
| M0 | Plan & design approval | PLAN.md, DESIGN.md, AGENTS.md, logo, HTML mockup ✅ |
| M1 | Skeleton | Solution, projects, CPM, CI, build script, themed window shell ✅ |
| M2 | Domain | Pattern parser, tokens, sanitiser, collision logic — fully unit tested |
| M3 | Metadata & FS | Reader adapter with sample-image tests, safe copy/move |
| M4 | UI | Folder pickers, pattern builder, preview list, run + progress, results |
| M5 | i18n & polish | EN/DE, dark mode, animations, accessibility (keyboard, screen reader names, contrast) |
| M6 | Release v0.1.0 | Docs, user guides, signed/attested GitHub Release |
