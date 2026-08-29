# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.8.1` is a runnable WPF/.NET 8 desktop-pet source build. The transparent, draggable RGS character remains the primary surface; its cream-paper companion popover now includes a complete local todo and reminder page alongside search, shortcuts, settings, and the remaining extension placeholder. Todos support manual create/edit/complete/restore/delete, date filters, one-time reminders, ten-minute snooze, in-page alerts, tray balloons, and single recovery delivery after restart. A saved OpenAI-compatible configuration can turn one sentence into a structured create/change draft; ambiguous or past times are clarified, same-name targets require selection, and no todo data is written until the user confirms. This patch also fixes provider selection display, post-test AI save availability, and the shortcut empty-state overlay.

The previous 720×760 multi-tab manager is no longer a runtime surface. The 0.8.1 source and automated tests are complete; release artifacts are regenerated through `scripts/package.ps1`. Complete portable/install/start/reminder/uninstall smoke validation still requires a normal interactive Windows session because the restricted build session cannot fully observe Credential Manager, Windows notification policy, or the installer shell folders. Code signing, the Git tag, and GitHub Release remain separate gates, so this is a release candidate rather than a published release.

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

This validates structure, versioning, UTF-8 text, the current `src/AiPet.sln`, 138 interaction and logic tests, todo persistence and state transitions, one-time reminder deduplication and recovery, AI draft parsing and confirm-before-write behavior, homepage shortcut icons and recovery, scope-switch isolation, RGS interaction, recursive scoped filename search, settings persistence, popover lifecycle, stay-open/topmost behavior, provider selection rendering, post-test save availability, shortcut empty-state visibility, and the test-before-save AI configuration state machine.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

The 0.8.1 release candidate produces:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

The generated 0.8.1 assets are 76,479,145 bytes (portable) and 53,798,388 bytes (installer); their SHA-256 values are recorded in `dist/checksums/SHA256SUMS.txt`.

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish the existing installer-staging ZIP as an installer. A release is not complete until a genuine `setup.exe` and its install/uninstall smoke tests exist.
