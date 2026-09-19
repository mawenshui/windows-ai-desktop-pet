# 资产清单与来源要求

2026-09-19 核对，适用于 0.26.0；本轮未增加或替换发行资产。发行资源来自本目录；用户导入包只存当前账户 AppData，不进入仓库或安装包；`res/` 仅为本地参考，不提交、不打包。

| 资产 | 路径 | 当前用途 / 许可记录 |
| :--- | :--- | :--- |
| RGS 8-Direction Characters | `pets/RGS_8Directional/` | 四个内置预设，247 帧及 5 张整图；`pet.json`、`License.txt`、`SOURCE.txt` 记录 CC0-1.0 与来源 |
| 方块伙伴图标 | `icons/` | 由 RGS hero 帧生成的 CC0-1.0 衍生图标，用于 EXE、托盘、快捷方式及安装器 |
| manifest schema | `pets/pet.schema.json` | 0.26.0 受限 2D 包结构约定；运行时仍以 `PetPackValidator` 的路径、许可、图片、预算和指纹校验为准 |

资源路径和当前渲染映射见 [RGS 说明](pets/RGS_8Directional/README.md)及[技术设计](../docs/TECHNICAL_DESIGN.md)。许可文本和引入来源文件为历史溯源材料，本次不修改其原文、不宣称重新取得授权。

新增非原创素材必须记录作者、官方来源、明确 SPDX、许可原文、下载/核对日期、原始文件 SHA-256、整理动作、署名及适用分发范围。代码仓库的许可不能自动授权其中的角色、图像、字体或商标；可浏览/下载不等于有再分发权。

宠物包须包含 `pet.json`、`SOURCE.txt`、`License.txt` 和声明帧。0.26.0 的正式 `PetPackValidator` 会校验固定动作/方向、SPDX、HTTPS 来源证据、路径、链接、图片真实解码、资源上限、缺帧和内容指纹；它不替代人工权属核验。检查入口为 `pwsh -NoProfile -File scripts/test.ps1 -CI`。

商业角色 IP 和未经授权参考不能迁入 assets。当前角色包边界见[0.26.0 安全本地角色包设计](../docs/0.26.0－安全本地角色包设计.md)；在线市场、商业化、3D、音频、可执行插件与新依赖需独立评估，不因文档列出候选就进入安装包。
