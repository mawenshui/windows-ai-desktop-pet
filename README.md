# Windows AI Desktop Pet

Windows 桌面宠物与本地效率工具，WPF / .NET 8。当前软件版本 **0.13.0**，于 **2026-09-07** 按现有扩展计划实施并同步文档。当前为候选版本，独立桌面输入、硬件和签名门禁尚未全部通过。

## 当前能力

- RGS 四种桌宠、四页工具窗、常驻与置顶、页内选择、托盘、自启和离线帮助。
- 授权元数据搜索：名称或相对路径、类别/范围、通配符/正则、匹配原因、固定结果、可选本地最近使用排序及 50 条分页。
- 索引重启监听、目录子树更新、事件溢出串行重建，取消或撤销授权不会提交旧任务结果。
- 快捷入口分组、固定、多选、拖动或键盘排序，失效扫描/取消、重新定位、批量移除预览和本次运行内撤销。
- 普通待办和独立提醒：一次性、每日、每周、工作日、自选星期、间隔/结束日期、保存时区、额外时间、下次预览、仅此次修改与整条规则取消。
- 持久化提醒中心、静默时段、错过提醒合并、5/10/30/60 分钟稍后和通道测试；“已提交”不代表“已看到”。
- 多组 AI 配置、模型列表内容验证、主动最小草稿生成验证/取消、不含 Key 的配置交换与自定义供应商预设。
- 维护模块及大小预览、恢复前校验/快照、重启切换/中断回滚、图标依赖恢复、双数据根日志处理和白名单诊断。

内容检索、外部 2D 角色包导入仅有隔离试验工程，未接入应用或发行包；未新增云同步、通用聊天、OCR、3D 或商城。见[当前状态](docs/CURRENT_STATUS.md)、[实施顺序与剩余验收](docs/后续功能扩展计划.md)。

## 使用与开发

[用户手册](docs/USER_MANUAL.md) / [离线手册](docs/USER_MANUAL.html)。便携版也默认使用当前账户 AppData，不是数据随目录移动模式。交互卸载默认保留数据，选择清理才删除已识别的应用数据和凭据引用；静默卸载始终保留个人数据。

需要 Windows、PowerShell、支持 net8.0-windows 的 .NET SDK；安装包构建另需 Inno Setup 6。global.json 允许 SDK 主版本滚动。

```powershell
pwsh -NoProfile -File scripts/test.ps1 -CI
dotnet run --project src/AiPet.App/AiPet.App.csproj -c Release
pwsh -NoProfile -File scripts/package.ps1
pwsh -NoProfile -File scripts/collect-release-evidence.ps1 -Gate package-smoke
```

test.ps1 执行结构校验、发布门禁反例测试、完整解决方案构建和 xUnit。独立证据可分别用 collect-release-evidence.ps1 的 automated、ui、system、performance、package-smoke、signatures、hardware 收集；hardware 自动输出待实机操作的 SKIP，不能代替人工实测。UI runner 不能前台激活应用时停止输入并报告 FAIL。

正式发布前运行 verify-release.ps1。它要求干净的已提交代码、同版本/提交/输入指纹、72 小时内的全部 PASS、两个资产及清单哈希一致。受保护 self-hosted Windows runner 发布已验证的相同字节并下载复核；当前没有创建 0.13.0 标签或 GitHub Release。

## 文档与候选产物

[PRD](docs/Windows桌面宠物产品需求文档_PRD.md) · [工程规范](docs/PROJECT_SPEC.md) · [技术设计](docs/TECHNICAL_DESIGN.md) · [测试计划](docs/TEST_PLAN.md) · [0.13.0 候选说明](docs/RELEASE_NOTES_0.13.0.md) · [验证报告](docs/release/0.13.0-test-report.md) · [隔离原型评估](docs/EXTENSION_PROTOTYPE_REPORT.md)

候选下载：[便携版 ZIP](dist/portable/windows-ai-desktop-pet-v0.13.0-portable.zip)、[安装器 EXE](dist/installer/windows-ai-desktop-pet-v0.13.0-setup.exe)、[SHA-256 清单](dist/checksums/SHA256SUMS.txt)。来源见 RELEASE_PROVENANCE.json，存在资产不等于正式发布。旧报告仅表示当次历史结果。

贡献先读 [AGENTS.md](AGENTS.md)。发行素材来自 assets/pets/RGS_8Directional，来源及 SPDX 见[资产清单](assets/README.md)；res 不参与提交或打包。
