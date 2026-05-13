# Avalonia 12 第六批迁移记录：Launcher + 插件加载链路收口

## 范围

- 仅在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 实施。
- 未回写原 `IIoT.EdgeClient`。
- 未修改 Cloud/MES API、数据库结构、配置 JSON 字段、PLC 读写策略或业务规则文档。
- 未启动真实 PLC、Cloud/MES 上传或完整后台生命周期。

## 主要变更

- 新增 `IIoT.Edge.Launcher.Core`。
  - 从 WPF Launcher 抽出 UI 无关的 `Models`、`Services`、`ViewModels`。
  - 保留原 `IIoT.Edge.Launcher.*` 命名空间，降低 WPF Launcher 和既有测试迁移成本。
  - Core 目标框架为 `net10.0`，不引用 WPF、Avalonia 或 `IIoT.Edge.UI.Shared`。
- 收口 WPF Launcher。
  - WPF 项目只保留窗口壳、XAML、主题和 DI 壳。
  - `LauncherDependencyInjection` 改为调用 `AddLauncherCore(baseDirectory)` 并注册 WPF `MainWindow`。
  - WPF profile 继续默认启动 `IIoT.Edge.Shell.exe`。
- 新增 `IIoT.Edge.Launcher.Avalonia`。
  - 使用 Avalonia 12 + Fluent 主题实现 Launcher 主窗口、登录、改密、profile 卡片、搜索和启动按钮。
  - 复用 `Launcher.Core` 服务。
  - 新增 Avalonia profile，启动目标为 `IIoT.Edge.AvaloniaShell.exe`。
- 收口 Avalonia 插件 catalog。
  - `AvaloniaShell` 不再硬编码 `new Homogenization.Avalonia.DependencyInjection()`。
  - `Host.Bootstrap.Core` 新增 `IEdgeProcessModuleCatalog`、`EdgeProcessModuleCatalogOptions`、`JsonEdgeProcessModuleCatalog`。
  - `Host.Bootstrap.Avalonia` 在未显式传入模块实例时，通过 catalog 读取 `plugin.json`、加载 `*.Avalonia.dll` 并实例化 `IEdgeProcessModule`。
  - 匀浆 Avalonia 插件继续按插件方式注册 `Homogenization.DataView`。
- UI 主题路线调整。
  - 第六批验证发现 `SukiUI 6.1.0` 在 Avalonia 12 下会引入 Avalonia 11 传递包，且 `SukiWindow` 在 Headless 测试中运行时失败。
  - 本批已移除 `SukiUI` / `SukiUI.Dock`，窗口根类型切回 Avalonia 原生 `Window`，主题使用 `Avalonia.Themes.Fluent` 和 `Dock.Avalonia.Themes.Fluent`。

## 边界检查

- `Launcher.Core` 扫描无 `System.Windows`、`UseWPF`、Avalonia、`IIoT.Edge.UI.Shared` 引用。
- `Launcher.Avalonia` 扫描无 `System.Windows`、`UseWPF`、WPF Presentation、`IIoT.Edge.UI.Shared` 引用。
- `AvaloniaShell` 扫描无匀浆插件硬编码实例。
- `SukiUI`、`SukiUI.Dock`、`SukiWindow` 扫描无残留。
- preview/prerelease 依赖仍仅为已批准的 SkiaSharp 系列传递依赖。
- 漏洞扫描无未处理告警。

## 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Core/IIoT.Edge.Launcher.Core.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher/IIoT.Edge.Launcher.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive`
- `dotnet list src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj package --vulnerable --include-transitive`

结果：

- AvaloniaShell.Tests：15 通过。
- Launcher.Tests：24 通过。
- Module.ContractTests：28 通过。
- NonUiRegressionTests：367 通过。
- Shell.Tests：71 通过。
- Shell.Tests 仍有既有 fake event 未使用警告，非本批新增失败。

## 已知说明

- 第六批目标是可从 Avalonia Launcher 进入 Avalonia Shell UI 入口，不做现场硬件联调。
- WPF Launcher 和 WPF Shell 继续保留作为对照和回退。
- SukiUI 暂不进入 Avalonia 12 主线，后续只有在其依赖图和运行时兼容性对齐后才重新评估。
