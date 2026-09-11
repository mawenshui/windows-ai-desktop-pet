> 历史评估：本文保留 0.13.0 当时的试验数据。EXT-07 已在 0.17.0 按更严格边界迁入正式产品，原型代码已移除；EXT-08 仍未接入。当前计划与状态见 [0.17.0－功能扩展计划](0.17.0－功能扩展计划.md) 和 [CURRENT_STATUS.md](CURRENT_STATUS.md)。

# EXT-07 / EXT-08 隔离原型评估

日期：2026-09-07；候选版本：0.13.0。范围来自本次扩展计划 D 阶段，不代表公共功能上线。原型位于 tests/prototypes/AiPet.Prototypes，由测试工程引用；应用工程不引用，不进入便携版或安装器。

## 1. 内容检索 EXT-07

ControlledTextIndex 只在明确的单独正文授权后读取 .txt。严格 UTF-8（允许 BOM），拒绝二进制 NUL 和非法编码；单文件 128 KiB、最多 1,000 个已索引文件、正文总量 16 MiB。重解析点不进入扫描，权限失败中止本次构建；取消/失败保留上一份成功内容索引。

内容数据库独立于名称索引，Pooling=false、secure_delete=ON；撤销取消在途任务并删除数据库及 sidecar，重启不保留授权，重新创建实例清除旧试验索引。不向 AI、日志或诊断发送正文。试验查询仅做大小写归一化子串匹配、最多 50 个相对路径，未引入 OCR、PDF/Office、FTS、向量或云端服务。

匿名样本：500 个文件、1,011,240 字节，构建约 95.3 ms、查询约 3.6 ms，50 个命中，撤销后无结果或数据库残留。测试覆盖单独授权、编码/大小、取消保留旧索引和撤销竞争。

**当时决策：保留隔离原型。** 0.17.0 后续根据真实产品范围完成了单独开关、同一授权目录、`.txt`/`.md`、UTF-8/带 BOM UTF-16、正文暂存表、开关代次、watcher 同步、命中摘要和设置状态，并迁入 `src/AiPet.Search`。删除 SQLite 行仍不是对物理介质的安全擦除声明。

## 2. 2D 角色包 EXT-08

StrictPetPack 对现有 2D 格式实施受限校验：包 ID/版本、固定相对帧路径、角色/动作/方向数量、帧数、PNG 头部尺寸及实际像素解码、SPDX 白名单和 SOURCE.txt 中的来源哈希。最大 1,000 帧、512×512 单帧、4 MiB 单文件、32 MiB 编码总量、128 MiB RGBA 估算量；manifest 64 KiB。不支持脚本、下载、ZIP 导入或任意状态机执行。

ExperimentalPetLibrary 支持预览指纹、导入前及复制后复核、重复 ID 拒绝、启用、损坏回退内置目录、移除；仅复制已验证文件清单到隔离目录。活动选择只在原型运行内有效，未改变正式应用的角色发现和渲染。

使用已有合法 RGS 样本，247 帧，RGBA 估算 16,187,392 字节，预览约 169.1 ms。测试覆盖路径越界、缺帧、超大头部、未知字段、缺许可、重复导入及损坏回退。没有下载新资产。SPDX 白名单和材料完整性校验不能替代作品权属核验。

**决策：保留隔离原型。** 只接受本原型冻结的 2D 子集；完整 JSON Schema 验证、所有方向/状态语义、持久化启用事务、用户导入界面、长时 GPU/解码预算及更多合法素材需下一份 RFC。3D、商城、商业 IP 和在线下载不在范围。

## 3. 搜索对照与复现

相同机器上名称索引 20,000 / 100,000 条，首屏查询 20 次的 P95 分别约 6.9 / 32.7 ms；构建约 107.4 / 454.4 ms。此结果衡量 SQLite 合成元数据，不代表完整桌面冷启动、真实磁盘扫描或多设备性能。

环境：Windows 10.0.26200，.NET 8.0.29，12 逻辑处理器。原始匿名数据和报告位于 build/roadmap-implementation-20260906/benchmarks/prototype-results.json。

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
dotnet run --project tests/prototypes/AiPet.Prototypes/AiPet.Prototypes.csproj -c Release --no-build -- build/prototype-evaluation assets/pets/RGS_8Directional
```

本文只记录 0.13.0 原型结论。EXT-07 的当前产品行为以 PRD SRCH-07 和 0.17.0 验证报告为准；EXT-08 最终是否转产品仍取决于后续 RFC。
