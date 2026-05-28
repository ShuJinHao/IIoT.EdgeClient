# Phase 4 Navigation Inventory

## Summary

本文件是 Phase 4 的前置事实基线，只记录 `IIoT.Edge.Presentation.Navigation` 当前实现状态，不包含代码迁移决策的实现结果。

- 仓库分支：`codex/edgeclient-local-homogenization-sync`
- 当前 HEAD：`1aac64d77641fb34c6335186c16a40fc61bd539e`
- 当前工作树在生成本文件前已存在 P1-P3 未提交改动，包含 `Directory.Packages.props`、Shell、Panels、Navigation、Shared UI 与 baseline 文档改动；因此后续 P4 执行应先确认稳定的 `<p3-merge-commit>`。
- EdgeClient 业务核心目录为 `src/Core`。错误路径警示：`src/Domain` 在本仓库不存在，冻结路径必须使用 `src/Core`。
- 本轮未修改 `src/**`、项目文件或依赖，只新增本 inventory 文档。

## Current Shell Navigation Facts

### NavRail

当前 NavRail 位于 `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/ViewModels/NavigationRailViewModel.cs`。实测状态如下：

| Index | ViewId | Title key | Current state | Phase |
|---:|---|---|---|---|
| 0 | `Navigation.Dashboard` | `Navigation_Menu_Dashboard` | enabled, selected | P3 已接入 |
| 1 | `Production.DataView` | `Navigation_Menu_Data` | disabled | P4b |
| 2 | `Production.CapacityView` | `Navigation_Menu_Capacity` | disabled | P4b |
| 3 | `Hardware.IOView` | `Navigation_Menu_Io` | disabled | P4a |
| 4 | `Production.Monitor` | `Navigation_Menu_Monitor` | disabled | P4b |
| 5 | `Formula.RecipeView` | `Navigation_Menu_Recipe` | disabled | P4c |
| 6 | `Config.ParamView` | `Navigation_Menu_ParamConfig` | disabled | P4c |
| 7 | `Hardware.HardwareConfigView` | `Navigation_Menu_HardwareConfig` | disabled | P4a |
| 8 | `Hardware.PlcTaskBindingView` | `Navigation_Menu_PlcTaskBinding` | disabled | P4a |

`Core.Diagnostics` 当前不在 NavRail。Diagnostics 仍通过 `src/Edge/IIoT.Edge.Host.Bootstrap/DependencyInjection.cs:233-247` 的 `RegisterRoute` / `RegisterMenu` 注册到旧菜单路由体系；P4d 需要把它作为第 10 个 NavRail 项接入。

### NavigationHost

当前 `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/NavigationHostView.axaml.cs` 构造函数只注入 `DashboardView`，并执行 `DashboardContentHost.Content = dashboardView`。`NavigationRailViewModel.Select(...)` 只切换 `IsSelected`，没有向中央内容区传播选中 ViewId。

结论：P4c 不能只把 Recipe/Param 的 `IsEnabled` 改为 `true`。P4c 必须同时完成 NavigationHost 中央内容切换机制，否则 Recipe/Param 页面无法通过 NavRail 打开。

### ViewId Source

`src/Presentation/IIoT.Edge.Presentation.Navigation/CoreViewIds.cs` 当前只定义：

| Constant | Value |
|---|---|
| `Dashboard` | `Navigation.Dashboard` |
| `Diagnostics` | `Core.Diagnostics` |

DataView、CapacityView、IOView、Monitor、RecipeView、ParamView、HardwareConfig、PlcTaskBinding 的 ViewId 当前来自 `NavigationRailViewModel.cs` 中的字符串。

## Page Inventory

