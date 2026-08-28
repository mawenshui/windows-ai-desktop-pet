# 待办与提醒设计

| 属性 | 值 |
| :--- | :--- |
| 目标版本 | 0.8.0 |
| 需求范围 | `TODO-01`～`TODO-08`、`AI-05` |
| 验收范围 | `AC-FUT-01`～`AC-FUT-08` |
| 设计状态 | 已冻结，可实现 |

## 1. 本版范围

- 将“计划”占位页替换为可用的“待办”页，提供待处理、今天、即将到期和已完成视图。
- 支持手动新建、查看、编辑、完成、恢复、仅取消提醒和删除待办。
- 支持一个待办最多一个一次性提醒；重复提醒和多提醒留到后续版本。
- 使用已保存且测试通过的 OpenAI 兼容配置，把一句自然语言解析为结构化草稿。
- AI 只生成草稿或变更预览。创建、修改、完成、稍后提醒和删除都必须再次确认后才写入。
- 应用运行时以页内提醒为可靠反馈，并同时提交 Windows 托盘气泡通知；应用退出后不保证准点提醒。

## 2. 数据模型

待办保存在当前 Windows 账户的 `%APPDATA%\WindowsAiDesktopPet\todos.json`，使用 UTF-8 JSON、`schemaVersion = 1` 和原子替换写入。

```text
TodoItem
  Id: Guid
  Title: string                     必填，去除首尾空白
  Notes: string                     可空
  DueAt: DateTimeOffset?            绝对时间，含偏移量
  ReminderAt: DateTimeOffset?       下一次一次性提醒时间
  Status: Pending | Completed
  ReminderState: None | Scheduled | Delivered | Snoozed | Cancelled | Failed
  CreatedAt / UpdatedAt: DateTimeOffset
  CompletedAt: DateTimeOffset?
  LastReminderAttemptAt: DateTimeOffset?
  ReminderFailureCode: string?
```

约束：

1. `Title` 不得为空；`DueAt` 和 `ReminderAt` 均按本机当前时区输入并转换为 `DateTimeOffset`。
2. 已完成待办不再触发提醒；恢复待办不会自动恢复已取消或已投递提醒。
3. “仅取消提醒”清空 `ReminderAt` 并记录 `Cancelled`，不删除待办。
4. “稍后提醒 10 分钟”只更新 `ReminderAt` 和 `ReminderState`，不改变完成状态。
5. 删除待办是物理删除；UI 必须在执行前显示包含待办标题的确认。

## 3. 提醒调度与补发

`ReminderScheduler` 在应用启动后工作，并串行检查 `Pending + Scheduled/Snoozed + ReminderAt <= now` 的记录。

1. 正常到期：页内展示提醒操作条，并调用托盘通知；成功提交后标记 `Delivered`。
2. 应用未运行或系统休眠：下次启动/恢复检查时，对仍为 `Scheduled/Snoozed` 的过期提醒补发一次，页内明确显示“补发提醒”。
3. 同一提醒一旦进入 `Delivered`、`Cancelled` 或 `Failed`，不会再次自动投递；用户选择稍后提醒后才重新进入 `Snoozed`。
4. 通知回调抛出异常或明确失败时记录 `Failed`，界面显示可诊断状态，不把它记作成功。
5. Windows 可能根据专注助手或系统策略隐藏托盘气泡，因此本版把页内提醒作为运行期间的可靠反馈；托盘气泡是补充通道。

## 4. AI 状态机

```text
Idle
  -> Parsing
  -> DraftReady              创建草稿或唯一目标的变更预览
  -> NeedsTarget             存在多个同名待办，用户选择唯一目标
  -> NeedsClarification      缺少时间、存在冲突、时间已过去或没有匹配目标
  -> Error                   未配置、鉴权、限流、网络、超时或格式错误

DraftReady / NeedsTarget
  -> Confirmed               仅此时写入 TodoStore
  -> Cancelled               清除草稿，不写入
  -> ManualFallback          保留原句并预填手动表单
```

AI 请求只包含：用户本次输入、本机当前绝对日期时间、当前时区，以及执行修改类意图时用于唯一目标确认的最小待办字段。不会附带搜索历史、文件路径、快捷项、API Key 以外的配置或其他本地数据。

本版不持久化 AI 对话。输入、澄清和草稿只保存在内存中，“清除 AI 内容”立即清空这些状态。失败时原输入保持不变，并提供重试和转手动填写。

## 5. OpenAI 兼容协议

- 请求：`POST {endpoint}/v1/chat/completions`；当用户端点已经以 `/vN` 结尾时追加 `/chat/completions`。
- 鉴权：`Authorization: Bearer <Key>`。
- 模型：使用设置页已保存且连接测试通过的模型。
- 输出：要求 JSON 对象，包含 `status`、`operation`、`title`、`targetTitle`、`notes`、`dueAt`、`reminderAt` 和 `clarification`。
- 客户端再次校验标题、绝对时间、过去时间、操作类型和目标唯一性；模型输出不能绕过领域校验。

## 6. 页面结构与状态

待办页采用紧凑操作队列布局：

1. 顶部是一句话输入条，解析时保留输入并显示进度。
2. 解析成功后在原位展示结构化确认卡，完整显示年月日、时间、UTC 偏移和操作前后差异。
3. 手动编辑器默认折叠；“新建待办”或“转手动填写”时展开，标题、备注、截止和提醒按单列排列。
4. 列表以待处理为默认视图，提供今天、即将到期和已完成筛选；每行显示标题、时间、一次性提醒状态和可用操作。
5. 首次空状态提供“新建待办”动作；错误、AI 降级、保存失败和提醒失败均使用可恢复的内联状态，不只依赖瞬时提示。

键盘与无障碍：所有操作为真实 WPF 控件并具有可访问名称；`Tab` 顺序跟随视觉顺序；单行输入按 `Enter` 解析；编辑器保存按钮保持可达并在提交时显示字段错误。

## 7. 测试矩阵

| 层级 | 覆盖 |
| :--- | :--- |
| 单元 | JSON 往返、损坏文件恢复、标题/时间校验、完成/恢复/取消提醒/删除、稍后提醒 |
| 单元 | 到期只投递一次、补发、通知失败不记成功、已完成不投递 |
| 单元 | AI 端点拼接、JSON 提取、错误分类、过去时间澄清、同名目标选择、确认前无副作用 |
| 单元 | 手动表单创建/编辑、筛选、AI 失败保留输入、清除内存会话、撤销 AI 操作 |
| UI 结构 | 待办导航、空状态、确认卡、手动表单、提醒操作条和可访问名称 |
| 完整门禁 | `scripts/test.ps1 -CI`、便携/安装打包及受限会话可执行的烟雾检查 |

## 8. 后续范围

- 每日、每周、工作日、自定义重复和同一待办多次提醒。
- 使用具有操作按钮和可查询权限状态的 Windows App SDK 通知通道。
- 应用未运行时由系统注册的后台任务准点唤起。
- AI 多轮对话持久化、保留周期配置和跨设备同步。
