# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Project plan, design system, contributor guide, logo and app icon.
- Application shell: themed main window (light/dark) with English and German UI,
  chosen automatically from the Windows display language.
- Layered solution (Domain, Application, Infrastructure, App) with central,
  locked and audited package management.
- Build script `scripts/build/build.ps1` (restore, audit, test, publish, package,
  sign) and GitHub workflows for CI and releases.
- Asset generator for the app icon and documentation screenshots.

[Unreleased]: https://github.com/jannikjordan/snapdate/commits/main
