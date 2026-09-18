# Windows AI Desktop Pet 项目规范

| 属性 | 值 |
| :--- | :--- |
| 文档版本 | 2.9（2026-09-18 依赖安全与真实 AI 验证基线） |
| 软件基线版本 | 0.25.2 |
| 状态 | 生效；WPF/.NET 8 技术栈已落地，0.25.2 已通过必需门禁并发布为 GitHub 普通正式版和 Latest |
| 适用范围 | 源码、测试、文档、构建、打包、版本、GitHub 与 AI 工具协作 |
| 需求基线 | `docs/Windows桌面宠物产品需求文档_PRD.md` V3.7 |

## 1. 目的与适用原则

本规范把 PRD 转换为可执行的项目级约束，供开发者、CI 和各类 AI 工具共同遵守。根目录 `AGENTS.md` 是统一操作入口，本文件提供更详细的工程约定。PRD 负责定义“做什么”，本规范负责定义“代码和资料放在哪里、如何验证、如何升级版本、如何打包和发布”。

当前仓库已有 WPF/.NET 8 产品源码、CC0 RGS 桌宠、页内单选与四页工具窗、自动化测试，以及便携版和 Inno Setup 安装版。0.25.0 在 0.24.0 基线上新增本地专注会话、低打扰桌宠和四角色真实首帧卡片；0.25.1 在不改变 schema 的前提下修复完整主题往返切换、跨 runner 时区的墙钟逻辑，并让更新检查与下载显式共用 Windows/环境代理、准确区分匿名更新源 404；0.25.2 将 SQLite 依赖升级到无已知漏洞的 .NET 8 维护版本，并增加需用户明确授权、只输出脱敏结果的真实 AI 自动化入口。Todo、通知、复盘、设置和快捷入口 schema 保持不变，不增加第三方资源、网络上传、账号、遥测或权限。EXT-08 外部宠物包仍只在 tests/prototypes 评估。当前测试与产物见 CURRENT_STATUS.md。

本项目只设置正式版发布通道，不发布 Alpha、Beta、RC、Preview 或 GitHub Pre-release。设计和研发阶段使用“目标设计”“实现中”“待验收”等状态描述，不把未完成实现包装成可安装预览版；功能、测试、文档和发布门禁全部完成后，直接以普通 GitHub Release 发布对应 SemVer 正式版。正式版允许并要求同步发布可追溯源码、便携包、安装包和 SHA-256 清单。

## 2. 项目结构

```text
windows-ai-desktop-pet/
├─ AGENTS.md                       # 所有 AI/代理的统一规则入口
├─ README.md                       # 项目入口、状态和常用命令
├─ VERSION                         # 软件 SemVer 唯一来源
├─ CHANGELOG.md                    # 面向版本的变更记录
├─ src/                            # 产品代码
├─ tests/
│  ├─ unit/                        # 单元测试
│  ├─ integration/                 # 集成测试
│  ├─ e2e/                         # Windows 端到端测试
│  └─ fixtures/                    # 匿名测试数据
├─ docs/
│  ├─ Windows桌面宠物产品需求文档_PRD.md
│  ├─ PROJECT_SPEC.md              # 本文
│  ├─ TECHNICAL_DESIGN.md          # 当前技术栈与实现基线
│  ├─ USER_MANUAL.md               # 当前可操作能力与限制
│  ├─ TEST_PLAN.md                 # 实际入口、覆盖与待验收项
│  ├─ <version>－功能扩展计划.md   # 对应版本的实施和后续规划
│  └─ RELEASE_NOTES_<version>.md   # 每次 Release 的 UTF-8 说明
├─ assets/
│  ├─ README.md                    # 资产许可与归档要求
│  ├─ icons/                       # 应用和托盘图标
│  └─ pets/                        # 宠物视觉资源
├─ config/                         # 可提交的默认配置与 schema
├─ scripts/                        # 统一的 PowerShell 自动化入口
├─ packaging/                      # 安装器源定义与打包适配器
├─ build/                          # 可删除的临时构建输出
├─ dist/
│  ├─ portable/                    # 便携版 ZIP
│  ├─ installer/                   # 双击安装版 EXE
│  └─ checksums/                   # SHA-256 与资产清单
└─ .github/workflows/              # GitHub CI/Release 工作流
```

空目录以 `.gitkeep` 维持结构。`build/` 中除 `.gitkeep` 外全部忽略；`dist/` 是发布区，不得放调试构建、日志或未验收文件。

