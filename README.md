# Windows AI Desktop Pet

Windows 桌面宠物与本地效率工具，WPF / .NET 8。当前软件版本 **0.17.0**，于 **2026-09-11** 新增默认关闭的受控本地文本正文搜索，并完善索引授权、撤销、状态与命中解释。当前为候选工作区，完整发布门禁仍按本次验证结果判定。

## 当前能力

- RGS 四种桌宠、四页工具窗、常驻与置顶、页内选择、托盘、自启和离线帮助。
- 授权本地搜索：名称或相对路径、类别/范围、通配符/正则、匹配原因、固定结果、可选本地最近使用排序及 50 条分页。
- 可选受控正文搜索：只读取授权范围内不超过 128 KiB 的 `.txt`/`.md`，每范围最多 1,000 个文件和 16 MiB；正文命中显示短摘要，停用或撤销范围立即清除派生索引。
- 索引重启监听、目录子树更新、事件溢出串行重建，取消或撤销授权不会提交旧任务结果。
- 快捷入口分组、固定、多选、拖动或键盘排序，失效扫描/取消、重新定位、批量移除预览和本次运行内撤销。
- 普通待办和独立提醒：一次性、每日、每周、工作日、自选星期、间隔/结束日期、保存时区、额外时间、下次预览、仅此次修改与整条规则取消。
- 持久化提醒中心、静默时段、错过提醒合并、5/10/30/60 分钟稍后和通道测试；“已提交”不代表“已看到”。
- 多组 AI 配置、模型列表内容验证、主动最小草稿生成验证/取消；完整配置包可加密迁移全部配置、当前项、自定义 Provider 预设和 API Key，导入后直接启用，同时保留无 Key 兼容迁移。
- 维护模块及大小预览、恢复前校验/快照、重启切换/中断回滚、图标依赖恢复、双数据根日志处理和白名单诊断。
- 默认 `Ctrl+Alt+Space` 唤出搜索、`Ctrl+Alt+T` 快速新建待办；可改键或停用，冲突时整组停用并保留托盘入口。
- 每天最多一次的本地自动备份，默认保留 7 份且可设为 1～30；不包含 API Key、搜索索引或日志。
- 每次正式启动检查一次 GitHub Release；可选 1～168 小时周期检查、系统代理和用户信任的 HTTPS 加速模板。私有仓库令牌仅在安全保存到 Windows 凭据管理器后使用；临时检查失败保留已发现版本，下载完成须通过 Release SHA-256 清单并由用户确认安装。
- 待办支持已逾期、未安排和今天筛选；明确勾选 1～12 项后可用 AI 或完全本地方式生成今日时间块，自动避让未选事项的既有安排，再逐项确认、整批原子写入并撤销最近一批。

外部 2D 角色包导入仍只有隔离试验工程；PDF/Office 正文、OCR、语义检索、云同步、通用聊天、3D 和商城未接入。见[当前状态](docs/CURRENT_STATUS.md)、[实施顺序与剩余验收](docs/0.17.0－功能扩展计划.md)。

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

0.17.0 完整 CI 当前 **289/289 PASS**，Release 构建 0 警告、0 错误；独立 UI、性能、打包烟雾、签名和物理设备结论将在本轮验证完成后写入[0.17.0 验证报告](docs/release/0.17.0-test-report.md)。

正式稳定发布前运行 verify-release.ps1。它要求干净的已提交代码、同版本/提交/输入指纹、72 小时内的全部 PASS、两个资产及清单哈希一致。受保护 self-hosted Windows runner 发布已验证的相同字节并下载复核；0.17.0 在完整证据产生前不得描述为稳定 Release。

## 文档与候选产物

[PRD](docs/Windows桌面宠物产品需求文档_PRD.md) · [工程规范](docs/PROJECT_SPEC.md) · [技术设计](docs/TECHNICAL_DESIGN.md) · [测试计划](docs/TEST_PLAN.md) · [0.17.0 候选说明](docs/RELEASE_NOTES_0.17.0.md) · [验证报告](docs/release/0.17.0-test-report.md) · [Release 清单](docs/release/GITHUB_RELEASE_STATUS.md) · [扩展计划](docs/0.17.0－功能扩展计划.md)

0.17.0 资产及 SHA-256 将由本轮 `scripts/package.ps1` 从当前代码生成；在报告写入实际字节、烟雾结果和远端核验前，不沿用任何历史资产结论。

贡献先读 [AGENTS.md](AGENTS.md)。发行素材来自 assets/pets/RGS_8Directional，来源及 SPDX 见[资产清单](assets/README.md)；res 不参与提交或打包。
