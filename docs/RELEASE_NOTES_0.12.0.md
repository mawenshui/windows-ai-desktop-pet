> 历史记录：正文保留当时版本、验证结果和提交状态。当前 0.13.0 范围与验收见 [CURRENT_STATUS.md](CURRENT_STATUS.md) 和 [本次报告](release/0.13.0-test-report.md)。

# Windows AI Desktop Pet 0.12.0 发布说明

## 重点更新

- 修复主页、待办与设置页全部下拉框选择后仍显示默认值的问题：移除会拦截真实输入的自绘覆盖模板，恢复 WPF 原生鼠标、键盘、焦点与可访问性语义。
- AI 配置新增模板化创建：先选择 DeepSeek、智谱、Qwen、Moonshot 等模板，再复制名称、端点和模型到可编辑表单；API Key 始终留空。
- 新增独立桌面 UI runner、schema v2 真实设备矩阵以及 20,000 条匿名元数据、200 次查询/唤出的性能预算 runner。
- 完整代码签名链覆盖应用、安装器和 Inno Setup 生成的卸载器，并在发布 smoke 中即时验签。

## 验证边界

- 自动化逻辑与 WPF 交互测试：172 项，0 失败、0 跳过。
- 当前桌面会话的跨进程 UI runner 仍需在稳定的隔离交互式 Windows runner 复验；失败证据只保留阶段码和截图。
- 物理多屏、不同缩放、Explorer 重启、显示器热插拔、休眠和时区变化按实际环境报告 PASS/FAIL/SKIP。
- 当前未提供可信代码签名证书时，产物保持 unsigned，不声称已经建立 SmartScreen 信誉。

## 发布资产

- `windows-ai-desktop-pet-v0.12.0-portable.zip`
- `windows-ai-desktop-pet-v0.12.0-setup.exe`
- `SHA256SUMS.txt`

资产哈希在正式打包后以 `dist/checksums/SHA256SUMS.txt` 为准。

- 便携版：`7e91783bb11ba11876963709573e4d9f801063143cc9cbab88efd1ada4a41d86`
- 安装版：`d3bd51cc0f634b3319c5946ce5a820d9e8260b00a571b617e2505ccdd0c8e206`
