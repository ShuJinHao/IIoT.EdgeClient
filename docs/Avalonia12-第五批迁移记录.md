# Avalonia 12 第五批迁移记录：匀浆插件拆分 + Avalonia 插件 UI

## 范围

- 仅在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 实施。
- 未回写原 `IIoT.EdgeClient`。
- 未修改 Cloud/MES API、数据库结构、配置 JSON 字段、PLC 读写策略或业务规则文档。
- 未启动真实 PLC、Cloud/MES 上传或后台生命周期。

## 主要变更

- 新增 `IIoT.Edge.Module.Homogenization.Core`。
  - 承载匀浆 UI 无关代码：`Config`、`Integration`、`Payload`、`Runtime`、`Samples` 和 `Resources/HomogenizationText.cs`。
  - `HomogenizationText` 改为 UI 中立 fallback 文本工具，不再访问 `System.Windows.Application.Current`。
  - 新增 `HomogenizationModuleBase`，集中注册运行时、上传器、参数、硬件模板和开发样本。
- 保留 WPF 插件项目 `IIoT.Edge.Module.Homogenization`。
  - 继续作为 WPF 插件入口，`plugin.json` 和 `DependencyInjection` 入口不变。
  - WPF `DependencyInjection` 继承 Core 基类，只注册 WPF `HomogenizationDataViewModel` 和 WPF 页面。
  - 配置文件从 Core 项目以 linked item 输出到 WPF 插件目录。
- 新增 `IIoT.Edge.Module.Homogenization.Avalonia`。
  - 作为 Avalonia 插件入口，引用 `Homogenization.Core` 和 `IIoT.Edge.UI.Avalonia`。
  - 新增 Avalonia 版 `HomogenizationDataPage` / `HomogenizationDataViewModel`。
  - ViewModel 使用 `IAvaloniaTimerFactory`、`IAvaloniaDispatcherService`、`IAvaloniaLanguageService`。
  - 数据页只读展示 `IProductionContextStore` 中的 `HomogenizationContext.OutboundRecords`。
- 调整 Avalonia 插件注册链。
  - `Host.Bootstrap.Avalonia` 新增 `AvaloniaEdgeProcessModuleBuilder`，将模块注册映射到 `IAvaloniaViewRegistry`。
  - `Host.Bootstrap.Core` 新增 UI 中立的 `CellDataRegistry`、`StationRuntimeRegistry`。
  - 宿主 `Navigation.Avalonia` 不再硬编码匀浆数据页，`Homogenization.DataView` 由插件注册。

## 边界检查

- `Homogenization.Core` 扫描无 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、Avalonia UI 引用。
- `Homogenization.Avalonia` 扫描无 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、WPF Presentation 引用。
- Avalonia 依赖图中的 preview/prerelease 包仅为已批准例外：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.* /3.119.4-preview.1.1`

## 验证

- `dotnet build src/Modules/IIoT.Edge.Module.Homogenization.Core/IIoT.Edge.Module.Homogenization.Core.csproj`
- `dotnet build src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj`
- `dotnet build src/Modules/IIoT.Edge.Module.Homogenization.Avalonia/IIoT.Edge.Module.Homogenization.Avalonia.csproj`
- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive`

结果：构建和测试通过；漏洞扫描未发现易受攻击包。

## 已知说明

- 第五批只迁移匀浆数据页，不迁 Launcher，不做现场硬件联调。
- WPF 插件仍保留，直到 Avalonia 主线完成切换。
- 匀浆业务核心已收敛到 Core，后续 WPF/Avalonia 只维护各自 UI 壳。
