# Next Minecraft Launcher (NML)

A cross-platform **Minecraft launcher** built in **C# / Avalonia UI 12**, aiming to
match HMCL and PCL while adding **first-class AI features**.

> Status: **M0 — project skeleton.** The launcher engine, mod ecosystem and AI
> features are scaffolded as projects but not yet implemented. See the roadmap
> below.

## Highlights (planned)

- **Cross-platform** — one C# / XAML codebase for Windows, macOS and Linux
  (mobile as a remote-management client later).
- **PCL-grade UI** — Avalonia's Skia self-drawing enables a fully themed, custom
  window chrome on every OS.
- **AI-native** — crash-log diagnosis, retrieval-grounded mod recommendation, and
  natural-language configuration, via a BYOK / local-model design.
- **Zero-cost by default** — works out of the box with local models (Ollama /
  LM Studio); cloud keys are opt-in.

## Architecture

```
src/
  NML.Core/     Engine: auth, download, modloaders, instances, Java, launch (UI-free)
  NML.Data/     API clients: Modrinth, CurseForge, Mojang
  NML.AICore/   Provider-agnostic AI + features (crash analysis, recs, NL config)
  NML.App/      Avalonia desktop entry point (DI + MVVM)
tests/
  NML.*.Tests/  xunit + FluentAssertions + NSubstitute
```

Layering rule: `NML.Core`, `NML.Data` and `NML.AICore` are plain .NET libraries
with **no UI dependency**, so they are fully unit-testable and reusable on every
platform. The Avalonia UI lives only in `NML.App`.

## Build

Requirements: **.NET 8 SDK**.

```bash
dotnet build NextMinecraftLauncher.sln
dotnet test  NextMinecraftLauncher.sln
```

Run the desktop app:

```bash
dotnet run --project src/NML.App
```

## Roadmap

| Milestone | Goal |
|---|---|
| **M0** ✅ | Project skeleton, DI, CI, runnable empty window |
| M1 | Launcher core: Microsoft/offline auth, vanilla + Forge/Fabric/Quilt download, Java auto-install, instances, launch |
| M2 | AI assistant: `IChatClient` abstraction, DPAPI key store, crash analysis, natural-language config |
| M3 | Mod ecosystem: Modrinth/CurseForge browse & download, AI mod recommendation |
| M4 | Polish: PCL-grade theme, save/screenshot/resource-pack management, i18n, auto-update |
| M5 | Mobile remote-management client (browsing / downloads / diagnostics, not on-device play) |
