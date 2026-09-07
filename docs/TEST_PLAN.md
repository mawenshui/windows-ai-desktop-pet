# 测试计划与覆盖

基线：0.13.0，2026-09-07；测试结果以[本次报告](release/0.13.0-test-report.md)为准。实现、进程内 WPF 渲染、独立桌面输入和物理设备验收分别记录。

## 1. 执行入口

| 命令 | 作用 |
| :--- | :--- |
| scripts/validate-project.ps1 -CI | 项目结构、必需文档、UTF-8、版本/CHANGELOG 等校验 |
| scripts/test.ps1 -CI | 结构、发布门禁反例、完整 solution 构建、xUnit |
| scripts/test-ui-e2e.ps1 -CI | 独立程序真实八组鼠标/键盘、AI 未保存导航/取消、焦点和实际托盘；无法前台激活立即停止输入 |
| scripts/test-system-e2e.ps1 -CI | 几何合同、启动/帮助/存储/退出 smoke、当前显示环境记录 |
| scripts/test-performance.ps1 -Enforce | 匿名 20,000 条名称查询与 UI/进程指标，按 config/performance-budgets.json 判断 |
| scripts/package.ps1 | ZIP / Inno Setup、离线手册/资源、SHA-256 和 provenance |
| scripts/smoke-release.ps1 | 便携运行、安装、安装后运行、静默卸载及安装路径自启清理；签名按实际配置报告 |
| scripts/collect-release-evidence.ps1 -Gate ... | 绑定当前版本/提交/输入指纹/环境/时间/资产的七类证据 |
| scripts/verify-release.ps1 | 验证干净工作区、完整新鲜证据、资产及清单哈希 |
| tests/prototypes/AiPet.Prototypes | EXT-07/08 隔离评估和 20,000/100,000 元数据测量 |

从仓库根使用 pwsh -NoProfile -File 运行 PowerShell 入口。单元工程包含进程内 WPF 和文件/SQLite 边界集成用例；目录命名不表示全部为纯内存单元测试。尚未设置覆盖率、格式或漏洞扫描阈值，不伪装为已执行。

## 2. 需求覆盖

| 需求 | 核心测试 |
| :--- | :--- |
| PET/WIN/NAV/SET | PetWindowPositioner、PetPopoverPositioner、PetToolWindowLifecycle、HomePageLayout、ShellNavigation；未保存导航取消保留、主题/页内选择、渲染与新管理窗 |
| SRCH-01～06 / EXT-04 | SearchService、SearchIndex、SearchLifecycle；授权撤销竞争、重启监听、目录改名、相对路径、稳定分页和固定/清历史 |
| QCK-01～06 / EXT-05 | ShortcutStore、ShortcutOrganization；重复 ID、100 项排序/分组、批量预览过期、撤销图标、扫描取消 |
| TODO-01～09 / EXT-01 | TodoStore、TodoViewModel、ReminderRule、ReminderScheduler；跨月闰年/DST/规则时区、最早额外时刻、停机跳过、整条取消和仅此次保存 |
| EXT-06 | NotificationCenter；20 项同时到期、去重/重启、静默跨午夜、提交拒绝、清历史、独立项稍后、空闲唤醒及旧快照改期隔离 |
| AI-01～05 / EXT-03 | HomeViewModel、AiTodoClient、AiCapability；HTTP 状态、模型存在/格式、列表成功生成失败、取消、固定测试载荷/预算、无 Key 导入与活动配置保留 |
| DATA-01/02 / EXT-02 | MaintenanceTransaction、FutureFeature、RecoverableAtomicFile；路径/容量/schema/hash、中途失败/中断回滚、真实模块/图标/队列成组恢复、损坏后写保护、卸载只删已知应用引用 |
| EXT-00 | test-release-evidence.ps1；缺失、过期、未来时间、版本/提交/输入不符、FAIL/SKIP、缺检查及资产错误必须失败 |
| EXT-07/08 | PrototypeTests；正文单独同意、大小/编码/取消/撤销；包路径/缺帧/超大/未知字段/许可/重复/损坏回退 |
| AUTO/TRAY/HELP | 自启、托盘重建合同、帮助路径与 smoke；真实重登录/Explorer 单列 |

外部服务默认 fake HTTP；无实际 API Key、待办标题、搜索原文或个人路径进入测试 fixture/诊断。渲染截图只使用匿名测试数据，build 目录不作为源码交付；不得将锁屏、PIN 或其他程序截为桌宠证据。

## 3. 独立发布门禁

七组 gate：automated、ui、system、performance、package-smoke、signatures、hardware。输入指纹覆盖源码/测试/脚本/资源/配置/离线手册及版本构建元数据，报告必须来自当前提交且不超过 72 小时。缺少必需检查、FAIL/SKIP、错资产或 dirty 都拒绝。

系统当前显示器“存在”和几何合同通过不等于多屏交互已通过。hardware 自动收集只建立 SKIP 清单，需人工操作多屏、100%～200% 缩放、热插拔、休眠、时区/时钟、Explorer 重启、升级保留数据并提供见证记录。smoke 的静默卸载只覆盖保留分支，交互清理和真实跨版本迁移另测。

NotifyIcon/桌宠提交无法证明展示，需实测专注助手、隐藏桌宠、动画关闭和中心处理。UI 无法获取前台或运行失败不应降低测试条件、替换为属性赋值并声称真实输入 PASS。完整门禁不齐只保留候选，不创建正式 Release。
