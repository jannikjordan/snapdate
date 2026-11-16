# ADR 0001 — Avalonia for the user interface

- Status: accepted
- Date: 2026-11-16

## Context
Snapdate targets non-technical Windows users and must look polished, ship as a
single portable exe and stay trim/AOT friendly. Candidates: WinForms, WPF,
WinUI 3, Avalonia.

## Decision
Use **Avalonia 12** with the Fluent base theme plus our own design tokens and
control styles (`src/Snapdate.App/Theme`), compiled bindings everywhere and
CommunityToolkit.Mvvm source generators.

## Consequences
- Full control over look and feel; light/dark via theme dictionaries.
- Trimming (and Native AOT) supported; reflection-based patterns are not allowed.
- `Avalonia.DesignerSupport` is excluded from published output (IDE-only, not
  trim-safe; see `Snapdate.App.csproj`).
- WinForms/WPF rejected (dated look, no AOT); WinUI 3 rejected (fragile
  unpackaged single-file deployment).
