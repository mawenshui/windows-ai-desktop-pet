# 技术设计与实现基线

日期：2026-09-08；软件：0.14.0 候选工作区。本文以当前代码为准；[实施追踪](后续功能扩展计划.md)、[验证报告](release/0.14.0-test-report.md)分别说明范围与验收状态。

## 1. 技术与模块

WPF / .NET 8，win-x64 自包含。Microsoft.Data.Sqlite 8.0.10、System.Text.Json、Windows Forms NotifyIcon；测试采用 xUnit 2.9.2、Test.Sdk 17.11.1、runner 2.8.2。不新增外部运行时依赖。global.json 允许 SDK 主版本滚动，不能称为锁死 SDK。Directory.Build.props 从根 VERSION 读取所有程序集版本；安装器和手册使用同一软件版本。

| 模块 | 当前职责 |
| :--- | :--- |
| App | STA、单实例、启动维护、服务装配、托盘、全局快捷键、自动备份、提醒提交和退出 |
| Pet / Common | RGS 帧缓存、手势、漫游、工作区定位、manifest 与方向映射 |
| ToolWindow | 四页、主题、Home/Todo ViewModel、AI/维护对话框、快捷管理窗、提醒中心 |
| Search | SQLite、授权、目录扫描、watcher、范围任务串行化、排序和分页 |
| Shortcuts | JSON、引用/图标、分组固定、批量预览/撤销、失效扫描 |
| Todos | schema 3、日历规则、最近到期调度、持久化通知记录 |
| AI | 模型验证、最小生成验证、草稿协议、配置交换、预设加载 |
| Storage / Secrets | 设置/位置、原子写入、维护事务、每日备份、白名单诊断、Windows 凭据 |
| SystemIntegration | HKCU 自启、离线帮助、RegisterHotKey / WM_HOTKEY |
| tests/prototypes | 独立内容索引和严格 2D 子集原型，应用不引用、不打包 |

## 2. 启动、窗口与退出

先获取单实例，再查资源；设置加载前 ApplyPending/RecoverInterrupted，在 SQLite、TodoStore 和窗口工作线程启动前完成恢复或缓存重建。维护失败不继续构造后台服务。App 通过 Attach 注入 XAML 创建的 HomeVM。

工具窗基准 440×536，按工作区空间调整，常驻与置顶独立。八组既有选择器以及新增规则/历史/分组选择继续使用页内 ListBox。TrySelectTab 统一鼠标、Ctrl+Tab 和提醒定向入口的 AI 未保存保护；取消停留原页。文件维护和管理对话框抑制失焦收起。

查询在后台执行并保存代次及索引 Revision，分页遇到变更重新查询。提醒调度使用一次性 Timer，空队列无限等待；TodoStore.Changed、TimeChanged、Resume 重新安排唤醒。SemaphoreSlim 防重入，退出取消任务并有限等待调度完成。通知 UI 每 5 秒处理，避免在每条调度回调中竞争气泡。

preview / ui-e2e 使用可自动化的非透明宿主及匿名草稿探针，明确跳过全局快捷键注册和自动备份写入。探针不替代真实鼠标键盘输入；正式透明窗、多屏、系统级快捷键和托盘必须独立验收。

## 3. 搜索与索引

授权目录只索引名称、路径、类型、大小和修改时间，不读正文；应用入口来自开始菜单/App Paths。每范围 RangeJob 持有 lifetime token、串行锁、待变路径集合和溢出标志。重建使用 staging，提交前检查授权/任务身份及取消；撤销先取消任务再移除索引。启动恢复 watcher；目录变更按子树替换，超过 2,048 个排队路径或 watcher 溢出合并为一次全量重建。

元数据扫描约束授权边界并拒绝重解析点。失败/取消保留上一份成功数据，状态记录稳定错误码。取消每个扫描批次、路径和查询循环均可观察；底层文件系统调用不能保证即时中断。

名称或相对路径匹配，普通文字、可选 wildcard/regex，精确/前缀/包含给出原因。固定优先；最近使用排序默认关，主动确认才记录路径/时间。search_preferences 以范围和完整路径为键，撤销范围时删除，清历史保留固定。最后按稳定路径/ID 排序，每页 50，Revision 防止混合不同代数据。

## 4. 快捷入口

shortcuts.json schema 1 新增 group/pinned，旧数据默认空组/未固定。Load 先固定、再 order、再创建时间；Reorder 去重 ID，避免重复记录。管理窗支持 100 项测试、多选、分组、拖动和键盘排序、取消失效扫描。

批量移除预览对所选记录计算指纹，确认前再次核对，防止旧预览误删已修改记录。撤销保存在内存，合并时不覆盖同 ID 的新记录；原目标从不删除。当前引用和撤销记录所需图标都受孤立缓存清理保护。

## 5. 待办与通知

TodoDocument schema 3 新增 recurrenceAnchorAt、时区及 queuedOccurrenceAt，升级前保存 .pre-v3.bak。主/额外提醒去重排序，最多 32 个额外时间；存储读取校验结构及规则，损坏可读为空但拒绝后续覆盖，保留原文件。

RecurrenceCalculator 用规则所属时区的日历钟点计算，每周以原锚点/星期一周界为基础，夏令时空档前移到首个有效分钟、重叠选后一次。无时区旧规则保持固定偏移。长期错过只汇总当前一次，下一时刻直接跳到未来。详细语义见[待办提醒设计](TODO_REMINDER_DESIGN.md)。

