# Changelog

All notable changes to this project are documented in this file. The format follows Keep a Changelog principles and Semantic Versioning.

## [0.12.1] - 2026-09-04

### Changed

- Prevent dropdown popups from triggering tool-window auto-hide, preserve all eight selections, and harden real-input regression coverage.

### Fixed

- 修复工具窗口启用失焦自动收起时，下拉弹层可能被当作外部窗口，导致用户尚未完成选择就收起并仍显示默认项的问题。
- 全部八个下拉框继续使用 WPF 原生模板和对象级 `SelectedItem` 绑定；展开期间暂停自动隐藏，按 Esc 时优先只关闭下拉框。

## [0.12.0] - 2026-09-03

### Changed

- Fix all shared dropdown selections, add template-based AI configuration creation, and establish the 0.12 desktop UI, compatibility, signing, and performance validation foundations.

## [0.11.0] - 2026-09-03

### Added

- 新增设置 schema v3 迁移备份、按模块备份恢复、可重建数据清理和白名单诊断导出。
- 新增授权目录增量监视、可解释相关性排序、分页加载与应用入口规范化去重。
- 新增多次提醒、每日/每周/工作日/自定义星期重复规则、自适应调度和托盘提醒操作。
- 新增系统/浅色/深色/高对比度主题、独立动态效果偏好，以及 Provider 能力/安全预设与 fake-server 兼容测试。
- 新增 CC0“方块伙伴”正式图标，统一应用 EXE、托盘、快捷方式与安装器身份。
- 新增系统 E2E runner、发布烟雾脚本、可选代码签名/验签和 Release provenance。

### Changed

- 路线图逐项标记已实现、部分实现、环境待验证与 RFC 暂缓状态，避免把外部门禁项目误报为完成。

## [0.10.0] - 2026-09-03

### Added

- 新增首次搜索授权卡，预选桌面、文档和下载；确认前零目录枚举，可取消候选或稍后设置。
- 新增搜索范围六态、累计进度、取消/重试/重新索引，以及失败或取消时保留上一份成功索引的 staging 事务切换。
- 新增 AI 配置删除确认、凭据与内存 Key 清理，以及切换/切页/退出时的未保存修改保护。
- 新增托盘实时状态刷新、Explorer `TaskbarCreated` 恢复和最长 5 秒的后台任务安全退出。
- 新增按发布债务、可靠性、搜索、界面、AI/待办和 RFC 探索分层的后续可扩展功能清单。

### Changed

- 支持以配置名称保存多组 AI 配置，并通过“已保存配置”切换当前使用项；旧版单配置自动迁移为 `legacy` 配置。
- 发布打包改用 `build/<version>/<run-id>` 唯一 staging，候选校验完成后才更新当前版本便携包、安装包和 SHA-256 清单。

### Fixed

- 统一修复搜索范围、结果类别、待办状态、AI 操作目标、AI 配置和供应商下拉框的显示与实际选择链路。
- 修复已保存 AI 配置重新测试成功后保存按钮仍不可用的问题；测试结果现在可以持久化验证时间。
- 修复删除搜索范围时可能残留 staging 索引行的问题，正式索引、暂存索引和范围记录现在在同一事务内清理。
- 修复便携版与安装版资源复制时产生 `assets/pets/pets` 二级目录的问题，确保发布包可直接发现 RGS 宠物资源。

## [0.9.0] - 2026-08-29

### Added

- 新增与普通待办语义分离的提醒项；每项可独立启用桌宠漫游、桌宠气泡或两者，AI 仅包含提醒时间的创建草稿也会生成提醒项。
- 提醒气泡与桌宠同窗呈现，漫游、拖动时保持跟随；提醒项成功投递后自动进入已完成并保留投递状态与原定时间。

### Fixed

- 修复共享下拉框模板及选择绑定，搜索范围、结果类别、待办筛选、AI 目标和 AI 供应商均会显示并应用真实选中项。
- 保留 0.8.2 的 AI 供应商切换、待办中文输入显示、连接测试后保存状态和快捷入口空状态修复。

## [0.8.2] - 2026-08-29

### Changed

- 修复 AI 供应商选择不生效与待办中文输入文字不可见问题

## [0.8.1] - 2026-08-29

### Fixed

