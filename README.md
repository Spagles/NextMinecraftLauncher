# Next Minecraft Launcher (NML)

A cross-platform **Minecraft launcher** built in **C# / Avalonia UI 11 + .NET 8**, aiming to
match HMCL and PCL while adding **first-class AI features**.

> **125 commits, 154 tests, 0 warnings, 31 languages.** A real, functional launcher.

## What works right now

### Core Engine (NML.Core)
- **Full vanilla launch pipeline:** Mojang version manifest parsing, SHA-1-verified multi-threaded
  downloads, vanilla installer (client.jar + libraries + assets + native extraction), offline +
  Microsoft device-code auth, Java runtime detection + Mojang JRT auto-download, command builder +
  process launcher with **live game console output**
- **5 modloaders:** Fabric, Quilt, Forge (with processor execution + type coverage), NeoForge, OptiFine
- **Authlib-injector:** Full external-login server support — server list persistence UI,
  Yggdrasil login, and `-javaagent` injection at launch time
- **Instance management:** version-isolated game dirs, clone, import/export (zip bundles), share
  codes (base64), batch export/delete, remove single
- **Modpack support:** Modrinth .mrpack + CurseForge manifest import with mod resolution
- **Multiplayer server list:** saves favorites (servers.json) + live Server-List-Ping
  (MOTD, player count, latency, favicon), add/remove/reorder, one-click connect
  (`--server/--port` game args)

### AI Features (NML.AICore)
- **Provider-agnostic streaming chat** (`IChatClient`): OpenAI-compatible SSE, Anthropic Messages API,
  local models (Ollama/LM Studio) — zero-cost default with BYOK for cloud
- **API keys encrypted** with Windows DPAPI (never in settings.json or accounts.json)
- **Crash diagnosis:** parses crash logs → focused LLM prompt → structured JSON diagnosis
- **Natural-language config:** function-calling tools (set_memory/version/modloader/java/resolution)
- **Mod recommendation:** retrieval-augmented (real catalog candidates → LLM ranks; hallucinated IDs dropped)

### Mod Ecosystem (NML.Data)
- **Dual-source search:** Modrinth (no key) + CurseForge (with key) via unified `IModCatalog`
- **Mod download/install** directly into the instance's mods/
- **Mod update detection** + **conflict detection** (duplicate IDs, mixed loaders) +
  **dependency checking** (missing deps, breaks conflicts via fabric.mod.json) +
  **batch enable/disable** all mods

### Launcher UI (NML.App)
- **7 pages** with sidebar navigation + smooth page transitions (PageSlide):
  Home / Download / Accounts / Mods / AI Assistant / Game Content / Settings
- **Custom frameless title bar** (PCL-style) with min/max/close
- **Theme system:** dark/light/system + **accent color picker** (7 presets) + **custom background image**
- **Splash screen** with fade-in/out boot animation (crash-safe)
- **Skin management:** 3D rotatable textured skin preview (drag to rotate, flip toggle),
  skin upload (Mojang API), community skin library (MineSkin)
- **Game content management:** saves (backup/export/import/delete), screenshots (open/delete),
  resource packs (delete + pack.png thumbnails), mods (toggle/batch/config editor),
  launch log viewer + search, config file editor
- **Home page:** instance list, memory allocation slider, JVM auto-tune button,
  custom launch args, **live game console**, instance export/import/clone/share/remove/batch operations
- **Download center:** full Mojang version manifest with search + type filtering + install progress bar
- **Accounts:** offline + Microsoft device-code + authlib-injector servers + skin preview

### Internationalization
- **31 languages, 239 keys each (7,409 total translated keys):** 中文, English, 日本語, 한국어,
  Русский, Français, Español, Deutsch, Português, Italiano, العربية, Türkçe, हिन्दी, ไทย,
  Tiếng Việt, Bahasa Indonesia, Polski, Українська, Nederlands, Svenska, Čeština,
  Norsk, Suomi, Dansk, Magyar, Română, Azərbaycan, Afrikaans, עברית, Català, Қазақша
- Live language switching via `{loc:Loc}` XAML extension, persisted across restarts
- RTL support (Arabic, Hebrew) via automatic FlowDirection binding

### Security
- **Access tokens** (Microsoft + authlib-injector) encrypted via DPAPI — never plaintext on disk
- **AI API keys** encrypted via DPAPI — never in settings.json
- **No secrets** in git or build artifacts

### Other
- **JVM auto-tuning:** recommends GC strategy (ZGC/G1GC+Aikar) based on CPU cores + RAM
- **Auto-update check:** queries GitHub Releases API, semantic version comparison, clickable release link
- **Self-contained exe:** available via `dotnet publish`

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

## License

MIT.
