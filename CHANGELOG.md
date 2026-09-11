# Changelog

All notable changes to this project are documented in this file. The format follows Keep a Changelog principles and Semantic Versioning.

## [0.18.0] - 2026-09-12

### Changed

- 为选中的搜索结果增加打开、资源管理器定位、复制路径和加入快捷入口操作区，同时保留双击与 Enter 打开。
- 将 Windows Shell 与剪贴板调用隔离到可测试适配器，完整路径作为单个参数传递，失败返回不含敏感路径的稳定提示。
- 失效目标禁用打开、定位和加入快捷入口，但仍允许复制路径；加入入口复用现有名称、图标、持久化和重复检查。
- 相同查询刷新按范围与路径恢复选择，关键词、类别、范围、字段或匹配模式变化时清除旧选择；设置和待办 schema 不变。

## [0.17.0] - 2026-09-11

### Changed

- 新增默认关闭的受控本地正文搜索，只读取授权范围内受编码、扩展名、单文件、文件数和语料总量限制的 `.txt`/`.md`。
- 正文与名称索引使用暂存提交、取消和开关代次保护；停用或移除范围立即清除派生正文，文件监视器同步新增、修改、删除和移动。
- 普通文字搜索补充正文命中，并保留名称/相对路径优先级；结果显示明确原因及最多 120 字符的折叠空白摘要，通配符和正则仍只匹配元数据。
- 在现有搜索设置中加入单独同意、索引统计、重建和停用清除操作；更新 PRD、技术设计、用户手册、测试计划和扩展原型状态。
- 设置继续使用 schema 5；正文索引属于本地派生 SQLite，不进入普通/自动备份、诊断、日志、更新或 AI 请求。

## [0.16.1] - 2026-09-09

### Changed

- 今日安排将未选中事项的既有计划合并为匿名占用时间；AI 与本地草稿均避让冲突，不发送其标识或正文。
- 本地安排从精确的下一刻钟开始，空闲不足时保持不写入；草稿显示避让数量。
- 批量应用和撤销继续使用原子写入与 `UpdatedAt` 保护，并分别提示草稿过期和旧撤销不能覆盖新编辑。
- GitHub 更新只使用已安全保存的凭据；未保存令牌不参与请求，临时检查失败保留已发现版本。
- 补齐历史候选 GitHub Pre-release 清单；每个已发布条目均提供便携版、安装版和 SHA-256，并完成远端下载复核。

## [0.16.0] - 2026-09-09

### Changed

- 新增“已逾期”和“未安排”筛选，“今天”同时识别已确认计划开始时间。
- 用户明确勾选 1～12 条待处理事项后，可用当前 AI 配置生成今天未来、带绝对偏移且互不重叠的时间块；未勾选事项不进入请求。
- 提供完全本地的截止优先 30 分钟安排；AI 和本地结果均先逐项预览，确认前不写数据。
- TodoStore 新增带 `UpdatedAt` 保护的原子批量更新/恢复；确认项整体保存，旧草稿整批拒绝，最近一批可整体撤销。
- `todos.json` 升级到 schema 4 并保留 `.pre-v4.bak`；新增 `plannedStartAt`，计划结束时间复用 `dueAt`。
- 修复明确清理个人数据时遗漏固定 GitHub 更新令牌和 `.pre-v5.bak` 设置迁移副本的问题。

## [0.15.0] - 2026-09-08

### Changed

