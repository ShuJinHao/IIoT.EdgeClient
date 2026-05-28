# Phase 4d：Diagnostics 原地 Avalonia 化

## 范围

本轮只迁移 `IIoT.Edge.Presentation.Navigation` 内的 Diagnostics 页面，并把 Diagnostics 接入 P4c 已建立的 `NavigationHostView` 中央内容切换机制。

已接入的中央页面：

- `CoreViewIds.Dashboard` -> `DashboardView`
- `Formula.RecipeView` -> `RecipeViewPage`
- `Config.ParamView` -> `ParamViewPage`
- `CoreViewIds.Diagnostics` -> `DiagnosticsPage`

P4d 不迁移 `Monitor`、`DataView`、`CapacityView`、`IOView`、`HardwareConfig`、`PlcTaskBinding`，也不迁移 Launcher 或模块 Presentation。

## 实施记录

- `NavigationRailViewModel` 增加第 10 项 Diagnostics，`ViewId` 使用现有 `CoreViewIds.Diagnostics`，默认可点击。
- `NavigationHostView` 增加 `CoreViewIds.Diagnostics` 分支，使用 DI 解析 `DiagnosticsPage`。
- `DiagnosticsPage.xaml(.cs)` 已删除。
- 新增 Avalonia 页面 `DiagnosticsPage.axaml(.cs)`，保留原命名空间 `IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView`。
- `DiagnosticsViewModel` 的 `System.Windows.Threading.DispatcherTimer` 改为 `Avalonia.Threading.DispatcherTimer`。
- `DiagnosticsViewModel` 的 WPF dispatcher 调用改为 `Avalonia.Threading.Dispatcher.UIThread`。
- `IDiagnosticsDeadLetterConfirmationService` 保留为 Navigation 内部确认服务，方法改为 async，旧 WPF `MessageBox.Show()` 改为页面局部 Avalonia 确认窗。
- 新增 `DiagnosticsConfirmationDialog.axaml(.cs)`；没有主窗口时返回 `false`，不执行死信操作。
- `Resources/Languages/zh-CN.axaml` 与 `en-US.axaml` 补充 Diagnostics、同步诊断、死信操作和表格列资源 key。

## 红线保持

- 未新建 `*.Avalonia`、`.Legacy` 或并行 UI 项目。
- 未新增 `IAvaloniaXxx`、`IDispatcherService`、`IDialogService`、`ITimerFactory`。
- 未新增 NuGet 包。
- 未改 `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap`。
- 未构造 fake 模块、fake 死信、fake MES 状态、fake Cloud 状态或 fake 运行状态。
- `System.Windows.Input.ICommand` 按计划保留，不作为 WPF 残留处理。

## 本地验证结果

- `dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj` 通过。
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false` 通过，0 warning / 0 error。
- Diagnostics 范围内 `System.Windows.Application`、`System.Windows.Threading`、`System.Windows.Controls`、`using System.Windows;`、`MessageBox`、`MaterialDesign`、`PackIcon` 扫描无命中。
- `DiagnosticsPage.xaml` 已不存在，`DiagnosticsPage.axaml` 已存在。
- `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap` 冻结路径 diff 为空。
- `IIoT.Edge.Presentation.Navigation.Avalonia`、`IIoT.Edge.UI.Avalonia`、`AddEdgeHostAvaloniaBootstrap`、`AvaloniaShellStartupCoordinator`、`AvaloniaShellBootstrapOptionsFactory`、`IAvaloniaXxx` 扫描无命中。

## 剩余风险

- P4d 只打开 Diagnostics；Data、Capacity、Monitor、IO、Hardware、PlcTaskBinding 仍按 P4 计划留给后续 sub-phase。
- `DiagnosticsViewModel` 仍超过 500 行，这是既有治理警告；本轮只做必要 Avalonia 迁移，不拆业务逻辑。
- `IIoT.Edge.Presentation.Navigation` 项目仍保留 WPF 编译开关和未迁移页面残留，全局清理放到后续清理 Phase。
- 三档截图需要在 Shell 可运行环境下复核；本轮已完成构建与静态验收。
