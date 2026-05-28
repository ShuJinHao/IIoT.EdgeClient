# Phase 0 WPF 行为基线盘点

## 盘点范围

- 仓库：`IIoT.EdgeClient`
- 分支：`codex/edgeclient-local-homogenization-sync`
- 基线提交：`d0b06c0dbd0beae734afb97c42de3cc2e0e074f3`
- 源码阅读范围：`src/`
- 本阶段产物：仅本目录下 7 个 Markdown 文档。

本阶段没有新增或修改公共 API、接口、类型、配置、项目文件、业务代码和依赖包。既有未跟踪文件 `src/Tests/IIoT.Edge.NonUiRegressionTests/edge_test_output.txt` 保持原样，不属于 Phase 0 产物。

## 文档清单

| 文件 | 内容 |
| --- | --- |
| `01-startup-sequence.md` | WPF Shell 从 `App` 构造到窗口展示、退出清理的 13 步启动基线。 |
| `02-plugin-loading.md` | `Modules/` 插件目录、`plugin.json` 清单、启用策略、程序集加载和诊断问题码。 |
| `03-launcher-shell-launch.md` | Launcher 本地启动、账号目录、产线 Profile 选择和 Shell 进程启动链路。 |
| `04-local-admin-login.md` | Launcher 本地管理员登录、密码校验、改密和安全边界。 |
| `05-device-seeding.md` | 设备云端 bootstrap、会话缓存、上传门控，以及开发样例 PLC 播种链路。 |
| `06-open-questions.md` | Avalonia 迁移前的明确记录项：VM 基类、图标方案、空模块实测、语言服务落点、PR #44 依赖对比。 |

## 实测记录

为获取原版 WPF Shell 启动日志，执行过一次 Shell 构建和一次短运行；生成内容落在 `publish/`、`bin/`、`obj/`、运行日志和运行数据目录中。未执行 `dotnet test`。

实测日志位置：

`C:\Users\jinha\Desktop\产线系统架构升级\1\publish\Debug\shell\data\profiles\Default\diagnostics\logs\2026-05-18.log`

该次运行使用发布目录默认状态，`publish\Debug\shell\Modules` 不存在。启动过程完成 EF Core 迁移、Dapper 表初始化和开发样例配置初始化后，被启动诊断拦截。日志明确给出：

- `PLUGIN_ROOT_MISSING`：插件根目录不存在。
- `PLUGIN_NONE_ENABLED`：未能从 `Modules` 配置节加载任何已启用插件。

该结果在 `02-plugin-loading.md` 和 `06-open-questions.md` 中按模块加载基线记录。

## 读取依据

主要入口文件包括：

- `src/Edge/IIoT.Edge.Shell/App.xaml.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/DependencyInjection.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/AppLifecycleManager.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/DirectoryModuleCatalog.cs`
- `src/Edge/IIoT.Edge.Launcher/App.xaml.cs`
- `src/Edge/IIoT.Edge.Launcher/Core/ShellLaunchService.cs`
- `src/Infrastructure/IIoT.Edge.Infrastructure.Integration/Devices/DeviceService.cs`
- `src/Modules/IIoT.Edge.Module.Homogenization/Development/HomogenizationDevelopmentSampleContributor.cs`

这些文档只记录当前 WPF 行为，不对 Avalonia 迁移代码作实现决策。
