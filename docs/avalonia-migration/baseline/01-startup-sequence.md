# 01. WPF Shell 启动顺序

本文记录 `IIoT.Edge.Shell` 在当前 WPF 基线中的启动行为。入口集中在 `src/Edge/IIoT.Edge.Shell/App.xaml.cs`，主机注册集中在 `src/Edge/IIoT.Edge.Host.Bootstrap/DependencyInjection.cs`。

## 13 步启动基线

1. `App` 构造函数先触发 `MaterialDesignThemes.Wpf.BundledTheme` 程序集加载，再创建只包含启动辅助服务的临时 ServiceProvider。对应 `App.xaml.cs:35-45` 和 `ShellStartupDependencyInjection.cs:10-19`。

2. 构造函数解析 `ICrashLogWriter`、`IShellConfigurationLoader`、`IShellRuntimePathResolver`、`IShellModuleCatalog`，并注册 `DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`。对应 `App.xaml.cs:39-46`、`App.xaml.cs:173-197`。

3. `OnStartup` 先调用 WPF `base.OnStartup(e)`，随后以 `AppDomain.CurrentDomain.BaseDirectory` 作为基目录加载配置。对应 `App.xaml.cs:50-54`。

4. 配置加载分两轮完成：第一轮读取 `appsettings.json`、环境配置和环境变量，用于解析 `Shell:MachineProfile`；第二轮按顺序叠加插件模块配置、主配置、环境配置、可选的 `appsettings.machine.{profile}.json`、环境变量和 Shell 元数据。对应 `ShellConfigurationLoader.cs:20-67`。

5. Shell 根据 `Shell:MachineProfile` 和 `Shell:RuntimeDataRoot` 解析运行目录，默认运行根目录是 `baseDirectory\data\profiles\{profile}`，默认 Profile 为 `Default`。数据库、Excel、诊断日志、设备缓存和 crash log 路径都在这里生成。对应 `ShellRuntimePathResolver.cs:19-48`。

6. crash log writer 被配置为主路径加本地应用数据 fallback 路径。写日志时先写主路径，失败后写 fallback，再写诊断 sink。对应 `App.xaml.cs:56`、`App.xaml.cs:282-287`、`CrashLogWriter.cs:60-113`。

7. Shell 获取单实例 Mutex，名称为 `Global\IIoT.EdgeClient_{InstanceId}`，默认 `InstanceId` 是 `IIoT-Edge-Default`。获取失败时弹出提示并以退出码 `0` 关闭。对应 `App.xaml.cs:58-62`、`App.xaml.cs:232-249`、`SingleInstanceMutexHandle.cs:11-25`。

8. 主 ServiceCollection 创建后先构造 `ViewRegistry`，再从 `baseDirectory\Modules` 发现插件、激活插件，并收集插件程序集、发现结果、启用结果和问题列表。对应 `App.xaml.cs:253-280`、`ShellModuleCatalog.cs:28-49`。

9. `AddEdgeHostBootstrap` 把配置、运行路径、视图注册表、模块结果、诊断存储、应用层、EF Core、Dapper、Integration、DeviceComm、Runtime、MediatR、AutoMapper 和 Presentation 组件注册进主容器。对应 `DependencyInjection.cs:44-151`。

10. 主机注册后台服务和协调器：运行状态自动保存、运行配置预热、设备心跳、MES 心跳、PLC runtime、数据管线队列、云端重试、MES 重试、产能同步、设备日志同步、配方同步，以及 `IAppLifecycleCoordinator`。对应 `DependencyInjection.cs:153-217`。

11. 主 ServiceProvider 构建完成后，Shell 解析 `IAppLanguageService` 并执行 `Initialize()`。当前 WPF 实现使用持久化语言文件、线程 Culture 和 WPF ResourceDictionary 切换语言资源。对应 `App.xaml.cs:64-66`、`AppLanguageService.cs:43-62`、`AppLanguageService.cs:80-125`。

12. Shell 调用 `IAppLifecycleCoordinator.StartAsync`。生命周期管理器依次执行启动初始化、启动诊断、PLC 运行时绑定、运行状态恢复和后台服务启动；若启动诊断存在阻塞问题，则返回失败结果，不继续绑定 PLC 和启动后台服务。对应 `App.xaml.cs:68-76`、`AppLifecycleManager.cs:36-68`。

13. 启动成功后解析并展示 `MainWindow`；启动失败则显示错误消息、请求 `Shutdown(-1)`。退出时取消应用 CTS，最多等待 8 秒停止服务，释放 Mutex，释放临时 ServiceProvider，并在超时或异常时写 crash log。对应 `App.xaml.cs:78-89`、`App.xaml.cs:91-159`。

## 生命周期内部顺序

`AppLifecycleManager.StartAsync` 的内部顺序是当前迁移最关键的行为边界：

- `AppStartupInitializer.InitializeAsync` 先执行 EF Core migration、Dapper 表初始化、开发样例配置初始化。对应 `AppStartupInitializer.cs:29-39`。
- `IStartupDiagnosticsReportBuilder.BuildAsync` 生成启动诊断，诊断项包括配置、插件、设备绑定、硬件 Profile、模块注册。对应 `StartupDiagnosticsReportBuilder.cs:80-115`。
- `HasBlockingIssues` 将除 `DEVICE_MODEL_INVALID` 之外的问题视为阻塞问题。对应 `StartupDiagnosticsReportBuilder.cs:118-119`。
- 无阻塞问题时才执行 `IPlcRuntimeBinder.BindAsync`、`AppRuntimeStateCoordinator.RestoreAsync` 和 `BackgroundServiceCoordinator.StartAsync`。对应 `AppLifecycleManager.cs:52-63`。
- `BackgroundServiceCoordinator` 按注册顺序启动，停止时按反向顺序停止；启动中途失败会停止已启动的服务。对应 `BackgroundServiceCoordinator.cs:22-90`。

## 实测启动日志

2026-05-18 的 WPF Shell 短运行日志显示，空插件目录场景下已进入生命周期启动并完成：

- EF Core 迁移完成。
- Dapper 初始化 `pipeline_cloud.db` 和 `pipeline_mes.db` 相关表。
- 开发样例配置初始化完成。
- 启动诊断失败，问题码为 `PLUGIN_ROOT_MISSING` 和 `PLUGIN_NONE_ENABLED`。

该行为说明当前 WPF Shell 的插件诊断发生在持久化初始化之后、PLC 绑定和后台服务启动之前。

## 迁移保持点

- Avalonia Shell 需要保留“配置加载 -> 运行路径解析 -> crash log -> 单实例 -> 插件发现/激活 -> 主机注册 -> 语言初始化 -> 生命周期启动 -> 主窗口展示”的顺序。
- 启动诊断仍必须阻止 PLC 绑定和后台任务启动。
- 退出路径仍必须覆盖服务停止、运行状态保存、Mutex 释放和异常 crash log。
