# Phase 2：Panels 原项目原地 Avalonia 化记录

## 范围

本阶段只围绕原项目 `src/Presentation/IIoT.Edge.Presentation.Panels` 接入 Avalonia 正式运行路径，并由 `src/Edge/IIoT.Edge.Shell` 右侧区域承载设备运行与系统日志面板。

本阶段没有新建 `IIoT.Edge.Presentation.Panels.Avalonia`、`IIoT.Edge.UI.Avalonia`、`.Legacy` 或任何并行 UI 项目。`IIoT.Edge.UI.Shared` 继续作为共享 UI 类库承载 Avalonia DataGrid 依赖与通用样式；WPF 旧层保留到后续统一清理阶段处理。

## 已完成

- `EquipmentView.axaml` 与 `LogView.axaml` 已在原 `IIoT.Edge.Presentation.Panels` 项目内接入正式 Shell 右侧区域。
- `EquipmentViewModel` 继续使用 `IEquipmentPanelService`、`IRecipeService` 与 `RecipeChanged` 事件，UI 线程投递从 WPF Dispatcher 改为 Avalonia Dispatcher。
- `LogDisplayService` 继续包装真实 `ILogService` / `Log4NetLogService`，日志事件改为投递到 Avalonia UI 线程。
- `LogViewModel.ClearCommand` 仍只清理 UI 集合，不删除日志文件。
- `AddPanelPresentation()` 与 `RegisterPanelViews()` 方法名保留，注册标识仍为 `Core.Equipment` 与 `Core.SysLog`。
- `IIoT.Edge.Shell` 只做右侧区域最小接线，Header、Footer、NavRail、中央区域没有在本阶段扩大改造。
- `Avalonia.Controls.DataGrid` 版本写入 `Directory.Packages.props`，实际包引用放在 `IIoT.Edge.UI.Shared.csproj`，Panels 项目不重复引用 DataGrid 包。

## 验证

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

结果：通过，0 警告，0 错误。

静态检查：

- 禁止的并行 UI 项目、Avalonia 专用 bootstrap、`IAvaloniaXxx` 未新增；扫描仅命中测试用例中的业务信号名 `Homogenization.Legacy`，不是 `.Legacy` 项目或迁移入口。
- 禁止依赖 `CommunityToolkit.Mvvm`、`ReactiveUI`、`Prism`、`Dock.Avalonia`、`Material.Avalonia`、`Material.Icons.Avalonia`、`DialogHost.Avalonia`、`LucideAvalonia`、`Avalonia.Headless.XUnit`、`xunit.v3` 均未新增。
- `src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap` 相对 Phase 0 commit `1aac64d` 无 diff。

运行验证：

- `Modules/Homogenization` 存在时，Shell 启动成功，右侧显示真实设备运行面板与系统日志面板。
- 无 `Modules/` 时，启动失败窗口显示 `PLUGIN_ROOT_MISSING` 与 `PLUGIN_NONE_ENABLED`，不进入主界面。
- 单实例锁保持原行为：第一个实例保持运行，第二个实例退出码为 `0`。
- 设备面板显示真实数据来源返回的空态，不构造设备、产量、良率或配方数据。
- 日志面板显示真实运行日志来源返回的内容；当前截图场景下日志集合为空，显示真实空态。

截图：

- `screenshots/panels-1900x1200.png`
- `screenshots/panels-1600x1000.png`
- `screenshots/panels-1366x768.png`
