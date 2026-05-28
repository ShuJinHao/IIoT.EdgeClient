# Phase 3：Navigation + Dashboard 原项目原地 Avalonia 化记录

## 范围

P3 将正式运行路径从 P1 的中央空态推进到 Navigation 项目提供的真实导航与 Dashboard 首页。

本阶段只改动以下 UI 边界：

- `src/Presentation/IIoT.Edge.Presentation.Navigation`：新增 Avalonia 导航栏、导航宿主、Dashboard 首页与中英资源。
- `src/Edge/IIoT.Edge.Shell`：左侧区域接入 Navigation 导航栏，中央区域接入 Navigation Dashboard Host，右侧继续沿用 P2 Panels。
- `src/Presentation/IIoT.Edge.Presentation.Shell`：语言服务加载 Navigation 资源，页脚阶段文案更新到 Phase 3。
- `src/Shared/IIoT.Edge.UI.Shared`：补充导航按钮通用样式。

未改动业务链路：`src/Core`、`src/Application`、`src/Runtime`、`src/Infrastructure`、`src/Modules`、`src/Edge/IIoT.Edge.Host.Bootstrap`。

## 实现

- `AddNavigationPresentation()` 保持原方法名，在原 Navigation 项目内追加 P3 所需的 `NavigationRailViewModel`、`DashboardViewModel`、`NavigationRailView`、`NavigationHostView`、`DashboardView` 注册。
- 左侧 NavRail 显示真实 Navigation 导航项：首页可进入；数据、产能、IO、监控、配方、参数、硬件、绑定保留为后续阶段禁用状态，不加载旧 WPF 页面。
- Dashboard 只读展示真实来源的设备连接、今日产量、良率、不良数、配方状态。节拍与趋势当前没有稳定真实来源，显示明确空态文案。
- 右侧 Equipment / Log 面板继续使用 P2 的真实 Panels，不构造设备、日志、产量、良率、配方或趋势数据。
- 为适配 1366x768 验收窗口，在不改变布局职责的前提下降低 Shell 窗口最小高度，内容区域依靠滚动承载。

## 验证

已执行：

```powershell
dotnet restore src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

结果：构建成功，0 警告，0 错误。

已执行红线扫描：

```powershell
rg "IIoT\.Edge\.Presentation\.Navigation\.Avalonia|IIoT\.Edge\.UI\.Avalonia|\.Legacy|AddEdgeHostAvaloniaBootstrap|AvaloniaShellStartupCoordinator|AvaloniaShellBootstrapOptionsFactory|IAvalonia[A-Z]" src
rg "CommunityToolkit\.Mvvm|ReactiveUI|Prism\.|Dock\.Avalonia|Material\.Avalonia|Material\.Icons\.Avalonia|DialogHost\.Avalonia|LucideAvalonia|Avalonia\.Headless\.XUnit|xunit\.v3" Directory.Packages.props src/Shared/IIoT.Edge.UI.Shared src/Presentation/IIoT.Edge.Presentation.Navigation
git diff 1aac64d -- src/Core src/Application src/Runtime src/Infrastructure src/Modules src/Edge/IIoT.Edge.Host.Bootstrap
```

结果：

- 黑名单依赖扫描为空。
- 冻结路径 diff 为空。
- 正式运行路径未新增并行项目或框架特定启动入口；`src/Tests` 存在既有历史测试字符串，不属于 P3 改动范围。

## 运行验收

- 正常 `Modules/Homogenization` 存在时，Shell 启动成功，左侧显示 Navigation 导航项，中央显示 Avalonia Dashboard，右侧显示 P2 Equipment / Log。
- 空 `Modules/` 时显示启动失败窗口，包含 `PLUGIN_ROOT_MISSING` 与 `PLUGIN_NONE_ENABLED`，不进入主界面。
- 单实例验证：第一实例运行时启动第二实例，第二实例退出码为 `0`。

截图：

- `screenshots/phase3-1900x1200.png`
- `screenshots/phase3-1600x1000.png`
- `screenshots/phase3-1366x768.png`
- `screenshots/phase3-empty-modules.png`

## 后续边界

P4 继续在原 Navigation 项目内迁移具体业务页面：DataView、Capacity、IO、Monitor、Recipe、ParamConfig、HardwareConfig、PlcTaskBinding。全仓 WPF 残留清理由后续统一清理阶段处理。
