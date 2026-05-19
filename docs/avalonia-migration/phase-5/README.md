# Phase 5 模块 Presentation 原地 Avalonia 化记录

## 范围

本轮处理宿主动态路由和 `IIoT.Edge.Module.Homogenization` 的自定义数据页：

- `NavigationRailViewModel` 从 `IViewRegistry.GetAllMenus()` 追加模块菜单。
- `NavigationHostView` 在固定宿主页未命中时通过 `IViewRegistry.GetViewRegistration(...)` 创建模块页面。
- `HomogenizationDataPage` 从 WPF `.xaml(.cs)` 替换为 Avalonia `.axaml(.cs)`。
- Homogenization 双语资源新增 Avalonia `.axaml` 版本，旧 `.xaml` 资源保留到统一清理 Phase。
- `HomogenizationText` 改为 Avalonia resource lookup，方法签名不变。
- 模块项目仅补齐 `AvaloniaUseCompiledBindingsByDefault=false`，`UseWPF=true` 保留。

## 业务边界

- 未修改 `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Edge/IIoT.Edge.Host.Bootstrap`。
- 未修改 Homogenization 的 `Config`、`Runtime`、`Integration`、`Payload`、`Samples`、`DependencyInjection.cs`、`plugin.json`、`HomogenizationNavigationRegistration.cs`。
- 未新增 `.Avalonia`、`.Legacy` 或并行 UI 项目。
- 未新增 NuGet 包或新的 `IAvaloniaXxx` / dispatcher / dialog 抽象接口。
- 未构造假数据；Homogenization 数据页继续读取真实 `IProductionContextStore`。

## 验证命令

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet restore src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj
dotnet build src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

结果：Shell 与 Homogenization 模块均 Build succeeded，0 warning，0 error。

```powershell
rg "FindByViewId|GetMenus\(" src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell
rg "System\.Windows\.Application|System\.Windows\.Threading|System\.Windows\.Controls|System\.Windows\.Data|MessageBox|MaterialDesign|PackIcon|PageActionShell|uiLoc:DataGridColumnLocalization|TryFindResource" src/Modules/IIoT.Edge.Module.Homogenization/Presentation src/Modules/IIoT.Edge.Module.Homogenization/Resources/HomogenizationText.cs
rg "CommunityToolkit\.Mvvm|ReactiveUI|Prism\.|Dock\.Avalonia|Material\.Avalonia|Material\.Icons\.Avalonia|DialogHost\.Avalonia|LucideAvalonia|Avalonia\.Headless\.XUnit|xunit\.v3" Directory.Packages.props src/Modules/IIoT.Edge.Module.Homogenization
```

结果：无输出。

```powershell
git diff --name-only -- src/Core src/Application src/Runtime src/Infrastructure src/Edge/IIoT.Edge.Host.Bootstrap src/Modules/IIoT.Edge.Module.Homogenization/Config src/Modules/IIoT.Edge.Module.Homogenization/Runtime src/Modules/IIoT.Edge.Module.Homogenization/Integration src/Modules/IIoT.Edge.Module.Homogenization/Payload
```

结果：无输出。

## 文件检查

```powershell
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.xaml
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.xaml.cs
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml.cs
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/zh-CN.axaml
Test-Path src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/en-US.axaml
```

结果：旧 WPF 页面为 `False`，新 Avalonia 页面和资源为 `True`。

## 剩余风险

- 本轮已完成构建与静态检查；未在真实窗口中执行三档截图验收。
- 当前仍保留模块项目 `<UseWPF>true</UseWPF>` 和旧 `.xaml` 资源，按计划进入后续统一 WPF 清理 Phase。
