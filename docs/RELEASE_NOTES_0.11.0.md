> 历史记录：正文保留当时版本、验证结果和提交状态。当前 0.15.0 范围与验收见 [CURRENT_STATUS.md](CURRENT_STATUS.md) 和 [本次报告](release/0.15.0-test-report.md)。

# Windows AI Desktop Pet 0.11.0 发布说明

## 主要变化

- 正式应用身份：CC0 方块伙伴多尺寸图标用于 EXE、托盘、快捷方式与安装器；补齐产品、公司、描述、支持和更新信息。
- 本地数据中心：设置 schema v3 与迁移前备份；按模块备份/恢复、缓存清理和白名单诊断导出，凭据始终排除。
- 搜索增强：授权目录 FileSystemWatcher 事件合并、重命名处理与溢出回退；完全匹配/前缀/连续包含排序说明；每页 50 条继续加载；应用入口按规范化目标去重。
- 提醒增强：多提醒时间、每日/每周/工作日/自定义星期重复规则、自适应下一到期调度、恢复/时间变化重排，以及托盘“完成/10 分钟后/打开详情”操作。
- 界面与无障碍：跟随系统、浅色、深色和高对比度主题；漫游、气泡和鼠标跟随可分别关闭；维护页与键盘可达的继续加载入口。
- AI 与快捷项：Provider 能力元数据、安全外部预设 schema、非敏感诊断模型、批量添加结果和仅清理无引用图标缓存。
- 发布工程：独立系统 E2E runner、签名与验签脚本、可选证书签名门禁和 Release provenance 文件。

## 验证边界

- 自动化回归为 170 项，0 失败、0 跳过（打包前记录）。
- 代码签名需要外部证书与 CI secret；没有配置证书时构建明确标记 unsigned，不声称已签名。
- Explorer 实际重启、物理多屏/缩放和休眠属于真实 Windows 会话矩阵；runner 对缺少的环境输出 SKIP，不把它伪装为 PASS。
- OCR、语义搜索、3D 角色和在线帮助依照 RFC 暂缓上线，原因及进入条件见 `docs/RFC_0.11_扩展方向评审.md`。

## 发布验证

- 源码提交：`60461cbd45e025f032acf0f4e59609e3d5fa4fa6`。
- 便携运行、隔离安装、安装后启动和卸载烟雾测试：通过。
- 系统 runner：边界契约 8/8 与应用 smoke 通过；Explorer 重启未获破坏性会话许可而 SKIP；当前主机仅一台物理显示器，因此物理多屏项 SKIP。
- 本次产物未签名；Windows 可能显示 SmartScreen 提示。

## 发布资产与 SHA-256

- `windows-ai-desktop-pet-v0.11.0-portable.zip`：`6e3b8b8dc939c6429afc4f67c5d9bd075aaaa2306103ab0abe1170e0d9a6b715`
- `windows-ai-desktop-pet-v0.11.0-setup.exe`：`a7521d26d4ccce6e64f6cf3bd6ea183f9ef1c925266cfd29b53a05f30ad659e3`
