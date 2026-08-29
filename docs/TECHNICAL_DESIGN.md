# Windows AI Desktop Pet — 技术设计

| 属性 | 值 |
| :--- | :--- |
| 文档版本 | 0.8.1 |
| 需求基线 | `docs/Windows桌面宠物产品需求文档_PRD.md` V1.2 |
| 工程基线 | `docs/PROJECT_SPEC.md` 1.0 |
| 软件基线 | `VERSION` 0.8.1 |
| 状态 | WPF/.NET 8 技术栈与待办/一次性提醒设计已冻结；系统级通知、安装和完整 E2E 仍待交互环境复核 |

> 本文档定义“代码如何写”，与 PRD（定义产品行为）和 PROJECT_SPEC（定义工程规则）形成三层文档体系。技术栈一旦冻结，章节将标记为 **已冻结**；实现过程中如发生变更，必须先在本文更新并经评审。

## 0. 文档目的与冻结条件

1. 给出 MVP 唯一技术栈与最低运行环境；
2. 划分模块边界、数据模型、线程/进程模型与对外 API；
3. 明确 Windows 凭据安全、本地搜索范围、AI 适配器三类敏感实现的工程约束；
4. 给出便携版与安装版构建、签名与升级策略；
5. 给出单元/集成/E2E 测试框架选型。

冻结条件：CI 通过、PRD 中 P0 验收所依赖的技术路径全部明确、安全/隐私评审完成。冻结后，模块边界与第三方依赖只允许通过 RFC 流程修改。

## 1. 技术栈选型

### 1.1 候选与决策

| 维度 | WPF（.NET 8） | WinUI 3（Windows App SDK 2.x） | PyQt 6 + PySide6 | Electron / Tauri |
| :--- | :--- | :--- | :--- | :--- |
| Windows 10/11 覆盖 | 良好（1809+） | 良好（10 1809+ 但部分控件 11 更稳） | 良好 | 良好 |
| 启动占用（冷启动） | 极低 | 中 | 中 | 高 |
| 资源占用（常驻） | 低 | 中 | 中 | 高 |
| 系统集成（托盘、通知、凭据） | 完整 | 完整 | 较完整（需 pyautogui/winrt） | 需 Node 原生模块 |
| 透明无边框置顶窗口 | 成熟 | 需额外包 | 成熟 | 较弱 |
| 打包自包含 | dotnet publish 单文件 | WindowsAppSDK self-contained | PyInstaller（体积大） | Tauri 较好；Electron 大 |
| AI/搜索代码可移植性 | 良好 | 良好 | 良好 | 一般 |
| 团队/学习成本 | 中 | 中 | 低 | 中 |

**决策（待冻结）**：

- **主选**：**WPF（.NET 8 + Windows Desktop Runtime 8）**。理由：
  1. 常驻轻量，符合 PRD “轻量常驻、点击即用”诉求；
  2. 透明无边框、托盘通知、CredMan P/Invoke、系统权限控制均有成熟路径；
  3. 打包工具链稳定（`dotnet publish` + Inno Setup），可生成 PRD 要求的两种分发；
  4. .NET 8 LTS 至 2026-11-10 之后有持续支持，足够 MVP 与后续两个 minor 版本。
- **备选**：若评审认定 Windows 11 视觉规范强相关，可切换至 **WinUI 3 + Windows App SDK 2.x**，但需重新评估打包体积与自包含依赖；任何切换必须先更新本文档。

> 注：虽然用户已有 PyQt 桌宠经验（见 memory 2026-07-24），但 WPF 更适合本项目的“常驻、轻量、配置安全”约束，UI 复杂度低于桌宠动画。

### 1.2 运行时与系统支持

> 基于 2026-08-26 用户确认：MVP 最低支持 **Windows 10 1809（10.0.17763）+ x64**。

| 项 | 要求 |
| :--- | :--- |
| 最低 Windows 版本 | Windows 10 1809（10.0.17763）+ x64 |
| 推荐版本 | Windows 10 22H2 / Windows 11 23H2 |
| 架构 | x64；暂不支持 Arm64（PRD 未承诺） |
| .NET 运行时 | .NET 8 Desktop Runtime（自包含发布时内嵌） |
| 显示器 | 多显示器、不同缩放（100/125/150/175/200%）与任务栏位置自适应 |
| 权限 | 不要求管理员；AI 凭据使用当前 Windows 账户安全存储 |

### 1.3 主要第三方依赖

| 包 | 用途 | 许可证 | 锁定策略 |
| :--- | :--- | :--- | :--- |
| `Microsoft.Extensions.Hosting` | 通用 Host/依赖注入/日志 | MIT | 中央包管理 + 锁定文件 |
| `Microsoft.Extensions.Logging` | 日志抽象 | MIT | 同上 |
| `CommunityToolkit.Mvvm` | MVVM 源生成器 | MIT | 同上 |
| `System.Data.SQLite` 或 `Microsoft.Data.Sqlite` | 搜索元数据缓存 | Public Domain / MIT | 同上 |
| `Inno Setup 6.x` | 安装器（仅打包期） | 运行时免费、源码定制需许可 | 工具链固定版本 |
| `Newtonsoft.Json` 或 `System.Text.Json` | 配置序列化 | MIT / MIT | 同上 |
| `Vanara.PInvoke`（可选） | Win32 凭据/通知封装 | MIT | 仅在简化 P/Invoke 时引入 |

任何新依赖必须记录于 `docs/THIRD_PARTY.md`（首个实现前创建）并通过供应链审计。

## 2. 解决方案结构

