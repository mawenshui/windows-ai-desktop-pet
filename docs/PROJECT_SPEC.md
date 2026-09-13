# Windows AI Desktop Pet 项目规范

| 属性 | 值 |
| :--- | :--- |
| 文档版本 | 2.3（2026-09-13 键盘与可恢复任务流修订） |
| 软件基线版本 | 0.21.0 |
| 状态 | 生效；WPF/.NET 8 技术栈已落地，0.21.0 为候选工作区 |
| 适用范围 | 源码、测试、文档、构建、打包、版本、GitHub 与 AI 工具协作 |
| 需求基线 | `docs/Windows桌面宠物产品需求文档_PRD.md` V3.1 |

## 1. 目的与适用原则

本规范把 PRD 转换为可执行的项目级约束，供开发者、CI 和各类 AI 工具共同遵守。根目录 `AGENTS.md` 是统一操作入口，本文件提供更详细的工程约定。PRD 负责定义“做什么”，本规范负责定义“代码和资料放在哪里、如何验证、如何升级版本、如何打包和发布”。

当前仓库已有 WPF/.NET 8 产品源码、CC0 RGS 桌宠、页内单选与四页工具窗、自动化测试，以及便携版和 Inno Setup 安装版。0.21.0 在 0.20.0 基线上新增工具窗键盘入口、可操作搜索/待办状态、待办未保存保护与可并发校验的最近撤销；不改变设置 schema 5、待办 schema 4、快捷入口 schema 1 或权限边界。EXT-08 外部宠物包仍只在 tests/prototypes 评估。当前测试与产物见 CURRENT_STATUS.md，真实输入、性能、签名和物理环境仍需独立证据。

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
| `RELEASE_NOTES_<version>.md` | 当次候选/Release 说明、升级注意、校验信息 | 每次发布或候选更新 |
| `CURRENT_STATUS.md` | 代码证据、文档入口、验证边界 | 代码状态核对 |
| `<version>－功能扩展计划.md` | 当版实施项、后续能力、依赖和验收标准 | 新版本规划或交付状态变化 |

中文文档统一使用 UTF-8。Markdown 链接使用仓库相对路径。扩展计划从 0.15.0 起固定使用 `<SemVer>－功能扩展计划.md`，当前状态文档只链接当前版本计划。代码、配置键、其他文件名、Git 提交标题和 Release 标签使用英文，避免跨终端编码差异。

## 4. 技术与模块边界

技术栈已经落地为 WPF/.NET 8；当前模块、数据格式、线程模型和协议见 `TECHNICAL_DESIGN.md`。后续架构变更必须同步维护以下内容：

1. GUI 框架与受支持的 Windows 版本/架构。
2. 宠物窗口、工具窗口、托盘、全局快捷键、设置、搜索、快捷项、AI 配置和通知调度的模块边界。
3. 配置、自动备份、搜索缓存、快捷项、待办与提醒的数据模型及迁移策略。
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

完整发布要求应覆盖以下检查；这不是当前 `test.ps1 -CI` 已经串联的步骤清单：

1. 项目结构、SemVer、UTF-8、AI 入口和必要文档校验。
2. 格式检查、静态分析和依赖安全检查。
3. 单元测试及覆盖率检查。
4. 集成测试，重点覆盖配置、安全存储、搜索范围和 AI 错误分类。
5. Windows E2E，映射当前版本所有 P0 验收条件。
6. 便携版和安装版烟雾测试。

当前未配置覆盖率阈值；覆盖目标及增补策略应写入 `TEST_PLAN.md`，关键安全、版本、配置迁移和时间规则不得只依赖总覆盖率。失败、跳过和不适用必须分别报告；任何失败都阻断版本升级和 Release。

当前 scripts/test.ps1 -CI 执行结构/文档校验、发布证据反例、隔离 NuGet 还原、完整 solution Release 构建及 xUnit。独立 UI、系统、性能和打包/烟雾仍须另外执行；格式、覆盖率及漏洞扫描阈值尚未接入。collect-release-evidence.ps1 收集七组证据，verify-release.ps1 校验版本、提交、输入指纹、时间和资产。Release workflow 使用受保护自托管环境并强制消费七组报告；缺失或 FAIL/SKIP 不得发布。

## 7. SemVer 与变更分类

`VERSION` 保存 `MAJOR.MINOR.PATCH`，且必须与程序元数据、安装器版本、文档、标签和 Release 一致。

| 级别 | 判断 | 示例 |
| :--- | :--- | :--- |
| Patch | 向后兼容修复、文档/测试/构建改进 | `1.2.3 → 1.2.4` |
| Minor | 向后兼容的新功能 | `1.2.3 → 1.3.0` |
| Major | 不兼容的数据、配置、API、安装或工作流变化 | `1.2.3 → 2.0.0` |

用户未指定时选择最小准确级别。每次进入 `main` 的完整交付都升级版本并更新 `CHANGELOG.md`；尚未合并的 WIP 不提前占用版本。版本升级脚本示例：

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

发布前必须从空白或受控 Windows 环境验证：便携运行、全新安装、覆盖升级、卸载、托盘、自启、帮助手册、配置保存和敏感数据清理。没有真实入口程序时，打包脚本必须失败，不能生成空 ZIP 或改名占位 EXE。

## 9. GitHub 与 Release 流程

1. 从 `GITHUB_TOKEN` 或 `GH_TOKEN` 获取凭据，Token 不得出现在命令输出、remote URL、配置或提交中。
2. 运行完整测试并保持工作区干净。
3. 升级版本、更新全部文档和 Release notes。
4. 生成并测试两个分发版本和校验和。
5. 使用英文 Conventional Commit，推送 `main`。
6. 创建并推送带注释标签 `v<version>`。
7. 创建同名 GitHub Release，使用 UTF-8 notes 文件上传说明。
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

2026-09-13 按 UX-210-01～19 完成键盘导航、搜索恢复、待办筛选摘要、编辑保护、统一撤销、今日安排批量选择、可访问播报和维护错误安全化，并使用 bump-version.ps1 将 0.20.0 升为 MINOR 0.21.0。能力证据、最终测试和产物状态见 [CURRENT_STATUS.md](CURRENT_STATUS.md)。

本次版本号表示用户明确要求的功能候选升级，不表示验收通过或正式发布。历史记录不能替代当次证据；源代码提交/远端同步、真实桌面、物理矩阵、签名及远端资产必须分别验证。未满足完整门禁时，不创建正式标签或 GitHub Release。

当前扩展计划入口为 [0.21.0－功能扩展计划](0.21.0－功能扩展计划.md)，详细决策见 [0.21.0－键盘与任务流优化设计](0.21.0－键盘与任务流优化设计.md)；历史计划和 RFC 不代表当前排期。实施结果见 [本次报告](release/0.21.0-test-report.md)。
