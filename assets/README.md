# 资产清单与来源要求

2026-09-08 核对，适用于 0.15.0 工作区；本轮未增加或替换资产。发行资源来自本目录；`res/` 仅为本地参考，不提交、不打包。

| 资产 | 路径 | 当前用途 / 许可记录 |
| :--- | :--- | :--- |
| RGS 8-Direction Characters | `pets/RGS_8Directional/` | 四个内置预设，247 帧及 5 张整图；`pet.json`、`License.txt`、`SOURCE.txt` 记录 CC0-1.0 与来源 |
| 方块伙伴图标 | `icons/` | 由 RGS hero 帧生成的 CC0-1.0 衍生图标，用于 EXE、托盘、快捷方式及安装器 |
| manifest schema | `pets/pet.schema.json` | 宠物元数据结构约定；存在 schema 不等于运行时执行了完整 schema 校验 |

资源路径和当前渲染映射见 [RGS 说明](pets/RGS_8Directional/README.md)及[技术设计](../docs/TECHNICAL_DESIGN.md)。许可文本和引入来源文件为历史溯源材料，本次不修改其原文、不宣称重新取得授权。

新增非原创素材必须记录作者、官方来源、明确 SPDX、许可原文、下载/核对日期、原始文件 SHA-256、整理动作、署名及适用分发范围。代码仓库的许可不能自动授权其中的角色、图像、字体或商标；可浏览/下载不等于有再分发权。

宠物包须包含 pet.json、来源及许可、帧文件和说明；CI 中现有 PetManifestTests 针对内置 RGS 结构，不是遍历任意新包的通用许可/安全扫描。新包还需补路径越界、图像大小、资源上限和缺帧校验。检查入口为 `pwsh -NoProfile -File scripts/test.ps1 -CI`。

商业角色 IP 和未经授权参考不能迁入 assets。未来角色包能力见[0.15.0－功能扩展计划](../docs/0.15.0－功能扩展计划.md)；3D 与新依赖需先评估，不因文档列出候选就进入安装包。
