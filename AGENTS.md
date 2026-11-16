# AGENTS.md

Guidance for AI coding agents and human contributors working on **Snapdate**.
This file is the single source of truth; tool-specific files (e.g.
[`.claude/CLAUDE.md`](.claude/CLAUDE.md)) only point here.

## Project in one paragraph

Snapdate is a Windows GUI app (single self-contained `.exe`, no runtime to install)
for non-technical users. It copies or moves photos from a source folder to a
destination folder and renames them by capture date (metadata, fallback file
timestamp) using a user-built placeholder pattern. UI in English and German.

## Roles

Agents work in one of three roles. The role is assigned in the prompt
("Role: Coder …"). **If your prompt does not name a role, ask which role you
have before doing anything else.** Then read your role file:

| Role | Does | File |
|---|---|---|
| **Orchestrator** | Plans, manages issues, delegates to Coders and Reviewers, integrates. Never writes code. Usually the main session. | [docs/agents/orchestrator.md](docs/agents/orchestrator.md) |
| **Coder** | Implements one task from a brief, with tests and docs. | [docs/agents/coder.md](docs/agents/coder.md) |
| **Reviewer** | Reviews a Coder's change and reports findings. Never changes code. | [docs/agents/reviewer.md](docs/agents/reviewer.md) |

Flow: Orchestrator → Coder → Reviewer → (fixes by Coder) → Orchestrator integrates.
The rules below apply to every role.

## Read first

| Document | Content |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Scope, tech stack, architecture, security, milestones |
| [DESIGN.md](DESIGN.md) | Design tokens, layout, components, i18n, a11y |
| [CHANGELOG.md](CHANGELOG.md) | Release history (Keep a Changelog) |
| [docs/architecture.md](docs/architecture.md) | Layers, use cases, run sequence |
| [docs/security.md](docs/security.md) | Threat model and mitigations |
| [docs/build-and-release.md](docs/build-and-release.md) | Build, screenshots, release process |
| [docs/user-guide.en.md](docs/user-guide.en.md) · [.de.md](docs/user-guide.de.md) | End-user guides (keep both in sync) |
| `docs/adr/` | Architecture decisions — add one for every significant choice |

## Tech stack

.NET 10 LTS (SDK pinned in `global.json`) · C# · Avalonia 12 · CommunityToolkit.Mvvm ·
MetadataExtractor · xUnit v3 (Microsoft Testing Platform) · Avalonia.Headless for UI
tests and screenshots · self-contained single-file publish (`win-x64`, `win-arm64`).

## Architecture rules

- Layers: `Domain` ← `Application` ← `Infrastructure` / `App`.
  - `Snapdate.Domain` has **no** package or project references.
  - `Snapdate.Application` references only Domain; defines ports (interfaces).
  - Only `Snapdate.Infrastructure` touches the file system or MetadataExtractor.
  - Only `Snapdate.App` references Avalonia.
- No reflection-based serialization or DI magic — must stay **AOT/trim safe**
  (use source generators: `System.Text.Json` context, CommunityToolkit.Mvvm,
  compiled bindings `x:CompileBindings="True"`).
- No user-visible string literals in code or XAML. Add every string (English and
  German) to `src/Snapdate.App/Localization/strings.tsv` and run
  `pwsh scripts/dev/Update-Strings.ps1`; never edit the generated `.resx` /
  `LocalizedStrings.cs` by hand.
- Colours, spacing, radii come from theme resources defined per DESIGN.md —
  never hard-code them in views.

## Security rules (non-negotiable)

- Treat image files and their metadata as **untrusted input**.
- Every generated path goes through `FileNameSanitizer` and must resolve
  inside the destination root (`Path.GetFullPath` check).
- Never overwrite files. Never delete a source before the copy is verified.
- No network access, telemetry, or auto-update code.
- New dependencies need a justification in the PR and an ADR if non-trivial;
  pin exact versions in `Directory.Packages.props`; keep `packages.lock.json`
  updated; NuGet audit must pass.

## Commands

```bash
pwsh scripts/build/build.ps1 -Target Test      # restore + build + tests
pwsh scripts/build/build.ps1 -Target Publish   # Native AOT exe(s) to artifacts/
pwsh scripts/build/build.ps1 -Target All       # full release pipeline
dotnet run --project src/Snapdate.App          # run the app in dev
dotnet run --project tools/Snapdate.AssetGen -c Release   # regenerate app.ico + doc screenshots
```

## Conventions

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/)
  (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `build:`, `ci:`, `chore:`),
  scope optional (`feat(pattern): add {camera} token`).
- **Versioning:** SemVer, single source in `Directory.Build.props`.
  Update `CHANGELOG.md` (`Unreleased` section) with every user-facing change.
- **Code style:** `.editorconfig`, nullable enabled, warnings as errors,
  file-scoped namespaces, one public type per file.
- **Tests:** domain logic is test-first; every bug fix gets a regression test.
- **Docs:** user-visible behaviour changes update `docs/` (EN and DE guides) and,
  when the UI changes, the screenshots (`tools/Snapdate.AssetGen`).
- **Public repo:** never commit personal data, machine paths or internal notes;
  internal-only material lives in the git-ignored `.local/` folder.
- **Package updates:** run `dotnet restore --force-evaluate` and commit the
  changed `packages.lock.json` files.

## Definition of done

Builds with zero warnings · all tests green · both languages present ·
works in light and dark theme · CHANGELOG updated · docs updated.
