# Windows AI Desktop Pet 项目规范

| 属性 | 值 |
| :--- | :--- |
| 文档版本 | 1.0 |
| 软件基线版本 | 0.1.0 |
| 状态 | 生效；技术栈章节待实现阶段冻结 |
| 适用范围 | 源码、测试、文档、构建、打包、版本、GitHub 与 AI 工具协作 |
| 需求基线 | `docs/Windows桌面宠物产品需求文档_PRD.md` V1.1 |

## 1. 目的与适用原则

本规范把 PRD 转换为可执行的项目级约束，供开发者、CI 和各类 AI 工具共同遵守。根目录 `AGENTS.md` 是统一操作入口，本文件提供更详细的工程约定。PRD 负责定义“做什么”，本规范负责定义“代码和资料放在哪里、如何验证、如何升级版本、如何打包和发布”。

当前仓库只有需求文档和工程基线，没有产品源码、技术设计或安装包。因此本规范不会虚构已经完成的构建能力；首次产品实现必须先冻结技术栈并补齐对应构建适配器。

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
│  ├─ TECHNICAL_DESIGN.md          # 实现前创建/冻结
│  ├─ USER_MANUAL.md               # 首个可运行版本前完成
│  ├─ TEST_PLAN.md                 # 实现阶段维护
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

## 3. 文档体系

| 文档 | 责任 | 更新触发条件 |
| :--- | :--- | :--- |
| PRD | 产品范围、优先级、流程、验收标准 | 产品行为、范围或优先级变化 |
| `PROJECT_SPEC.md` | 目录、流程、版本、测试、发布规则 | 工程规则或工具链变化 |
| `TECHNICAL_DESIGN.md` | 技术栈、模块、数据模型、线程/进程、API | 架构或关键依赖变化 |
| `USER_MANUAL.md` | 用户可见操作和故障处理 | 可见行为、设置或安装方式变化 |
| `TEST_PLAN.md` | 测试矩阵、环境、数据和覆盖关系 | 需求、架构或风险变化 |
| `CHANGELOG.md` | 每个软件版本的用户可感知变更 | 每次版本升级 |
| `RELEASE_NOTES_<version>.md` | 当次 Release 说明、升级注意、校验信息 | 每次发布 |

中文文档统一使用 UTF-8。Markdown 链接使用仓库相对路径。代码、配置键、文件名、Git 提交标题和 Release 标签使用英文，避免跨终端编码差异。

## 4. 技术与模块边界

技术栈尚未冻结。首次实现前必须在 `TECHNICAL_DESIGN.md` 明确：

1. GUI 框架与受支持的 Windows 版本/架构。
2. 宠物窗口、工具窗口、托盘、设置、搜索、快捷项、AI 配置和通知调度的模块边界。
3. 配置、搜索缓存、快捷项、待办与提醒的数据模型及迁移策略。
4. Windows 账户安全存储方案，确保 AI Key 不进入普通配置和日志。
5. 搜索授权范围、索引/查询线程模型和取消机制。
6. 便携版与安装版构建工具、签名方式、安装/升级/卸载策略。
7. 单元、集成和 Windows UI 自动化测试框架。

在技术栈冻结前，可新增文档、原型和验证性代码，但不得把验证性代码当作生产基线，也不得发布安装包。

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

流水线至少包含：

1. 项目结构、SemVer、UTF-8、AI 入口和必要文档校验。
2. 格式检查、静态分析和依赖安全检查。
3. 单元测试及覆盖率检查。
4. 集成测试，重点覆盖配置、安全存储、搜索范围和 AI 错误分类。
5. Windows E2E，映射当前版本所有 P0 验收条件。
6. 便携版和安装版烟雾测试。

目标覆盖率应在技术栈确定后写入 `TEST_PLAN.md`，但关键安全、版本、配置迁移和时间规则不得只依赖总覆盖率。失败、跳过和不适用必须分别报告；任何失败都阻断版本升级和 Release。

当前初始化阶段 `scripts/test.ps1` 会执行结构与文档校验，并明确报告“尚无应用源码，运行时测试不适用”，不会把跳过伪装为产品测试通过。

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

真实构建适配器应实现：

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

## 11. 当前基线的完成条件

本次初始化完成条件仅包括：规范和兼容入口存在、目录骨架可识别、结构校验通过、版本为 `0.1.0`、GitHub 私有仓库创建并推送成功。由于尚无应用代码，本次不创建虚假安装包或二进制 Release；首个可运行版本必须先实现第 8 节适配器，并从此执行完整发布流程。
