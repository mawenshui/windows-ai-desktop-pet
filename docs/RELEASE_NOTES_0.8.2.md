# Windows AI Desktop Pet 0.8.2 发布说明

- 发布日期：2026-08-29
- 版本类型：Patch（向后兼容缺陷修复）
- 状态：发布候选

## 本次修复

- 修复 AI 配置供应商下拉框使用字符串值回写导致选择无效的问题。现在通过 `SelectedItem` 绑定完整的 `AiProviderDescriptor`，选择任意供应商都会同步保存其标识、默认服务地址和模型。
- 修复待办页“一句话安排”输入框中文字实际存在但不可见的问题。自定义 `Field` 模板改为将圆角背景边框与 `PART_ContentHost` 同级叠放，避免 WPF 将 `TextBoxView` 错误测量为 2px 高；保留输入、选择、复制和 ViewModel 双向更新能力。
- 保留并回归验证 0.8.1 中 AI 测试成功后启用“保存配置”和快捷入口已有项目时隐藏空状态装饰的修复。

## 兼容性与隐私

- 本版本不改变已有设置、待办、提醒、快捷项和搜索索引的数据格式。
- AI Key 继续只保存于当前 Windows 账户保护的凭据存储；供应商切换和输入框回归测试均不访问真实网络或真实凭据。
- 本次为向后兼容的 UI、绑定和渲染缺陷修复，不新增第三方依赖。

## 验证

- `scripts/validate-project.ps1 -CI`：通过。
- `dotnet test src/AiPet.sln --configuration Release --no-restore`：140 项测试通过，0 失败，0 跳过。
- 新增 WPF 回归覆盖：供应商实际下拉交互与对象绑定、待办中文输入内容保留、内容宿主高度及最终渲染对比度；并保留 AI 保存按钮状态机回归。
- `scripts/test.ps1 -CI` 在受限构建会话中被本机 NuGet.Config 权限阻断，未将该阻断误报为通过；GitHub Actions 发布门禁需在正常 runner 上执行完整入口。

## 发布资产

由 `scripts/package.ps1` 生成并在发布前复核：

- `dist/portable/windows-ai-desktop-pet-v0.8.2-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v0.8.2-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

最终资产大小与 SHA-256 以 `dist/checksums/SHA256SUMS.txt` 和对应 GitHub Release 为准。
