# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.9.0` is a runnable WPF/.NET 8 desktop-pet source build. The transparent, draggable RGS character remains the primary surface; its cream-paper companion popover includes local search, shortcuts, settings, ordinary todos, and independent one-time reminder items. A reminder item can enable pet roaming, a bubble attached to the pet, or both; the bubble moves with the pet, successful delivery automatically completes only the reminder item, and ordinary todo reminders keep their existing complete/snooze/detail flow. A saved OpenAI-compatible configuration can turn one sentence into a structured create/change draft; a create draft with a reminder time but no due time becomes a reminder item with the pet bubble enabled by default. This release also repairs every shared dropdown selection path, including search scope, result category, todo filter, AI target, and AI provider.

The previous 720×760 multi-tab manager is no longer a runtime surface. The 0.9.0 source has 146 passing automated interaction and logic tests. `scripts/package.ps1` regenerated the portable ZIP, genuine Inno Setup installer, and SHA-256 manifest; final-package portable and isolated install/start/Credential Manager/uninstall smoke checks passed. Real notification suppression, reminder roaming/bubble motion, sleep recovery, multi-display behavior, code signing, the Git tag, and GitHub Release remain separate gates, so this is a release candidate until those checks finish.

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

This validates structure, versioning, UTF-8 text, the current `src/AiPet.sln`, 146 interaction and logic tests, todo/reminder persistence and distinct state transitions, one-time delivery deduplication and recovery, pet reminder channel preferences, all shared dropdown selections, AI draft parsing and confirm-before-write behavior, homepage shortcut icons and recovery, scope-switch isolation, RGS interaction, recursive scoped filename search, settings persistence, popover lifecycle, stay-open/topmost behavior, Chinese-input rendering, post-test save availability, shortcut empty-state visibility, and the test-before-save AI configuration state machine.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

The 0.9.0 release candidate produces:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

The generated 0.9.0 assets and their SHA-256 values are recorded in `dist/checksums/SHA256SUMS.txt`.

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish an installer-staging ZIP as an installer. The 0.9.0 candidate uses the genuine `setup.exe`; formal release still requires the remaining interactive and remote gates in `PROJECT_SPEC.md`.
