# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-16

First public release. 🎉

Snapdate copies or moves your photos and renames them by capture date. It comes with an installer and tells you when a new version is out.

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
- 🪟 Custom title bar that behaves like a normal Windows title bar: drag to move, double-click to maximise or restore, and snap layouts.
- 💿 Installer (`Snapdate-Setup-vX.Y.Z-win-x64.exe` / `-win-arm64.exe`): installs Snapdate like a normal Windows program, with Start menu and optional desktop shortcut and an entry in Settings → Apps. Choose "only for me" (no administrator rights) or "for all users". A newer installer updates in place and keeps your settings; uninstalling asks whether to keep them. English and German.
- 📦 Portable version (`Snapdate-vX.Y.Z-win-x64-portable.exe` / `-win-arm64-portable.exe`): one self-contained exe each for `win-x64` and `win-arm64` (about 22 MB); no installation and no runtime needed.
- 🔔 Update check (#2): at most once a day, on start, Snapdate asks GitHub for the latest release and shows a slim banner. Installed copies get "Update now", "What's new", "Later" and "Skip this version"; portable copies get "Download", which opens the latest download page. A one-time notice explains this, and About has a "Check for updates automatically" switch (on by default) and a "Check now" button. Only the version request is sent; photos and personal data never leave your PC. Everything else works offline.
- ⬇️ In-app update (#3): on an installed copy, "Update now" downloads the new installer with a progress bar and a Cancel button, checks its signature against a key built into Snapdate and runs it silently (for an "all users" install, Windows asks for administrator approval first). Snapdate then closes and starts again in the new version. If anything fails (download, signature, declined approval), nothing is installed and the banner offers "Try again" and the download page. Setup also refuses to install over a newer version.
- 📖 README and user guides in English and German with screenshots and diagrams: installer and portable version, update check and in-app update, the Windows SmartScreen warning, privacy and uninstalling.

### Security
- Untrusted metadata can never create folders or write outside the target folder (sanitising + path guard).
- Pinned, locked and audited dependencies; SBOM and SHA-256 checksums for every release.
- Release files carry a detached ECDSA P-256 signature (`<file>.sig`) that Snapdate verifies with a public key built into it, so the in-app updater only runs files we signed.

[Unreleased]: https://github.com/jannikjordan/snapdate/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0
