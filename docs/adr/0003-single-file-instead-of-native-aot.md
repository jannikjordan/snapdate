# ADR 0003 — Self-contained single-file exe instead of Native AOT

- Status: accepted (supersedes the AOT part of [ADR 0002](0002-native-aot-and-locked-restore.md))
- Date: 2026-11-16

## Context
The first v0.1.0 release candidate was built with Native AOT. The exe crashed on
start on a machine where another program had put an old `libSkiaSharp.dll` on
the `PATH`:

> The version of the native libSkiaSharp library (88.1) is incompatible …

Native AOT compiles the managed code into the exe, but Avalonia's **native
libraries** (`libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`)
are not embedded — they must ship *next to* the exe. Shipping only the exe made
Windows search the DLL path and load a foreign, incompatible copy. Besides the
crash, that is a **DLL-planting risk**.

## Decision
- Releases are **self-contained single-file** executables
  (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, compression,
  partial trimming). One file of about 22 MB per architecture.
- The .NET host extracts the bundled native libraries to a per-user folder
  (`%TEMP%\.net\Snapdate\<hash>`) and loads them **from there by full path**,
  never from `PATH` or the working directory.
- Every published exe is started with `--self-test` from an **empty folder**
  in the build (`build.ps1`, CI and release). The self-test initialises the
  graphics stack, loads all views and both languages and exits; a non-zero exit
  fails the build. This would have caught the AOT problem.
- The code stays trim- and AOT-clean (analysers on, warnings are errors), so
  AOT can be reconsidered once Avalonia supports statically linked native
  libraries.

## Consequences
- No C++ toolchain is needed anywhere; local builds equal release builds.
- Start-up uses the JIT (about a second on a typical PC) instead of AOT.
- The lock files no longer contain ILCompiler packs.
