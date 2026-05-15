# Avalonia 12 Shell 阶段记录

## 记录信息

- 日期：2026-05-13
- 范围：仅 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本
- 决策：采用 Avalonia 12 最新稳定线，允许当前稳定包传递带出的 `SkiaSharp` preview 依赖例外
- 例外文档：`docs/NuGet预览传递依赖例外记录.md`
- 隔离状态：原 `IIoT.EdgeClient` 不承载 Avalonia 代码，迁移代码只在旁路副本中继续

## 已实现内容

- 旁路副本中已将原 `IIoT.Edge.AvaloniaPoc` 收敛为 `src/Edge/IIoT.Edge.AvaloniaShell`
- 新增 Avalonia 专用共享层 `src/Shared/IIoT.Edge.UI.Avalonia`
- 新增 Headless 测试项目 `src/Tests/IIoT.Edge.AvaloniaShell.Tests`
- UI 栈：`Avalonia 12.0.3`、`SukiUI 6.1.0`、`SukiUI.Dock 6.1.0`、`Dock.Avalonia 12.0.0.2`、`Avalonia.Controls.DataGrid 12.0.0`、`DialogHost.Avalonia 0.12.2`、`CommunityToolkit.Mvvm 8.4.2`
- 已实现 Avalonia `MainWindow`、Dock 主区域、设备面板、日志面板、确认弹窗、Monitor 代表视图、IO `DataGridTemplateColumn` 代表视图、Equipment 代表视图
- 已实现 Avalonia 版语言服务、DataGrid 列头动态本地化、Dispatcher 服务、Dialog 服务、导航注册模型
- AvaloniaShell 不引用现有 WPF UI 项目，只引用 `IIoT.Edge.Application`、`IIoT.Edge.SharedKernel`、`IIoT.Edge.UI.Avalonia`

## 依赖检查

只允许下列已批准的 preview 传递依赖：

- `SkiaSharp/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`

漏洞检查结果：未发现易受攻击包。

## 验证结果

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj`：通过，0 警告，0 错误
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj`：通过，0 警告，0 错误
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj`：通过 5
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj`：通过 28
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj`：通过 367
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj`：通过 71，保留既有未使用事件警告
- WPF 引用边界检查：AvaloniaShell、Avalonia 共享层、AvaloniaShell 测试中未发现 `IIoT.Edge.UI.Shared`、`IIoT.Edge.Presentation`、`UseWPF`、`System.Windows`

## 尚未完成

- 尚未执行真实窗口手工验证
- 尚未验证 Windows 工控机触摸、高 DPI、多显示器
- 尚未连接真实 PLC
- 尚未触发真实 Cloud/MES 上传
- 尚未迁移 38 个 XAML 的全量页面