- 修复 AI 供应商下拉框选中项显示对象字符串且切换无效的问题，现在显示供应商名称并保留当前选择。
- 修复 AI 连接测试成功后“保存配置”仍不可点击的问题，验证通过后会及时刷新保存命令状态。
- 修复已有快捷入口时空状态背景和“文件夹＋”图标仍显示的问题。
- 修复统一测试入口可能从 `build/` 历史副本选取解决方案的问题，固定测试当前 `src/AiPet.sln`。
- 放宽 WPF 交互测试的冷启动等待上限，避免 GitHub Actions 首次加载桌面程序集时产生误报超时。
- 安装器构建适配器在未配置 PATH 时自动探测标准 Inno Setup 6 安装路径，保持环境变量和 PATH 覆盖优先。

## [0.8.0] - 2026-08-28

### Added

- 新增完整待办页：支持待处理、今天、未来 7 天和已完成筛选，以及手动创建、编辑、完成、恢复、仅取消提醒和删除。
- 新增 `%APPDATA%\WindowsAiDesktopPet\todos.json` 原子持久化、一次性提醒调度、页内提醒、托盘气泡、10 分钟稍后提醒和启动补发。
- 新增 OpenAI 兼容的一句话待办解析，覆盖创建、修改、完成、删除和稍后提醒；歧义、过去时间、同名目标和服务错误均先澄清或降级。
- 新增结构化确认卡、确认前零写入、AI 操作撤销和不落盘的内存会话清理。
- 新增 16 项待办、提醒、AI 解析和 ViewModel 单元测试。

### Changed

- 将 PRD 中 `TODO-01`～`TODO-08` 从后续候选冻结为 0.8.0 正式范围，并补充提醒运行条件、隐私边界、技术设计、测试矩阵和用户手册。

## [0.7.1] - 2026-08-28

### Changed

- Tighten homepage search text spacing beside the leading magnifier icon.

## [0.7.0] - 2026-08-28

### Added

- 快捷入口新增目录、拖放添加、自定义图标、名称/描述编辑、重新定位以及上下排序；失效目标可从管理菜单恢复或移除。
- 重复添加同一目标时提供取消或继续添加确认，快捷项悬停可读取用户填写的描述。
- 新增 Windows Shell 图标解析与缓存：应用显示自身图标，文件显示关联类型图标，目录显示文件夹图标；自定义图标失败时回退默认图标并保留编辑内容。
- 新增主页用户路径与布局回归测试，覆盖范围切换重查、异步结果隔离、图标校验、排序边界、失效恢复和紧凑布局。

### Changed

- 精简主页层级，将页签与常驻、置顶、关闭按钮合并到同一行，搜索区只保留范围和输入框，并把搜索结果移到快捷入口之前。
- 快捷入口改为单行水平滚动，避免大量入口压缩搜索结果区。

### Fixed

