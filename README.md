# Next Minecraft Launcher (NML)

A cross-platform **Minecraft launcher** built in **C# / Avalonia UI 11 + .NET 8**, aiming to
match HMCL and PCL while adding **first-class AI features**.

> Status: **M0–M4 complete.** The launcher engine, AI assistant, mod ecosystem, and a
> real launcher UI are all implemented, unit-tested (76 tests), and pushed.
> The desktop app launches and auto-diagnoses crashes. See the roadmap below.

## What works right now

- **Cross-platform** desktop app (Windows/macOS/Linux) from one C#/XAML codebase — Avalonia
  self-draws with Skia so the dark launcher UI looks identical on every OS.
- **Full vanilla-launch pipeline** (UI-free core, fully unit-tested):
  Mojang version manifest parsing, SHA-1-verified multi-threaded downloads, vanilla installer
  (client.jar + libraries + assets + native extraction), offline + Microsoft device-code auth,
  Java runtime detection + Mojang JRT auto-download, command builder + process launcher.
- **AI assistant (BYOK + zero-cost local):**
  - Provider-agnostic streaming chat (`IChatClient`) over OpenAI-compatible SSE (covers OpenAI
    cloud + **Ollama/LM Studio locally with no key**) and Anthropic Messages API.
  - API keys encrypted at rest with **Windows DPAPI** (never written to settings files).
  - **Crash diagnosis**: parses the launch log → focused prompt → structured JSON diagnosis
    (root cause, confidence, likely fixes, affected mods). Auto-runs on non-zero exit.
  - **Natural-language config**: constrained function-calling (set memory/version/modloader/
    java/resolution) — the model can only propose, the launcher applies after user confirm.
  - **Mod recommendation** (retrieval-augmented, anti-hallucination): real candidates from
    Modrinth/CurseForge APIs → LLM only ranks them; invented mod ids are dropped.
- **Mod ecosystem**: Modrinth (no key) + CurseForge (user key) catalogs behind one
  `IModCatalog` interface. Fabric + Quilt installers.
- **Launcher UI**: dark-themed main window with sidebar nav, instance list, version browser,
  offline login, and a launch button that runs the whole engine pipeline.
- **Version isolation** via per-instance game directories (`InstanceStore`).

## Architecture

```
src/
  NML.Core/     Engine: auth, download, modloaders, instances, Java, launch, game-content (UI-free)
  NML.Data/     Modrinth + CurseForge catalogs behind IModCatalog
  NML.AICore/   Provider-agnostic AI + features (crash analysis, NL config, mod recs) + secrets
  NML.App/      Avalonia desktop: launcher UI + DI wiring + Microsoft exchange
tests/
  NML.*.Tests/  xunit + FluentAssertions + NSubstitute — 76 passing
```

Layering rule: `NML.Core`, `NML.Data`, `NML.AICore` are plain .NET libraries with **no UI
dependency** — fully unit-testable and reusable on every platform (including the future
mobile client). The Avalonia UI lives only in `NML.App`.

## Build & run

Requirements: **.NET 8 SDK**.

```bash
dotnet build NextMinecraftLauncher.sln -c Release
dotnet test  NextMinecraftLauncher.sln -c Release
dotnet run   --project src/NML.App -c Release
```

## Roadmap

| Milestone | Status | Goal |
|---|---|---|
| **M0** | ✅ | Project skeleton, DI, CI, runnable window |
| **M1** | ✅ | Launcher engine: auth, download, modloaders, Java, instances, launch |
| **M2** | ✅ | AI assistant: provider abstraction, DPAPI secrets, crash analysis, NL config |
| **M3** | ✅ | Mod ecosystem: Modrinth/CurseForge catalogs, anti-hallucination AI recommendation |
| **M4** | ✅ | Launcher UI + game-content management + crash-diagnosis integration |
| **M5** | ⏳ | Mobile remote-management client (HTTP API + browse/download/diagnostics, no on-device play) |

## Security notes

- **API keys are never written to disk in plaintext.** Cloud-provider keys are encrypted with
  Windows DPAPI (CurrentUser scope) and stored separately from `settings.json`.
- **The recommender cannot hallucinate mods.** It only returns candidates that came from a real
  catalog API; any id the LLM invents is silently dropped.
- **AI features are individually gated** — core launcher functionality never depends on AI
  being reachable. With no provider configured, the launcher still installs and launches games.

## License

MIT (see LICENSE — to be added).
