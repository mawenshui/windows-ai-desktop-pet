# GitHub Release 状态

日期：2026-09-18。仓库 `mawenshui/windows-ai-desktop-pet` 已将 0.1.0～0.24.0 共 34 个历史版本建立为正式 GitHub Release。所有既有 Release 均不是 Draft 或 Pre-release；标题统一为“Windows AI 桌面宠物 `<版本>` 正式版”，正文为 UTF-8 中文，v0.24.0 仍是唯一 Latest。

## 0.25.1 当前状态

- 0.25.1 修复代码和文档已完成，最终便携 ZIP、安装 EXE、SHA-256 及 clean provenance 已生成；automated 与 package-smoke 两项必需门禁均为 PASS。当前尚未创建 `v0.25.1` 标签、Draft、Pre-release 或普通 Release。
- 远端仓库已由维护者设为 Public。2026-09-18 的匿名验收中，GitHub 官方 latest API、`gh-proxy.com` 元数据与校验和地址均返回 HTTP 200；项目更新客户端匿名发现 `v0.24.0`，经内置“智能加速线路”下载完整安装器并通过 SHA-256 校验。
- 仓库可见性与真实更新阻塞已经解除。正式发布仅由当次 automated 与 package-smoke 两组必需证据决定；UI、system、performance、signatures、hardware 是可选诊断，必须如实记录，但 FAIL/SKIP 不阻断普通正式 Release。

## 0.25.0 当前状态

- 0.25.0 的代码、文档、便携 ZIP、安装 EXE、SHA-256 和 provenance 已推送到 `main`；远端 `main` 提交为 `3e7628c55e9b2d07bde87515ee20ec0e649c9fac`。
- 当次 automated、system、package-smoke 为 PASS；UI、performance 为 FAIL，signatures、hardware 为 SKIP，详见 [0.25.0 验证报告](0.25.0-test-report.md)。这是当时规则下未发布的历史状态，不追溯改写。
- 远端不存在 `v0.25.0` 标签或 GitHub Release；没有创建 Draft、Pre-release、Preview、Alpha、Beta 或 RC。

## 远端版本清单

| 版本 | 状态 | 发布资产 |
| :--- | :--- | :--- |
| [v0.1.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.1.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.2.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.2.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.2.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.2.1) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.2.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.2.2) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.2.3](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.2.3) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.3.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.3.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.4.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.4.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.5.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.5.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.6.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.6.0) | 正式版（历史补档） | 便携 ZIP、SHA-256 |
| [v0.7.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.7.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.1) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.1) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.2) | 正式版（历史补档） | 源码标签与中文更新说明；无可追溯二进制 |
| [v0.9.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.9.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.10.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.10.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.11.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.11.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256、provenance |
| [v0.12.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.0) | 正式版（历史重封装） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.1) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.2) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.13.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.13.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.14.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.14.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.15.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.15.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.0) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.1) | 正式版（历史整理） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.17.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.17.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.18.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.18.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.18.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.18.1) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.19.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.19.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.20.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.20.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.21.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.21.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.22.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.22.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.23.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.23.0) | 正式版（历史补档） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.24.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.24.0) | 正式版、Latest | 便携 ZIP、安装 EXE、SHA-256 |

## 追溯与资产边界

- 0.2.0～0.6.0、0.8.2、0.19.0、0.20.0、0.22.0 和 0.23.0 没有逐版独立保留的源码提交；标签指向仓库中最早可追溯且覆盖相应能力的提交，Release 正文已明确说明。
- 0.1.0～0.6.0 只保留便携归档，没有真实安装 EXE；0.8.2 没有可追溯二进制，因此只发布源码标签和中文更新说明。没有使用相邻版本资产改名填补缺口。
- v0.12.0 资产是既有历史重封装，Release 正文保留其来源边界。所有历史安装器均未进行 Authenticode 签名。
- 对所有本地仍存在的远端二进制，已逐项核对 GitHub 返回的资产大小和 `sha256:` digest，本轮差异数为 0。

## 0.24.0 当次验证

- 干净源码提交：`2a1f8b1fc53c1ecb0cfae02ae9a32b6b54a09cf6`；发布资产提交：`e2540fb`。
- `scripts/test.ps1 -CI`：349/349 通过，0 跳过，Release 构建 0 警告、0 错误。
- `scripts/smoke-release.ps1 -Version 0.24.0`：便携运行、静默安装、安装后运行、卸载和自启清理通过。
- `scripts/test-system-e2e.ps1 -CI`：8/8 系统几何合同和应用 smoke 通过；当前仅单屏、100% 缩放，Explorer 重启、热插拔、休眠、改时及其他缩放条件为 SKIP。
- 独立 UI 和强制性能测试因当前交互桌面不能把应用置前而停止输入，不能计为通过；未配置受保护签名证书，Authenticode 为 SKIP。
- 正式 Release 是维护者明确要求的发布状态，不会把上述 FAIL/SKIP 或未执行项改写为 PASS。

## 远端终检

GitHub API 返回 34 个 Release，与 `CHANGELOG.md` 的 34 个版本完全一致：无缺失、无额外版本、无 Draft、无 Pre-release、无标题偏差、无空正文或 Unicode 替换字符。v0.24.0 是唯一 Latest，包含 `windows-ai-desktop-pet-v0.24.0-portable.zip`、`windows-ai-desktop-pet-v0.24.0-setup.exe` 和 `SHA256SUMS.txt`。
