# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.12.1` is a runnable WPF/.NET 8 desktop companion. It keeps native WPF selection semantics for every dropdown and prevents popup interaction from being mistaken for tool-window deactivation, so selected values remain visible and effective across the home, todo, and settings pages.

The 0.12.1 candidate has 173 passing automated interaction and logic tests. Packaging uses a unique build workspace and atomic publication. A certificate supplied through protected release secrets signs and verifies the application, installer, and generated uninstaller; builds without a certificate remain explicitly unsigned. Physical multi-screen/scaling, Explorer restart, and sleep scenarios remain environment-dependent checks and are reported as PASS/FAIL/SKIP rather than inferred.

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

This validates structure, versioning, UTF-8 text, the current `src/AiPet.sln`, 173 interaction and logic tests, all eight dropdown selections, dropdown/auto-hide coordination, AI templates, schema migration, diagnostic privacy, search, reminders, shortcuts, settings persistence, and popover lifecycle. Dedicated runners are `scripts/test-ui-e2e.ps1`, `scripts/test-system-e2e.ps1`, and `scripts/test-performance.ps1`.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

The local 0.12.1 candidate provides:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`
- `dist/checksums/RELEASE_PROVENANCE.json`

The generated 0.12.1 assets and SHA-256 values are recorded in `dist/checksums/SHA256SUMS.txt`. A GitHub Release is intentionally not claimed until the independent interactive-desktop UI gate and remaining external release gates are closed.

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish an installer-staging ZIP as an installer. Version 0.12.1 uses a genuine `setup.exe`; unsigned binaries can still trigger Windows SmartScreen. Configure protected signing secrets to enable application, installer, and uninstaller Authenticode verification.
