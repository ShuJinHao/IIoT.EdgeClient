# Avalonia 12 第十三批迁移记录

## Summary

- 本批继续只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本实施。
- 第十二批发布包和现场证据包能力已先提交为 `ae99401`。
- 本批新增切换前差异矩阵、P0/P1/P2 阻断清单和候选包验收脚本，把 Avalonia 迁移从“功能继续扩展”收束到“切换前审计与验收闭环”。
- 未修改 Cloud/MES API、数据库结构、配置 JSON、PLC 块规划策略、补偿规则或业务规则文档。

## Implementation

- 新增 `Avalonia12-切换前差异矩阵.md`，覆盖 Launcher、Shell、Header/Footer、菜单、Dock、登录、Monitor、DataView、Capacity、PlcTaskBinding、Diagnostics、HardwareConfig、IOView、Recipe、Param、匀浆插件 UI。
- 新增 `Avalonia12-切换阻断清单.md`，按 P0/P1/P2 固化生产切换、现场试运行和可延期事项。
- 新增 `scripts/TestAvaloniaMigrationCandidate.ps1`，串联 Avalonia 发布、现场证据采集 preflight、临时证据包生成、依赖 preview 检查、漏洞扫描、WPF/SukiUI 边界扫描和 I/O 直接 PLC 读写扫描。
- 更新 Avalonia 发布脚本和证据采集脚本，发布包和证据包同步携带差异矩阵与阻断清单。
- Diagnostics 现场摘要增加“运行目录证据”和“Cloud/MES 差异”两条只读信息，只展示状态和证据路径，不增加清理、重试、删除、补偿或强制上传入口。

## Verification

- `powershell -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release`
  - 结果：通过。
  - 生成发布目录：`publish\avalonia-migration\Release`。
  - 生成候选验收摘要：`publish\avalonia-migration\Release\candidate-validation-summary.json`。
  - preview/prerelease 依赖仍只允许已批准的 SkiaSharp 系列。
  - 漏洞扫描未发现易受攻击包。

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 warning，0 error。

- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 warning，0 error。

- `dotnet build src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 warning，0 error。

- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，51 个测试。

- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，25 个测试。

- `dotnet test src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，28 个测试。

- `dotnet test src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，373 个测试。

- `dotnet test src\Tests\IIoT.Edge.Shell.Tests\IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，71 个测试；保留既有测试 fake 事件未使用警告。

- 边界扫描：
  - Avalonia 项目未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、SukiUI。
  - Avalonia IOView 未发现直接 `ReadDataAsync` / `WriteDataAsync` 调用。
  - 证据采集脚本和候选验收脚本未发现数据库删除、Cloud/MES 清理、重试、删除或死信操作命令。

## Remaining Risk

- Avalonia 仍不是生产默认入口；没有完成 P0/P1 验收前，不允许替换 WPF Launcher/WPF Shell。
- 现场多屏、高 DPI、触摸、真实 `--start-runtime` 和 PLC 侧状态仍需人工联调证据。
- Cloud/MES 当前只读展示，不开放人工清理、重试、删除或补偿入口。
- WPF 项目清理、Excel 模板化导出和视觉细节打磨留到后续批次。

## Next Entry Criteria

- Claude 审核本批差异矩阵、阻断清单和候选验收脚本。
- 现场按 `Avalonia12-现场联调检查清单.md` 完成 UI-only、`--start-runtime`、手动读取、写入申请、PLC 写入轨迹截图和证据包采集。
- 若 P0 为零且 P1 可控，再进入“试运行切换准备”批次。

