# 当前代码状态与文档索引

日期：2026-09-11；软件基线：**0.17.0 候选工作区**，由 0.16.1 按 MINOR 升级。本版将受控本地文本正文搜索接入正式产品，没有新增商业化、云端或账号能力。

## 当前实现

| 部分 | 当前能力 |
| :--- | :--- |
| 桌宠/窗口 | RGS 四预设、四页工具窗、常驻/置顶、页内选择、未保存 AI 编辑保护、离线帮助 |
| 搜索 | 授权元数据索引、名称/相对路径、筛选/分页/固定/历史、监听重建；可选 `.txt`/`.md` 受控正文索引、命中摘要、状态、重建与停用清除 |
| 快捷入口 | 分组、固定、多选、拖动或键盘排序、失效扫描、重新定位、批量移除预览和本次运行内撤销 |
| 待办/提醒 | 六种筛选、日历重复、多次时间、规则时区、单项 AI 草稿、今日安排、持久提醒中心、静默/汇总/稍后 |
| AI/维护 | 多配置与能力验证；加密完整配置包迁移全部配置、Provider 预设和 Key；模块备份、事务恢复、诊断、缓存清理 |
| 在线更新 | 启动检查稳定 GitHub Release，可选周期、系统代理和可信 HTTPS 加速；私库令牌仅安全保存后使用，安装器下载须通过 Release SHA-256 清单 |
| EXT-08 | 外部 2D 角色包仍只在 `tests/prototypes` 隔离评估，未接入应用或发行包 |

正文搜索默认关闭，只处理已有授权范围内不超过 128 KiB 的 `.txt`/`.md`，每范围最多 1,000 个正文文件和 16 MiB。普通文字可匹配正文；通配符和正则只处理元数据。正文索引是本机派生数据，不进入普通/自动备份、诊断、日志、更新或 AI 请求，停用或撤销范围会清除对应数据。

TodoDocument 维持 schema 4，设置维持 schema 5；`features.enableContentSearch` 复用已有默认 `false` 字段，无数据迁移。

## 当前验证边界

`scripts/test.ps1 -CI` 已完成：结构与 UTF-8 校验、发布证据反例、Release 全解决方案构建及 **289/289** 单元/集成测试均为 PASS，构建 0 警告、0 错误。正文编码、资源上限、排名、摘要、查询模式分流、撤销竞态、watcher、设置持久化和 WPF 合同均有回归覆盖。

0.17.0 的便携版、安装版、SHA-256、package-smoke、独立 UI、system、performance、signatures 和 hardware 仍待本轮实际收集；历史 0.16.1 结果不作为本轮证据。完整结论见[0.17.0 验证报告](release/0.17.0-test-report.md)。全部正式门禁通过前，0.17.0 不能标记为稳定 GitHub Release。

## 文档入口

- [PRD](Windows桌面宠物产品需求文档_PRD.md)、[工程规范](PROJECT_SPEC.md)、[技术设计](TECHNICAL_DESIGN.md)。
- [用户手册](USER_MANUAL.md) / [离线 HTML](USER_MANUAL.html)、[测试计划](TEST_PLAN.md)。
- [0.17.0－功能扩展计划](0.17.0－功能扩展计划.md)、[已有功能优化](已有功能优化.md)。
- [0.17.0 候选说明](RELEASE_NOTES_0.17.0.md)、[0.17.0 验证报告](release/0.17.0-test-report.md)。
- [GitHub Release 状态](release/GITHUB_RELEASE_STATUS.md)。
- [待办提醒设计](TODO_REMINDER_DESIGN.md)、[原型隔离评估](EXTENSION_PROTOTYPE_REPORT.md)、[角色资产来源](../assets/README.md)。

## 非当前产品能力

PDF/Office/图片 OCR、语义检索、云同步、通用聊天、外部角色包导入、插件执行、3D、商城、支付、账号、广告和遥测未接入。历史计划及报告保留当时证据，不表示当前功能或本轮验收。
