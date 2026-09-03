# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

Version `0.11.0` is a runnable WPF/.NET 8 desktop companion with a formal CC0 square-companion identity across the executable, tray, shortcuts, and installer. It adds schema-backed local backup/restore and diagnostics, incremental authorized-folder monitoring, explainable paged search, multi-time recurring reminders, actionable tray reminder controls, system-aware themes, independent motion preferences, safer custom AI provider presets, and reproducible signing/provenance workflows.

The 0.11.0 release has 170 passing automated interaction and logic tests. Packaging uses a unique build workspace and atomic publication. A certificate can be supplied through protected environment variables to sign and verify the EXE and installer; the current release is explicitly unsigned because no certificate was available. Physical multi-screen/scaling, Explorer restart, and sleep scenarios remain environment-dependent checks and are reported as PASS/FAIL/SKIP by the system runner rather than inferred.

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

This validates structure, versioning, UTF-8 text, the current `src/AiPet.sln`, 170 interaction and logic tests, schema migration and selective backup, diagnostic privacy, search authorization/ranking/pagination, reminder recurrence, AI fake-server/profile safety, shortcuts, RGS interaction, settings persistence, popover lifecycle, and the test-before-save AI configuration state machine.

To run a development build:

```powershell
dotnet run --project src/AiPet.App/AiPet.App.csproj
```

Single-click the pet to open the companion popover, double-click to play an action, and drag beyond the movement threshold to reposition it. Right-click the pet to hide it or use the tray to restore it.

## Release artifacts

The 0.11.0 release provides:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`
- `dist/checksums/RELEASE_PROVENANCE.json`

The generated 0.11.0 assets and SHA-256 values are recorded in `dist/checksums/SHA256SUMS.txt`.
Release: [GitHub v0.11.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.11.0).

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.

Do not publish an installer-staging ZIP as an installer. Version 0.11.0 uses a genuine `setup.exe`; unsigned binaries can still trigger Windows SmartScreen. Configure `AIPET_SIGN_CERT_PATH` and optionally `AIPET_SIGN_CERT_PASSWORD`/`AIPET_SIGN_TIMESTAMP_URL` to enable signing and verification.
