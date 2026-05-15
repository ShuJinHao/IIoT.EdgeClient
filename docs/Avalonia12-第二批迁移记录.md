# Avalonia 12 第二批迁移记录

## 记录信息

- 日期：2026-05-13
- 仓库：`IIoT.EdgeClient.AvaloniaMigration`
- 范围：仅旁路副本，不回写原 `IIoT.EdgeClient`
- 目标：从 Shell 基座推进到真实客户端骨架，拆出 Avalonia 可用的启动/注册链路，并接入真实 Shell、Panel 和首批核心导航页

## 本批完成内容

- 新增 `src/Edge/IIoT.Edge.Host.Bootstrap.Core`
  - 承载 Avalonia 可用的运行路径、配置、HostEnvironment、日志、模块参数注册、集成注册和底层应用/基础设施/运行时 DI 组装
  - 不启动 `IAppLifecycleCoordinator`，避免真实 PLC、Cloud/MES 和后台数据管线启动
- 新增 `src/Edge/IIoT.Edge.Host.Bootstrap.Avalonia`
  - 组装 Core、Avalonia UI 共享层、Shell/Panel/Navigation Avalonia Presentation
  - 提供统一 Avalonia 视图注册入口
- 现有 WPF `src/Edge/IIoT.Edge.Host.Bootstrap` 已引用 `Bootstrap.Core`
  - WPF Shell 的旧注册路径保持不变，仍继续注册 WPF Presentation
- 新增 `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia`
  - 迁移 Header、Footer、Login 视图和 ViewModel
  - 登录走现有 `IAuthService`，弹窗/语言走 Avalonia 服务
- 新增 `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia`
  - 迁移 Equipment、Log 面板骨架
  - 刷新定时器走 `IAvaloniaTimerFactory`
  - 日志 UI 更新走 `IAvaloniaDispatcherService`
- 新增 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia`
  - 迁移首批真实导航页骨架：Monitor、DataView、Capacity、PlcTaskBinding、Diagnostics
  - 保持模块 ViewId 命名语义：`Homogenization.Monitor`、`Homogenization.DataView`、`Homogenization.CapacityView`、`Homogenization.PlcTaskBindingView`
  - DataGrid 列头统一使用 `LocalizedDataGrid.HeaderResourceKey`
- 扩展 `src/Shared/IIoT.Edge.UI.Avalonia`
  - 新增资源贡献模型 `IAvaloniaResourceContributor`
  - 新增 `IAvaloniaTimerFactory` / `IAvaloniaTimer`
  - 新增 `IAvaloniaWindowService`
  - 语言服务支持跨项目中英文资源合并
- 调整 `src/Edge/IIoT.Edge.AvaloniaShell`
  - 移除第一批本地假菜单、假页面、假面板
  - 改为通过 `IIoT.Edge.Host.Bootstrap.Avalonia` 组装真实菜单、Dock 页面和面板
- 更新 `IIoT.EdgeClient.slnx` 和 `Directory.Packages.props`
  - 新项目已纳入解决方案
  - 补齐 `Microsoft.Extensions.Configuration 10.0.5` 中央版本

## 边界说明

- 本批未连接真实 PLC。
- 本批未触发真实 Cloud/MES 上传。
- 本批未启动完整后台数据管线。
- 本批未迁移 HardwareConfig、IOView 真实页、Recipe、Param、匀浆插件 UI、Launcher。
- Avalonia 项目未引用 `IIoT.Edge.UI.Shared`、WPF Presentation 项目、`UseWPF` 或 `System.Windows`。
- WPF Shell 仍可构建和通过既有 Shell 测试。

## 依赖结论

- 顶层 Avalonia/SukiUI/Dock/DialogHost/Material Icons 包均为稳定版。
- 漏洞检查无告警。
- preview/prerelease 传递依赖仅出现已批准的 `SkiaSharp` 系列：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`

## 验证结果

```powershell
dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj
dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj
dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj
dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive
```

- AvaloniaShell build：通过，0 警告，0 错误
- WPF Shell build：通过，0 警告，0 错误
- AvaloniaShell Headless 测试：通过 5
- Module Contract Tests：通过 28
- NonUiRegressionTests：通过 367
- Shell Tests：通过 71，保留既有测试中的未使用事件警告
- 漏洞检查：未发现易受攻击包
- prerelease 检查：仅发现已批准的 `SkiaSharp` preview 传递依赖
- 边界扫描：AvaloniaShell、Avalonia Bootstrap、Avalonia Presentation、Avalonia UI Shared、AvaloniaShell Tests 中未发现 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared` 或 WPF Presentation 项目引用

## 后续批次建议

- 第三批迁移 HardwareConfig 和 I/O 真实页，重点处理 `DataGridTemplateColumn`、交互按钮和 PLC 写入防护。
- 第三批开始前先抽象 ViewModel 中剩余 WPF `DispatcherTimer` / `Application.Current` / `System.Windows.Media` 渗透点。
- 匀浆插件 UI 单独批次处理，继续保持模块 key、ProcessType、任务 key 字符串值不变。
- Launcher 继续独立决策，不并入主 Shell 迁移批次。
