# Windows AI Desktop Pet 0.7.1 发布候选说明

> 状态：修复已完成，源码、文档、发布资产和校验和已同步到 GitHub `main`。尚未创建版本标签或 GitHub Release，不代表已正式公开发布。

## 修复

- 收紧主页搜索框左内边距，输入文字现在紧贴左侧放大镜图标，并保留约半个正文字符的视觉间距。
- 更新主页布局契约测试，锁定搜索框间距与当前紧凑布局，避免后续回归。

## 验证

- `pwsh -NoProfile -File scripts/validate-project.ps1`：通过。
- `dotnet test src/AiPet.sln --configuration Release --no-restore`：121 项通过，0 失败，0 跳过。
- `scripts/test.ps1 -CI` 的结构校验通过；其还原阶段受本机用户级 `NuGet.Config` 权限限制，已使用相同解决方案的 `--no-restore` 全套测试完成代码回归。
- 重新生成便携版、Inno Setup 安装版和 `SHA256SUMS.txt`，并完成发布文件存在性与哈希生成校验。
- 便携包 `--smoke` 已通过资源、搜索、快捷入口和 AI 预设检查，但在 Windows 凭据管理器往返检查处被当前受限会话拒绝；安装器静默烟雾测试因当前会话无法展开 `{localappdata}` shell folder 而未完成。

## 尚未完成

- 代码签名、版本标签、GitHub Release 上传及远端下载复核仍需单独执行。
