# Avalonia 12 第七批迁移记录：资源纠偏 + 生产/诊断真实页收口

## 范围

- 仅在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 实施。
- 未回写原 `IIoT.EdgeClient`。
- 未修改 Cloud/MES API、数据库结构、配置 JSON 字段、PLC 读写策略或业务规则文档。
- 未启动真实 PLC、Cloud/MES 上传或完整后台生命周期。
- 继续保持 Avalonia 12 + Fluent + Dock.Fluent 路线，SukiUI 不进入主线。

## 主要变更

- 修正 Avalonia Navigation 资源。
  - 修正 `NavigationAvaloniaResources.cs` 中 I/O 菜单、标题、按钮、列头和提示文本。
  - 将匀浆 Avalonia 资源纳入乱码扫描范围。
  - 新增 `ResourceEncodingHygieneTests`，扫描 Avalonia 资源贡献者、Launcher profile/sample JSON 和迁移记录文档中的常见乱码片段。
- 收口生产类页面。
  - `MonitorViewModel` 改为通过 `IMonitorViewService` 读取设备快照，通过 `IAvaloniaTimerFactory` 定时刷新，激活启动 timer，停用停止 timer。
  - `DataViewModel` 改为通过 `IDataViewService` 查询日期范围、今日汇总和生产记录；导出按钮仅提示“本批不写出导出文件”。
  - `CapacityViewModel` 改为通过 `ICapacityViewService` 读取设备列表、在线状态、今日产能、历史产能和图表条；离线提示走 `IAvaloniaDialogService`。
  - `PlcTaskBindingViewModel` 改为通过 `IPlcTaskBindingService` 和 `IClientPermissionService` 加载/保存任务绑定；禁用心跳类任务前走 Avalonia 确认弹窗。
  - `DiagnosticsViewModel` 改为只读诊断页，读取运行时注册、模块注册、持久化诊断、插件状态和启动问题；不执行清理、重试或修复动作。
- 测试覆盖增强。
  - 新增 `ProductionViewModelBehaviorTests`，用 fake service 覆盖 Monitor、DataView、Capacity 的服务驱动行为、timer、离线 dialog 和导出待迁移状态。
  - 扩展 AvaloniaShell 测试数量至 23 个。
  - 诊断页对可选诊断服务做延迟解析，测试容器未注册完整诊断链时仍能创建 Shell 布局。

## 边界检查

- AvaloniaShell、Avalonia Bootstrap、Avalonia Presentation、Avalonia UI Shared、Homogenization.Avalonia、Launcher.Avalonia 扫描无 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared` 或 WPF Presentation 引用。
- `SukiUI` / `Suki` 扫描无残留。
- Avalonia 资源、匀浆 Avalonia 资源、Launcher profile/sample JSON 和迁移记录文档扫描未命中常见乱码片段。
- preview/prerelease 依赖仍仅为已批准的 SkiaSharp 系列传递依赖。
- AvaloniaShell 与 Launcher.Avalonia 漏洞扫描无未处理告警。

## 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive`
- `dotnet list src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj package --vulnerable --include-transitive`

结果：

- AvaloniaShell.Tests：23 通过。
- Launcher.Tests：24 通过。
- Module.ContractTests：28 通过。
- NonUiRegressionTests：367 通过。
- Shell.Tests：71 通过。
- Shell.Tests 仍有既有 fake event 未使用警告，非本批新增失败。
- 依赖图中的 preview 包仅为 `SkiaSharp 3.119.4-preview.1.1` 及其 NativeAssets 系列，符合已批准例外。

## 已知说明

- 第七批把核心导航面从样例骨架推进为真实服务驱动页，但仍使用 fake service 做单元验证，不做现场硬件联调。
- DataView 导出、Diagnostics 清理/重试、真实 PLC 写入和真实硬件保存仍留给后续“运行联调/操作闭环”批次。
- 诊断页在测试容器或迁移期容器中允许缺少完整诊断服务，此时显示“未接入”状态，不阻断 Shell 创建。
