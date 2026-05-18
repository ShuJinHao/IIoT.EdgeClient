# 03. Launcher 启动 Shell 基线

本文记录 `IIoT.Edge.Launcher` 从 WPF 应用启动、本地登录、选择产线 Profile 到启动 Shell 进程的链路。

## Launcher 应用启动

- `App.OnStartup` 创建 ServiceCollection 并注册 Launcher 服务。对应 `App.xaml.cs:11-20`。
- 服务注册完成后立即调用 `ILauncherAccountCatalogInitializer.EnsureCatalogExists()`，确保本地账号目录存在。对应 `App.xaml.cs:18-19`。
- Launcher 解析 `MainWindow` 并展示。异常会通过 MessageBox 展示并 `Shutdown(-1)`。对应 `App.xaml.cs:20-33`。
- 退出时释放 ServiceProvider。对应 `App.xaml.cs:36-40`。

注册服务包括：

- `ILauncherAccountCatalogInitializer`
- `ILauncherAccountCatalog`
- `ILauncherProfileCatalog`
- `ILocalLauncherAuthService`
- `IProcessStarter`
- `IShellLaunchService`
- `LauncherMainViewModel`
- `MainWindow`

对应 `LauncherDependencyInjection.cs:16-26`。

## Profile 目录

Launcher Profile 文件固定为 `baseDirectory\launcher.profiles.json`。对应 `LauncherProfileCatalog.cs:16-23`。

加载规则：

- 文件不存在时抛出 `FileNotFoundException`。对应 `LauncherProfileCatalog.cs:25-29`。
- JSON 必须能反序列化为非空列表。对应 `LauncherProfileCatalog.cs:31-40`。
- `ProfileId`、`DisplayName`、`MachineProfile` 必填。对应 `LauncherProfileCatalog.cs:43-58`。
- `ExecutablePath` 为空时默认使用 `baseDirectory\IIoT.Edge.Shell.exe`；相对路径会以 Launcher 基目录解析。对应 `LauncherProfileCatalog.cs:60-82`、`LauncherProfileCatalog.cs:84-90`。
- `IconKind` 默认 `Cog`，`AccentColor` 默认 `#0F766E`。对应 `LauncherProfileCatalog.cs:74-81`。

当前 Profile 文件包含 `HomogenizationLine`：

- 展示名称：`匀浆`
- `MachineProfile`：`HomogenizationLine`
- Shell 路径：`..\homogenization\IIoT.Edge.Shell.exe`

对应 `launcher.profiles.json:1-11`。

## 登录到产线选择

`LauncherMainViewModel.LoginAsync` 的行为：

- 清空错误，进入 busy 状态。
- 调用 `ILocalLauncherAuthService.Authenticate`。
- 失败时重置为未登录状态并显示错误。
- 成功时加载 Profile 列表，设置 `IsAuthenticated=true`，显示欢迎文案，并应用过滤。

对应 `LauncherMainViewModel.cs:96-134`。

登录成功后，UI 从登录视图切换到 Profile 选择视图。切换逻辑位于 `MainWindow.xaml.cs:74-83`。

## Shell 进程启动

用户选择 Profile 后，`LauncherMainViewModel.LaunchAsync` 调用 `IShellLaunchService.Launch`。对应 `LauncherMainViewModel.cs:168-185`。

`ShellLaunchService` 的当前行为：

- Profile 不能为空。对应 `ShellLaunchService.cs:16-20`。
- Profile 指向的 Shell 可执行文件必须存在，否则抛出 `FileNotFoundException`。对应 `ShellLaunchService.cs:22-25`。
- 使用 `ProcessStartInfo`，`UseShellExecute=false`。对应 `ShellLaunchService.cs:27-31`。
- `WorkingDirectory` 设置为 Shell 可执行文件所在目录。对应 `ShellLaunchService.cs:29-31`。
- 通过环境变量 `Shell__MachineProfile` 传递 Profile 的 `MachineProfile`。对应 `ShellLaunchService.cs:33`。
- `Process.Start` 返回空进程时抛出 `InvalidOperationException`。对应 `ShellLaunchService.cs:35-39`。

Shell 侧配置加载器会读取环境变量，因此 `Shell__MachineProfile=HomogenizationLine` 会参与选择 `appsettings.machine.HomogenizationLine.json`。对应 `ShellConfigurationLoader.cs:20-67`。

## WPF UI 依赖

Launcher 当前使用 WPF 与 MaterialDesignThemes：

- `MainWindow.xaml` 和 `ChangePasswordWindow.xaml` 使用 `materialDesign:PackIcon`、MaterialDesign 文本框、按钮、卡片样式。
- `Themes/LauncherTheme.xaml` 合并 MaterialDesign3 默认资源，并基于 MaterialDesign 样式定义 Launcher 控件样式。
- Shell 构造阶段主动加载 MaterialDesignThemes 程序集。对应 `IIoT.Edge.Shell/App.xaml.cs:37`。

Avalonia 迁移时，Launcher 的行为边界是本地账号验证、Profile 选择和环境变量启动 Shell；MaterialDesign WPF 控件只是当前表现层实现。

## 迁移保持点

- 账号目录初始化必须在主窗口展示前完成。
- Profile 必须继续通过文件驱动，不应硬编码产线列表。
- Shell 启动必须继续使用目标 exe 目录作为工作目录。
- `Shell__MachineProfile` 是 Launcher 到 Shell 的关键传参，不应替换为 UI 内部状态。
