# RGS 8-Direction Characters 发行资源

2026-09-08 按 0.15.0 工作区核对；本轮角色资源未变。本目录为实际可分发输入，打包器复制 `assets/pets/`；本地 `res/` 不参与分发。

## 来源记录

作者 RGS_Dev，资源名 Hand-Drawn Square Characters Animated 8 Directions Top Down，许可记录为 **CC0-1.0**。官方出处、引入日期、原始 ZIP 哈希及整理记录见同目录 `SOURCE.txt`，许可原文见 `License.txt`。文档核对不改写原始授权材料。

## 已核对的文件结构

- `pet.json`：ID 为 rgs-8dir，preferred 为 hero；另有 base、skeleton、monster。
- `frames/`：247 张 PNG，四角色各 60 帧，全局 death 7 帧。
- `spritesheets/`：5 张 PNG 对照整图。
- 每角色 5 个原绘方向，各 idle 4 帧、jump 8 帧；左侧方向由镜像派生，未另存 PNG。

## 代码如何使用

AssetsResolver 找到资源根，PetManifestLoader 反序列化 manifest，PetFrameCache 按角色/动作/方向缓存图片，PetWindow 驱动显示。素材支持八向映射，但当前产品采用前向偏置，只展示正面及左右侧面，避免背对用户。jump 兼作动作/运动，没有独立 walk 素材；death 资源存在不代表有死亡或养成玩法。

默认 hero，可在设置中切换四个预设。详细路径与线程说明见[技术设计](../../../docs/TECHNICAL_DESIGN.md)。

## 校验边界

PetManifestTests 等测试检查内置包 manifest 和帧结构，当前实际计数为 247/5。PetManifestLoader 当前主要反序列化 JSON，不是通用包导入安全验证器；未来导入其他包需增加 schema、路径、尺寸/数量上限、许可和失败回退检查。SOURCE.txt、License.txt 保留原始内容。
