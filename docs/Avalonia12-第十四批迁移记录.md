# Avalonia 12 第十四批迁移记录：现场试运行准备 + 回退闭环

## 范围

- 只修改旁路副本 `IIoT.EdgeClient.AvaloniaMigration`。
- 原 `IIoT.EdgeClient`、Cloud、AICopilot 未参与本批改动。
- 本批不新增业务功能，不开放 Cloud/MES 清理、重试、删除，不修改数据库、配置 JSON、Cloud/MES API、PLC 策略或业务规则。
- Avalonia 仍不是生产默认入口；WPF Launcher/WPF Shell 继续作为现场回退线。

## 本批完成内容

- 新增现场试运行手册 `docs/Avalonia12-现场试运行手册.md`，固定流程包含 UI-only 启动、窗口/菜单/Dock/语言检查、显式 `--start-runtime`、只读快照、受控 I/O 缓冲写入申请、PLC 写入轨迹截图、证据包采集和 WPF 回退验证。
- 新增现场试运行验收记录模板 `docs/Avalonia12-现场试运行验收记录模板.md`，把启动方式、截图证据、P0/P1/P2 阻断项、WPF 回退结果和现场签字记录统一收口。
- 新增 `scripts/StartAvaloniaTrialRun.ps1`。脚本默认只启动 UI-only；只有显式 `-StartRuntime` 且目标为 `Shell` 时才追加 `--start-runtime`，并记录启动命令、时间、进程号和日志目录提示。
- 扩展 `scripts/PublishAvaloniaMigration.ps1`，发布包 now 携带试运行手册、验收模板、启动脚本、差异矩阵、阻断清单、现场联调清单、证据包说明和 SkiaSharp preview 例外记录。
- 扩展 `scripts\TestAvaloniaMigrationCandidate.ps1`，新增 `-VerifyWpfFallback`，候选验收时同时构建 WPF Shell 和 WPF Launcher，并把回退验证结果写入 `candidate-validation-summary.json`。
- 扩展 `scripts\CollectAvaloniaFieldEvidence.ps1`，证据包同步携带试运行手册和验收模板。
- Diagnostics 的“现场联调摘要”新增“试运行资料”只读提示，只指向发布包资料位置，不增加任何运维操作按钮。
- 补充脚本和发布布局测试，覆盖试运行手册、验收模板、启动脚本、WPF 回退验证字段和脚本安全边界。

## 验证结果

- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：52
- `powershell -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback`
  - 通过：发布包、证据采集预检、临时证据包、依赖列表、漏洞扫描、WPF Shell 回退构建、WPF Launcher 回退构建
  - 输出：`publish\avalonia-migration\Release\candidate-validation-summary.json`
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：25
- `dotnet test src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：28
- `dotnet test src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：373
- `dotnet test src\Tests\IIoT.Edge.Shell.Tests\IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：71

## 边界检查

- Avalonia Shell、Avalonia Bootstrap、Avalonia Presentation、Avalonia UI Shared、匀浆 Avalonia 插件扫描未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI`。
- Avalonia IOView 扫描未发现直接 `ReadDataAsync` / `WriteDataAsync`。
- 证据采集、候选验收和试运行启动脚本扫描未发现数据库删除、Cloud/MES 清理、重试、删除或 PLC 直接写入命令。
- 依赖图中 preview/prerelease 仍只允许已批准的 SkiaSharp 系列；漏洞扫描未报告未处理漏洞包。

## 注意事项

- 曾并行运行 WPF 测试时出现一次 XAML MarkupCompile cache 文件锁，单进程重跑 `Launcher.Tests` 后通过；后续 WPF 构建/测试仍建议使用 `-m:1` 串行执行。
- PowerShell 5.1 对无 BOM UTF-8 脚本中的中文字符串解析不稳定。本批将会被 `powershell.exe` 直接执行的迁移脚本保存为带 BOM 的 UTF-8，避免中文字符串在现场机器上被错误解码。
- 第十四批仍不做现场真实 PLC 成功率调参；现场试运行由人工按手册执行，证据包负责回收日志、摘要、截图占位和验收记录。

## 下一步建议

- 进入第十五批前先让 Claude 审核本批试运行包、回退链路和脚本边界。
- 第十五批建议聚焦“现场试运行问题回收 + P0/P1 阻断清零 + 是否切默认入口决策”，不要继续盲目扩功能。
