# Phase 4a 硬件配置链原地 Avalonia 化

## 范围

本轮处理 `IIoT.Edge.Presentation.Navigation` 内 Hardware 页面：

- `Hardware.IOView`
- `Hardware.HardwareConfigView`
- `Hardware.PlcTaskBindingView`

页面仍在原项目、原目录、原命名空间内承担正式运行职责。未新建 `.Avalonia`、`.Legacy` 或并行 UI 项目。

## 已完成

- `IOViewPage`、`HardwareConfigPage`、`NetworkDevicePage`、`SerialDevicePage`、`IoMappingPage`、`PlcTaskBindingPage` 已替换为 Avalonia `.axaml(.cs)`。
- 本轮迁移范围内旧 WPF `.xaml(.cs)` 已删除。
- `NavigationRailViewModel` 已打开 IO、硬件、任务绑定三项。
- `NavigationHostView` 已接入 IO、硬件配置、PLC 任务绑定页面。
- `System.Windows.Application`、`System.Windows.Data.ICollectionView`、`MessageBox.Show()`、`TryFindResource` 已从 Hardware 正式运行路径移除。
- 硬件配置分组改为 `IoMappingGroups`，继续基于真实 `IoMappingVm` 集合构建。
- 危险操作确认改为 Hardware 范围内 Avalonia 确认对话框；无主窗口时返回取消。

## 业务边界

- 未修改 PLC 读写、块规划、IO 映射保存、任务绑定保存、硬件配置 CRUD 的业务语义。
- 未构造假 PLC、假 IO 点位、假连接状态、假设备、假任务绑定、假硬件模板。
- 未新增 NuGet 包。
- 未做全仓 WPF 清理；Navigation 项目的全局 `UseWPF` 和旧资源清理由后续清理 Phase 处理。

## 验证命令

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

```powershell
rg "System\.Windows\.Application|System\.Windows\.Threading|System\.Windows\.Controls|System\.Windows\.Data|using System\.Windows;|MessageBox|MaterialDesign|PackIcon|PageActionShell|uiLoc:DataGridColumnLocalization|TryFindResource" src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware
```

## 剩余风险

- 硬件配置表格编辑体验为 Avalonia DataGrid 的基础实现，视觉精修进入后续统一 UI 精修轮。
- 本轮不验证真实 PLC 写入动作，只保证页面仍通过既有 ViewModel 和 service 调用链触发。
