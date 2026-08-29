# Windows AI Desktop Pet 0.8.1 发布说明

- 发布日期：2026-08-29
- 版本类型：Patch（向后兼容缺陷修复）
- 状态：发布候选

## 本次修复

- 修复 AI 配置供应商下拉框选中项显示内部对象文本、选择结果不直观的问题；现在显示供应商名称，并同步所选预设的默认端点和模型。
- 修复 AI 连接测试成功后“保存配置”按钮仍不可用的问题；连接正常后会及时刷新命令状态，保存后或再次修改字段时仍按“测试后保存”规则禁用。
- 修复快捷入口已有项目时空状态背景和“文件夹＋”装饰图标仍保留的问题；存在快捷项时只展示实际入口。
- 修复统一测试脚本递归选择 `build/` 历史解决方案的问题，当前 WPF 测试入口固定为 `src/AiPet.sln`。
- 安装器适配器在未配置 PATH 时自动探测标准 Inno Setup 6 安装路径，同时保留 `AIPET_INNO` 和 PATH 的优先级。

## 兼容性与隐私

- 本版本不改变已有配置、待办、提醒、快捷项和搜索索引的数据格式。
- AI Key 继续只保存于当前 Windows 账户保护的凭据存储；连接测试不携带搜索词、路径、快捷项或待办数据。
- 本次为向后兼容的 UI、命令状态和工程门禁修复，不新增第三方依赖。

## 验证与发布资产

- `scripts/validate-project.ps1 -CI`：通过。
- `scripts/test.ps1 -CI`：通过；固定运行 `src/AiPet.sln`，138 项测试通过，0 失败，0 跳过。
- `scripts/package.ps1`：通过标准 Inno Setup 6 自动探测生成 0.8.1 便携版、安装版和 SHA-256 清单；两项资产哈希均已复核。
- 最终便携 ZIP 解压后执行 `WindowsAiDesktopPet.exe --smoke`：通过；资源、递归搜索、快捷项、9 个 AI 预设、凭据往返、自启读取和帮助文档检查均通过。
- 安装包静默安装、安装目录可执行文件和卸载程序检查：通过；安装后 `--smoke` 通过，卸载进程退出码为 0。测试使用预创建的临时安装目录，卸载后保留空目录属于测试夹具行为。
- 多屏/缩放、通知和普通交互窗口路径仍需在受支持的交互式 Windows 会话完成；受限构建会话中的未运行项目不表述为通过。

发布资产：

- `dist/portable/windows-ai-desktop-pet-v0.8.1-portable.zip`
- `dist/installer/windows-ai-desktop-pet-v0.8.1-setup.exe`
- `dist/checksums/SHA256SUMS.txt`

最终文件与 SHA-256：

| 资产 | 大小（字节） | SHA-256 |
| :--- | ---: | :--- |
| `windows-ai-desktop-pet-v0.8.1-portable.zip` | 76,479,145 | `9ed705bed397cb0014682d4a4ad899afa5d2a7ec1408e018cd8c73f76f9ff5a8` |
| `windows-ai-desktop-pet-v0.8.1-setup.exe` | 53,798,401 | `daed911b14a1b6d281b278ca675ee7390bdb000caa221561e70c3186159767c0` |
