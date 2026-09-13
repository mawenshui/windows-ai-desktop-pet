# 测试计划与覆盖

基线：0.21.0，2026-09-13；测试结果以[本次报告](release/0.21.0-test-report.md)为准。实现、进程内 WPF 渲染、独立桌面输入和物理设备验收分别记录。

## 1. 执行入口

| 命令 | 作用 |
| :--- | :--- |
| scripts/validate-project.ps1 -CI | 项目结构、必需文档、UTF-8、版本/CHANGELOG 等校验 |
| scripts/test.ps1 -CI | 结构、发布门禁反例、完整 solution 构建、xUnit |
| scripts/test-ui-e2e.ps1 -CI | 独立程序真实八组鼠标/键盘、搜索结果选择及四项操作、按需展开单项 AI/今日安排、今日安排候选/本地草稿/逐项排除/确认/整批撤销、AI 未保存导航/取消、焦点和实际托盘；无法前台激活立即停止输入 |
| scripts/test-system-e2e.ps1 -CI | 几何合同、启动/帮助/存储/退出 smoke、当前显示环境记录 |
| scripts/test-performance.ps1 -Enforce | 匿名 20,000 条名称查询与 UI/进程指标，按 config/performance-budgets.json 判断 |
| scripts/package.ps1 | ZIP / Inno Setup、离线手册/资源、SHA-256 和 provenance |
| scripts/smoke-release.ps1 | 便携运行、安装、安装后运行、静默卸载及安装路径自启清理；签名按实际配置报告 |
| scripts/collect-release-evidence.ps1 -Gate ... | 绑定当前版本/提交/输入指纹/环境/时间/资产的七类证据 |
| scripts/verify-release.ps1 | 验证干净工作区、完整新鲜证据、资产及清单哈希 |
| tests/prototypes/AiPet.Prototypes | EXT-08 严格 2D 宠物包隔离评估和 20,000/100,000 元数据测量 |

从仓库根使用 pwsh -NoProfile -File 运行 PowerShell 入口。单元工程包含进程内 WPF 和文件/SQLite 边界集成用例；目录命名不表示全部为纯内存单元测试。尚未设置覆盖率、格式或漏洞扫描阈值，不伪装为已执行。

## 2. 需求覆盖