- 新增带口令加密的完整 AI 配置包，迁移全部配置、活动项、自定义 Provider 预设和 API Key，导入后立即可用。
- 使用 PBKDF2-HMAC-SHA256 与 AES-256-GCM 加密并认证 `.aipet-ai-config`；错误口令、篡改、超限和无效字段在写入前拒绝。
- 完整导入先显示非敏感摘要，再以新凭据目标整体替换 AI 配置；凭据或预设写入失败时清理本次数据并保留原配置。
- 保留无 Key JSON 兼容迁移；扩展计划文件统一命名为 `<版本号>－功能扩展计划.md`，删除两个不符合命名规范的旧计划文件。
- 以好用和必要为原则规划今日安排、已选内容提炼、提醒冲突检查和脱敏诊断，不加入商业化功能。
- 提醒调度空闲回归测试改为等待真实启动信号后检查无自旋，消除高负载 CI 对固定延时的偶发误判。
- 新增 GitHub Release 在线更新：每次正式启动检查一次，可选 1～168 小时周期检查、系统代理和用户配置的 HTTPS 加速模板，失败不阻止启动。
- 支持私有仓库只读 GitHub 令牌并存入 Windows 凭据管理器；带令牌时只请求 GitHub 官方地址，避免令牌发送给第三方加速服务。
- 安装器按版本固定名称下载到本机更新目录，限制响应和文件大小，匹配 Release `SHA256SUMS.txt` 后才提示用户启动安装。
- 设置升级到 schema 5 并保存 `.pre-v5.bak`；修复提醒 UI 回归用例依赖当前钟点导致跨午夜误报的问题。

## [0.14.0] - 2026-09-08

### Changed

- 新增可配置的全局快捷键：默认 `Ctrl+Alt+Space` 打开搜索、`Ctrl+Alt+T` 快速新建待办，并在托盘提供等价入口。
- 快捷键使用 `RegisterHotKey`，不安装键盘钩子；保存前校验格式与重复项，注册冲突时原子回滚并给出可恢复提示。
- 新增每日本地自动备份，默认启用、24 小时最多一次、保留 7 份且可配置 1～30 份；支持立即创建和打开备份目录。
- 自动备份复用维护包校验与 SHA-256 清单，包含设置、位置、待办/提醒、快捷入口/图标和 Provider 预设，排除 Key、索引及日志；失败保留已有备份。
- 设置升级到 schema 4，并保留 `.pre-v4.bak`；同步 PRD、工程/技术设计、手册、测试计划和后续扩展计划。
- 当前为 0.14.0 候选；真实全局按键、独立桌面 UI、签名和物理环境门禁未全部通过，不创建正式标签或 Release。

## [0.13.0] - 2026-09-07

### Changed

- 按 EXT-00～06 接入七组发布证据、日历重复/多次提醒、重启事务恢复、AI 验证与无 Key 迁移、搜索监听和偏好、快捷管理及持久提醒中心。
- 统一未保存 AI 修改的鼠标/键盘/定向入口保护，修复提醒改期竞争、损坏数据覆盖、撤销索引任务和图标引用清理边界。
- 增加模块预览、中断回滚、跨午夜静默、通知汇总/多档稍后、卸载数据选择与安装位置自启清理。
- 在 tests/prototypes 单独评估受控 txt 索引和严格 2D 宠物包，不接入产品、不进入发行包；保留 RFC 和实机评估门禁。
- 使用 VERSION 统一全部程序程序集版本；同步 PRD、工程/技术设计、手册、测试、计划、候选说明和验证报告。
- 当前为 0.13.0 候选，真实 UI、签名及物理环境门禁未全部通过，不创建正式版本标签或 Release。

## [0.12.2] - 2026-09-05

### Documentation — 2026-09-06

- 根据当前未合并工作区重核所有现行文档，独立重写后续功能扩展计划；明确已接入、部分实现与未验证边界，历史报告保留当时结论。软件版本继续沿用该候选的 0.12.2。

### Changed

- Replace all popup dropdowns with visible in-window single-selection controls and restore the selected tab content to the accessibility tree.
- 统一页内选择器的分段底板、卡片层级、焦点和按压反馈，并将工具窗内生成的滚动条改为暖色窄轨道与清晰拖动状态。

### Fixed

- 将搜索范围、结果类别、AI 目标、待办筛选、主题、AI 模板、已保存配置和供应商八处弹出式下拉框改为页内单选按钮组或可见列表，点击后直接更新显示值与业务值。
- 选中状态同时使用勾选或下划线、文字字重和颜色表达；保留方向键、Tab 焦点与原生 WPF 单选语义，不再依赖独立弹层焦点。
- 恢复选中 Tab 内容的标准 `PART_SelectedContentHost`，使页面控件重新进入 UI Automation 与屏幕阅读器树。

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
