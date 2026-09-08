# 应用图标来源与生成

2026-09-08 按 0.15.0 代码核对；本轮图标内容未变。`windows-ai-desktop-pet.ico` 与同名 PNG 由 `scripts/build-app-icon.ps1` 生成，基础帧为 `assets/pets/RGS_8Directional/frames/hero/idle_down_01.png`。

- 作者素材：RGS_Dev 的 RGS 8-Direction Characters，许可记录 CC0-1.0。
- 处理：暖橙圆角底板、透明边距、最近邻缩放及多尺寸 ICO。
- 使用：AiPet.App 项目 ApplicationIcon、TrayIcon、安装器及应用快捷方式。
- 来源/许可原文见[资源包说明](../pets/RGS_8Directional/README.md)、同目录 SOURCE.txt 和 License.txt。

在仓库根重新生成：`pwsh -NoProfile -File scripts/build-app-icon.ps1`。本次仅同步说明，没有重新生成或替换图标文件。