```text
src/
├─ AiPet.App/                      # WPF 启动、组合根、App.xaml
│  ├─ Bootstrap/                   # Host 构建、配置加载
│  ├─ Shell/                       # 主窗口、托盘、单实例
│  └─ Resources/                   # 图标、主题、字符串
├─ AiPet.Pet/                      # 桌面宠物窗口（透明无边框）
├─ AiPet.ToolWindow/               # 工具窗口（主页 + 两个扩展占位页 + 设置）
│  ├─ Pages/
│  │  ├─ HomePage.xaml             # SRCH、QCK
│  │  ├─ PlaceholderPage.xaml      # NAV-02 占位
│  │  └─ Settings/                 # SET、SRCH-02、AI
│  ├─ Controls/                    # 搜索框、结果列表、快捷栏
│  └─ ViewModels/
├─ AiPet.Search/                   # 本地搜索服务（索引、查询、取消）
├─ AiPet.Shortcuts/                # 快捷项 CRUD、排序、图标
├─ AiPet.Todos/                    # 待办领域模型、JSON 存储、一次性提醒调度
├─ AiPet.Storage/                  # 配置持久化、备份/恢复
├─ AiPet.Secrets/                  # AI Key 安全存储（CredMan）
├─ AiPet.AI/                       # AI 适配器接口与内置实现
├─ AiPet.SystemIntegration/        # 自启、电源、通知、显示器
├─ AiPet.Diagnostics/              # 日志、崩溃 dump、诊断导出
└─ AiPet.Common/                   # 通用类型、扩展方法
tests/
├─ unit/
├─ integration/
├─ e2e/                            # Windows Application Driver
└─ fixtures/
```

每个项目独立 csproj；`AiPet.App` 是唯一启动入口；其余项目只暴露接口（`AiPet.Contracts` 由各模块按需公开 `*.Abstractions`）。

## 3. 模块边界

### 3.1 模块依赖图

```text
AiPet.App
   └─> AiPet.ToolWindow
         └─> AiPet.Search, AiPet.Shortcuts, AiPet.Todos, AiPet.Storage, AiPet.AI
   └─> AiPet.Secrets          (只有 App 与 AiPet.AI 直接依赖)
   └─> AiPet.SystemIntegration
   └─> AiPet.Diagnostics
```

**规则**：

- UI 层不得直接调用 `System.Data.Sqlite` / `CredMan` / 第三方 AI SDK；
- 任何外部 IO 必须通过接口注入，UI 只消费 ViewModel；
- ViewModel 不得在构造函数中执行阻塞 IO。

### 3.2 关键模块职责

| 模块 | 职责 | 不应包含 |
| :--- | :--- | :--- |
| `AiPet.Pet` | 透明无边框宠物窗口、拖动、点击唤出、头朝向 | 任何业务逻辑 |
| `AiPet.ToolWindow` | 主页/扩展页/设置 UI 与导航 | 索引、凭据、网络 |
| `AiPet.Search` | 用户授权范围元数据采集、模糊查询、状态广播 | UI、凭据 |
| `AiPet.Shortcuts` | 快捷项持久化、目标校验、启动调用 | UI、网络 |
| `AiPet.Todos` | 待办校验、`todos.json` 持久化、一次性提醒去重与补发 | UI、凭据、网络 |
| `AiPet.Storage` | JSON/SQLite 持久化、备份、迁移 | 业务规则 |
| `AiPet.Secrets` | `CredRead/CredWrite/CredDelete` 封装、掩码生成 | UI |
| `AiPet.AI` | `IAiClient` 接口、错误分类、并发限流 | 真实凭据、UI |
| `AiPet.SystemIntegration` | 自启、电源请求、显示器枚举、通知 | UI 控件 |
| `AiPet.Diagnostics` | 日志、崩溃快照、诊断导出 | 任何 Key/路径/原文 |

## 4. 进程与线程模型

### 4.1 进程拓扑

- **主进程（单进程）**：所有窗口、托盘、后台服务共享同一进程。
- 未来若 AI 解析耗时严重，再评估拆出 `AiPet.AI.Worker` 子进程；MVP 不引入。

### 4.2 线程/任务模型

| 角色 | 线程/SynchronizationContext | 规则 |
| :--- | :--- | :--- |
| UI 线程（WPF Dispatcher） | 渲染、输入、ViewModel 属性变更 | 任何阻塞调用必须切到 `Task.Run` |
| 后台 Worker | `TaskScheduler.Default` + 受控并发 | 通过 `IAsyncEnumerable` 流式回报进度 |
| 搜索取消 | `CancellationTokenSource` 链 | 每次查询生成新 token，UI 切换/重新输入时取消旧 token |
| 单实例 | 命名 Mutex `Local\WindowsAiDesktopPet_v1` | 第二实例向已运行实例发 IPC 唤起 |
| 托盘 | UI 线程消息循环 | 右键菜单弹窗必须在 UI 线程 |

### 4.3 后台任务

- **搜索索引刷新**：用户新增/撤销范围时触发，单范围工作跑在 Worker；进度通过事件总线广播。
- **AI 连接测试**：由 ViewModel 的 `IsTestingAi` 状态阻止重复提交，外层超时 20 秒；适配器内部可使用更短的 HTTP 超时。
- **AI 待办解析**：30 秒外层超时；只发送本次输入、参考绝对时间和时区；结果先进入内存草稿，不直接写入。
- **提醒调度**：应用启动后单一后台计时器串行检查最近到期待办；页内状态更新通过 WPF Dispatcher 回到 UI 线程。
- **诊断快照导出**：UI 触发，Worker 写入 `%TEMP%`，完成后切回 UI 线程打开目录。

## 5. 数据模型与持久化

### 5.1 存储位置

