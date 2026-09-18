# Windows AI Desktop Pet 0.25.2 正式版说明

发布日期：2026-09-18。0.25.2 是向后兼容的 PATCH 正式版，不是 Alpha、Beta、RC、Preview 或 GitHub Pre-release。

## 本版变化

- 将 `Microsoft.Data.Sqlite` 从 8.0.10 升级到 .NET 8 维护线 8.0.31，传递的 `SQLitePCLRaw.lib.e_sqlite3` 从 2.1.6 升至 2.1.12，修复依赖审计发现的 High 级 CVE-2025-6965。
- 新增 `scripts/test-live-ai.ps1 -AllowSavedCredential` 本机诊断入口。只有用户明确授权后才读取当前活动 AI 配置的 Windows 凭据，真实验证模型列表、结构化待办草稿和今日安排草稿。
- 真实 AI 报告只保留稳定状态、错误类别、延迟和数量；不输出配置名、Provider、端点、模型、凭据目标、API Key、响应正文或生成内容，也不写入用户待办。
- 产品功能、设置 schema、待办/提醒/复盘/专注数据格式和用户工作流均不变。

## 验证摘要

- 完整 Release 构建与 371 项自动化回归通过，0 警告、0 错误、0 跳过。
- 修复后复扫解决方案全部 13 个项目，未发现当前 NuGet 已知漏洞。
- 系统边界 8/8、应用 smoke、搜索数据库往返通过；20,000 条匿名数据、200 次首屏查询 P95 为 5.1041 ms。
- 经用户授权的真实 AI：结构化待办草稿与今日安排草稿通过；模型列表未列出当前模型别名，单列为 `NOT_CONFIRMED`，实际生成能力已独立验证通过。
- 便携版、安装、安装后启动、卸载、远端资产与更新下载结论以 [0.25.2 验证报告](release/0.25.2-test-report.md)为准。

独立 UI 与桌面交互性能诊断因当前 Codex 桌面不能把应用切到前台，在发送输入前安全停止；单屏 100% 环境之外的物理多屏/缩放、休眠、改时、热插拔和 Explorer 重启未执行。它们是可选诊断，不替代也不阻断已定义的正式发布门禁。

## 升级与安装

- 可直接覆盖安装 0.25.1；本版不迁移或改写现有用户数据 schema。
- 便携版也继续使用当前 Windows 账户的 AppData，不是数据随 ZIP 移动模式。
- 下载 `windows-ai-desktop-pet-v0.25.2-portable.zip` 或 `windows-ai-desktop-pet-v0.25.2-setup.exe`，并使用同一 Release 的 `SHA256SUMS.txt` 校验。
- 安装器与二进制未配置 Authenticode 证书时会保持未签名；该状态会在验证报告中如实标记为 SKIP。

## 安全资料

- [GitHub Advisory：CVE-2025-6965 / GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)
- [NuGet：Microsoft.Data.Sqlite 8.0.31](https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.31)
