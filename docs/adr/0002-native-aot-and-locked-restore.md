# ADR 0002 — Native AOT releases and locked package restore

- Status: accepted
- Date: 2026-11-16

## Context
Users must be able to run one file without installing anything. The supply
chain must be pinned and auditable.

## Decision
- Release builds use **Native AOT** (`PublishAot=true` in the app project) for
  `win-x64` and `win-arm64`. AOT needs the MSVC linker, which GitHub's
  Windows runners provide.
- Developer machines without the C++ tools fall back to a **self-contained,
  trimmed single-file** exe (`build.ps1 -Mode Auto`). Functionally the same,
  larger and slower to start; never used for official releases.
- All projects declare `RuntimeIdentifiers` so **one locked restore**
  (`--locked-mode`) covers every publish; publishing runs with `--no-restore`.
- NuGet audit treats every advisory (including transitive and SDK-provided
  packs) as an error. Example: SDK 10.0.302 shipped a vulnerable
  `Microsoft.DotNet.ILCompiler`; `global.json` therefore pins SDK **10.0.404**.

## Consequences
- Updating the SDK or a package means re-running `dotnet restore --force-evaluate`
  and committing the changed `packages.lock.json` files.