DeliverReminderIfCurrent 在存储锁内核对原到期时间、接受入队并推进对应规则，避免旧快照消耗并发改期后的新时间。notifications.json 独立保存排队、提交、处理、取消；同 TodoId/时刻去重。先持久化队列再推进待办，跨文件失败允许恢复时重试；不承诺跨进程崩溃的展示 exactly-once。

静默采用当前系统时区 HH:mm；提交最小间隔 30 秒，批量摘要并保留每项记录。普通待办不自动完成；最后一次独立提醒提交后完成，队列中尚未提交则保持待处理。Windows 通知显示未确认，无已读状态。记录最多 2,000；满额不丢弃排队事项，以失败反馈。清历史保留待处理提醒，队列随待办成组备份。

## 6. AI

GET models 验证响应对象/data 数组/所选模型；最小生成验证独立主动确认，固定示例、max_tokens=256、只校验草稿不写入。普通草稿最多 1,024 output tokens；模型列表响应 2 MiB、草稿响应 256 KiB，UTF-8、25 秒请求与读取超时，取消独立传播。错误包含鉴权、权限、限流、未知模型、无效响应和网络/超时。

字段指纹绑定成功结果，编辑后旧结果不能保存。API Key 经 Windows 账户凭据存储，普通设置只保存引用。导入不含 Key，最多 1 MB/100 项，校验地址无嵌入凭据/查询/fragment，HTTPS 或本机 HTTP。重名跳过/重命名，新 ID、新引用、Untested，不替换活动配置。自定义预设最多 32 项，不覆盖内置模板。

AI 只接收当前一句话、时间及时区。草稿支持重复和额外时间，确认时使用同一 TodoStore 规则校验。目标在本地按标题匹配，多目标可重新选择；确认后写入并保留一次内存撤销，不上传现有待办或索引。

## 7. 维护与恢复

备份格式 v2 保存文件 SHA-256，兼容 v1 读取。容量 512 MiB、单项 64 MiB、10,000 文件；拒绝路径越界/ADS/链接/未知模块、异常 JSON/schema、凭据字段。SQLite 是派生数据，不复制活跃数据库。模块包括设置、位置、待办/通知、快捷项/图标、预设及实际 LocalAppData 日志。设置 schema 4 增加 hotkeys 与 backup，并在迁移前写入 `.pre-v4.bak`。

QueueRestore 校验并复制待恢复包、记录哈希。用户确认退出，下次启动校验全部输入后保留每个模块 before 快照和 journal，再切换。失败整体 rollback；发现 applying 日志则启动恢复快照。恢复快捷项重写图标为当前根路径；恢复设置清除旧授权索引。旧待办备份不含通知时创建空队列，排队待办缺配套队列时拒绝。

缓存维护只删除无引用图标，并排队在下次启动重建 SQLite/sidecar 和日志。RecoverableAtomicFile 使用唯一临时文件和 .previous，不覆盖不可解析的旧 JSON。诊断只输出白名单版本、计数和稳定状态，不含正文/路径/凭据。

交互卸载默认保留，明确清理才通过离线应用入口删除已知模块、维护快照和设置中记录的本应用凭据；静默保留。损坏/丢失配置中未记录的凭据或未知文件不擅自扩大清理范围。

## 8. 快捷唤出与每日备份

GlobalHotkeyGesture 只接受至少一个 Ctrl/Alt/Shift/Win 修饰键与一个受控普通键，规范化显示并拒绝重复组合。GlobalHotkeyService 创建消息窗口，通过 RegisterHotKey 接收 WM_HOTKEY；不使用低级键盘钩子。每次应用设置先释放原注册，任一新组合失败则释放本轮全部注册。应用退出统一 Dispose；桌宠和托盘入口不依赖全局注册。

默认组合为 `Ctrl+Alt+Space` 搜索和 `Ctrl+Alt+T` 快速待办。搜索入口显示宠物并按既有规则聚焦主页；快速待办选择待办页、执行新建命令并聚焦标题。两者继续调用 TrySelectTab，AI 设置存在未保存修改时仍走已有保留/放弃/取消流程。

AutomaticBackupService 复用 DataMaintenanceService 的模块依赖、JSON/路径/凭据校验和备份格式 v2。正常启动读取设置后在后台执行；文件名保存 UTC 毫秒时间，24 小时内最多一次，成功后才按 1～30 份上限删除旧包。范围固定为设置、位置、待办/通知、快捷项/图标及 Provider 预设，排除凭据、搜索索引和日志。校验、写入或保留清理失败时返回失败状态并保留旧包；卸载明确清理个人数据时删除 automatic-backups。

## 9. 发布证据

collect-release-evidence 收集七组独立报告。release-evidence 绑定版本、提交、输入指纹、时间、环境以及烟雾/签名/硬件资产哈希；verify-release 拒绝 dirty、缺失/过期、FAIL/SKIP、缺项、错资产和错 SHA256SUMS。输入包含源码、测试、脚本、配置、素材、workflow、VERSION、Directory.Build.props 和离线手册。

Release workflow 在受保护的 self-hosted Windows runner 上发布已经通过验证的字节，下载后再比哈希。修改提交或构建输入必须重取证，既有历史成功不能迁移为当前 PASS。候选状态及限制见[当前状态](CURRENT_STATUS.md)。
