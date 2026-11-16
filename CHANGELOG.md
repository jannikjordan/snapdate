# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- 📁 Choose a photo folder and a target folder by click or drag & drop, optionally including sub-folders.
- 🕒 Capture date from photo metadata (EXIF, XMP, QuickTime/MP4, PNG), with the file date as clearly marked fallback.
- 🧩 Visual file name builder: clickable blocks for date, time, original name, camera and counter; leading zeros on or off (`07` / `7`); short or long month and weekday names; free text anywhere; sub-folders with `/`; quick presets; live example.
- 👀 Preview of every old → new name with thumbnails, date-source labels and filters.
- 🛡️ Safe copy (default) or move with confirmation: never overwrites, numbers duplicates `(2)`, verified temp-file copies, atomic moves on the same drive, cancel at any time.
- 🧾 CSV log of every run in the target folder.
- 🌍 English and German interface, chosen from the Windows display language and switchable at any time; help opens the user guide in the same language.
- 🌗 Light and dark theme following Windows.
- 💾 Remembers folders, name pattern, options, language and theme.
- 📦 One portable executable each for `win-x64` and `win-arm64` (self-contained, about 22 MB); no installation, works offline.
- 📖 User guides in English and German with screenshots and diagrams.
- 💿 Installer (`Snapdate-Setup-vX.Y.Z-win-x64.exe` / `-win-arm64.exe`): installs Snapdate like a normal Windows program, with Start menu and (optional) desktop shortcut and an entry in Settings → Apps. Choose "only for me" (no administrator rights) or "for all users"; a newer installer updates in place and keeps your settings; uninstalling asks whether to keep them. English and German. The portable single exe is still available as `Snapdate-vX.Y.Z-win-x64-portable.exe` / `-win-arm64-portable.exe`.

### Changed
- Release files are renamed: portable exes now end in `-portable.exe`, and the zip packages are gone (they only held the exe plus README, CHANGELOG and LICENSE).

### Fixed
- 👀 While the file name pattern is invalid, the preview no longer keeps showing the previous names as if they were current: the list is dimmed with the new names struck through, a red note explains that the names can't be built right now, and the "Numbered" tab is hidden until the pattern is valid again (#9).
- 🪟 The title bar now behaves like a normal Windows title bar: drag it to move the window, double-click to maximise or restore, and use snap layouts. The system window title no longer shows on top of the Snapdate logo.

### Security
- Untrusted metadata can never create folders or write outside the target folder (sanitising + path guard).
- Pinned, locked and audited dependencies; SBOM and SHA-256 checksums for every release.
- Release files now carry a detached ECDSA P-256 signature (`<file>.sig`) that the app can verify with a public key built into it, so a future in-app updater only runs files we signed.

[Unreleased]: https://github.com/jannikjordan/snapdate/commits/main
