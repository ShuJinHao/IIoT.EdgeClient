# Phase 0：冻结范围与事实核查小计划

> 本阶段只做事实核查和边界冻结。禁止改业务代码，禁止开始 UI 美化。
> 目的：先把 PR #44 中 UI、启动、Profile、日志、运行链路、历史遗留分清楚，避免后面 Codex 乱改。

---

## 1. 阶段目标

1. 明确当前 PR 中哪些文件是 UI 展示层。
2. 明确哪些文件涉及 Launcher/Profile/启动参数/运行链路。
3. 明确哪些文件涉及日志来源、设备状态来源、I/O 状态来源、匀浆数据来源。
4. 明确哪些是历史遗留：旧 WPF、旧 Launcher、旧 Dock、旧资源、旧文档/脚本。
5. 明确下一阶段允许改哪些文件，禁止改哪些文件。

---

## 2. Codex 必须执行的核查动作

### 2.1 Launcher/Profile 核查

必须检查：

- `src/Edge/IIoT.Edge.Launcher.Avalonia/launcher.profiles.json`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Services/LauncherProfileCatalog.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Services/ShellLaunchService.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/ViewModels/LauncherProfileViewModel.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Views/LauncherProfileView.axaml`

必须输出：

- 当前共有多少正式 profile。
- 是否存在同一个 `MachineProfile` 多个入口。
- 哪些入口是 UI-only。
- 哪些入口传入 `--start-runtime`。
- Launcher 是否显示 Variant。
- 搜索框是否有实际业务必要。

### 2.2 Shell 启动语义核查

必须检查：

- `src/Edge/IIoT.Edge.AvaloniaShell/App.axaml.cs`
- `src/Edge/IIoT.Edge.AvaloniaShell/Services/AvaloniaShellStartupCoordinator.cs`
- `src/Edge/IIoT.Edge.AvaloniaShell/Services/AvaloniaShellBootstrapOptionsFactory.cs`
- `src/Edge/IIoT.Edge.AvaloniaShell/appsettings*.json`

必须输出：

- Shell 默认是否启动真实运行链路。
- `--start-runtime` 的语义。
- UI-only 是否会被正式 Launcher 触发。
- `MachineProfile` 如何传入 Shell。
- 运行目录和日志目录如何解析。

### 2.3 Shell 骨架核查

必须检查：

- `src/Edge/IIoT.Edge.AvaloniaShell/Views/MainWindow.axaml`
- `src/Edge/IIoT.Edge.AvaloniaShell/ViewModels/MainWindowViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/**`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/**`
- 旧 WPF `src/Edge/IIoT.Edge.Shell/MainWindow.xaml`，只作为信息骨架参考

必须输出：

- 当前是否有左侧导航、中央业务页、右侧设备/日志、底部状态。
- 右侧日志是否默认常驻。
- Dock 默认 chrome 是否被隐藏。
- 还有哪些硬框、蓝条、嵌套框、Dock 控件感。

### 2.4 日志来源核查

必须检查：

- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/ViewModels/LogViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/LogView.axaml`
- `src/Edge/IIoT.Edge.Host.Bootstrap/EdgeHostLogService.cs`
- `ILogService` / `ILogDisplayService` 定义位置

必须输出：

- 日志行是否来自 `EntryAdded`。
- 初始日志是否来自真实缓存。
- 初始日志是否读取真实 `.log` 文件。
- 文件日志时间是否被错误地设为 `DateTime.Now`。
- 启动诊断摘要是否被混成运行日志。
- 没有日志时是否是真实空态。

### 2.5 设备状态/I/O 来源核查

必须检查：

- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/ViewModels/EquipmentViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/EquipmentView.axaml`
- I/O Avalonia 页面相关 ViewModel
- PLC write trace 相关接口

必须输出：

- 设备状态真实来源。
- Cloud/MES 状态真实来源。
- I/O 写入闸门真实来源。
- 当前 UI 是否把所有状态都显示成 success。
- 是否存在 fake 在线状态。

### 2.6 匀浆数据来源核查

必须检查：

- `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/HomogenizationDataViewModel.cs`
- `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml`
- `src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/*.xaml`

必须输出：

- 匀浆数据是否来自 `IProductionContextStore`。
- 是否存在 mock 出料记录。
- 空态是否真实。
- 表格列是否是业务真实列。

### 2.7 DDD / 插件加载核查

必须检查：

- Shell 是否直接 hardcode 模块 UI。
- 模块页面是否通过 ViewRegistry / 插件注册进入。
- Shell 是否直接引用模块 Runtime/Integration。
- UI 是否依赖 Infrastructure 具体实现。
- 模块加载是否仍由配置和目录/catalog 驱动。

---

## 3. 允许修改的文件类别

本阶段只允许新增或修改：

- `docs/**`
- 本阶段核查报告 Markdown

建议输出：

```text
docs/Avalonia-UI-Phase0-事实核查与冻结范围.md
```

---

## 4. 禁止修改的文件类别

本阶段禁止修改：

- `src/**`
- `scripts/**`
- `Directory.Packages.props`
- `*.csproj`
- `*.slnx`
- 任何业务、UI、测试代码

---

## 5. 阶段输出格式

Codex 必须输出一份报告，包含：

```markdown
# Phase 0 事实核查报告

## 1. 当前 Launcher/Profile 事实
...

## 2. 当前 Shell 启动语义事实
...

## 3. 当前 Shell 信息骨架事实
...

## 4. 当前日志来源事实
...

## 5. 当前设备/I/O 来源事实
...

## 6. 当前匀浆数据来源事实
...

## 7. DDD / 依赖倒置 / 插件加载核查
...

## 8. 历史遗留清单
...

## 9. 下一阶段允许修改文件
...

## 10. 下一阶段禁止修改文件
...

## 11. GitHub/仓库无法确认事项
...
```

---

## 6. 验收标准

本阶段通过条件：

- 明确指出同一 `MachineProfile` 多入口问题。
- 明确指出 UI-only 与 `--start-runtime` 真实启动语义。
- 明确日志真实来源和当前风险。
- 明确设备状态/I/O 真实来源和当前风险。
- 明确匀浆真实数据来源。
- 明确哪些历史遗留后续要处理。
- 没有改任何代码。

---

## 7. 统一验收命令

本阶段理论上只改文档，但仍建议执行：

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 8. 停止条件

遇到以下情况必须停止：

- 无法确认 profile 文件实际来源。
- 无法确认 Shell 是否默认 UI-only。
- 无法确认日志真实来源。
- 需要改代码才能完成核查。

停止后只输出：

```text
已停止：Phase 0 只允许事实核查，不允许代码改动。
无法确认事项：...
建议人工确认：...
```
