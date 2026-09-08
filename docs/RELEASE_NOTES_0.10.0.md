> 历史记录：正文保留当时版本、验证结果和提交状态。当前 0.15.0 范围与验收见 [CURRENT_STATUS.md](CURRENT_STATUS.md) 和 [本次报告](release/0.15.0-test-report.md)。

# Windows AI Desktop Pet 0.10.0 发布说明

- 发布日期：2026-09-03
- 版本类型：Minor（多 AI 配置持久化与全局下拉选择修复）
- 状态：正式发布（私有 GitHub 仓库，二进制未签名）
- Release：`https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v0.10.0`

## 新增能力

- AI 配置支持填写“配置名称”，保存多组 OpenAI 兼容配置，并在“已保存配置”下拉框中选择当前使用项。
- 切换已保存配置会加载对应供应商、服务地址、模型和 Windows 凭据；待办 AI 始终读取当前 `ActiveProfileId`。
- 旧版本只保存一组 AI 配置时，读取会自动迁移为稳定的 `legacy` 配置，不要求重新录入字段。
- 首次主页显示预选的桌面/文档/下载授权卡；确认前不枚举目录，可取消候选或稍后设置。
- 搜索范围显示等待、准备中、可用、失败、已取消和路径失效，支持取消、重试、重新索引与移除；失败或取消保留上一份成功索引。
- AI 配置支持带名称二次确认的安全删除；切换、切页或退出时保护未保存修改。
- 托盘菜单打开时同步桌宠、工具窗口与自启真实状态，并监听 Explorer 的 `TaskbarCreated`；退出统一取消后台任务并限制等待 5 秒。

## 修复

- 统一修复所有下拉框的选择链路：搜索范围、结果类别、AI 操作目标、待办状态、AI 配置和供应商均以实际选中对象更新显示与业务值。
- 移除供应商 `SelectedItem` 与 `SelectedValue` 双重绑定竞争，内置供应商切换后端点和模型立即对应更新。
- 修复已保存配置重新测试连接后“保存配置”仍不可点击的问题；测试成功会产生可持久化的验证更新。
- 共享下拉框模板改用原生选中项模板，避免对象文本、旧选项和显示值脱节。

## 兼容性与隐私

- `settings.json` schema 升级为 2；旧单配置按向后兼容规则迁移，非敏感配置包含配置名称、连接字段、状态、验证时间和凭据引用，不保存 Key。
- 新建配置的凭据 target 使用 `WindowsAiDesktopPet:AI:profile:<ProfileId>`；迁移配置保留原凭据 target，避免破坏已有凭据。
- 本版本未新增第三方依赖；AI Key 继续只保存于当前 Windows 账户保护的凭据存储。

## 验证

- `scripts/test.ps1 -CI`：项目校验通过，当前 `src/AiPet.sln` 的 156 项测试通过，0 失败，0 跳过。
- 新增回归覆盖：首次授权确认前零扫描、索引取消/失败保留旧数据、稳定错误码、AI 删除凭据/状态/内存 Key、重名拒绝和未分类异常标准化。
- 已生成 `dist/portable/windows-ai-desktop-pet-v0.10.0-portable.zip`（约 73 MiB）、`dist/installer/windows-ai-desktop-pet-v0.10.0-setup.exe`（约 51 MiB）及 `dist/checksums/SHA256SUMS.txt`，并验证压缩包入口、资源路径和 SHA-256。
- `scripts/package.ps1` 在唯一 `build/0.10.0/package-<run-id>` 中重建候选后更新发布资产；便携版与安装后 `--smoke` 均通过资源、搜索、快捷方式、AI 供应商、Windows 凭据往返、自启读取和离线手册检查，隔离安装与卸载通过。
- Explorer 实际重启、系统托盘提醒、桌宠真实漫游/跟随、系统通知抑制、休眠恢复、多显示器与 100%～200% 缩放仍需在普通交互式 Windows 会话完成人工验证。

## 已知限制

- 安装程序和应用可执行文件尚未接入可信代码签名，Windows 可能显示 SmartScreen 提示。
- Explorer 实际重启、系统通知抑制、休眠恢复、多显示器和缩放矩阵不记为已通过；当前后续工作见 `docs/0.15.0－功能扩展计划.md`。
- 本次 Release 不改变搜索只读取文件名元数据、AI 写入必须确认及 Key 只进入 Windows Credential Manager 的隐私边界。

## 发布产物

- 便携版：`dist/portable/windows-ai-desktop-pet-v0.10.0-portable.zip`
  - SHA-256：`f93bbced7cc2afadb4ffdab3ff1a8537e0cc441c986e6b928b25f4d9abf174b9`
- 安装版：`dist/installer/windows-ai-desktop-pet-v0.10.0-setup.exe`
  - SHA-256：`b69acf9e9bc10c08672258dcf6114a7410ab023a79f826fd47e0b1ad9deb2f7b`
- 校验和：`dist/checksums/SHA256SUMS.txt`
