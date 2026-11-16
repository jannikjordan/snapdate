# scripts/build

Build and release tooling (PowerShell 7.4+). Developer/CI only — not shipped.

```bash
pwsh scripts/build/build.ps1 -Target Test
```

```bash
pwsh scripts/build/build.ps1 -Target All -Version 0.1.0
```

| Target | Does |
|---|---|
| `Clean` | Removes `artifacts/`, `bin/`, `obj/` |
| `Restore` | `dotnet restore --locked-mode` (all runtime identifiers) |
| `Audit` | Fails on any vulnerable NuGet package (incl. transitive) |
| `Test` | Builds and runs all test projects (TRX in `artifacts/test-results`) |
| `Publish` | `win-x64` + `win-arm64` exe into `artifacts/publish/<rid>` |
| `Package` | Release exe/zip, `SHA256SUMS.txt`, SBOM into `artifacts/release/` |
| `Sign` | Authenticode signing — skipped unless `SNAPDATE_SIGN_PFX` is set |
| `All` | Clean → Restore → Audit → Test → Publish → Sign → Package |

`-Mode Auto|Aot|SingleFile` — Native AOT needs the MSVC linker (Visual Studio
Build Tools, "Desktop development with C++"). `Auto` falls back to a
self-contained single-file exe when it is missing. Releases always use `Aot`
(see [ADR 0002](../../docs/adr/0002-native-aot-and-locked-restore.md)).

GitHub releases are built by `.github/workflows/release.yml` when a `vX.Y.Z`
tag is pushed.
