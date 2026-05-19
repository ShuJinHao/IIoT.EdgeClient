# Phase 1 启动链路等价记录

| 步骤 | Phase 0 WPF 基线 | Phase 1 Avalonia 实现 |
|---|---|---|
| 1. 启动辅助服务 | `App.xaml.cs` 构造函数创建 startup provider 并调用 `AddShellStartupServices()` | `App.axaml.cs` 的 `StartShellAsync` 创建 startup provider 并调用同一方法 |
| 2. 配置加载 | `IShellConfigurationLoader.Load(AppDomain.CurrentDomain.BaseDirectory)` | 相同调用 |
| 3. 运行路径解析 | `IShellRuntimePathResolver.Resolve(...)` | 相同调用 |
| 4. crash log 配置 | `ICrashLogWriter.ConfigurePaths(...)` | 相同调用 |
| 5. 单实例锁 | `Global\IIoT.EdgeClient_{InstanceId}` | 相同 Mutex 名称和 `SingleInstanceMutexHandle` |
| 6. 插件目录 | `baseDirectory\Modules` | 相同目录，由 `ShellModuleCatalog.GetPluginRootPath` 解析 |
| 7. 插件发现和启用 | `DiscoverModules` + `CreateEnabledModules` | 相同调用，并保留 `moduleCatalogIssues` |
| 8. ViewRegistry | `new ViewRegistry()` | 相同类型 |
| 9. Host bootstrap | `AddEdgeHostBootstrap(...)` | 相同调用，不新增 Avalonia 专用 bootstrap |
| 10. Shell Presentation | `AddShellPresentation()` 由 Host.Bootstrap 调用 | 方法名保留，注册 Avalonia 版 `AppLanguageService` |
| 11. 语言初始化 | `IAppLanguageService.Initialize()` | 相同接口调用，资源实现从 WPF 字典改为 Avalonia 资源 |
| 12. 生命周期启动 | `IAppLifecycleCoordinator.StartAsync()` 成功后显示主窗口 | 相同顺序，成功后设置并显示 Avalonia `MainWindow` |
| 13. 退出停机 | `StopAsync()`、释放 Mutex、释放 ServiceProvider | 相同停机顺序 |

空 `Modules/` 的拦截语义沿用 Phase 0 基线：启动诊断中出现 `PLUGIN_ROOT_MISSING` / `PLUGIN_NONE_ENABLED` 时，不进入主界面。
