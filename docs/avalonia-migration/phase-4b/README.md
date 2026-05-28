# Phase 4b：生产数据链原地 Avalonia 化记录

## 范围

- 原地迁移 `IIoT.Edge.Presentation.Navigation` 的 Production 三个页面：
  - `Production.DataView`
  - `Production.CapacityView`
  - `Production.Monitor`
- `NavigationRailViewModel` 已打开生产数据、产能查询、实时监控三项。
- `NavigationHostView` 已接入 `DataViewPage`、`CapacityViewPage`、`MonitorViewPage`。
- 本轮未迁移 IO、硬件配置、PLC 任务绑定；这些仍属于 P4a。

## 实现记录

- 删除本轮范围内旧 WPF 视图文件：`DataViewPage.xaml(.cs)`、`CapacityViewPage.xaml(.cs)`、`MonitorView.xaml(.cs)`。
- 新增对应 Avalonia 视图文件：`DataViewPage.axaml(.cs)`、`CapacityViewPage.axaml(.cs)`、`MonitorView.axaml(.cs)`。
- `CapacityViewModel` 的 WPF `MessageBox` 已移除，离线查询改为页面状态提示；WPF Dispatcher 已替换为 `Avalonia.Threading.Dispatcher.UIThread`。
- `MonitorViewModel` 的 WPF `DispatcherTimer` 已替换为 Avalonia `DispatcherTimer`，刷新间隔保持 500ms。
- `DataViewModel`、`CapacityViewModel`、`MonitorViewModel` 继续使用现有真实服务来源，不构造样例数据。
- Navigation 双语 Avalonia 资源已补齐 Production 页面所需 key。

## 验证

已执行：

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

结果：构建成功，0 warning，0 error。

已执行静态检查：

```powershell
rg "System\.Windows\.Application|System\.Windows\.Threading|System\.Windows\.Controls|using System\.Windows;|MessageBox|MaterialDesign|PackIcon|PageActionShell|uiLoc:DataGridColumnLocalization" src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/DataView src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/CapacityView src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/Monitor
```

结果：无命中。

已执行文件检查：旧 `.xaml` 为 `False`，新 `.axaml` 为 `True`。

已执行资源 key 重复检查：`zh-CN.axaml`、`en-US.axaml` 均无重复 key。

## 剩余风险

- 本轮未执行三档桌面截图验收。
- `MonitorView` 的在制数据表继续使用真实 `DataTable` 来源，Avalonia 页面通过 `DefaultView` 暴露给 DataGrid；运行态仍需在有真实模块数据的环境中确认列自动生成效果。
