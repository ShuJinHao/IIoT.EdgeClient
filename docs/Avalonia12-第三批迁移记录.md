# Avalonia 12 第三批迁移记录

## 记录信息

- 日期：2026-05-13
- 仓库：`IIoT.EdgeClient.AvaloniaMigration`
- 范围：仅旁路副本，不回写原 `IIoT.EdgeClient`
- 目标：迁移硬件配置和真实 I/O 代表页，验证复杂 `DataGridTemplateColumn`、确认弹窗、动态列头、权限入口、PLC 交互安全入口和 WPF ViewModel 渗透处理方式

## 本批完成内容

- 新增 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Features/Hardware/HardwareConfig`
  - 迁移硬件配置页骨架：网络设备、串口设备、IO 映射三个区块
  - 使用 Avalonia `TabControl`、`DataGrid` 和 `DialogHost.Avalonia`
  - `IoMapping` 过滤改为显式集合刷新，不使用 WPF `CollectionViewSource/ICollectionView`
  - 保存、新增交互点、新增数据点只进入迁移验证确认流，不写真实硬件配置
- 新增 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Features/Hardware/IOView`
  - 迁移真实 I/O 页骨架：设备选择、交互点位、数据点位、连续读取矩阵、手动读取、写入按钮
  - 新增 `IIoViewSafeInteractionPort` 和默认 `NoopIoViewSafeInteractionPort`
  - 默认端口只刷新页面预览值，不连接真实 PLC，不执行真实读写
  - 页面模型不使用 `Application.Current.TryFindResource`，显示文本走 Avalonia 语言资源服务
- 扩展 `src/Shared/IIoT.Edge.UI.Avalonia/Services`
  - `IAvaloniaDialogService` 保留 `ShowInfoAsync`
  - 新增 `ConfirmAsync`
  - `AvaloniaDialogRequest` 增加 `Info/Confirm` 类型、确认结果和完成状态
- 调整 `src/Edge/IIoT.Edge.AvaloniaShell`
  - `MainWindowViewModel` 接入 `IAvaloniaDialogService`
  - Shell 增加统一系统确认弹窗 `DialogHost`
  - 登录弹窗继续保留，不和系统确认弹窗混用状态
- 调整 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia`
  - `StandardAvaloniaModuleViewIds` 新增 `HardwareConfigView` 和 `IOView`
  - `NavigationAvaloniaPresentationRegistration` 注册硬件配置和 I/O route/menu/dock pane
  - `DependencyInjection` 注册 I/O 安全默认端口
  - `NavigationAvaloniaResources` 补齐硬件配置、I/O、DataGrid 列头和弹窗中英文资源
- 扩展 `src/Tests/IIoT.Edge.AvaloniaShell.Tests`
  - 覆盖硬件配置 route 创建、假数据增删、IO 映射过滤、保存确认流
  - 覆盖 I/O route 创建、fake 设备加载、手动读取入口、写入入口
  - 覆盖 Confirm Dialog、Dispatcher、Timer、WindowService、DataGrid 列头刷新

## 边界说明

- 本批未连接真实 PLC。
- 本批未触发真实 Cloud/MES 上传。
- 本批未启动完整后台数据管线。
- 本批未迁移 Recipe、Param、匀浆插件 UI、Launcher。
- 本批未修改 Cloud/MES API、数据库结构、配置 JSON 字段、PLC 读写策略、补偿规则或业务规则文档。
- Avalonia 项目未引用 `IIoT.Edge.UI.Shared`、WPF Presentation 项目、`UseWPF` 或 `System.Windows`。
- WPF Shell 仍可构建和通过既有 Shell 测试。

## 依赖结论

- 顶层 Avalonia/SukiUI/Dock/DialogHost/Material Icons 包均为稳定版。
- `IIoT.Edge.Presentation.Navigation.Avalonia` 为使用页面级 `DialogHost` 新增顶层稳定包 `DialogHost.Avalonia 0.12.2`。
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
rg --pcre2 -n "UseWPF|System\.Windows|IIoT\.Edge\.UI\.Shared|IIoT\.Edge\.Presentation\.(Navigation|Shell|Panels)(?!\.Avalonia)" src/Edge/IIoT.Edge.AvaloniaShell src/Edge/IIoT.Edge.Host.Bootstrap.Avalonia src/Shared/IIoT.Edge.UI.Avalonia src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia src/Tests/IIoT.Edge.AvaloniaShell.Tests
```

- AvaloniaShell build：通过，0 警告，0 错误
- WPF Shell build：通过，0 警告，0 错误
- AvaloniaShell Headless 测试：通过 9
- Module Contract Tests：通过 28
- NonUiRegressionTests：通过 367
- Shell Tests：通过 71，保留既有测试中的未使用事件警告
- 漏洞检查：未发现易受攻击包
- prerelease 检查：仅发现已批准的 `SkiaSharp` preview 传递依赖
- 边界扫描：未发现 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared` 或 WPF Presentation 项目引用

## 剩余风险

- HardwareConfig 和 I/O 当前仍是 fake 数据与安全端口，尚未接真实配置服务、权限服务和现场 PLC 运行链路。
- I/O 写入入口只验证命令路径，真实块读取/块写入必须后续继续通过现有运行时抽象和 `IPlcSignalBlockPlanner`，不得逐点读取。
- DataGrid 样式可用性已通过 Headless 创建验证，仍需要后续人工运行窗口检查列宽、滚动、触摸和高 DPI 表现。
- HardwareConfig 的权限控制目前未接真实权限服务，后续接入时不能让 ViewModel 直接访问 WPF 或 Avalonia Window。

## 后续批次建议

- 第四批迁移 Recipe、Param，优先处理动态列、编辑器、保存确认和资源列头。
- 第四批开始接入只读配置服务或 fake adapter，逐步替换本批页面中的纯假数据，但仍不连接现场 PLC。
- 匀浆插件 UI 单独批次处理，继续保持模块 key、ProcessType、任务 key 字符串值不变。
- Launcher 继续独立决策，不并入主 Shell 迁移批次。