本地 `res/` 仅可保存尚未取得再分发授权的视觉参考，默认不属于仓库结构、Git 提交或安装包输入。角色 IP、官方图片、同人素材及第三方字体在迁入 `assets/` 前，必须按照 `assets/README.md` 记录来源、权利范围和必要署名；“来源仓库采用开源许可证”不能替代对素材本身权利的确认。

### 2.1 角色资产策略

发行默认资源为 `assets/pets/RGS_8Directional/`，manifest ID 为 `rgs-8dir`，默认 hero，含 base、skeleton、monster 四个预设。2026-09-06 本地核对为 247 帧 PNG 与 5 张整图；五个原绘方向可派生镜像，运行时前向偏置。资源清单、CC0-1.0 原文与引入记录见 `assets/README.md`、包内 `License.txt` 和 `SOURCE.txt`。

`res/` 继续只作为本地参考；nailong 等商业 IP 不得进入仓库、安装包和 Release。Styloo 3D 等历史候选不在当前实现或排期中，若重新提出须经过 RFC、许可及 WPF 常驻资源评估。

新角色必须有合法 SPDX、许可原文、作者/官方来源、下载/核对日期、SHA-256 和必要署名；包内路径、帧数、尺寸和内存上限需验证。发行运行时仍只使用内置 RGS；隔离原型验证严格 2D 子集、许可、路径、帧数和解码预算，不等于任意资源包的完整 schema 或第三方权利已审核。新增发行资源仍需独立 RFC 和资产门禁。

## 3. 文档体系

| 文档 | 责任 | 更新触发条件 |
| :--- | :--- | :--- |
| PRD | 产品范围、优先级、流程、验收标准 | 产品行为、范围或优先级变化 |
| `PROJECT_SPEC.md` | 目录、流程、版本、测试、发布规则 | 工程规则或工具链变化 |
| `TECHNICAL_DESIGN.md` | 技术栈、模块、数据模型、线程/进程、API | 架构或关键依赖变化 |
| `USER_MANUAL.md` | 用户可见操作和故障处理 | 可见行为、设置或安装方式变化 |
| `TEST_PLAN.md` | 测试矩阵、环境、数据和覆盖关系 | 需求、架构或风险变化 |
| `CHANGELOG.md` | 每个软件版本的用户可感知变更 | 每次版本升级 |
| `RELEASE_NOTES_<version>.md` | 当次正式 Release 说明、升级注意、校验信息 | 每次正式发布 |
| `CURRENT_STATUS.md` | 代码证据、文档入口、验证边界 | 代码状态核对 |
| `<version>－功能扩展计划.md` | 当版实施项、后续能力、依赖和验收标准 | 新版本规划或交付状态变化 |

中文文档统一使用 UTF-8。Markdown 链接使用仓库相对路径。扩展计划从 0.15.0 起固定使用 `<SemVer>－功能扩展计划.md`，当前状态文档只链接当前版本计划。代码、配置键、其他文件名、Git 提交标题和 Release 标签使用英文，避免跨终端编码差异。

## 4. 技术与模块边界

技术栈已经落地为 WPF/.NET 8；当前模块、数据格式、线程模型和协议见 `TECHNICAL_DESIGN.md`。后续架构变更必须同步维护以下内容：

1. GUI 框架与受支持的 Windows 版本/架构。
2. 宠物窗口、工具窗口、托盘、全局快捷键、设置、搜索、快捷项、AI 配置和通知调度的模块边界。
3. 配置、自动备份、搜索缓存、快捷项、待办、提醒、复盘与专注会话的数据模型及迁移策略。
4. Windows 账户安全存储方案，确保 AI Key 不进入普通配置和日志；用户主动导出的完整配置包必须经口令加密和完整性认证。
5. 搜索授权范围、索引/查询线程模型和取消机制。
6. 便携版与安装版构建工具、签名方式、安装/升级/卸载策略。
7. 单元、集成和 Windows UI 自动化测试框架。
8. 在线更新只能匿名读取指定公开仓库的稳定 Release；官方元数据失败后使用内置 API 线路，安装器按内置下载线路和官方地址逐级恢复。仓库、标签、资产、重定向主机、大小、Release digest 与 SHA-256 清单必须校验，且只能由用户确认后启动安装器；用户界面不得要求填写代理模板或更新凭据。
9. 搜索结果系统动作必须通过可测试适配器调用 Windows Shell 或剪贴板；路径作为参数传递，失败映射为稳定提示，不能把完整路径写入日志、诊断、设置或 AI 请求。

