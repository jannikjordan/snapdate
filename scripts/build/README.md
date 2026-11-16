# scripts/build

Build and release tooling. Developer/CI only — not shipped to users.

`build.ps1` (PowerShell 7) will be added in milestone **M1** (see
[docs/PLAN.md §6](../../docs/PLAN.md#6-build--release-scriptsbuild)).

| Target | Does |
|---|---|
| `Clean` | Removes `artifacts/`, `bin/`, `obj/` |
| `Restore` | `dotnet restore --locked-mode` |
| `Audit` | Fails on vulnerable NuGet packages |
| `Test` | Builds and runs all test projects |
| `Publish` | Native AOT `win-x64` + `win-arm64` |
| `Package` | Release files, `SHA256SUMS.txt`, SBOM into `artifacts/release/` |
| `Sign` | Authenticode signing (skipped without certificate) |
| `All` | Everything above in order |

Release on GitHub is triggered by pushing a tag `vX.Y.Z`
(`.github/workflows/release.yml`).
