# Avalonia 12 第一批迁移记录

## 记录信息

- 日期：2026-05-13
- 仓库：`IIoT.EdgeClient.AvaloniaMigration`
- 范围：仅旁路副本，不回写原 `IIoT.EdgeClient`
- 基线提交：`63946f7 baseline: isolate Avalonia migration copy`
- 目标：把原 PoC 收敛为可持续迁移的 Avalonia Shell 基座

## 本批完成内容

- 将 `src/Edge/IIoT.Edge.AvaloniaPoc` 改为 `src/Edge/IIoT.Edge.AvaloniaShell`
- 新增 `src/Shared/IIoT.Edge.UI.Avalonia`
- 新增 `src/Tests/IIoT.Edge.AvaloniaShell.Tests`
- 建立 Avalonia UI 注册、导航、Dialog、Dispatcher、语言服务、DataGrid 列头本地化基础设施
- Shell 接入 SukiUI、Dock、Header、Footer、SysMenu、确认弹窗、Dock 主区、Equipment/Log 面板
- 保留 Monitor 和 IO 代表页，用于验证 DataGrid、模板列、状态色和语言切换
- AvaloniaShell 只引用 `IIoT.Edge.Application`、`IIoT.Edge.SharedKernel`、`IIoT.Edge.UI.Avalonia`
- 未引用 `IIoT.Edge.UI.Shared`、`IIoT.Edge.Presentation.*` 或 `UseWPF` 项目

## 依赖结论

- 顶层 Avalonia/SukiUI/Dock/DialogHost/Material Icons 包均为稳定版
- 漏洞检查无告警
- preview 传递依赖仅出现已批准的 `SkiaSharp` 系列：
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
rg -n "IIoT\.Edge\.UI\.Shared|IIoT\.Edge\.Presentation|UseWPF|System\.Windows" src/Edge/IIoT.Edge.AvaloniaShell src/Shared/IIoT.Edge.UI.Avalonia src/Tests/IIoT.Edge.AvaloniaShell.Tests
```

- AvaloniaShell build：通过，0 警告，0 错误
- WPF Shell build：通过，0 警告，0 错误
- AvaloniaShell Headless 测试：通过 5
- Module Contract Tests：通过 28
- NonUiRegressionTests：通过 367
- Shell Tests：通过 71，保留既有测试中的未使用事件警告
- WPF 引用边界检查：无匹配结果

## 未覆盖内容

- 尚未执行真实窗口手工验证
- 尚未验证 Windows 工控机触摸、高 DPI、多显示器
- 尚未连接真实 PLC
- 尚未触发真实 Cloud/MES 上传
- 尚未迁移 38 个 XAML 的全量页面