| Page | ViewId | WPF / Avalonia view | ViewModel | DI registration | Data source / sub-service | Resource keys | WPF-specific usage | Phase notes |
|---|---|---|---|---|---|---|---|---|
| Dashboard | `Navigation.Dashboard` | `Features/Dashboard/Views/DashboardView.axaml` | `Features/Dashboard/ViewModels/DashboardViewModel.cs` (153 lines) | `DependencyInjection.cs:23,55` | `IEquipmentPanelService`, `IAppLanguageService` | 18 `Navigation_Dashboard_*` keys in `Resources/Languages/*.axaml` | none found in Dashboard view or VM | P3 已接入；作为 P4 content host 初始页 |
| DataView | `Production.DataView` | `Features/Production/DataView/Views/DataViewPage.xaml` | `Features/Production/DataView/ViewModels/DataViewModel.cs` (124 lines) | `DependencyInjection.cs:43,63` | `IDataViewService`, `IAppLanguageService` | 15 keys, including query/export/output/yield column keys | WPF `Page` code-behind, MaterialDesign XAML, `System.Windows.Input.ICommand` | P4b；`ICommand` 可保留，View 需 Avalonia 化 |
| CapacityView | `Production.CapacityView` | `Features/Production/CapacityView/Views/CapacityViewPage.xaml` | `Features/Production/CapacityView/ViewModels/CapacityViewModel.cs` (272 lines) | `DependencyInjection.cs:41,61` | `ICapacityViewService`, `IAppLanguageService` | 22 capacity/query/shift keys | WPF `Page` code-behind, MaterialDesign XAML, `System.Windows.Application.Current?.Dispatcher` at line 263, `using System.Windows` | P4b；需要 Dispatcher 替换 |
| IOView | `Hardware.IOView` | `Features/Hardware/IOView/Views/IOViewPage.xaml` | `Features/Hardware/IOView/ViewModels/IoViewViewModel.cs` (384 lines) | `DependencyInjection.cs:25-30,57` | `IPlcDataStore`, `IAppLanguageService`, `IIoViewMappingBuilder`, `IIoViewSignalValueUpdater`, `IIoViewBufferBindingCoordinator`, `IIoViewInteractionWriter`, `IIoViewManualReadService` | 19 IO/read/write/status keys | WPF `Page` code-behind, MaterialDesign XAML, `ICommand`; related IO models use WPF resource lookup | P4a；相关 `Models/*` 与 buffer coordinator 需要一起盘入执行范围 |
| Monitor | `Production.Monitor` | `Features/Production/Monitor/Views/MonitorView.xaml` | `Features/Production/Monitor/ViewModels/MonitorViewModel.cs` (287 lines) | `DependencyInjection.cs:42,62` | `IMonitorViewService`, `IAppLanguageService` | 21 monitor/context/sync/output keys | WPF `Page` code-behind, `System.Windows.Threading.DispatcherTimer` | P4b；实时刷新计时器需要 Avalonia 等价 |
| RecipeView | `Formula.RecipeView` | `Features/Formula/RecipeView/Views/RecipeViewPage.xaml` | `Features/Formula/RecipeView/ViewModels/RecipeViewModel.cs` (269 lines) | `DependencyInjection.cs:40,60` | `IRecipeViewCrudService`, `IRecipeService`, `IAppLanguageService`, local `IEditorValidator<LocalRecipeParamEditModel>` | 23 recipe/source/sync/local-param keys | WPF `Page` code-behind, MaterialDesign XAML and PackIcon; VM only has `System.Windows.Input.ICommand` | P4c；需要保持 local admin 编辑约束和真实 Recipe 服务，不造假配方 |
| ParamView | `Config.ParamView` | `Features/Config/ParamView/Views/ParamViewPage.xaml` | `Features/Config/ParamView/ViewModels/ParamViewModel.cs` (151 lines) | `DependencyInjection.cs:24,56` | `IParamViewCrudService`, `IClientPermissionService`, `IAppLanguageService` | 8 param/tab/column keys | WPF `Page` code-behind, MaterialDesign XAML and PackIcon, `System.Windows.Application.Current?.Dispatcher` at line 118 | P4c；必须替换 Dispatcher，并保持 MES/Cloud/Business 参数分组真实来源 |
| HardwareConfig | `Hardware.HardwareConfigView` | `Features/Hardware/HardwareConfigView/Views/HardwareConfigPage.xaml` plus sub-pages | `Features/Hardware/HardwareConfigView/ViewModels/HardwareConfigViewModel.cs` (404 lines) | `DependencyInjection.cs:31-37,58` | `IClientPermissionService`, `IHardwareConfigLoadSaveCoordinator`, `IHardwareConfigDeviceSelectionCoordinator`, `IHardwareConfigEditSession`, plus validation/draft/save builders | top-level 4 keys; sub-pages and validators use additional hardware keys in old WPF resources | WPF `Page` and sub-page code-behind, MaterialDesign XAML, `System.Windows.Data.ICollectionView`, `System.Windows.Application.Current?.Dispatcher` at line 349; load/save coordinator uses WPF app/message box | P4a；范围最大，需包含 NetworkDevice/SerialDevice/IoMapping 子页与 coordinator |
| PlcTaskBinding | `Hardware.PlcTaskBindingView` | `Features/Hardware/PlcTaskBindingView/Views/PlcTaskBindingPage.xaml` | `Features/Hardware/PlcTaskBindingView/ViewModels/PlcTaskBindingViewModel.cs` (267 lines) | `DependencyInjection.cs:38-39,59` | `IPlcTaskBindingService`, `IClientPermissionService`, `IPlcTaskBindingConfirmationService`, `IAppLanguageService` | 17 binding/device/save keys | WPF `Page` code-behind, MaterialDesign XAML and PackIcon, `System.Windows.Application.Current?.Dispatcher` at line 155; confirmation service uses WPF dialog types | P4a；确认服务需 Avalonia 化但业务绑定服务不改 |
| Diagnostics | `Core.Diagnostics` | `Features/System/DiagnosticsView/Views/DiagnosticsPage.xaml` | `Features/System/DiagnosticsView/ViewModels/DiagnosticsViewModel.cs` (555 lines) | `DependencyInjection.cs:21,44-51,64`; Host.Bootstrap route/menu at `src/Edge/IIoT.Edge.Host.Bootstrap/DependencyInjection.cs:233-247` | `IStartupDiagnosticsStore`, `IEdgeSyncDiagnosticsQuery`, `IClientPermissionService`, `IDiagnostics*` builders/coordinators/operators | 44 diagnostics/dead-letter/module keys | WPF `UserControl` code-behind, `System.Windows.Threading.DispatcherTimer`, `System.Windows.Application.Current?.Dispatcher` at line 539; dead-letter confirmation uses WPF dialog types | P4d；需要新增 NavRail 项，VM 超 500 行是重构警告但本阶段应避免改业务语义 |