| 数据 | 路径 | 格式 |
| :--- | :--- | :--- |
| 普通设置 | `%APPDATA%\WindowsAiDesktopPet\settings.json` | JSON（带 schema 版本） |
| 快捷项 | `%APPDATA%\WindowsAiDesktopPet\shortcuts.json` | JSON |
| 搜索范围 | `%APPDATA%\WindowsAiDesktopPet\ranges.json` | JSON |
| 搜索元数据缓存 | `%APPDATA%\WindowsAiDesktopPet\index.db` | SQLite |
| 窗口/宠物位置 | `%APPDATA%\WindowsAiDesktopPet\layout.json` | JSON |
| 待办与提醒 | `%APPDATA%\WindowsAiDesktopPet\todos.json` | JSON（schema v1，原子替换） |
| 崩溃/诊断日志 | `%LOCALAPPDATA%\WindowsAiDesktopPet\logs\` | 滚动文本 |
| AI Key | Windows 凭据管理器（目标名 `WindowsAiDesktopPet:AI:<service>`） | CredMan |
| 临时下载/缓存 | `%TEMP%\WindowsAiDesktopPet\` | 进程退出清理 |

### 5.2 关键数据结构

```csharp
public sealed record AppSettings(
    int SchemaVersion,
    bool AutostartEnabled,
    string Theme,
    string UiLanguage,
    AiProviderConfig? Ai);                  // 仅保存非敏感字段与凭据引用

public sealed record AiProviderConfig(
    string ProviderId,                      // 预设 ID，如 "deepseek" / "zhipu" / "qwen" / "moonshot" / "qianfan" / "hunyuan" / "yi" / "siliconflow" / "custom"
    string? Endpoint,                       // 非敏感
    string? Model,                          // 非敏感
    string SecretTargetName,                // CredMan target
    string? LastStatus,                     // "Untested" | "Verified" | 错误码
    DateTimeOffset? LastVerifiedAt);

