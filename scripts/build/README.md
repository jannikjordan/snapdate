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
| `Publish` | `win-x64` + `win-arm64` single-file exe into `artifacts/publish/<rid>`, plus self-test |
| `Package` | Release exe/zip, `SHA256SUMS.txt`, SBOM into `artifacts/release/` |
| `Sign` | Authenticode signing — skipped unless `SNAPDATE_SIGN_PFX` is set |
| `All` | Clean → Restore → Audit → Test → Publish → Sign → Package |

`Publish` produces exactly one self-contained `Snapdate.exe` per runtime and
starts it with `--self-test` from an empty folder (on the matching CPU
architecture); a failing self-test stops the build. See
[ADR 0003](../../docs/adr/0003-single-file-instead-of-native-aot.md).

GitHub releases are built by `.github/workflows/release.yml` when a `vX.Y.Z`
tag is pushed.

## Release notes

`New-ReleaseNotes.ps1` renders `.github/RELEASE_NOTES_TEMPLATE.md` with the
matching `CHANGELOG.md` section, so every release has the same layout
(download table, EN/DE help links, changes, verification). The release
workflow uses it to create a **draft** release for review.

```bash
pwsh scripts/build/New-ReleaseNotes.ps1 -Version 0.1.0
```
