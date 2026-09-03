# 应用图标来源

`windows-ai-desktop-pet.ico` 与同名 PNG 由 `scripts/build-app-icon.ps1` 确定性生成，
基础角色帧取自 `assets/pets/RGS_8Directional/frames/hero/idle_down_01.png`。

- 原始素材：RGS 8-Direction Characters
- 作者：RGS_Dev
- 许可：CC0-1.0
- 用途：应用可执行文件、桌面/开始菜单快捷方式、托盘与安装器标识
- 处理：暖橙圆角底板、透明边距、最近邻缩放，并封装 16～256 像素 PNG 帧的 ICO

重新生成：`pwsh -NoProfile -File scripts/build-app-icon.ps1`。
