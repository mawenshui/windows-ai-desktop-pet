# Asset policy

Only assets with documented redistribution rights may be committed here or included in installers and GitHub Releases.

For each non-original asset, record its source, author, license, required attribution, permitted distribution scope, and the date the permission was verified. A repository license for surrounding code does not automatically grant rights to embedded characters, artwork, fonts, audio, or trademarks.

Local reference material without confirmed redistribution permission must remain outside `assets/`, must be ignored by Git, and must not be packaged. The current local `res/images/nailong/` directory is intentionally excluded until the project owner provides applicable redistribution authorization.

## 目录约定

```text
assets/
├── icons/                 # 应用和托盘图标
├── pets/                  # 桌宠视觉资源(每个角色/资源包一个子目录)
│   ├── README.md          # 本目录的资源清单
│   └── RGS_8Directional/  # RGS_Dev 的 8 方向桌宠(CC0,MVP 默认)
└── README.md              # 本文件
```

## 当前资源清单

| 资源包 | 路径 | 协议 | 状态 |
| :--- | :--- | :--- | :--- |
| RGS 8-Direction Characters | `assets/pets/RGS_8Directional/` | **CC0 1.0 Universal** | 0.1.0 MVP 默认,已确认可商用、可修改、可再分发 |
| 方块伙伴应用图标 | `assets/icons/` | **CC0 1.0 Universal（衍生）** | 0.11.0 EXE、托盘、快捷方式与安装器正式标识 |

每个资源包子目录里必须有:

- `README.md` — 资源说明、状态机映射、用法
- `pet.json`(仅 pet 类资源) — 资源元数据(id、license、frameInventory、directionModel、stateMachine)
- `License.txt` — 协议原文(从原作者交付物复制)
- `SOURCE.txt` — 出处留痕(SHA-256、下载时间、整理动作)
- `frames/` — 单帧 PNG
- `spritesheets/` — 整图(可选,作为对照)

CI 校验项见 `res/images/RGS_8Directional/README.md` §8 资源交付检查清单;`assets/pets/*/pet.json` 的 `source.license` 字段必须在白名单内(`CC0` / `CC-BY` / `CC-BY-SA` / `MIT` / `Apache-2.0`)。

## 资源引入流程

1. 在 `assets/pets/<name>/` 创建子目录;
2. 复制资源文件(frames/、spritesheets/、License.txt);
3. 写 `pet.json`(参考 `RGS_8Directional/pet.json` 模板);
4. 写 `README.md` 与 `SOURCE.txt`;
5. 在本 README 与 `docs/PROJECT_SPEC.md §2.1` 各加一行;
6. 跑 `pwsh -NoProfile -File scripts/test.ps1 -CI` 校验通过。
