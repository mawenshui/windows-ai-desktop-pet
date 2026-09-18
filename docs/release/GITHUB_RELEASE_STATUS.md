# GitHub Release 状态

日期：2026-09-18。仓库 `mawenshui/windows-ai-desktop-pet` 已建立 36 个正式 GitHub Release。所有既有 Release 均不是 Draft 或 Pre-release；标题统一为“Windows AI 桌面宠物 `<版本>` 正式版”，正文为 UTF-8 中文，v0.25.2 是唯一 Latest。`CHANGELOG.md` 另保留没有创建 Release 的历史 0.25.0，因此版本记录数为 37。

## 0.25.2 当前状态

- [v0.25.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.25.2) 已发布为普通正式版和 Latest，不是 Draft 或 Pre-release；便携 ZIP、安装 EXE 与 `SHA256SUMS.txt` 三项资产均可匿名读取。
- [main CI 35355233335](https://github.com/mawenshui/windows-ai-desktop-pet/actions/runs/35355233335) 与 [v0.25.2 Release 工作流 35355639785](https://github.com/mawenshui/windows-ai-desktop-pet/actions/runs/35355639785) 均通过。Release 工作流在标签提交重新执行 automated 与 package-smoke，验证后发布资产。
- 远端便携 ZIP 为 77,049,161 字节、SHA-256 `cef8a4f541132fd3b05226c40c0c6085c1a921c4f60590a9856569109e3cfefc`；安装器为 54,160,507 字节、SHA-256 `93418c501e0af9cfd6a2292bf88314bdec37dbfed31610bb0f60c977a93e9aec`。GitHub API digest、发布清单与本地资产一致。
- 从模拟 0.25.1 调用产品更新客户端，可经“智能加速线路”匿名发现 0.25.2、下载完整安装器，并完成元数据 digest、`SHA256SUMS.txt` 和下载文件 SHA-256 一致性校验；更新端到端结果为 PASS。
- automated 与 package-smoke 两项必需门禁通过。真实 AI 的结构化待办和今日安排通过，模型列表别名单列 `NOT_CONFIRMED`；UI 和完整桌面性能诊断受当前前台能力限制为 FAIL/PARTIAL，签名和扩展物理硬件矩阵为 SKIP，均按可选诊断保留原始状态。

## 0.25.1 当前状态

- [v0.25.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.25.1) 已发布为普通正式版，不是 Draft 或 Pre-release；便携 ZIP、安装 EXE 与 `SHA256SUMS.txt` 三项资产均可下载。Latest 已由 v0.25.2 接替。
- main CI 与 v0.25.1 Release 工作流均通过。Release 工作流重新执行 automated 与 package-smoke 后上传资产，并回下载逐项比对；GitHub API 返回的二进制大小和 SHA-256 digest 与本地清单一致。
- 远端仓库为 Public。共享出口的 GitHub 官方匿名 API 当次达到速率限制，客户端自动回退到内置线路；项目更新客户端匿名发现 `v0.25.1`，经“智能加速线路”下载完整安装器并通过 SHA-256 校验。
- 旧工作流遗留的 v0.12.2～v0.16.1 六条 `release` deployment 均为不承载资产的历史错误记录，已标记 inactive 后删除；不再使用的 `release` environment 也已删除。新 Release 工作流不再创建 deployment。
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
| [v0.24.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.24.0) | 正式版 | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.25.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.25.1) | 正式版 | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.25.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.25.2) | 正式版、Latest | 便携 ZIP、安装 EXE、SHA-256 |

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

GitHub API 返回 36 个 Release；与 `CHANGELOG.md` 的 37 个版本相比，唯一没有 Release 的 0.25.0 正是上文保留的历史未发布状态，没有其他缺失或额外版本。现有 Release 无 Draft、无 Pre-release；v0.25.2 是唯一 Latest，包含 `windows-ai-desktop-pet-v0.25.2-portable.zip`、`windows-ai-desktop-pet-v0.25.2-setup.exe` 和 `SHA256SUMS.txt`。Release 工作流、GitHub API size/digest、本地清单与产品更新客户端真实下载安装器共同确认远端资产可用且哈希一致。
