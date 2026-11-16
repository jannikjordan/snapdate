# 🏗️ Architecture

Snapdate follows a small **clean / hexagonal architecture**: the domain knows
nothing about files, metadata libraries or the UI, so the important rules
(naming, sanitising, never overwriting) are pure and fully unit-tested.

```mermaid
flowchart TB
    subgraph App["Snapdate.App (Avalonia)"]
        V[Views · Theme · Localization] --> VM[ViewModels]
        VM --> S[Desktop services<br/>folder picker · launcher · thumbnails]
    end
    subgraph Application["Snapdate.Application"]
        UC1[ScanSourceUseCase] --> P[(Ports<br/>IFileSystem · IMetadataReader · ISettingsStore<br/>IReleaseFeed · IClock)]
        UC2[BuildPlanUseCase] --> P
        UC3[ExecutePlanUseCase] --> P
        UC4[UpdateCheckService] --> P
    end
    subgraph Domain["Snapdate.Domain (no dependencies)"]
        D1[NamePattern · TokenCatalog]
        D2[FileNameSanitizer · PathGuard]
        D3[UniqueNameResolver · CaptureDate]
        D4[AppVersion · SemVer]
    end
    subgraph Infra["Snapdate.Infrastructure"]
        I1[MetadataExtractorReader]
        I2[LocalFileSystem]
        I3[JsonSettingsStore]
        I4[GitHubReleaseFeed]
    end
    VM --> UC1 & UC2 & UC3
    Application --> Domain
    Infra -. implements .-> P
    App --> Infra
```

The dependency rules are enforced by `tests/Snapdate.Architecture.Tests`.

## Projects

| Project | Responsibility |
|---|---|
| `Snapdate.Domain` | Pattern parsing/rendering, placeholder catalogue, sanitising, path guard, duplicate-free names, `AppVersion` (SemVer). No package references. |
| `Snapdate.Application` | Use cases (scan → plan → execute, update check), ports, folder checks, settings model, UI language rules. |
| `Snapdate.Infrastructure` | MetadataExtractor adapter, safe local file system, JSON settings store, GitHub release feed (the only network code). |
| `Snapdate.App` | Avalonia UI: MVVM with CommunityToolkit source generators, compiled bindings, design tokens, EN/DE resources, composition root in `App.axaml.cs`. |
| `tools/Snapdate.AssetGen` | Developer tool: renders `app.ico` and the documentation screenshots headlessly from the real views. |

## The run, step by step

```mermaid
sequenceDiagram
    actor U as User
    participant VM as MainWindowViewModel
    participant Scan as ScanSourceUseCase
    participant Plan as BuildPlanUseCase
    participant Run as ExecutePlanUseCase
    U->>VM: choose source folder
    VM->>Scan: ScanAsync(source, recursive)
    Scan-->>VM: photos (streamed, metadata or file date)
    U->>VM: choose target / edit pattern / copy-move
    VM->>Plan: Build(photos, pattern, culture, target)
    Plan-->>VM: RenamePlan (preview)
    U->>VM: press "Copy N photos"
    VM->>Plan: Build again (fresh disk state)
    VM->>Run: ExecuteAsync(plan, progress, cancel)
    Run-->>VM: outcomes + CSV log path
```

## Key decisions

- [ADR 0001 — Avalonia for the user interface](adr/0001-avalonia-ui.md)
- [ADR 0002 — Native AOT releases and locked package restore](adr/0002-native-aot-and-locked-restore.md) (AOT part superseded)
- [ADR 0003 — Self-contained single-file exe instead of Native AOT](adr/0003-single-file-instead-of-native-aot.md)

## Conventions

- AOT/trim safe only: no reflection-based serialisation or DI, compiled XAML
  bindings, source-generated JSON and MVVM.
- UI text lives in `src/Snapdate.App/Localization/strings.tsv`; run
  `pwsh scripts/dev/Update-Strings.ps1` to regenerate the `.resx` files and the
  typed accessor.
- Colours, radii and fonts come from `Theme/Tokens.axaml` (see [DESIGN.md](../DESIGN.md)).
