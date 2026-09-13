# GitHub Release 状态

日期：2026-09-13。此清单按最近一次已核对的 GitHub 远端实际状态记录。维护者已明确要求把现有 GitHub Release 全部转为正式发布；转换只修改 Release 状态和标题，不改变历史报告中的 PASS、FAIL、SKIP、未签名事实、标签或资产字节。

## 已发布版本

| 版本 | GitHub 状态 | 资产 |
| :--- | :--- | :--- |
| [v0.7.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.7.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.7.1) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.8.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.8.1) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.9.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.9.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.10.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.10.0) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.11.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.11.0) | 正式 Release（历史既有） | 便携 ZIP、安装 EXE、SHA-256、provenance |
| [v0.12.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.0) | 正式 Release（历史重封装转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.1) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.12.2](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.12.2) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.13.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.13.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.14.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.14.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.15.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.15.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.0](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.0) | 正式 Release（历史转换） | 便携 ZIP、安装 EXE、SHA-256 |
| [v0.16.1](https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.16.1) | 正式 Release（历史转换，Latest） | 便携 ZIP、安装 EXE、SHA-256 |

## 当前 0.21.0 本地状态

0.21.0 已按 UX-210-01～19 完成键盘、搜索恢复、待办可撤销与可访问状态优化，327/327 自动化和本地打包 PASS。独立 UI/强制性能因宿主无法前台激活应用而 FAIL；系统与候选包 smoke 因当前登录会话无法访问安全存储而 FAIL；签名和多数物理矩阵为 SKIP。候选资产来自 dirty 工作区，本轮未创建标签或远端 Release，因此不得写入上方“已发布版本”。完整结果与哈希见 [0.21.0 验证报告](0.21.0-test-report.md)。

## 0.20.0 状态

0.20.0 在 dirty 候选工作区完成内置智能更新线路和设置减负，317/317 自动化、系统 smoke、打包及 package-smoke PASS；独立 UI/强制性能因宿主无法前台激活应用而 FAIL，签名和多数物理矩阵为 SKIP。本轮没有提交、推送、创建标签或创建远端资产，因此只保留本地候选，不得写入上方“已发布版本”。完整结果与哈希见 [0.20.0 验证报告](0.20.0-test-report.md)。

## 0.19.0 状态

0.19.0 已在 dirty 候选工作区生成便携版、安装版和 SHA-256，并通过 package-smoke；独立 UI 与性能因宿主无法前台激活应用而 FAIL，签名与物理环境仍为 SKIP。本轮没有提交、推送、创建标签或访问 GitHub 核对远端，因此 0.19.0 只保留本地候选，未写入上方“已发布版本”。详细结果见 [0.19.0 验证报告](0.19.0-test-report.md)。

## 0.18.1 状态

0.18.1 的候选便携版、安装版和 SHA-256 已从干净源提交 `9b8b1a96e4ebac66531ebe5719b0e12568b4a5b8` 生成并通过 package-smoke，随 `main` 的资产提交 `b3d1b4f405ab6368c5575adac6b88bb733c5c626` 同步。GitHub Tree API 返回的两个资产及清单 blob SHA/大小与本地 Git 对象一致；Release 列表仍没有 `v0.18.1`。UI 与性能为 FAIL，签名与硬件为 SKIP，因此没有创建 `v0.18.1` 标签或 GitHub Release。

## 0.18.0 与 0.17.0 状态

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
