# Contributing to Next Minecraft Launcher

Thank you for your interest in contributing! This guide covers the basics.

## Development Setup

1. **Prerequisites:** .NET 8 SDK, Git
2. **Clone:** `git clone https://github.com/weige0831/NextMinecraftLauncher.git`
3. **Restore:** `dotnet restore NextMinecraftLauncher.sln`
4. **Build:** `dotnet build NextMinecraftLauncher.sln -c Release`
5. **Test:** `dotnet test NextMinecraftLauncher.sln -c Release`
6. **Run:** `dotnet run --project src/NML.App -c Release`

## Architecture

The project follows clean layering:

- **NML.Core** — pure C# engine (no UI dependency). All launcher logic lives here.
- **NML.Data** — API clients (Modrinth, CurseForge, Mojang).
- **NML.AICore** — AI abstraction + features (no UI dependency).
- **NML.App** — Avalonia UI + DI wiring. The only project that references Avalonia.

**Rule:** Core/Data/AICore must never reference Avalonia or NML.App.

## Coding Standards

- C# 12, nullable reference types enabled, latest language version
- CommunityToolkit.Mvvm for ViewModels (source generators)
- xunit + FluentAssertions for tests
- Central package management (Directory.Packages.props)
- **0 warnings policy:** the build must be warning-free

## Adding a Feature

1. Engine logic → `NML.Core` (with unit tests)
2. UI → `NML.App` (ViewModel + XAML View)
3. i18n → add keys to all language files under `src/NML.App/Localization/`
4. Build + test → ensure 0 warnings, all tests pass
5. Commit with a descriptive message

## Adding a Language

1. Copy `en-US.json` to `{culture}.json` (e.g., `tr-TR.json`)
2. Translate all 217 keys
3. Verify key count matches: `grep -c '":' {culture}.json` should print `217`
4. Build + test — the language appears automatically in the Settings dropdown

## Pull Requests

- One feature/fix per PR
- Include tests for new engine logic
- Ensure CI passes (0 warnings, all tests green)
- Update README if the feature is user-facing

## License

By contributing, you agree your contributions are licensed under the MIT License.
