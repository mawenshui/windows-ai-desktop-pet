# Windows AI Desktop Pet

A Windows desktop-pet project planned around local file/application search, quick launch, tray controls, secure AI configuration, and extensible productivity pages.

## Current status

The repository is at the governance and requirements baseline (`0.1.0`). It currently contains the Chinese PRD, project rules, directory scaffold, and CI validation. Application source code and genuine installer artifacts have not been implemented yet.

Do not treat placeholder directories as a runnable application or publish fake binaries.

## Start here

- AI agents and contributors: read [`AGENTS.md`](AGENTS.md).
- Product requirements: read [`docs/Windows桌面宠物产品需求文档_PRD.md`](docs/Windows桌面宠物产品需求文档_PRD.md).
- Engineering rules: read [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md).
- Software version: read [`VERSION`](VERSION).

## Validation

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
```

At the current bootstrap stage, this validates structure, versioning, UTF-8 text, and AI-tool instruction entry points. Runtime tests and packaging become mandatory when application source code is introduced.

## Release artifacts

Future build adapters must produce:

- `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

See [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md) for the complete test, SemVer, packaging, GitHub, and Release workflow.
