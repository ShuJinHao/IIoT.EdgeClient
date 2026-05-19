# Phase 1：Shell 启动壳原位替换记录

## 范围

本阶段完成原 `IIoT.Edge.Shell` 与 `IIoT.Edge.Presentation.Shell` 的 Avalonia 启动壳替换，并在现有 `IIoT.Edge.UI.Shared` 内新增 Avalonia 等价层。

`IIoT.Edge.UI.Shared` 的 WPF 旧层本阶段保留，用于支撑尚未迁移的 Navigation、Panels、Modules Presentation 和 Launcher；后续上层项目迁移完成后，再单独清理 WPF 旧层。

## 已完成

- Phase 0 七份 baseline 文档已单独提交。
- Shell 入口从 WPF `App.xaml` / `MainWindow.xaml` 替换为 Avalonia `Program.cs` / `App.axaml` / `MainWindow.axaml`。
- `AddShellStartupServices()`、`AddEdgeHostBootstrap()`、`AddShellPresentation()`、单实例 Mutex、插件目录 `baseDirectory\Modules` 均保持原语义。
- Presentation.Shell 保留 `AddShellPresentation()`，语言服务原位改为 Avalonia 资源加载。
- P1 主界面只展示五区骨架和真实空态文案，不展示模拟设备、日志、I/O、产量或良率。

## 验证

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

构建结果：通过，0 警告，0 错误。

运行验证：

- 临时铺设 `Homogenization` 模块生成物到 `publish/Debug/shell/Modules/Homogenization` 后，Shell 进程启动并进入主窗口，后台服务启动。
- 无 `Modules` 目录时，启动拦截在 `PLUGIN_ROOT_MISSING` 与 `PLUGIN_NONE_ENABLED`，不进入主窗口。
- 单实例锁验证通过：第一个实例保持响应，第二个实例直接退出。

截图：

- `screenshots/shell-1900x1200.png`
- `screenshots/shell-1600x1000.png`
- `screenshots/shell-1366x768.png`
