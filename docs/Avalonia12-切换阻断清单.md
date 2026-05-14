# Avalonia 12 切换阻断清单

## P0：阻断生产切换

| 阻断项 | 验收口径 |
| --- | --- |
| Avalonia 发布包验收脚本失败 | `scripts/TestAvaloniaMigrationCandidate.ps1` 必须通过 |
| 现有 Edge 回归失败 | `AvaloniaShell.Tests`、`Launcher.Tests`、`Module.ContractTests`、`NonUiRegressionTests`、`Shell.Tests` 必须通过 |
| 非 SkiaSharp preview/prerelease 依赖进入依赖图 | 只允许已记录的 SkiaSharp preview 传递依赖例外 |
| 漏洞扫描出现未处理告警 | `dotnet list ... package --vulnerable --include-transitive` 不得有未处理漏洞 |
| Avalonia 项目重新引入 WPF 依赖 | 不得出现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、SukiUI |
| I/O 页面直接调用 PLC 读写接口 | 不得出现直接 `ReadDataAsync` / `WriteDataAsync` 调用 |
| Diagnostics 或脚本开放 Cloud/MES 清理、重试、删除 | 只能只读展示，不能提供操作入口 |
| 现场证据包缺失 | 必须包含 Launcher profile、Diagnostics 摘要、日志、截图占位说明和联调清单 |
| 未通过默认入口评审门禁 | `TestAvaloniaDefaultEntryReadiness.ps1` 必须输出 `ApprovedForDefaultEntrySwitch`，且真实切换必须另起独立批次 |

## P1：阻断现场试运行

| 阻断项 | 验收口径 |
| --- | --- |
| UI-only 启动失败 | 必须能打开 Avalonia Shell，Header/Footer 显示 UI-only 状态 |
| `--start-runtime` 启动失败且无诊断详情 | 启动失败必须展示错误详情和日志路径 |
| 运行时快照无法读取 | IOView 必须区分未启动、无快照、设备未绑定、PLC 未连接 |
| I/O 写入申请无轨迹 | Diagnostics 必须能看到 I/O 写入申请和 PLC 块写入轨迹 |
| 现场多屏、高 DPI、触摸未确认 | 现场试运行前必须人工确认窗口拖拽、缩放、触摸点击和 DataGrid 滚动 |
| Launcher profile 指向错误 | UI-only 和 `--start-runtime` 两个 profile 必须指向 AvaloniaShell 发布输出 |

## P2：可延期但必须记录

| 延期项 | 记录要求 |
| --- | --- |
| Cloud/MES 人工运维操作 | 继续保持只读，后续单独规划权限、审计和补偿边界 |
| Excel 模板化导出 | 当前 CSV 可用，模板化导出另起批次 |
| WPF 项目清理 | 现场试运行通过后再决定移除 WPF Shell、WPF Launcher 和 WPF UI 包 |
| 视觉细节打磨 | 不阻断试运行，记录具体页面和截图 |
| Linux/macOS 兼容 | 首轮验收只面向 Windows 工控现场 |

## 当前判定

截至第十七批开始，候选包 FullGate 已通过自动化验证；P1 中的多屏、高 DPI、触摸、真实 `--start-runtime` 和 PLC 侧状态仍需现场证据关闭。没有完整现场证据、人工签字和默认入口评审门禁通过前，不允许把 Avalonia Launcher 或 Avalonia Shell 切为生产默认入口。