| 需求 | 核心测试 |
| :--- | :--- |
| PET/WIN/NAV/SET | PetWindowPositioner、PetPopoverPositioner、PetToolWindowLifecycle、HomePageLayout、HomeViewModel、GitHubReleaseUpdate、ShellNavigation；未保存导航取消保留、`Alt+1`～`Alt+4`/`Ctrl+L`/`Ctrl+N`/`Ctrl+Enter`/`F1`、Esc 层级、页签帮助文本、设置五项目录、渲染与管理窗 |
| SRCH-01～11 / EXT-04 | SearchService、SearchIndex、SearchLifecycle、ContentSearch、SearchResultActions、HomeViewModel/HomePageLayout；授权撤销竞争、重启监听、稳定分页、固定/清历史、正文边界、安全结果动作、选择生命周期、单次重置三个条件、可操作空态、数量/序号与 live region |
| QCK-01～09 / EXT-05 | ShortcutStore、ShortcutOrganization、HomePageLayout；重复 ID、100 项排序/分组、选择数量、固定区边界、扫描互斥、Delete 预览、批量撤销图标、静止管理入口、失效非颜色线索及含目标名的 UIA 语义 |
| TODO-01～14 / EXT-01 | TodoStore、TodoViewModel、ReminderRule、ReminderScheduler、PetToolWindowLifecycle/HomePageLayout；筛选/总数摘要、上下文空态、全字段未保存基线、页内放弃确认、手动新建/修改/完成/恢复/稍后/取消提醒/删除统一撤销和并发拒绝 |
| TODAY-01～06 | TodayPlan、TodoStore、TodoViewModel、HomePageLayout；只发送选中项正文、未选项匿名占用时间、AI/本地冲突避让、精确刻钟边界、过期整批拒绝、前 12 条批量选择/清除与旧草稿失效 |
| EXT-06 | NotificationCenter；20 项同时到期、去重/重启、静默跨午夜、提交拒绝、清历史、独立项稍后、空闲唤醒及旧快照改期隔离 |
| AI-01～05 / EXT-03 | HomeViewModel、AiTodoClient、AiCapability；HTTP 状态、模型存在/格式、列表成功生成失败、取消、固定测试载荷/预算、无 Key 导入与活动配置保留 |
| AI-06 / EXT-11 | EncryptedAiConfigurationBundle、HomeViewModel；全字段加密往返、密文无 Key/端点、错误口令/篡改拒绝、跨数据目录导入后即用、自定义 Provider 迁移、凭据中途失败回滚 |
| DATA-01/02/04/05 / EXT-02 | MaintenanceTransaction、FutureFeature、RecoverableAtomicFile、HomePageLayout/HomePageExperience；路径/容量/schema/hash、中途失败/中断回滚、真实模块成组恢复、损坏后写保护、危险层级/明确确认、独立状态通道、成功不泄露路径与失败不拼接原异常 |
| HOTKEY-01/02 / EXT-09 | EssentialFeature、PetToolWindowLifecycle、HomePageLayout；组合键规范化/拒绝/去重、设置持久化、快速待办定向及标题焦点、托盘等价入口；真实跨程序按键与占用释放单列实测 |
| DATA-03 / EXT-10 | EssentialFeature、SettingsStore、FutureFeature；24 小时节流、1～30 份保留、模块包含/排除、损坏输入失败保留、schema 4 与 `.pre-v4.bak` |
| UPDATE-01～04 | GitHubReleaseUpdate、SettingsStore、HomeViewModel/HomePageLayout；稳定版本、固定仓库/标签/资产、内置元数据与下载线路顺序、全部失败、匿名请求、失败保留已发现版本、HTTPS/大小/重定向/digest/清单、临时文件清理、启动一次、周期设置、专业输入移除、状态 live region、schema 5 旧模板清空与 `.pre-v5.bak` |
| EXT-00 | test-release-evidence.ps1；缺失、过期、未来时间、版本/提交/输入不符、FAIL/SKIP、缺检查及资产错误必须失败 |
| EXT-08 | PrototypeTests；宠物包路径/缺帧/超大/未知字段/许可/重复/损坏回退。原 EXT-07 正文试验已迁入 SRCH-07 正式回归 |
| AUTO/TRAY/HELP | 自启、托盘重建合同、帮助路径与 smoke；真实重登录/Explorer 单列 |

外部服务默认 fake HTTP；完整配置包使用匿名伪 Key，并断言密文、普通设置和错误信息不出现该值。无实际 API Key、待办标题、搜索原文或个人路径进入测试 fixture/诊断。渲染截图只使用匿名测试数据，build 目录不作为源码交付；不得将锁屏、PIN 或其他程序截为桌宠证据。

## 3. 独立发布门禁

七组 gate：automated、ui、system、performance、package-smoke、signatures、hardware。输入指纹覆盖源码/测试/脚本/资源/配置/离线手册及版本构建元数据，报告必须来自当前提交且不超过 72 小时。缺少必需检查、FAIL/SKIP、错资产或 dirty 都拒绝。

系统当前显示器“存在”和几何合同通过不等于多屏交互已通过。hardware 自动收集只建立 SKIP 清单，需人工操作多屏、100%～200% 缩放、热插拔、休眠、时区/时钟、Explorer 重启、升级保留数据并提供见证记录。0.21.0 还需在解锁的同桌面会话验证新快捷键、Esc 层级、可操作空态、页内放弃、直接删除/撤销、动态播报和维护反馈，并回归 0.19.0/0.20.0 的主页布局、搜索焦点、待办渐进披露、设置未保存保护、智能更新线路、正文开关、今日安排、全局快捷键、自动备份和完整 AI 迁移。smoke 的静默卸载只覆盖保留分支，交互清理和真实跨版本迁移另测。

NotifyIcon/桌宠提交无法证明展示，需实测专注助手、隐藏桌宠、动画关闭和中心处理。UI 无法获取前台或运行失败不应降低测试条件、替换为属性赋值并声称真实输入 PASS。完整门禁不齐只保留候选，不创建正式 Release。
