# 测试计划与覆盖

基线：0.18.1，2026-09-12；测试结果以[本次报告](release/0.18.1-test-report.md)为准。实现、进程内 WPF 渲染、独立桌面输入和物理设备验收分别记录。

## 1. 执行入口

| 命令 | 作用 |
| :--- | :--- |
| scripts/validate-project.ps1 -CI | 项目结构、必需文档、UTF-8、版本/CHANGELOG 等校验 |
| scripts/test.ps1 -CI | 结构、发布门禁反例、完整 solution 构建、xUnit |
| scripts/test-ui-e2e.ps1 -CI | 独立程序真实八组鼠标/键盘、搜索结果选择及四项操作、今日安排候选/本地草稿/逐项排除/确认/整批撤销、AI 未保存导航/取消、焦点和实际托盘；无法前台激活立即停止输入 |
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
| PET/WIN/NAV/SET | PetWindowPositioner、PetPopoverPositioner、PetToolWindowLifecycle、HomePageLayout、HomeViewModel、GitHubReleaseUpdate、ShellNavigation；未保存导航取消保留、主题/页内选择、三组显式保存基线、渲染与管理窗 |
| SRCH-01～09 / EXT-04 | SearchService、SearchIndex、SearchLifecycle、ContentSearch、SearchResultActions、HomeViewModel/HomePageLayout；授权撤销竞争、重启监听、目录改名、相对路径、稳定分页、固定/清历史、正文边界，以及安全结果动作、选择生命周期、一键清空和快捷入口重复状态 |
| QCK-01～07 / EXT-05 | ShortcutStore、ShortcutOrganization、HomePageLayout；重复 ID、100 项排序/分组、选择数量、固定区边界、扫描互斥、Delete 预览、批量预览过期、撤销图标和扫描取消 |
| TODO-01～10 / EXT-01 | TodoStore、TodoViewModel、ReminderRule、ReminderScheduler；编辑器空白标题命令状态、跨月闰年/DST/规则时区、最早额外时刻、停机跳过、整条取消和仅此次保存 |
| TODAY-01～05 | TodayPlan、TodoStore、TodoViewModel、HomePageLayout；六种筛选、只发送选中项正文、未选项匿名占用时间、AI/本地冲突避让、精确刻钟边界、过期应用/撤销整批拒绝及分离提示、schema 4 迁移和整批撤销 |
| EXT-06 | NotificationCenter；20 项同时到期、去重/重启、静默跨午夜、提交拒绝、清历史、独立项稍后、空闲唤醒及旧快照改期隔离 |
| AI-01～05 / EXT-03 | HomeViewModel、AiTodoClient、AiCapability；HTTP 状态、模型存在/格式、列表成功生成失败、取消、固定测试载荷/预算、无 Key 导入与活动配置保留 |
| AI-06 / EXT-11 | EncryptedAiConfigurationBundle、HomeViewModel；全字段加密往返、密文无 Key/端点、错误口令/篡改拒绝、跨数据目录导入后即用、自定义 Provider 迁移、凭据中途失败回滚 |
| DATA-01/02 / EXT-02 | MaintenanceTransaction、FutureFeature、RecoverableAtomicFile；路径/容量/schema/hash、中途失败/中断回滚、真实模块/图标/队列成组恢复、损坏后写保护、卸载只删已知应用引用 |
| HOTKEY-01/02 / EXT-09 | EssentialFeature、PetToolWindowLifecycle、HomePageLayout；组合键规范化/拒绝/去重、设置持久化、快速待办定向及标题焦点、托盘等价入口；真实跨程序按键与占用释放单列实测 |
| DATA-03 / EXT-10 | EssentialFeature、SettingsStore、FutureFeature；24 小时节流、1～30 份保留、模块包含/排除、损坏输入失败保留、schema 4 与 `.pre-v4.bak` |
| UPDATE-01～03 | GitHubReleaseUpdate、SettingsStore、HomeViewModel；稳定版本比较、精确资产、加速回退、仅使用已保存令牌、带令牌禁用加速、失败保留已发现版本、Authorization、HTTPS/大小/校验和、临时文件清理、启动一次、周期设置、schema 5 与 `.pre-v5.bak` |
| EXT-00 | test-release-evidence.ps1；缺失、过期、未来时间、版本/提交/输入不符、FAIL/SKIP、缺检查及资产错误必须失败 |
| EXT-08 | PrototypeTests；宠物包路径/缺帧/超大/未知字段/许可/重复/损坏回退。原 EXT-07 正文试验已迁入 SRCH-07 正式回归 |
| AUTO/TRAY/HELP | 自启、托盘重建合同、帮助路径与 smoke；真实重登录/Explorer 单列 |

外部服务默认 fake HTTP；完整配置包使用匿名伪 Key，并断言密文、普通设置和错误信息不出现该值。无实际 API Key、待办标题、搜索原文或个人路径进入测试 fixture/诊断。渲染截图只使用匿名测试数据，build 目录不作为源码交付；不得将锁屏、PIN 或其他程序截为桌宠证据。

## 3. 独立发布门禁

七组 gate：automated、ui、system、performance、package-smoke、signatures、hardware。输入指纹覆盖源码/测试/脚本/资源/配置/离线手册及版本构建元数据，报告必须来自当前提交且不超过 72 小时。缺少必需检查、FAIL/SKIP、错资产或 dirty 都拒绝。

系统当前显示器“存在”和几何合同通过不等于多屏交互已通过。hardware 自动收集只建立 SKIP 清单，需人工操作多屏、100%～200% 缩放、热插拔、休眠、时区/时钟、Explorer 重启、升级保留数据并提供见证记录。0.18.1 还需在解锁的同桌面会话验证搜索清空后的焦点、快捷入口已加入状态、三组设置按钮状态、快捷管理器键盘与扫描状态、空标题保存禁用，以及 0.18.0 搜索结果动作、正文开关、今日安排、两个默认全局组合、自动备份和完整 AI 配置迁移的回归。smoke 的静默卸载只覆盖保留分支，交互清理和真实跨版本迁移另测。

NotifyIcon/桌宠提交无法证明展示，需实测专注助手、隐藏桌宠、动画关闭和中心处理。UI 无法获取前台或运行失败不应降低测试条件、替换为属性赋值并声称真实输入 PASS。完整门禁不齐只保留候选，不创建正式 Release。
