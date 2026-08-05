# Next Minecraft Launcher (NML)

A cross-platform **Minecraft launcher** built in **C# / Avalonia UI 11 + .NET 8**, aiming to
match HMCL and PCL while adding **first-class AI features**.

> **51 commits, 148 tests passing, 5 languages.** This is a real, functional launcher — not a shell.

## What works right now

### Core Engine (NML.Core)
- **Full vanilla launch pipeline:** Mojang version manifest parsing, SHA-1-verified multi-threaded
  downloads, vanilla installer (client.jar + libraries + assets + native extraction), offline +
  Microsoft device-code auth, Java runtime detection + Mojang JRT auto-download, command builder +
  process launcher with **live game console output**
- **5 modloaders:** Fabric, Quilt, Forge (with processor execution + type coverage), NeoForge, OptiFine
- **Authlib-injector:** Full external-login server support (HMCL's signature feature) — server list
  persistence UI, Yggdrasil login, and `-javaagent` injection at launch time
- **Instance management:** version-isolated game dirs, clone, import/export (zip bundles), share
  codes (base64), batch export/delete

### AI Features (NML.AICore)
- **Provider-agnostic streaming chat** (`IChatClient`): OpenAI-compatible SSE, Anthropic Messages API,
  local models (Ollama/LM Studio) — zero-cost default with BYOK for cloud
- **API keys encrypted** with Windows DPAPI (never in settings.json)
- **Crash diagnosis:** parses crash logs → focused LLM prompt → structured JSON diagnosis
- **Natural-language config:** function-calling tools (set_memory/version/modloader/java/resolution)
- **Mod recommendation:** retrieval-augmented (real catalog candidates → LLM ranks; hallucinated IDs dropped)

### Mod Ecosystem (NML.Data)
- **Dual-source search:** Modrinth (no key) + CurseForge (with key) via unified `IModCatalog`
- **Mod download/install** directly into the instance's mods/
- **Mod update detection** + **conflict detection** (duplicate IDs, mixed loaders) +
  **dependency checking** (missing deps, breaks conflicts via fabric.mod.json)
- **Batch enable/disable** all mods at once

### Launcher UI (NML.App)
- **7 pages** with sidebar navigation + smooth page transitions (PageSlide):
  Home / Download / Accounts / Mods / AI Assistant / Game Content / Settings
- **Custom frameless title bar** (PCL-style) with min/max/close
- **Theme system:** dark/light/system + **accent color picker** (7 presets)
- **Custom background image** (PCL-style wallpaper)
- **Splash screen** with fade-in/out boot animation
- **Skin management:** 3D rotatable textured skin preview (drag to rotate, flip toggle),
  skin upload (Mojang API), community skin library (MineSkin)
- **Game content management:** saves (backup/export/import/delete), screenshots (open/delete),
  resource packs (delete + pack.png thumbnails), mods (toggle/batch/config editor),
  launch log viewer, config file editor
- **Home page:** instance list, memory allocation slider, JVM auto-tune button,
  custom launch args, **live game console**, instance export/import/clone/share/batch operations
- **Download center:** full Mojang version manifest with search + type filtering
- **Accounts:** offline + Microsoft device-code + authlib-injector servers + skin preview

### Internationalization
- **5 languages, 210+ keys each:** 中文, English, 日本語, 한국어, Русский
- Live language switching via `{loc:Loc}` XAML extension
- All sidebar labels, page headers, and UI strings react to language changes instantly

### Other
- **JVM auto-tuning:** recommends GC strategy (ZGC/G1GC+Aikar) based on CPU cores + RAM
- **Auto-update check:** queries GitHub Releases API, semantic version comparison
- **Self-contained exe:** 93MB single-file Windows build available

## Architecture

```
src/
  NML.Core/     Engine: auth, download, modloaders, instances, Java, launch, skins, update, modpacks
  NML.Data/     Modrinth + CurseForge catalogs behind IModCatalog
  NML.AICore/   Provider-agnostic AI + features (crash analysis, NL config, mod recs) + secrets
  NML.App/      Avalonia desktop: 7-page UI + DI + i18n + theme system + splash
tests/
  NML.*.Tests/  xunit + FluentAssertions + NSubstitute — 148 passing
```

## Build & run

Requirements: **.NET 8 SDK**.

```bash
dotnet build NextMinecraftLauncher.sln -c Release
dotnet test  NextMinecraftLauncher.sln -c Release
dotnet run   --project src/NML.App -c Release
```

## Roadmap

| Done | Feature |
|---|---|
| ✅ | Cross-platform engine + 5 modloaders + authlib-injector |
| ✅ | AI assistant (crash diagnosis, NL config, mod recommendation) |
| ✅ | 7-page UI with theme system, splash screen, background image |
| ✅ | 5 languages (zh/en/ja/ko/ru), 210+ keys each |
| ✅ | Mod management (download/update/conflict/dependency/batch toggle) |
| ✅ | Game content management (saves/screenshots/packs/logs/configs) |
| ✅ | Instance management (clone/import/export/share/batch) |
| ✅ | Skin management (3D preview/upload/community library) |
| ⏳ | Mobile remote-management client |
| ⏳ | Per-modloader per-version compatibility matrix |

## License

MIT.