## WPF API Hotspots

The following findings are scoped to Navigation and are intended to seed the detailed P4 sub-phase plans.

| Area | Hotspots |
|---|---|
| P4c | `ParamViewModel.cs:118` uses `System.Windows.Application.Current?.Dispatcher`; Recipe/Param views use WPF `Page`, MaterialDesign styles and PackIcon. |
| P4d | `DiagnosticsViewModel.cs` uses `DispatcherTimer` and WPF dispatcher; `DiagnosticsDeadLetterConfirmationService.cs` uses WPF dialog types; Diagnostics is not currently in NavRail. |
| P4b | `MonitorViewModel.cs` uses `DispatcherTimer`; `CapacityViewModel.cs:263` uses WPF dispatcher; DataView mainly has WPF XAML and `ICommand`. |
| P4a | IO models use `System.Windows.Application.Current?.TryFindResource`; IO buffer coordinator uses WPF dispatcher; HardwareConfig uses `ICollectionView`, WPF dispatcher, and WPF message box; PlcTaskBinding uses WPF dispatcher and confirmation dialog. |

`System.Windows.Input.ICommand` appears in several ViewModels. It is treated as a command contract already used by the existing shared MVVM layer and is not by itself a reason to change public ViewModel APIs.

## Resource Inventory

Avalonia resource files currently exist at:

- `src/Presentation/IIoT.Edge.Presentation.Navigation/Resources/Languages/zh-CN.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/Resources/Languages/en-US.axaml`

Old WPF resource files still exist at the same directory with `.xaml` extension and currently contain many business page keys that are not yet present in `.axaml`. Phase 4 sub-phases should copy only the keys needed by the migrated pages into `.axaml`; `.xaml` cleanup remains outside this inventory task.

Key counts observed from page views and ViewModels:

| Page | Key count | Representative keys |
|---|---:|---|
| Dashboard | 18 | `Navigation_Dashboard_Title`, `Navigation_Dashboard_Devices`, `Navigation_Dashboard_TaktEmpty` |
| DataView | 15 | `Navigation_Button_Query`, `Navigation_Button_ExportExcel`, `Navigation_Column_Yield` |
| CapacityView | 22 | `Navigation_Title_CapacityQuery`, `Navigation_Capacity_TotalOutput`, `Navigation_Hint_QueryDimension` |
| IOView | 19 | `Navigation_Button_Write`, `Navigation_Button_ReadIoData`, `Navigation_Status_Connected` |
| Monitor | 21 | `Navigation_Monitor_TodaySummary`, `Navigation_Monitor_ContextPersistencePrefix`, `Navigation_Monitor_NoTaskStep` |
| RecipeView | 23 | `Navigation_Button_SyncCloud`, `Navigation_Recipe_LocalSaveSuccess`, `Navigation_Hint_ParamName` |
| ParamView | 8 | `Navigation_Tab_MesParams`, `Navigation_Column_ParamValue`, `Navigation_Button_Save` |
| HardwareConfig | 4 top-level keys | `Navigation_Tab_NetworkDevice`, `Navigation_Tab_SerialDevice`, `Navigation_Tab_IoMapping` |
| PlcTaskBinding | 17 | `Navigation_Hint_SelectDevice`, `Navigation_Column_TaskKey`, `Navigation_PlcTaskBinding_SaveSuccess` |
| Diagnostics | 44 | `Navigation_Diagnostics_Modules`, `Navigation_Button_Requeue`, `Navigation_Column_ModuleId` |

## Sub-phase Assignment

| Sub-phase | Pages | Required shared setup |
|---|---|---|
| P4c | RecipeView, ParamView | Add NavigationHost content switching and enable NavRail indexes 5 and 6. Keep namespace roots as `IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView` and `IIoT.Edge.Presentation.Navigation.Features.Config.ParamView`; do not add `.Views` / `.ViewModels` namespace suffixes. |
| P4d | Diagnostics | Add Diagnostics as a new NavRail item and route it through the same NavigationHost switch. |
| P4b | Monitor, DataView, CapacityView | Reuse P4c content switching; replace timer/dispatcher usage where present. |
| P4a | IOView, HardwareConfig, PlcTaskBinding | Reuse P4c content switching; include nested hardware pages/models/coordinators in the detailed plan. |

## Verification Notes

Commands used during this inventory:

```powershell
Get-ChildItem src -Directory
git status --short
rg --files src/Presentation/IIoT.Edge.Presentation.Navigation
rg -n "AddSingleton|AddTransient|CoreViewIds|RegisterRoute|RegisterMenu" src/Presentation/IIoT.Edge.Presentation.Navigation src/Edge/IIoT.Edge.Host.Bootstrap
rg -n "System\.Windows\.Application|System\.Windows\.Threading\.Dispatcher|System\.Windows\.Data|System\.Windows\.Controls|using System\.Windows" src/Presentation/IIoT.Edge.Presentation.Navigation -g "*.cs"
```

Post-inventory expectation:

- `git diff -- src/Core src/Application src/Runtime src/Infrastructure src/Modules src/Edge/IIoT.Edge.Host.Bootstrap` should not gain new changes from this task.
- `git diff -- docs/avalonia-migration/phase-4/navigation-inventory.md` should show this document as the only intentional Phase 4 preflight output.