现有可运行代码为实现基线；新增验证性代码不得直接视为已交付能力。`FeatureSettings.EnableContentSearch` 已有设置页、查询和撤销执行链；其余未接入字段和 tests/prototypes 中的外部宠物包原型仍须按实际入口区分完成度。预设导入及重复提醒现有产品入口，以技术设计说明为准。

## 5. 开发规范

- 源码模块应围绕业务能力组织，UI 不直接处理安全存储、索引或网络细节。
- 外部服务通过适配器隔离；测试默认使用 fake/mock，禁止 CI 调用真实 AI Key。
- 公共接口、配置 schema 和持久化格式变更必须提供兼容或迁移说明。
- 所有耗时 I/O 和网络操作不得阻塞 UI 线程，并必须支持超时、取消或可恢复状态。
- 错误必须映射为用户可理解的类别；界面和日志不得直接暴露堆栈、服务响应正文或敏感字段。
- 新增依赖必须说明用途、许可证和锁定版本；能用标准库或现有依赖完成时不重复引入。
- 测试数据不得引用开发者真实目录、账户、Key、邮件、待办或私人文件。

## 6. 自动化测试门禁

统一入口为：

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
```

正式发布的必需检查为：

1. 项目结构、SemVer、UTF-8、AI 入口和必要文档校验。
2. 完整 solution 的 Release 构建、单元与集成自动化测试。
3. 便携版运行、安装版安装、安装后启动和卸载 smoke。
4. 版本、标签、文档、资产名称与 SHA-256 一致性检查。

当前未配置覆盖率阈值；覆盖目标及增补策略应写入 `TEST_PLAN.md`，关键安全、版本、配置迁移和时间规则不得只依赖总覆盖率。必需检查的任何失败都会阻断版本升级和 Release。

当前 `scripts/test.ps1 -CI` 执行结构/文档校验、发布证据反例、隔离 NuGet 还原、完整 solution Release 构建及 xUnit。Release 必须另外通过 `package-smoke`；`verify-release.ps1` 只强制消费 `automated` 与 `package-smoke` 两组新鲜证据，并校验版本、提交、输入指纹、时间和资产。独立 UI、system、performance、signatures 与 hardware 仍可按需收集为诊断证据，但缺失、FAIL 或 SKIP 不阻断正式发布。Release workflow 使用 GitHub 托管 Windows runner 自动执行必需门禁，不再依赖自托管部署环境。

## 7. SemVer 与变更分类

`VERSION` 保存 `MAJOR.MINOR.PATCH`，且必须与程序元数据、安装器版本、文档、标签和 Release 一致。

| 级别 | 判断 | 示例 |
| :--- | :--- | :--- |
| Patch | 向后兼容修复、文档/测试/构建改进 | `1.2.3 → 1.2.4` |
| Minor | 向后兼容的新功能 | `1.2.3 → 1.3.0` |
| Major | 不兼容的数据、配置、API、安装或工作流变化 | `1.2.3 → 2.0.0` |

用户未指定时选择最小准确级别。每次进入 `main` 的完整交付都升级版本并更新 `CHANGELOG.md`；尚未合并的 WIP 不提前占用版本。版本升级脚本示例：

版本设计文档可以先用目标 SemVer 命名和冻结范围。正式范围实现并完成第一次全量测试后，按开发流程运行版本升级脚本，再使用该版本同步文档、复测和生成正式候选资产；版本号变化本身不代表已经发布。全部发布门禁通过前不得创建标签、GitHub Release 或对外预览资产，公开版本号也不附加 `-alpha`、`-beta`、`-rc`、`-preview` 等预发布标识。

```powershell
pwsh -NoProfile -File scripts/bump-version.ps1 -Part Patch -Summary "Describe the release"
```

## 8. 构建与分发

当前已有真实构建适配器：

- `packaging/build-portable.ps1`：从干净构建生成自包含便携版目录并压缩。
- `packaging/build-installer.ps1`：生成可双击安装、升级和卸载的 Windows 安装器。
- `scripts/package.ps1`：调用两种适配器、验证版本和文件名、生成 SHA-256。

最终命名：

```text
dist/portable/windows-ai-desktop-pet-v<version>-portable.zip
dist/installer/windows-ai-desktop-pet-v<version>-setup.exe
dist/checksums/SHA256SUMS.txt
```

发布前必须在受控 Windows 环境验证：便携运行、全新安装、安装后启动和卸载。覆盖升级、托盘、自启、帮助手册、配置保存和敏感数据清理由自动化或专项诊断持续覆盖，但不单独阻断 Release。没有真实入口程序时，打包脚本必须失败，不能生成空 ZIP 或改名占位 EXE。

## 9. GitHub 与 Release 流程

GitHub 只发布正式版本。研发期间可以在本地或普通开发提交中保存设计和实现进度，但不得创建 GitHub Pre-release、预览安装包、预发布标签或对外宣称版本可用。通过门禁后，源码提交、便携版、安装版和校验和均可发布到 GitHub，并在同一普通 Release 中保持一一对应；仓库可见性继续遵守维护者设置，不因发布流程自动改为公开。

1. 从 `GITHUB_TOKEN` 或 `GH_TOKEN` 获取凭据，Token 不得出现在命令输出、remote URL、配置或提交中。
2. 运行完整测试并保持工作区干净。
3. 升级版本、更新全部文档和 Release notes。
4. 生成并测试两个分发版本和校验和。
5. 使用英文 Conventional Commit，推送 `main`。
6. 创建并推送带注释标签 `v<version>`。
7. 创建同名普通 GitHub Release，明确关闭 Pre-release 标记，并使用 UTF-8 notes 文件上传说明。
8. 上传便携版、安装版和 `SHA256SUMS.txt`，重新下载并校验哈希。
9. 验证仓库默认分支、Release 页面和资产 URL，再宣布完成。

为避免中文乱码：设置 Git 提交/日志编码为 UTF-8；PowerShell 脚本设置 UTF-8 输出；包含中文的提交正文或 Release notes 通过 UTF-8 文件传递，不把中文正文直接拼入多层 shell 参数。

## 10. AI 工具兼容

仓库采用单一事实来源：`AGENTS.md`。以下兼容入口只提示工具读取统一规则：

- Claude Code：`CLAUDE.md`
- Gemini CLI：`GEMINI.md`
- GitHub Copilot：`.github/copilot-instructions.md`
- Cursor：`.cursor/rules/project-governance.mdc`
- Windsurf：`.windsurfrules`
- Cline：`.clinerules`
- Roo Code：`.roo/rules/01-project-governance.md`
- Continue：`.continue/rules/project-governance.md`

新工具若有专用规则文件，只能新增指向 `AGENTS.md` 的入口，不得复制整份规范。任何 AI 执行修改后，都必须遵守与人工贡献者相同的测试、版本、文档、打包和 Release 门禁。

## 11. 基线状态与当前完成条件

2026-09-17 按 FOCUS-01～09、QUIET-01～08 与 CHAR-01～06 完成专注会话、低打扰协调、全屏检测、点击穿透恢复和四角色首帧卡片，并将源码版本由 0.24.0 升为 MINOR 0.25.0。能力证据、最终测试和产物状态见 [CURRENT_STATUS.md](CURRENT_STATUS.md)。

2026-09-18 按 SET-03 与 UPDATE-01～04 完成主题资源动态化、浅色完整恢复、Windows/环境代理显式装配及匿名 404 更新源分类，并将源码版本按 PATCH 升为 0.25.1。

2026-09-18 依赖审计发现 SQLitePCLRaw 2.1.6 命中 CVE-2025-6965，升级 `Microsoft.Data.Sqlite` 至 8.0.31 并解析到 SQLitePCLRaw 2.1.12；同时加入显式授权的脱敏真实 AI 诊断入口，将源码版本按 PATCH 升为 0.25.2。此变更不修改产品能力、数据 schema 或用户工作流。

0.24.0 及以前版本的历史 Release 与证据保持原状。0.25.0～0.25.2 只允许创建普通正式 Release，不创建 Alpha、Beta、RC、Preview 或 GitHub Pre-release；源码、两个安装资产与校验和在 automated 与 package-smoke 通过后即可发布到 GitHub。匿名自动更新的固定 Release 仓库必须公开；改变仓库可见性属于维护者外部操作，代码和文档不得擅自执行。真实 AI、真实桌面、性能、物理矩阵和签名仍按实际结果记录，但不再作为版本升级或 Release 前置条件；真实 AI 诊断只有获得用户明确授权时才允许读取本机已保存 Key。

当前功能设计入口为 [0.25.0：专注陪伴、低打扰与内置角色库设计](0.25.0－专注陪伴、低打扰与角色库设计.md)，0.25.1 为兼容修复，0.25.2 为依赖安全与测试自动化修复。正式发布结果以 [0.25.2 验证报告](release/0.25.2-test-report.md) 和 [GitHub Release 状态](release/GITHUB_RELEASE_STATUS.md) 为准。
