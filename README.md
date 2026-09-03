# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.10.0` is a runnable WPF/.NET 8 desktop-pet source build. The transparent, draggable RGS character remains the primary surface; its cream-paper companion popover includes local search, shortcuts, settings, ordinary todos, and independent one-time reminder items. A reminder item can enable pet roaming, a bubble attached to the pet, or both; the bubble moves with the pet, successful delivery automatically completes only the reminder item, and ordinary todo reminders keep their existing complete/snooze/detail flow. A saved OpenAI-compatible configuration can turn one sentence into a structured create/change draft; a create draft with a reminder time but no due time becomes a reminder item with the pet bubble enabled by default. This release makes every shared dropdown use the actual selected object for both display and application, and adds named, persistent AI profiles that can be selected for use without re-entering fields.

The previous 720×760 multi-tab manager is no longer a runtime surface. Version 0.10.0 is the first formally tagged GitHub release of this implementation line and has 156 passing automated interaction and logic tests, including first-run search authorization, cancellable staging indexes, failure rollback, staged-data cleanup, and AI-profile deletion. The portable ZIP and genuine Inno Setup installer are built in a unique run workspace before atomically replacing the current-version assets and SHA-256 manifest. Portable smoke, Windows credential round-trip, isolated install, installed smoke, and uninstall pass locally. The release is unsigned; Explorer-restart behavior, real notification suppression, sleep recovery, and the multi-display/scaling matrix remain documented verification debt rather than claimed passes.

## Start here

- AI agents and contributors: read [`AGENTS.md`](AGENTS.md).
- Product requirements: read [`docs/Windows桌面宠物产品需求文档_PRD.md`](docs/Windows桌面宠物产品需求文档_PRD.md).
- Engineering rules: read [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md).
- Technical design: read [`docs/TECHNICAL_DESIGN.md`](docs/TECHNICAL_DESIGN.md) (stack, modules, data, threads, secrets, packaging).
- User manual: read [`docs/USER_MANUAL.md`](docs/USER_MANUAL.md).
- Test plan: read [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md) (P0 acceptance mapping, environment, coverage).
- Future extension candidates: read [`docs/后续可扩展功能项清单.md`](docs/后续可扩展功能项清单.md).
- Software version: read [`VERSION`](VERSION).

## Validation

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
```

This validates structure, versioning, UTF-8 text, the current `src/AiPet.sln`, 156 interaction and logic tests, search authorization before enumeration, cancellable/rollback-safe indexes and staged-data cleanup, AI profile deletion and duplicate-name rejection, todo/reminder persistence and state transitions, shared dropdown selections, named AI profile switching, confirm-before-write AI drafts, shortcuts, RGS interaction, scoped filename search, settings persistence, popover lifecycle, and the test-before-save AI configuration state machine.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

The 0.10.0 release provides:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

The generated 0.10.0 assets and SHA-256 values are recorded in `dist/checksums/SHA256SUMS.txt`.
The formal release is available at [GitHub Releases](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.10.0).

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish an installer-staging ZIP as an installer. Version 0.10.0 uses the genuine `setup.exe`; unsigned binaries can still trigger Windows SmartScreen, and the remaining system-interaction matrix is tracked in the test plan and future-extension checklist.
