# Phase 4c：Recipe + Param 原地 Avalonia 化

## 范围

本轮只迁移 `IIoT.Edge.Presentation.Navigation` 内的 `RecipeView` 与 `ParamView`，并建立 `NavigationHostView` 根据 `NavigationRailViewModel.SelectedItem` 切换中央内容的基础机制。

已接入的中央页面：

- `CoreViewIds.Dashboard` -> `DashboardView`
- `Formula.RecipeView` -> `RecipeViewPage`
- `Config.ParamView` -> `ParamViewPage`

P4c 不迁移 `Monitor`、`DataView`、`CapacityView`、`IOView`、`HardwareConfig`、`PlcTaskBinding`、`Diagnostics`，也不迁移 Launcher 或模块 Presentation。

## 实施记录

- `NavigationRailViewModel` 增加 `SelectedItem`，Recipe / Param 导航项改为可点击。
- `NavigationHostView` 从固定 Dashboard 改为监听导航选择并解析 Dashboard / Recipe / Param。
- `RecipeViewPage.xaml(.cs)` 与 `ParamViewPage.xaml(.cs)` 已删除。
- 新增同名 Avalonia 页面 `RecipeViewPage.axaml(.cs)` 与 `ParamViewPage.axaml(.cs)`。
- `ParamViewModel` 的 WPF Dispatcher 调用改为 `Avalonia.Threading.Dispatcher.UIThread`。
- `RecipeViewModel` 保持真实 `IRecipeService` / `IRecipeViewCrudService` 数据来源、本地管理员编辑约束与命令语义。
- `Resources/Languages/zh-CN.axaml` 与 `en-US.axaml` 补充 Recipe / Param 页面用到的双语 key。

## 红线保持

- 未新建 `*.Avalonia`、`.Legacy` 或并行 UI 项目。
- 未新增 `IDispatcherService`、`IDialogService`、`ITimerFactory`、`IAvaloniaXxx`。
- 未新增 NuGet 包。
- 未改 `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap`。
- 未构造 fake 配方、fake 参数、fake 设备、fake 日志或 fake 运行状态。
- `System.Windows.Input.ICommand` 按计划保留，不作为 WPF 残留处理。

## 验证命令

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

```powershell
rg "System\.Windows\.Application|System\.Windows\.Threading|System\.Windows\.Controls|MaterialDesign|PackIcon" src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Config/ParamView src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Formula/RecipeView
```

```powershell
Test-Path src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Config/ParamView/Views/ParamViewPage.xaml
Test-Path src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Formula/RecipeView/Views/RecipeViewPage.xaml
Test-Path src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Config/ParamView/Views/ParamViewPage.axaml
Test-Path src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Formula/RecipeView/Views/RecipeViewPage.axaml
```

```powershell
git diff -- src/Core src/Application src/Runtime src/Infrastructure src/Modules src/Edge/IIoT.Edge.Host.Bootstrap
```

## 剩余风险

- 本轮只打开 Recipe / Param 两个导航项，其余业务页仍保持禁用，后续 P4a/P4b/P4d 逐项接入。
- `IIoT.Edge.Presentation.Navigation` 项目仍保留 WPF 编译开关和未迁移页面残留，全局清理放到后续清理 Phase。
- 三档截图需要在 Shell 可运行环境下复核，本文件只记录 P4c 代码范围和验证命令。

## 本地验证结果

- `dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj` 通过。
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false` 通过，0 warning / 0 error。
- Recipe / Param 迁移范围内 `System.Windows.Application`、`System.Windows.Threading`、`System.Windows.Controls`、`MaterialDesign`、`PackIcon` 扫描无命中。
- `ParamViewPage.xaml` 与 `RecipeViewPage.xaml` 已不存在，`ParamViewPage.axaml` 与 `RecipeViewPage.axaml` 已存在。
- `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap` 冻结路径 diff 为空。
- `IIoT.Edge.Presentation.Navigation.Avalonia`、`IIoT.Edge.UI.Avalonia`、`AddEdgeHostAvaloniaBootstrap`、`AvaloniaShellStartupCoordinator`、`AvaloniaShellBootstrapOptionsFactory`、`IAvaloniaXxx` 扫描无命中。
