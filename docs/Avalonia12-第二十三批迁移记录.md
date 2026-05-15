# Avalonia 12 第二十三批迁移记录

## 目标

本批把迁移副本收敛为 Avalonia-only 工作区，避免后续查看、开发和审核时继续混看两套 UI。

## 调整结果

- 移除旧桌面壳、旧启动器、旧 Presentation、旧共享 UI、旧匀浆插件壳和旧桌面壳测试。
- 解决方案只保留 Avalonia Shell、Avalonia Launcher、Avalonia Bootstrap、UI.Avalonia、业务 Core、Runtime、Infrastructure、Application、SharedKernel、匀浆 Core/Avalonia 插件和仍有价值的测试项目。
- 迁移副本中的匀浆插件只保留 Core 与 Avalonia 插件壳，插件页面继续按模块方式注册。
- Launcher Avalonia 使用自己的 profile 图片资源，不再从旧启动器目录取资源。
- 测试改为验证 Avalonia Bootstrap、Avalonia 视图注册和 Avalonia 匀浆插件契约。
- 清理旧 UI 专用包版本与审核、现场证据、默认入口评审相关脚本和文档。
- 修正 Bootstrap Core 的历史命名空间，避免被误判为旧桌面壳代码。

## 验证

- `dotnet build IIoT.EdgeClient.slnx -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`

## 边界

- 未修改 CloudPlatform、AICopilot、Cloud/MES API、数据库结构、PLC 块规划策略或业务规则文档。
- 原始 WPF 仓库仍作为对照和回退来源；迁移副本不再保存旧 UI 项目。
