# 02. 插件加载基线

本文记录当前 WPF Shell 的模块插件发现、启用、加载和诊断行为。入口是 `src/Edge/IIoT.Edge.Shell/App.xaml.cs` 调用 `IShellModuleCatalog`，核心实现位于 `src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/DirectoryModuleCatalog.cs`。

## 入口和目录

- Shell 插件根目录固定为 `baseDirectory\Modules`。对应 `ShellModuleCatalog.cs:28-38`。
- Shell 使用配置节名 `Modules` 调用目录型模块目录。对应 `ShellModuleCatalog.cs:40-49`。
- `App.ConfigureServices` 在调用 `AddEdgeHostBootstrap` 前完成插件发现和激活，并把发现模块、激活模块、模块程序集、配置启用 ID 和问题列表传入主机注册。对应 `App.xaml.cs:253-280`。

## 发现规则

`DirectoryModuleCatalog.Discover` 的当前规则：

- 插件根目录不存在时，不扫描任何模块，并返回 `PLUGIN_ROOT_MISSING`。对应 `DirectoryModuleCatalog.cs:36-49`。
- 插件根目录存在时，只枚举第一层子目录。每个子目录必须包含 `plugin.json`。对应 `DirectoryModuleCatalog.cs:53-59`。
- `plugin.json` 读取或校验失败时返回 `PLUGIN_MANIFEST_INVALID`，该目录不会产生可激活模块。对应 `DirectoryModuleCatalog.cs:61-72`。
- 发现完成后检查 `ModuleId` 和 `ProcessType` 唯一性，重复项会从可激活列表排除。对应 `DirectoryModuleCatalog.cs:75-87`、`DirectoryModuleCatalog.cs:395-427`。

## 清单字段

当前 `plugin.json` 必填字段由 `ModulePluginManifest` 和 `LoadDescriptor` 共同约束：

- `moduleId`
- `displayName`
- `version`
- `hostApiVersion`
- `minHostVersion`
- `maxHostVersion`
- `entryAssembly`
- `entryType`
- `supportedProcessType`
- `dependencies`

字段模型对应 `ModulePluginManifest.cs:7-35`。必填和版本解析校验对应 `DirectoryModuleCatalog.cs:313-366`。

当前仓库唯一插件是 Homogenization：

- 清单文件：`src/Modules/IIoT.Edge.Module.Homogenization/plugin.json`
- `moduleId`：`Homogenization`
- `displayName`：`匀浆`
- `supportedProcessType`：`Homogenization`
- `entryAssembly`：`IIoT.Edge.Module.Homogenization.dll`
- `entryType`：`IIoT.Edge.Module.Homogenization.DependencyInjection`
- `dependencies`：空数组

插件项目通过 `IsEdgePluginModule=true` 和 `PluginModuleId=Homogenization` 标记插件模块。对应 `IIoT.Edge.Module.Homogenization.csproj:8-11`。

## 启用规则

`Activate` 使用 `Modules:Enabled` 字符串数组决定启用模块：

- `Modules:Enabled` 为空时，默认启用所有已发现模块。对应 `DirectoryModuleCatalog.cs:236-274`。
- 启用列表中重复 ID 会产生 `PLUGIN_ENABLED_DUPLICATE`。对应 `DirectoryModuleCatalog.cs:249-257`。
- 启用列表中不存在的模块会产生 `PLUGIN_ENABLED_NOT_FOUND`。对应 `DirectoryModuleCatalog.cs:107-116`。
- 若最终激活模块数为 `0`，会产生 `PLUGIN_NONE_ENABLED`。对应 `DirectoryModuleCatalog.cs:211-216`。

配置来源由 Shell 配置加载器叠加：

- `Modules/**/*.module.json` 中路径含 `Config` 的模块默认配置。
- Shell `appsettings.json`、环境配置、机器 Profile 配置。
- 环境变量。

对应 `ShellConfigurationLoader.cs:45-67`、`ShellConfigurationLoader.cs:82-95`。

## 加载和依赖规则

激活阶段执行以下校验和加载：

- Host API 版本和 Host 版本范围不兼容时返回 `PLUGIN_HOST_VERSION_INCOMPATIBLE`。对应 `DirectoryModuleCatalog.cs:118-128`、`DirectoryModuleCatalog.cs:368-393`。
- 依赖模块未发现或未配置启用时返回 `PLUGIN_DEPENDENCY_MISSING`。对应 `DirectoryModuleCatalog.cs:133-154`。
- 模块按依赖拓扑顺序激活，无法继续推进时返回未解析依赖问题。对应 `DirectoryModuleCatalog.cs:156-209`。
- `ModulePluginLoader` 使用 `IModulePluginAssemblyResolver` 加载入口程序集，入口类型必须实现 `IEdgeProcessModule`，并且必须有无参构造函数。对应 `ModulePluginLoader.cs:19-50`。
- 程序集解析器使用默认 `AssemblyLoadContext`，会预登记插件目录下第一层 DLL，并在 `AssemblyLoadContext.Default.Resolving` 中按程序集名解析依赖。对应 `ModulePluginAssemblyResolver.cs:19-40`、`ModulePluginAssemblyResolver.cs:52-135`。

## 诊断输出

主机启动诊断会把插件问题转换为 `StartupDiagnosticIssue`，并输出：

- 已发现模块 ID。
- 配置启用模块 ID。
- 已激活模块 ID。
- 插件生命周期快照。
- 模块服务注册快照。

对应 `StartupDiagnosticsReportBuilder.cs:87-115`、`StartupPluginLifecycleSnapshotBuilder.cs:18-56`、`StartupDiagnosticsReportBuilder.cs:472-485`。

## 空 Modules 实测

实测时间：2026-05-18。运行目录：`publish\Debug\shell`。

运行前状态：`publish\Debug\shell\Modules` 不存在。

实测结果：

- 发现模块数：`0`
- 配置启用模块数：`0`
- 激活模块数：`0`
- 阻塞问题码：`PLUGIN_ROOT_MISSING`、`PLUGIN_NONE_ENABLED`
- 生命周期位置：EF Core migration、Dapper 表初始化、开发样例配置初始化之后；PLC 绑定和后台服务启动之前。

日志文件：

`C:\Users\jinha\Desktop\产线系统架构升级\1\publish\Debug\shell\data\profiles\Default\diagnostics\logs\2026-05-18.log`

## 迁移保持点

- Avalonia Shell 不能把插件加载延后到主窗口显示之后。
- `Modules/` 路径、`plugin.json` 字段、启用规则、问题码和诊断快照需要保持可对照。
- 插件未加载时应继续由启动诊断统一拦截，而不是在 UI 层吞掉错误。
