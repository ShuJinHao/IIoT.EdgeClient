# Avalonia 12 PoC 阶段记录

## 记录信息

- 日期：2026-05-13
- 范围：仅 `IIoT.EdgeClient`
- 决策：采用 Avalonia 12 最新稳定线，允许当前稳定包传递带出的 `SkiaSharp` preview 依赖例外。
- 例外文档：`docs/NuGet预览传递依赖例外记录.md`
- 隔离状态：原 `IIoT.EdgeClient` 已清除 Avalonia PoC 代码，PoC 代码位于旁路副本 `..\IIoT.EdgeClient.AvaloniaMigration`。

## 已实现内容

- 旁路副本中新增独立 PoC 项目：`src/Edge/IIoT.Edge.AvaloniaPoc`
- 目标框架：`net10.0`
- UI 栈：`Avalonia 12.0.3`、`SukiUI 6.1.0`、`SukiUI.Dock 6.1.0`、`Dock.Avalonia 12.0.0.2`、`Avalonia.Controls.DataGrid 12.0.0`、`DialogHost.Avalonia 0.12.2`、`CommunityToolkit.Mvvm 8.4.2`
- 已在旁路副本中接入中央包版本：`Directory.Packages.props`
- 已在旁路副本中加入解决方案：`IIoT.EdgeClient.slnx`
- PoC 不引用现有 WPF UI 项目；当前只引用 `IIoT.Edge.Application` 和 `IIoT.Edge.SharedKernel`
- 已实现 Avalonia `MainWindow`、Dock 主区域、设备面板、日志面板、确认弹窗、Monitor 代表视图、IO `DataGridTemplateColumn` 代表视图、Equipment 代表视图
- 已实现 Avalonia 版语言服务和 DataGrid 列头动态本地化验证
- 已实现 Dispatcher 抽象接口和 Avalonia 实现

## 依赖图检查

顶层包均为稳定版本。

完整依赖图中当前仅发现以下 preview/prerelease 包：

- `SkiaSharp/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`

这些依赖符合已批准的 `SkiaSharp` preview 传递依赖例外范围。

## 已执行验证

```powershell
rg -n "IIoT.Edge.AvaloniaPoc|Avalonia|SukiUI|Dock\.Avalonia|DialogHost\.Avalonia|Material\.Avalonia|Material\.Icons\.Avalonia|CommunityToolkit\.Mvvm" Directory.Packages.props IIoT.EdgeClient.slnx src docs
dotnet restore IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaPoc/IIoT.Edge.AvaloniaPoc.csproj
dotnet build IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaPoc/IIoT.Edge.AvaloniaPoc.csproj --no-restore
dotnet list IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaPoc/IIoT.Edge.AvaloniaPoc.csproj package --include-transitive
dotnet list IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaPoc/IIoT.Edge.AvaloniaPoc.csproj package --vulnerable --include-transitive
dotnet build IIoT.EdgeClient/src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj
dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj
dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj
```

验证结果：

- 原仓 `src/Edge/IIoT.Edge.AvaloniaPoc`：已移除
- 原仓 `IIoT.EdgeClient.slnx`：已移除 PoC 项目引用
- 原仓 `Directory.Packages.props`：已移除仅供 Avalonia PoC 使用的包版本
- 原仓 Avalonia 相关文本：仅保留在 `docs/` 计划和例外记录中
- 旁路副本 `..\IIoT.EdgeClient.AvaloniaMigration`：已初始化本地 git 仓库，未配置 remote
- `dotnet restore`：通过
- `dotnet build`：通过，0 警告，0 错误
- 现有 WPF Shell 构建：通过，0 警告，0 错误
- 模块契约测试：28 个通过，0 失败
- Edge 非 UI 回归测试：367 个通过，0 失败
- Shell 测试：71 个通过，0 失败；保留既有测试桩事件未使用警告
- preview/prerelease 依赖：仅 `SkiaSharp` 及 `SkiaSharp.NativeAssets.*`
- 漏洞检查：未发现易受攻击包

## 尚未完成

- 尚未在 Windows 工控机执行触摸、高 DPI、多屏手工验证。
- 尚未接入 Headless UI 测试项目。
- 尚未迁移生产 Shell；当前仅为旁路副本中的独立 PoC。
- 原 WPF 主线未移除任何现有 WPF 生产项目引用或 WPF 包。
