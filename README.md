# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.7.0` is a runnable WPF/.NET 8 desktop-pet source build. The transparent, draggable RGS character remains the primary surface; its companion popover uses a cream-paper and soft warm-orange design system, puts the search results before a compact shortcut strip, and provides persisted stay-open/topmost controls. Homepage shortcuts support files, folders, system icons, drag-and-drop adding, editing, relocating, ordering, and recovery of stale targets. AI settings follow a guarded workflow: the complete current configuration must pass a connection test before Save becomes available, and any edit invalidates that approval without clearing the user's input.

The previous 720×760 multi-tab manager is no longer a runtime surface. Local 0.7.0 candidates now include a self-contained portable ZIP, an interactive Inno Setup installer, SHA-256 checksums, and successful portable/install/start/uninstall smoke tests. Code signing, the Git tag, remote synchronization, and GitHub Release are still pending, so this remains a local release candidate rather than a published release.

## Start here

- AI agents and contributors: read [`AGENTS.md`](AGENTS.md).
- Product requirements: read [`docs/Windows桌面宠物产品需求文档_PRD.md`](docs/Windows桌面宠物产品需求文档_PRD.md).
- Engineering rules: read [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md).
- Technical design: read [`docs/TECHNICAL_DESIGN.md`](docs/TECHNICAL_DESIGN.md) (stack, modules, data, threads, secrets, packaging).
- User manual: read [`docs/USER_MANUAL.md`](docs/USER_MANUAL.md).
- Test plan: read [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md) (P0 acceptance mapping, environment, coverage).
- Software version: read [`VERSION`](VERSION).

## Validation

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
```

This validates structure, versioning, UTF-8 text, the .NET solution, 117 interaction and logic tests, homepage shortcut icons and recovery, drag-and-drop contracts, scope-switch requery isolation, compact layout text contracts, RGS character switching, click/double-click/drag routing, idle-behavior bounds, recursive scoped filename search, wildcard/regex/category filtering, settings persistence, front-facing rendering, popover lifecycle/repositioning, stay-open/topmost behavior, and the test-before-save AI configuration state machine.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

An eventual release must produce:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish the existing installer-staging ZIP as an installer. A release is not complete until a genuine `setup.exe` and its install/uninstall smoke tests exist.
