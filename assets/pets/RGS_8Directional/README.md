# RGS 8-Direction Characters (Assets 副本)

> 此目录是 `res/images/RGS_8Directional/` 的**合规可分发副本**。`res/` 目录是本地参考、不进仓库;`assets/pets/` 是项目可分发资源、进仓库与安装包。CC0 1.0 协议允许任意复制与再分发。

---

## 1. 出处与授权

| 项 | 值 |
| :--- | :--- |
| 资源名 | Hand-Drawn Square Characters Animated 8 Directions Top Down |
| 作者 | RGS_Dev (rgsdev) |
| 平台 | itch.io |
| 项目页 | <https://rgsdev.itch.io/hand-drawn-square-characters-animated-8-directions-top-down-free-cc0> |
| 授权 | **CC0 1.0 Universal**(公有领域献礼) |
| ZIP SHA-256 | `8171d8d220882dec638d1a56fce63421325468a3765d833ff5d93d4b5be8d174` |
| 引入版本 | 0.1.0(2026-08-26) |

完整协议文本见 `License.txt`;详细资源结构与状态机映射见 `pet.json` 与 `SOURCE.txt`。

---

## 2. 目录

```text
assets/pets/RGS_8Directional/
├── README.md           ← 本文件
├── pet.json            ← 资源元数据
├── SOURCE.txt          ← 出处留痕(下载时间 + SHA-256 + 整理动作)
├── License.txt         ← CC0 1.0 原文
├── frames/             ← 247 单帧 PNG
│   ├── _global/        ← death 7 帧(全局)
│   ├── base/           ← 60 帧
│   ├── hero/           ← 60 帧
│   ├── monster/        ← 60 帧
│   └── skeleton/       ← 60 帧
└── spritesheets/       ← 5 张整图(对照)
```

---

## 3. 应用如何使用

`AiPet.App` 启动时通过 `AssetsResolver.FindPetsRoot()` 找到本目录,加载 `pet.json`,根据 `pet.id = rgs-8dir` 与用户偏好角色(默认 `hero`)创建 `PetFrameCache`,驱动 `PetWindow` 的 8 方向切帧与拖动。

详细技术说明见 `docs/TECHNICAL_DESIGN.md §6.1`。

---

## 4. CI 校验项

- `pet.json` 是合法 JSON,`id = rgs-8dir`,`source.license = CC0-1.0`
- `License.txt` 存在
- `frames/_global/death_*.png` ≥ 1 张
- 至少 1 个角色 `frames/<character>/` 帧数 = 5 方向 × (idle 4 帧 + jump 8 帧) = 60
- `spritesheets/` ≥ 1 张整图
- `SOURCE.txt` 包含 SHA-256
