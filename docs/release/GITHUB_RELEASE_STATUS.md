# GitHub Release 状态

日期：2026-09-12。此清单按最近一次已核对的 GitHub 远端实际状态记录；`Pre-release` 只是集中提供历史候选下载，不改变原测试报告中的 PASS、FAIL、SKIP，也不等同于正式稳定发布。

## 已发布版本

| 版本 | GitHub 状态 | 资产 |
| :--- | :--- | :--- |
| [v0.7.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.7.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.1) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.1) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.9.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.9.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.10.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.10.0) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.11.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.11.0) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256、provenance |
| [v0.12.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.0) | Pre-release；历史重封装 | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.1) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.2) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.13.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.13.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.14.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.14.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.15.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.15.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.0) | Pre-release | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.1) | Pre-release；历史候选 | 便携 ZIP、安装 EXE、SHA-256 |

## 当前 0.18.0 状态

0.18.0 的候选便携版、安装版和 SHA-256 已从干净源提交 `ca0683249486689f91d6b517fea5c01a6349b77b` 生成并通过 package-smoke，随 `main` 的资产提交 `8ebcfad8a78c87cd4ddf33745ad85f5140193973` 同步。GitHub Tree API 返回的两个资产及清单 blob SHA/大小与本地 Git 对象一致；Release 列表最高仍为 `v0.16.1`。UI 与性能为 FAIL，签名与硬件为 SKIP，因此没有创建 `v0.18.0` 标签或 GitHub Release。

0.17.0 的候选便携版、安装版和 SHA-256 已随源码和文档同步到 `main` 的资产提交 `4ee8809bd703c1417c1d4ed0c29fbfba5a37b558`。GitHub Git Tree API 返回的 blob SHA/大小与本地 Git 对象一致。UI 与性能为 FAIL，签名与硬件为 SKIP，因此没有创建 `v0.17.0` 标签或 GitHub Release。

本次新增的 12 个历史候选均先以草稿上传，确认三个资产齐全后再发布为 Pre-release。随后通过 GitHub 资产 API 逐版下载全部 15 个 Release 的便携版、安装版和 `SHA256SUMS.txt`；下载字节与本地 SHA-256 全部一致。所有安装器均未签名。

v0.12.0 没有保留原始 ZIP/安装器，但工作区保留了产品版本为 `0.12.0+87da027` 的完整便携目录。本次从该目录重新封装 ZIP，并使用当前 Inno Setup 定义生成安装器；静默安装、应用 `--smoke` 和卸载均为 PASS。Release 说明明确标注它是历史重封装。

## 未建立 Release 的早期记录

| 版本 | 原因 |
| :--- | :--- |
| 0.1.0、0.2.0～0.2.3 | 只有早期便携/暂存归档，没有真实安装 EXE；程序元数据仍为默认 `1.0.0`，不能冒充对应正式版本。 |
| 0.3.0～0.6.0 | 只有便携归档，没有对应安装器与可重现的独立源码提交。 |
| 0.8.2 | 只有变更记录和候选说明，没有该版本的便携包、安装器或独立源码快照。 |

这些记录不满足“每个 Release 同时提供便携版和安装版”的要求，因此没有使用相邻版本改名、回填当前程序或生成无法追溯的假资产。