- 修复搜索输入文字垂直截断和范围下拉框选中项不可见的问题。
- 修复已有关键词时切换搜索范围不立即刷新，以及旧异步搜索结果可能覆盖新范围结果的问题。
- 修复诊断日志写入安装目录导致卸载残留的问题，运行日志现在只写入 `%LOCALAPPDATA%\WindowsAiDesktopPet\logs\`。

## [0.6.0] - 2026-08-28

### Changed

- AI 配置改为“字段完整 → 测试连接 → 测试通过后保存”；修改当前配置会立即使原测试结果失效。
- 工具气泡标题栏新增“常驻”和“保持置顶”状态按钮，两项偏好按账户持久化。

### Fixed

- 修复字段填写完整后“测试连接”仍不可点击的问题，命令可用状态现在随输入即时刷新。
- 连接失败或测试期间修改配置时保留全部输入，并持续禁用保存，避免未经验证的配置写入。

## [0.5.0] - 2026-08-28

### Changed

- 重构工具气泡为奶油米白与柔和暖橙视觉系统，以轻背景差和暖调阴影替代大部分分隔线。
- 统一按钮、搜索框、下拉框、密码框、复选框、快捷项、结果行和设置分组的圆角、间距、焦点、悬停与按压状态。
- 搜索区改为唯一的暖色视觉焦点，空快捷项和空结果状态改为居中提示；桌宠语句气泡同步新配色。

### Added

- 新增遵循 Windows 动画偏好的 2px 卡片 hover 变换，以及不改变布局的自动化回归测试。
- WPF 页面渲染测试支持通过 `AIPET_UI_SNAPSHOT_DIR` 输出主页、计划、扩展和设置页 PNG 快照用于视觉验收。

## [0.4.0] - 2026-08-27

### Changed

- Add interruptible idle companion behaviors and repair legacy JSON-path migration, AI provider presets, and search category filtering.

## [0.3.0] - 2026-08-27

### Changed

- Add scoped recursive filename search with opt-in wildcard and regex matching, repair pet character and pointer gestures, enforce front-facing rendering, and restore hidden pets from the tray.

## [0.2.3] - 2026-08-27

### Changed

- Stop timed-out single-instance waits from reopening the tool window or resetting its active tab.
- Keep the open popover attached to the pet through synchronous position events while suppressing focus-loss hiding during pet pointer interactions.
- Restrict pointer-facing pet poses to the front and side directions so the character never turns its back on the user.

## [0.2.2] - 2026-08-27

### Changed

- Clamp restored pet positions to visible monitor work areas, make pointer-driven and Ctrl+Tab page navigation deterministic, and add automated WPF interaction coverage.

## [0.2.1] - 2026-08-26

### Changed

- Anchor the tool popover to the rendered pet on its current DPI-aware monitor, prefer above/below placement without covering the character, and point the popover notch back to the pet.
- Replace the space-heavy left rail with a compact top navigation, clearer search hierarchy, visible four-character selection, and a persistent close control.
- Delay focus-loss hiding to prevent a second pet click from immediately reopening the popover; add WPF close/reuse lifecycle and placement boundary tests.

## [0.2.0] - 2026-08-26

### Changed

- Recenter the application on an interactive RGS desktop pet with a compact companion tool popover, four built-in character presets, real shortcut/range pickers, enabled tray actions, and corrected asset loading.

## [0.1.0] - 2026-08-26

### Added

- Added the initial Chinese product requirements baseline.
- Added canonical AI-agent governance and tool compatibility entry points.
- Added the project directory, testing, versioning, packaging, and GitHub Release specification.
- Added automated structure and UTF-8 validation for the documentation-only bootstrap stage.
- Added an asset-licensing gate that keeps unapproved third-party visual references out of Git and release packages.
- Added the technical design, user manual, and test plan drafts that translate the PRD V1.1 requirements into a runnable WPF/.NET 8 baseline. These documents remain pre-freeze and must be re-validated on the first runnable build.
- Documented the AI provider product decision: default provider DeepSeek; built-in presets for DeepSeek / Zhipu / Qwen / Moonshot / Qianfan / Hunyuan / Yi / SiliconFlow; a "Custom (OpenAI compatible)" entry for self-hosted or proxied services; preset fields are editable after selection so vendor-side updates do not require a release; the connection test must surface a clear "Connected" / "Failed" verdict with a categorized error.
- Confirmed four open product decisions: minimum supported Windows is 10 1809 x64; first-run search onboarding pre-selects the Desktop/Documents/Downloads candidates and waits for user confirmation before any disk IO; application entry sources are limited to Start Menu `.lnk` and `App Paths` registry entries; the user manual is shipped as offline HTML only with no online fallback in the MVP.
- Documented the pet asset policy: MVP uses RGS 8-Direction Characters (RGS_Dev, CC0) as the default pet; nailong stays as a personal reference only and is not committed, packaged, or released; Styloo Chibi Characters (CC0 3D) is held as a post-MVP candidate pending an RFC for 3D rendering and WPF compatibility. The strategy is recorded in `docs/PROJECT_SPEC.md` §2.1, and a `res/images/RGS_8Directional/` directory was scaffolded with `README.md` / `pet.json` / `SOURCE.txt` / `download.ps1` (default dry-run, requires `-Apply` after the user manually places the CC0 ZIP).
- First runnable build (0.1.0) shipped on .NET 8 / WPF:
  - Solution layout: `src/AiPet.sln` with 5 src projects (`AiPet.Common` / `AiPet.Storage` / `AiPet.Pet` / `AiPet.ToolWindow` / `AiPet.App`) and `tests/unit/AiPet.Tests.Unit`.
  - Locked the SDK to 8.0 via `global.json` (roll-forward to `latestMajor` so the dev box's installed 10.0.301 SDK can still drive a `net8.0-windows` build).
  - `AiPet.Pet` loads the four RGS characters (base / hero / skeleton / monster) from `assets/pets/RGS_8Directional/frames/`, runs an 8-direction cutout loop at 12 fps, and tracks the global cursor with a `GetCursorPos` P/Invoke (no WinForms dependency in the Pet project). The 3 missing 8-direction angles (left / up_left / down_left) are derived at runtime via `ScaleTransform(-1,1)` horizontal mirroring — no 8-direction physical PNGs are produced. Idle / jump / death states wired to PRD's `stateMachine` JSON; jump plays once on single-click and returns to idle; death plays once on failure and returns to idle.
  - `AiPet.ToolWindow` is a single WPF window with three pages (Home / Placeholder A / Placeholder B) switched via radio-button tabs; Esc hides the window. WIN-02 clamping is implemented so the body always stays inside the current monitor's work area.
  - `AiPet.App` provides single-instance (named `Local\WindowsAiDesktopPet_v1` Mutex), a WinForms `NotifyIcon` tray with "显示/隐藏" and "退出" (and disabled placeholders for 设置 / 开机自启 / 帮助 to be filled in later versions), assets resolution that walks up from `AppContext.BaseDirectory` (or honours `AIPET_ASSETS`), and graceful error handling that shows a balloon instead of crashing.
  - `AiPet.Storage` writes `%APPDATA%\WindowsAiDesktopPet\settings.json` (window position, preferred pet) and `layout.json` (pet pixel coordinates). Corrupted files fall back to defaults without overwriting the previous good copy (PRD DATA-01).
  - 34 xUnit unit tests cover `DirectionMapper` (8-direction angle + vector mapping, authored vs derived split, mirror invariant), `PetManifestLoader` (load + path substitution, mirror-for-derived direction), and `SettingsStore` (defaults / round-trip / corrupted-file recovery / layout round-trip). All pass.
  - `scripts/test.ps1 -CI` now discovers `src/AiPet.sln`, runs `dotnet test --configuration Release`, and passes on both pwsh 7 and Windows PowerShell 5.1 (the PS 5.1 + GBK path-decoding trap in `validate-project.ps1` was fixed by switching to `.NET BCL existence checks` + a wildcard expansion for the CJK PRD path).
  - `packaging/build-portable.ps1` and `packaging/build-installer.ps1` are implemented. The portable build runs `dotnet publish` for `win-x64`, copies `assets/pets/` next to the EXE, and produces `dist/portable/windows-ai-desktop-pet-v0.1.0-portable.zip` (3.25 MB). The installer build produces an `installer-staging.zip` (not a renamed `.exe`) because Inno Setup is not guaranteed on dev machines or CI; when it is available, the same staging can be wrapped without re-publishing. 0.1.0 does NOT ship a real Windows installer.
  - Headless smoke test on the built EXE: `Start-Process WindowsAiDesktopPet.exe -WindowStyle Hidden` survives 2 seconds of idle without exiting.
- After downloading the CC0 archive (`rgs-square-8dir.zip`, 3,234,518 bytes, SHA-256 `8171d8d2…be8d174`), inspected the actual content: 4 characters (`base`/`hero`/`skeleton`/`monster`) × 60 frames + 7-frame global death FX = 247 single-frame PNGs plus 5 author-provided spritesheets. The resource is **5-direction only** (down/down_right/right/up_right/up); the 3 missing 8-direction angles (left/up_left/down_left) are derived at runtime via `ScaleTransform(-1,1)`. There is no separate walk animation; `jump` (8 frames) doubles as the move/loop animation. Rewrote `pet.json` / `README.md` / `SOURCE.txt` / `docs/TECHNICAL_DESIGN.md` §6.1 / `docs/PROJECT_SPEC.md` §2.1 to reflect the actual layout, and added `organize.ps1` to flatten the author's messy extraction root (`Square characters animated 8 directions top down free cc0/...`) into the standard `frames/<character>/<action>_<direction>_<NN>.png` + `spritesheets/` + `License.txt` layout.
