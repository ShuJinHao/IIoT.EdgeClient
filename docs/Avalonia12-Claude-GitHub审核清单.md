# Avalonia 12 Claude/GitHub 审核清单

## 分支与范围

- 确认长期迁移分支为 `codex/avalonia-default-entry-review`，审核快照分支为 `codex/avalonia-default-entry-review-pr`。
- 确认 PR #44 仍为 Draft，且没有把 Avalonia 迁移代码回写原 `IIoT.EdgeClient` WPF 主线。
- 确认本批只处理审核闭环、同步规范、脚本和文档，不扩展业务功能。

## 架构边界

- Avalonia 项目不得引用 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、WPF Presentation 项目或 `SukiUI`。
- AvaloniaShell、Launcher.Avalonia、Homogenization.Avalonia 只能使用 Avalonia UI 契约和无 WPF 依赖的底层能力。
- 匀浆插件继续保持 Core + WPF 壳 + Avalonia 壳分离，模块 key、菜单 key、ViewId、ProcessType 不变。

## 安全边界

- IOView 不得直接调用 PLC `ReadDataAsync` / `WriteDataAsync`，写入只允许进入运行时缓冲并保留证据链。
- Cloud/MES 仍只读展示，不增加清理、重试、删除、补偿或强制上传按钮。
- 默认入口真实切换必须经过 `ReadyForDefaultEntryReview`、FullGate、WPF 回退验证、P0/P1 清零、人工签字和 `ApprovedForDefaultEntrySwitch`。
- `SwitchAvaloniaDefaultEntry.ps1 -Apply` 只能改发布包内 profile，并必须生成 rollback snapshot。

## 发布与证据

- FullGate 必须通过：发布、证据预检、依赖/漏洞扫描、WPF 回退构建和全部回归测试。
- 现场证据不足时必须保持 `P1Pending`，不得把模板或占位截图当作已验收。
- 发布包必须包含现场手册、验收模板、问题回收清单、决策包模板、SkiaSharp preview 例外记录和回退说明。
- 证据导入、复审、决策包和默认入口 readiness 脚本必须保持只读，除发布包 profile 受控切换外不得改业务数据。

## 文本与依赖

- 新增/修改文本必须是 UTF-8，中文文案不得出现常见乱码片段。
- Avalonia 依赖图中的 preview/prerelease 只允许已文档化的 SkiaSharp 系列。
- 任何 `NU190x` 或漏洞告警不得无记录放行。