public sealed record ShortcutItem(
    Guid Id,
    ShortcutKind Kind,                      // File | Application | Url | Folder
    string TargetPath,                      // 原始路径
    string DisplayName,
    string? Description,
    string? IconPath,                       // 用户自定义图标副本
    int Order,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SearchRange(
    Guid Id,
    string Path,
    SearchRangeState State,                 // NotConfigured | Preparing | Ready | Failed
    string? LastError,
    DateTimeOffset? LastIndexedAt);

public sealed record WindowLayout(
    int PetX, int PetY, string PetMonitorId,
    int ToolX, int ToolY, string ToolMonitorId,
    int ToolWidth, int ToolHeight);
```

快捷入口未配置自定义图标时由 WPF 边界的 `ShellIconProvider` 在渲染时向
Windows Shell 请求目标图标：目录使用文件夹图标，文件按扩展名使用关联图标，
应用优先使用程序图标；目标失效、Shell 返回空句柄或自定义图片无法解码时回退
到应用图标。自定义图片仅接受 ICO、PNG、JPG、JPEG，并在复制前验证可解码性。

### 5.3 迁移策略

- 每个 JSON 文件带 `SchemaVersion`；
- 启动时若发现旧版本，先把现有文件改名为 `*.bak-<timestamp>`，再按当前 schema 写新文件；
- 任何破坏性变更必须提供迁移器 `Migrate(int from, int to)`，并在 `TEST_PLAN.md` 列入回归用例。

## 6. 关键子系统设计

### 6.1 桌面宠物

> 角色资源策略见 `docs/PROJECT_SPEC.md` §2.1。MVP 默认使用 **RGS 8-Direction Characters**（CC0），`pet.id = rgs-8dir`；`nailong` 等商业 IP 仍仅作个人参考，不进 `AiPet.Pet` 模块默认资源路径。

- 透明无边框 `Window`，`WindowStyle=None`、`AllowsTransparency=true`、`Topmost=true`；
- 启动时读取 `layout.json`；未找到时落到主屏右下角任务栏上方 80px；
- 显示器枚举使用 `EnumDisplayMonitors`；位置越界时夹回最近工作区；
- 指针移动不足 12px 时仍判定为点击；越过阈值后才进入拖动。单击延迟 250ms 仲裁，双击只触发 `jump` 动作，不经过工具窗口开关路径；
- **资源加载路径**：`AiPet.App/AssetsResolver.cs` 与 `AiPet.Pet/PetFrameCache.cs` 按以下顺序解析宠物资源：
  1. 用户在 `settings.json` 中指定的 `pet.id`（默认 `rgs-8dir`）；
  2. 若该 id 的 `pet.json` 缺失或 `source.license` 非白名单协议（CC0 / CC-BY / CC-BY-SA / MIT / Apache-2.0 / 自有版权），记录警告并回退到 `rgs-8dir`；
  3. 若仍缺失，则以"占位灰圆 + 文字 'Pet'"显示，不抛出异常。
- **RGS 8-Direction Characters 资源结构（2026-08-26 实查）**：
  - 4 角色：`base` / `hero` / `skeleton` / `monster`，每个 60 帧（5 方向 × `idle` 4 帧 + 5 方向 × `jump` 8 帧）；
  - 5 原生方向：`down` / `down_right` / `right` / `up_right` / `up`；
  - 1 全局 death：7 帧，无方向，全局共用；
  - 单帧文件 247 + 整图 5 + License.txt；`frames/_global/death_NN.png` 与 `frames/<character>/<action>_<dir>_NN.png`。
- **8 方向派生**：原资源**只**画了右半圆 5 个方向；左半圆（`left` / `up_left` / `down_left`）由 WPF 运行时 `Image.RenderTransform = ScaleTransform(-1, 1)` 水平镜像派生：

  | 派生方向 | 来源 | 派生方式 |
  | :--- | :--- | :--- |
  | `left`      | `right`       | `ScaleTransform(-1, 1)` 水平镜像 |
  | `up_left`   | `up_right`    | `ScaleTransform(-1, 1)` 水平镜像 |
  | `down_left` | `down_right`  | `ScaleTransform(-1, 1)` 水平镜像 |

  `pet.json.directionModel.derived` 字段明确登记派生关系；切帧器在 `directionModel.columnOrder8Dir` 解析出非原生方向时自动应用 `ScaleTransform(-1, 1)`。**不**事先生成 8 方向的物理 PNG，**所有**派生在 UI 渲染时按需发生。
- **单帧加载与透明裁切**：`PetFrameCache.cs` 读取 `pet.json` 的路径模板，按角色、动作、方向和帧号加载单帧 PNG；左向帧运行时镜像。载入后裁掉透明留白并缓存 `BitmapSource`，使 128×128 原图中的可见角色以桌宠尺寸清晰显示。路径模板只来自 `pet.json`，不得在 UI 中硬编码 `frames/frames` 或资源包目录名。
- **8 方向角度映射**（按 22.5° 离散，与 `pet.json.directionModel.angleMap8Dir` 一致；WPF 屏幕坐标 Y 轴向下，0° = +X = right，+90° = down）：

  | 方向 | 角度区间 (°) | 来源 |
  | :--- | :--- | :--- |
  | `right`      | (337.5, 22.5)  | 原生 |
  | `down_right` | (22.5, 67.5)   | 原生 |
  | `down`       | (67.5, 112.5)  | 原生 |
  | `down_left`  | (112.5, 157.5) | 原生 |
  | `left`       | (157.5, 202.5) | 原生 |
  | `up_left`    | (202.5, 247.5) | **派生**（镜像 `down_left`） |
  | `up`         | (247.5, 292.5) | 原生 |
  | `up_right`   | (292.5, 337.5) | 原生 |

  鼠标角度按 Win32 `GetCursorPos` 与宠物锚点向量计算，先量化到 8 区间，再由 `FromFrontFacingVector` 把 `up_left / up / up_right` 分别折叠为 `left / down / right`。`RenderFrame` 在查帧前再次调用 `ToFrontFacing`，因此拖动、动作或未来状态机入口也无法绕过限制。运行时只展示正面和左右侧面；左向派生方向自动加 `ScaleTransform(-1, 1)`。
- **工具气泡同步**：宠物获得鼠标捕获时暂停气泡失焦自动隐藏；拖动每次更新 `Left/Top` 后同步触发 `VisualPositionChanged`，应用仅对可见气泡调用 `RepositionNear`。重新定位不得调用 `Show`、`Activate` 或修改 `SelectedIndex`，因此跟随不依赖定时轮询，也不会重置当前页签。
- **单实例唤醒**：后台线程使用 `WaitHandle.WaitAny(wakeEvent, cancellationToken.WaitHandle)` 阻塞等待。只有命名事件真实触发时才回到 UI Dispatcher 显示主页；取消或未收到信号不得显示窗口。
- **状态机**：`pet.json` 的 `stateMachine` 描述转换规则；代码侧只负责触发事件（`tick-100ms` / `drag-start` / `click` / `background-task-running` / `background-task-failed`）和参数计算（方向、循环），动画名由配置决定。原资源**没有**单独的 walk 动画；`jump`（8 帧）兼作"运动中/拖动中"动画。
- **远距回正**：`GetCursorPos` 的设备坐标先经 `PointFromScreen` 转换为当前 WPF 窗口的逻辑坐标；180px 近距半径内使用正面/侧面方向跟随，超出后固定使用 `down` 正面帧，避免混合 DPI 下的方向误判。
- **空闲陪伴调度**：`PetIdleBehaviorPolicy` 在 32–66 秒之间安排语句、单次动作或 96–224px 短距离漫游。漫游以 33ms 渲染计时器和对称 cubic ease-in-out 更新原生窗口位置；工具气泡可见、鼠标进入、按下拖动、桌宠隐藏或 Windows 关闭动画效果时不启动/立即中断，并在完成时写回位置。
- **后续扩展**：当 F2+ 评估通过且决定引入 Styloo Chibi 3D 模型时，本节追加 RFC 小节说明：① 3D 渲染（HelixToolkit 或自封装）资源占用；② WPF 透明无边框窗口与 3D 内容的叠加兼容性；③ 资源目录 `res/images/Styloo_Chibi/` 与对应 `pet.json`；④ `AiPet.Pet` 模块新增 `Pet3DSpriteSource.cs`，与 2D 切帧路径互斥切换。

### 6.2 工具窗口

- 单实例，UI 采用透明圆角 `PetToolWindow` + 顶部紧凑导航（Home / Plan placeholder / Extension placeholder / Settings）；主页只呈现搜索、快捷入口和状态，低频配置进入 Settings；
- 唤出定位算法：

  ```
  anchor = renderedPetImageBounds
  workArea = Screen.FromHandle(petHwnd).WorkingArea  // PerMonitorV2 logical coordinates
  if preferredHeight fits above: place above
  else if preferredHeight fits below: place below
  else: choose the larger side and shrink the scrollable popover
  clamp horizontal position to workArea; point the notch toward pet center
  ```
- `PetPopoverPositioner` 为纯函数并覆盖主屏、副屏偏移、上下回退、边缘夹取与可用高度不足测试；不得对 `Screen.WorkingArea` 再次执行 DPI 变换；
- 收起条件：常驻关闭按钮、再次点击宠物、`Esc`、窗口外点击；失焦收起延迟 120ms 仲裁，避免先隐藏后又被宠物点击重新打开；
- 标题栏提供两个独立 `ToggleButton`：`StayOpen` 仅抑制失焦自动收起，`AlwaysOnTop` 仅同步 WPF `Topmost`；显式关闭路径不受 `StayOpen` 影响。两项写入 `settings.json/toolWindow`，默认分别为 `false` / `true`；
- 再次唤出时回到主页（满足 NAV-01）。
- `--preview` 使用与正式窗口相同的视觉树，但在首次 `Show` 前切换为非分层宿主，供 Windows UI 自动化枚举；正式运行仍使用透明分层窗口。

### 6.3 本地搜索

> 基于 2026-08-26 用户确认：
> - **首次启动引导**：预勾选 `桌面 / 文档 / 下载` 三个候选目录（基于 `KnownFolders` 解析结果），用户一键确认或调整后，索引任务才启动；未经确认前不读取任何磁盘内容；
> - **应用入口数据源**：仅 `开始菜单 *.lnk` 与 `注册表 App Paths`，不默认包含 UWP/AppsFolder、PATH 环境变量里的可执行，或协议处理器。

- 数据源：
  - 用户授权文件夹（递归整个子树的名称元数据，跳过隐藏、系统与重解析点）；
  - 系统应用入口：`开始菜单 *.lnk` + `HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths`；
- 首次启动引导流程：
  1. 应用启动后检测 `ranges.json` 为空且无上次缓存；
  2. 解析 `KnownFolders.Folder3DObjects` / `DocumentsLibrary` / `Downloads`（失败则降级为 `Environment.GetFolderPath(SpecialFolder.UserProfile)` 下常见子目录），预勾选展示在主页顶部卡片；
  3. 用户点击“确认”后，逐个目录入队索引；点“稍后”或关闭卡片则保持空范围、主页显示空状态。
- 索引字段：名称、相对路径、扩展名、类型标记、最近修改时间（不读内容）；
- 查询仅匹配 `name` 字段，不匹配父目录路径且不读取文件内容；普通输入使用转义后的大小写不敏感 `LIKE '%kw%'`；
- 用户可在设置中分别开启 `*` / `?` 通配符和 `re:` 前缀正则。正则使用大小写不敏感、区域无关匹配和 200ms 单次超时，非法表达式以可恢复错误返回；
- 主页范围选择器支持全部范围、仅应用及单个授权目录；SQLite 查询通过 `range_id` 过滤；
- 节流：停止输入 300ms 后触发；`CancellationToken` 取消上一次；
- `SearchService` 对单连接 SQLite 访问加互斥门，后台索引和 UI 查询不会并发使用同一连接；结果集合只在捕获的 UI 上下文更新；
- 状态广播：`INotifySearchRangeStateChanged`，UI 决定是否展示“准备中”提示。

### 6.4 快捷项

- 添加流程：选择器 → 解析目标 → 读取系统图标副本（仅 ICO/PNG 嵌入 32×32）→ 持久化；
- 启动：调用 `Process.Start`（默认 `UseShellExecute=true`），捕获 Win32 错误映射为标准化错误；
- 失效检测：启动前 `File.Exists` 或注册表命中；`不存在` 不删除，仅标记；
- 图标副本：复制到 `%APPDATA%\WindowsAiDesktopPet\icons\<guid>.ico`；源文件丢失时回退默认图标。
- 配置写入统一使用唯一临时文件；若旧预览版把 `settings.json` / `shortcuts.json` 错建为目录，则先改名为 `.invalid-directory-backup*` 保留，再写入正确 JSON 文件。重定向配置目录不支持 `File.Replace` 时降级为同卷覆盖移动。

### 6.5 AI 接入

> 基于 2026-08-26 用户决策：默认供应商为 **DeepSeek**；预设国内主流厂家清单，选择内置供应商时立即覆盖填入其服务地址和模型，**填入后所有字段仍可修改**以应对厂商信息更新；提供“自定义（OpenAI 兼容协议）”入口覆盖自建/代理场景；配置完成后支持“一键测试连接”，结果以**连接正常 / 连接异常**两态明确呈现。

#### 6.5.1 核心接口

```csharp
public interface IAiClient {
    Task<AiConnectionResult> TestConnectionAsync(
        string endpoint, string model, string apiKey, CancellationToken ct);
}

public sealed record AiConnectionResult(
    AiConnectionStatus Status,            // Connected | Failed
    AiErrorCategory? ErrorCategory,       // 仅 Failed 时填充
    string? LocalizedMessage,             // 面向用户的本地化摘要
    string? Suggestion,                   // 建议下一步
    DateTimeOffset TestedAt,
    int LatencyMs);
```

`AiClient` 实现统一走 **OpenAI 兼容协议**：测试请求为 `POST {endpoint}/v1/models`（或 `GET {endpoint}/v1/models`），最小请求体，不携带本地业务数据。请求/响应只读取状态码与最小字段用于错误分类。

#### 6.5.2 默认供应商

| 项 | 值 |
| :--- | :--- |
| 名称 | DeepSeek |
| 协议 | OpenAI 兼容 |
| 端点（默认） | `https://api.deepseek.com` |
| 模型（默认） | `deepseek-chat` |
| 鉴权 | 请求头 `Authorization: Bearer <Key>` |
| 测试方式 | `POST {endpoint}/v1/models` |

首次安装且用户未改供应商时，UI 下拉框默认选中 DeepSeek，端点与模型字段自动填充为上述值。

#### 6.5.3 预设国内厂家清单

清单存储为内嵌 JSON 资源（`AiPet.AI/Providers/builtin-presets.zh-CN.json`），**所有字段都是初始默认值，可在 UI 中任意修改后再保存**。字段值更新不及时时，用户直接修改即可，无需等待版本升级。

| 厂家显示名 | ProviderId | 端点 | 模型示例 | 协议 |
| :--- | :--- | :--- | :--- | :--- |
| DeepSeek（默认） | `deepseek` | `https://api.deepseek.com` | `deepseek-chat` | OpenAI 兼容 |
| 智谱 BigModel | `zhipu` | `https://open.bigmodel.cn/api/paas/v4` | `glm-4.5`、`glm-4-flash` | OpenAI 兼容 |
| 通义千问 Qwen | `qwen` | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus`、`qwen-turbo`、`qwen-max` | OpenAI 兼容 |
| 月之暗面 Moonshot | `moonshot` | `https://api.moonshot.cn/v1` | `moonshot-v1-8k`、`moonshot-v1-32k`、`moonshot-v1-128k` | OpenAI 兼容 |
| 百度千帆 | `qianfan` | `https://qianfan.baidubce.com/v2` | `ernie-4.5-8k`、`ernie-3.5-8k` | OpenAI 兼容（v2） |
| 腾讯混元 | `hunyuan` | `https://api.hunyuan.tencent.com/v3` | `hunyuan-turbos`、`hunyuan-standard` | OpenAI 兼容（v3） |
| 零一万物 Yi | `yi` | `https://api.lingyiwanwu.com/v1` | `yi-large`、`yi-medium` | OpenAI 兼容 |
| 硅基流动 SiliconFlow | `siliconflow` | `https://api.siliconflow.cn/v1` | `Qwen/Qwen2.5-72B-Instruct`、`deepseek-ai/DeepSeek-V3` | OpenAI 兼容 |
| 自定义（OpenAI 兼容） | `custom` | （用户填写） | （用户填写） | OpenAI 兼容 |

> 维护策略：清单为产品可读 JSON，技术评审通过后由产品/运营更新；更新通过升级安装包下发，不修改客户端二进制逻辑。

#### 6.5.4 字段与行为

- **下拉选择**：UI 提供“DeepSeek / 智谱 / 通义千问 / 月之暗面 / 百度千帆 / 腾讯混元 / 零一万物 / 硅基流动 / 自定义”九项。
- **自动填充**：选择预设后，**端点（Endpoint）、模型（Model）、协议版本**字段自动填入选中预设的默认值，**Key 字段不会自动填**（永远由用户输入）。
- **覆盖编辑**：上述任意字段在 UI 中始终可编辑；保存以用户当前填写的值为准，不绑定所选预设。
- **提示信息**：下拉右侧展示“信息来源：<厂家官网>”，并提示“字段为厂商公开默认值，如有变化请直接修改”。
- **自定义模式**：选“自定义（OpenAI 兼容）”时，端点、模型、协议版本全部由用户填写，下方显示“本选项不绑定任何厂商，请确认服务地址与模型名称”。
- **必填校验**：端点必须是 `http(s)://` 开头的绝对 URL；模型名非空；Key 长度 ≥ 8 且不含空白。
- **验证快照**：ViewModel 在内存中记录本次通过测试的供应商、端点、模型和 Key 快照；不写日志，不在普通配置中保存 Key。
- **保存动作**：只有当前输入与已通过快照完全一致且存在未保存修改时才启用；先写 Windows Credential Manager，再原子保存非敏感配置、`Connected` 状态和验证时间。保存成功后无待保存更改，按钮重新禁用。
- **修改重置**：任一字段（供应商、端点、模型、Key）变化后立即丢弃验证快照、回到“待测试”并禁用保存；输入内容保持不变。
- **“测试连接”按钮**：字段完整且当前没有测试任务时启用；点击后禁用并显示“测试中…”，使用 20 秒外层超时。测试不消耗真实业务数据，不携带搜索词、路径、快捷项或待办信息；测试结果不直接持久化。
- **失败回退**：连接失败、超时、未分类异常或测试期间配置被编辑时，不写设置/凭据，不清空表单，保存保持禁用。

#### 6.5.5 测试结果呈现

UI 在“测试连接”按钮旁给出**明确两态**反馈：

| 结果 | 视觉 | 文案 | 附加信息 |
| :--- | :--- | :--- | :--- |
| 连接正常 | 绿色对勾 + 成功态背景 | **连接正常** | 耗时（如 `148 ms`）、验证时间（本地时区）、模型名 |
| 连接异常 | 红色叉号 + 失败态背景 | **连接异常** | 错误类别（见下表）+ 建议下一步；不展示响应体、请求头或 Key |

错误类别（与 PRD §3.3 AI 配置行一致）：

| 错误类别 | 触发条件 | 建议文案（示例） |
| :--- | :--- | :--- |
| 必填缺失 | 端点 / 模型 / Key 未通过校验 | “请检查必填项格式” |
| 鉴权失败 | HTTP 401 / Key 无效 | “API Key 无效或已过期，请核对后重试” |
| 无权限 | HTTP 403 / 账户对该模型无访问权 | “当前账户无该模型访问权限，请在服务方检查” |
| 限流 | HTTP 429 | “触发频率限制，请稍后重试” |
| 地址或模型错误 | HTTP 404 / 400 路径 / 模型未找到 | “服务地址或模型名称拼写错误，请核对” |
| 网络不可达 | DNS 失败 / TCP 连接失败 | “无法连接服务地址，请检查网络与服务状态” |
| 超时 | 请求超过 20 秒未返回 | “连接测试超时，请检查网络与服务状态” |
| 未知错误 | 5xx / 异常返回 | “服务返回未分类错误，请稍后重试或查看帮助” |

> 错误提示仅包含类别、本地化文案与建议；不显示原始响应体、HTTP 头、URL 查询参数、Key 任何片段。

#### 6.5.6 实现要点

- `AiPet.AI/Providers/`：每个预设一份 `*ProviderCatalog.cs`（静态元数据 + 默认端点/模型），`ProviderCatalog.LoadAsync()` 加载内嵌资源 + 后续用户扩展（见 §6.5.7）。
- `AiPet.AI/OpenAiCompatibleClient.cs`：唯一 `IAiClient` 实现，按 `Descriptor` 构造请求。
- `AiPet.AI/AiErrorClassifier.cs`：把 `HttpResponseMessage` / `HttpRequestException` / `TaskCanceledException` / DNS 异常映射到 `AiErrorCategory`。
- `AiPet.AI/ConnectionTester.cs`：受 `SemaphoreSlim(1,1)` 串行化，UI 端通过 `IAsyncRelayCommand` 绑定；测试在 `Task.Run` 跑，超时由 `CancellationTokenSource` 控制。
- UI 层（`AiPet.ToolWindow/Pages/Settings/AiSettingsViewModel.cs`）：下拉切换时填充默认值并标记 Provider 来源；测试结果通过 `IConnectionTestState` 状态机呈现“未测试 / 测试中 / 正常 / 异常”四态。

#### 6.5.7 用户扩展预设（后续可选）

MVP 阶段不开放 UI 自定义预设。若后续版本需要，按以下方式扩展：

- 用户在设置中“新增预设”，填写显示名、端点、模型、协议版本，保存到 `%APPDATA%\WindowsAiDesktopPet\providers\user.json`；
- 启动时合并内嵌预设与用户预设，UI 下拉同时显示；
- 卸载或“一键重置 AI 配置”时提示是否一并删除用户预设，默认保留。

#### 6.5.8 安全约束

- Key **仅**存入 CredMan（target 名 `WindowsAiDesktopPet:AI:<ProviderId>`，ProviderId 自定义时使用 `custom`）；
- 普通配置 `settings.json` 只保存 `ProviderId / Endpoint / Model / Protocol / LastStatus / LastVerifiedAt`；
- 测试请求不带业务数据；响应只读取 `StatusCode` 与最少必要头部；
- 日志、崩溃、遥测在所有路径下经 `KeyMaskInspector` 自检（见 §8）。

### 6.6 待办、AI 草稿与提醒

详细产品与状态决策见 `docs/TODO_REMINDER_DESIGN.md`。0.8.0 的实现由三个边界组成：

1. `AiPet.Todos` 提供 `TodoItem`、`TodoStore` 和 `ReminderScheduler`。`TodoStore` 使用 `RecoverableAtomicFile` 写入独立 `todos.json`，损坏文件只在本次会话回退为空列表，不覆盖原文件。
2. `AiPet.AI` 新增独立 `ITodoAiClient`，不改变只负责连接测试的 `IAiClient`。`OpenAiCompatibleTodoClient` 调用 `/chat/completions`，严格提取 JSON，并把鉴权、权限、限流、地址/模型、网络、超时和格式失败映射为标准状态。
3. `AiPet.ToolWindow/TodoViewModel` 维护手动编辑、筛选、AI 草稿、唯一目标选择、确认、撤销和页内提醒状态；`HomeViewModel` 只暴露组合后的 `Todo` 子 ViewModel，不承载待办业务逻辑。

#### 6.6.1 数据与时间

- `TodoItem` 包含标题、备注、`DueAt`、`ReminderAt`、完成状态、提醒状态和必要时间戳；时间使用 `DateTimeOffset` 保存绝对瞬间与创建时偏移。
- 手动输入按 `TimeZoneInfo.Local` 解释并校验夏令时无效区间；页面和 AI 确认卡始终显示完整年月日、`HH:mm` 和 UTC 偏移。
- 一条待办最多一个一次性提醒；完成待办会停止尚未触发的提醒，恢复待办不会自动恢复已取消或已投递提醒。
- “仅取消提醒”不删除待办；“10 分钟后提醒”只更新下一次提醒时间和提醒状态。

#### 6.6.2 AI 确认边界

- 创建请求不发送现有待办列表；修改、完成、删除和稍后提醒只在本地按 AI 返回的 `targetTitle` 查找，并在多个同名目标时要求用户选择。
- AI 返回的标题、时间和操作都视为不可信输入；过去时间、缺失标题、未知操作或非法 ISO 时间转为澄清，不进入存储。
- 确认前 `TodoStore` 与调度器无新增记录；确认后只执行确认卡中的一次操作。
- 本版不持久化 AI 对话或原始响应；失败保留输入并提供重试与手动回退，清除按钮立即清空内存草稿。

#### 6.6.3 提醒运行条件

- 应用处于运行状态时，`ReminderScheduler` 默认每 15 秒串行检查 `Pending + Scheduled/Snoozed + ReminderAt <= now` 的记录。
- 到期时先在待办 ViewModel 生成页内提醒，再提交 WinForms `NotifyIcon` 托盘气泡；两者成功建立后记录为 `Delivered`。
- 应用退出或系统休眠期间不会后台唤起；下次应用启动时，对仍为待投递状态的过期一次性提醒补发一次并标注“补发提醒”。
- 回调失败记录为 `Failed`，不伪装为成功，也不会自动重复轰炸；用户可编辑或重新设置提醒后再次调度。
- 托盘气泡可能被 Windows 专注助手或系统策略隐藏，因此本版把页内提醒作为运行期间的可靠状态，托盘气泡只作补充通道。

### 6.7 自启

- 注册表：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `WindowsAiDesktopPet`，值数据 `"<exe> --tray"`；
- 不写 `HKLM`，不要求管理员；
- 写入后立即 `RegQueryValueEx` 校验；失败回滚 UI 开关并显示原因。

### 6.8 帮助

> 基于 2026-08-26 用户确认：MVP **仅交付离线 HTML 手册**，不内置在线兜底；后续若启用在线方案，需先经过隐私与官网链接评审后再加。

- 离线手册随安装包提供，路径 `docs\USER_MANUAL.html`（多页 `<iframe>` 或单页带锚点）；
- 打开使用 `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`；
- 失败时显示安装内相对位置 + “修复安装”提示，不打开未确认网址；
- 离线手册与安装包版本强绑定：`USER_MANUAL.html` 与 `VERSION` 一同通过 Inno Setup 打包，发布前由 `scripts/test.ps1 -CI` 校验存在性。

## 7. 异常、日志与诊断

| 类别 | 处理 |
| :--- | :--- |
| 业务异常 | 自定义类型 `PetException` + 分类 `ErrorCategory`；UI 映射为标准化文案 |
| 第三方库异常 | 在适配器边界 catch 并转换为领域异常，原始 stack 写入日志 |
| 崩溃 | `AppDomain.UnhandledException` + `Dispatcher.UnhandledException`；写崩溃 dump 至 `%LOCALAPPDATA%\WindowsAiDesktopPet\crash\` |
| 日志分级 | `Information` / `Warning` / `Error`；Key、查询原文、完整路径、待办标题/备注默认 `Information` 之外不记录 |
| 诊断导出 | 用户在托盘菜单触发；生成 `diagnostic-<timestamp>.zip`（设置、布局、范围、日志、崩溃列表），**不包含 Key 和搜索缓存** |

## 8. 安全与隐私约束

1. **AI Key 仅存 CredMan**。普通配置只保存凭据 target 名与状态；任何写普通配置、日志、崩溃、遥测、UI 状态的代码路径都必须做 `KeyMaskInspector` 自检；
2. **搜索范围最小化**：未授权目录不得读取名称以外的元数据；撤销后删除该范围对应缓存行；
3. **不静默提权**：不调用 `runas`，不修改 UAC；
4. **AI 请求最小化**：测试连接只发最小请求；后续对话能力复用同一约束；
5. **遥测先评审**：MVP 关闭远程埋点，仅本地匿名统计可在调试模式下开启；
6. **诊断导出白名单**：导出前过滤敏感字段；导出动作记录到本地审计日志。

## 9. 国际化与主题

- MVP 仅中文（zh-CN），UI 字符串走 `.resx`；
- 主题：浅色 / 深色 / 跟随系统，依赖 `WindowsTheme` API；
- 字体：默认 `Microsoft YaHei UI` + `Segoe UI Variable`，不引入第三方字体。

## 10. 性能预算

| 指标 | 目标 |
| :--- | :--- |
| 工具窗口唤出（点宠物到可交互） | P95 ≤ 300ms（PRD AC-NFR-01） |
| 搜索首屏可交互 | P95 ≤ 1s，20,000 条元数据（PRD AC-NFR-01） |
| 空闲内存 | 待技术验证后锁定，MVP 不在文档中虚构数字 |
| 启动到托盘可见 | ≤ 3s（验收机） |
| 冷启动到主页 | 不作为验收口径，但需在性能日志中记录 |

性能验证方法：固定一台参考机（CPU/内存/磁盘型号记入 `TEST_PLAN.md`），CI 上不强制；灰度期人工抽样。

## 11. 打包与发布

### 11.1 便携版

```powershell
pwsh -NoProfile -File packaging/build-portable.ps1 -Version 0.8.1
```

- 入口：`dotnet publish src/AiPet.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true`；
- 输出目录：`build/portable/win-x64/`；
- 打包脚本：复制 `assets/`, `docs/USER_MANUAL.html`, `LICENSE`；使用 `Compress-Archive` 生成 `dist/portable/windows-ai-desktop-pet-v<version>-portable.zip`。

### 11.2 安装版

```powershell
pwsh -NoProfile -File packaging/build-installer.ps1 -Version 0.8.1
```

- 工具：Inno Setup 6.x；
- 行为：安装到 `%LOCALAPPDATA%\Programs\WindowsAiDesktopPet\`，提供开始菜单与桌面快捷方式（默认关闭），支持升级与卸载；卸载不删除用户配置（除非用户勾选）；
- 产物：`dist/installer/windows-ai-desktop-pet-v<version>-setup.exe`。

### 11.3 签名

- MVP 阶段使用测试签名（自签）并在 README 标注“不保证 SmartScreen 信任”；
- 公开发布前接入 EV 代码签名证书（计划项，不在 MVP 范围）。

### 11.4 升级与回滚

- 安装器升级：旧版本数据目录不重命名；`SchemaVersion` 触发迁移；
- 便携版：仅替换 `AiPet.App.exe` 与依赖；`settings.json` 保留；
- 回滚：保留最近一次成功启动的备份在 `backups/`；`--safe-mode` 启动时跳过用户自定义 UI。

## 12. 测试框架选型

| 类型 | 工具 | 备注 |
| :--- | :--- | :--- |
| 单元 | xUnit + FluentAssertions + NSubstitute | .NET 生态主流 |
| 集成 | xUnit + 真实 SQLite / 真实 CredMan（测试账户） | 隔离到 `AiPet.TestVault` |
| E2E | Windows Application Driver（WinAppDriver）/ FlaUI | 覆盖 UI 自动化与可访问性 |
| UI 视觉 | 仅人工（锁屏/缩放/多屏） | 不在 CI 自动跑 |
| 性能 | BenchmarkDotNet + 脚本抽样 | 灰度期人工 |
| 安全 | 自研扫描 + `Microsoft.Security.Code.Analysis` | Key 字符串模式匹配 |

## 13. 风险与待定

| 风险 | 触发 | 应对 |
| :--- | :--- | :--- |
| CredMan 在受限账户不可用 | 公司账户策略 | 降级为 DPAPI 加密的本地文件 + 用户告知 |
| 透明无边框在锁屏下显示异常 | 焦点切换 | 锁屏时隐藏宠物，登录后恢复 |
| Inno Setup 升级破坏旧配置 | 用户主动覆盖安装 | 安装器先备份 `settings.json` 再覆盖 |
| AI 供应商协议差异 | 多供应商 | 统一走 OpenAI 兼容协议；预置国内主流 8 家 + 自定义；选中预设后字段仍可改以应对厂商更新 |
| AI 待办结构化输出差异 | 多供应商不完全支持 JSON 模式 | 使用纯文本 JSON 契约、本地严格解析和字段校验；失败保留输入并允许手动填写 |
| 应用退出/休眠导致提醒延迟 | 本版没有系统后台任务 | 启动时对未投递的一次性提醒补发一次；页面明确运行条件，不宣称退出后准点唤起 |
| 托盘气泡被系统抑制 | 专注助手或账户策略 | 同时保留页内提醒与失败诊断；后续评估 Windows App SDK 通知注册和操作按钮 |
| 索引大目录耗时 | 桌面/下载含数十万文件 | 仅扫描用户授权范围，跳过重解析点；按范围拆分并缓存查询，后续引入增量索引 |
| 主题色对比度 | 暗色下文本 | 走 Fluent 主题 + 文本对比度自检 |

## 14. 评审与变更流程

1. 任何对本文“已冻结”章节的修改必须先开 RFC（轻量：本文末尾追加 `## RFC-xxxx` 段落说明影响、替代方案、风险）；
2. CI 会在文档格式校验阶段检查本文存在性、版本号、链接完整性；
3. 技术栈从“待冻结”转为“已冻结”后，必须同步更新 `PROJECT_SPEC.md` 第 4 节交叉引用。
