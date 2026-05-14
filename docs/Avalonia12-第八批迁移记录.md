# Avalonia 12 第八批迁移记录

## 范围

- 继续只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本实施。
- 本批把 Avalonia 客户端从“服务驱动页面”推进到“可控运行联调入口”。
- 默认启动仍为 UI-only；只有显式传入 `--start-runtime` 才启动 `IAppLifecycleCoordinator`。
- 不做真实 PLC 写入，不做 Cloud/MES 清理或重试，不修改数据库结构、Cloud/MES API、PLC 策略或业务规则文档。

## 已完成

- 新增 AvaloniaShell 启动协调器：
  - 支持默认 UI-only 启动。
  - 支持 `--start-runtime` 显式启动运行链路。
  - 退出时按 8 秒超时调用生命周期停机。
  - 启动失败显示 Avalonia 启动错误窗口并关闭。
- 将 UI 无关生命周期能力从 WPF Bootstrap 的 `Core` 文件夹迁入 `IIoT.Edge.Host.Bootstrap.Core`：
  - `IAppLifecycleCoordinator`、`AppLifecycleManager`、启动诊断、诊断存储、插件 catalog 基础、运行时状态恢复/保存等进入 Core。
  - WPF Shell 继续构建通过，Avalonia 不引用 WPF Bootstrap。
- Launcher profile 支持可选 `Arguments`：
  - 保留 UI-only 迁移验证入口。
  - 新增运行联调入口，启动 `IIoT.Edge.AvaloniaShell.exe --start-runtime`。
- HardwareConfig / IOView 收口到真实配置形状：
  - HardwareConfig 使用 `IHardwareConfigCrudService` 加载与保存网络设备、串口设备、I/O 映射和候选信号。
  - IOView 使用真实配置形状加载设备和映射。
  - 生命周期未启动时手动读取给出提示；生命周期启动后只读取运行时快照，不直接逐点访问 PLC。
  - 写入入口继续禁用真实写入。
- Panels / Export / Diagnostics 小闭环：
  - Equipment 面板读取设备、上传闸门和诊断快照。
  - Log 面板读取迁移运行目录日志和启动诊断摘要。
  - DataView / Capacity 导出 UTF-8 CSV 到 `runtimePaths.ExcelDirectory`。
  - Diagnostics 继续只读刷新和详情展示，不加入清理/重试/删除操作。
- 修复 Avalonia Navigation 与匀浆 Avalonia 资源乱码，中文资源恢复为可读 UTF-8。

## 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false --no-restore`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false --no-restore`

## 边界检查

- AvaloniaShell、Avalonia Bootstrap、Avalonia Presentation、Avalonia UI Shared、Launcher.Avalonia、Homogenization.Avalonia 未发现 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared`、`SukiUI`。
- `Host.Bootstrap.Core` 与 `Launcher.Core` 未发现 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared`、`Avalonia`、`SukiUI`。
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive` 未发现易受攻击包。
- 传递 preview 依赖仍仅为已批准的 `SkiaSharp 3.119.4-preview.1.1` 系列。

## 剩余风险

- `--start-runtime` 只是开发机联调入口，尚未做现场 PLC 联调。
- 写入 PLC、Cloud/MES 清理与重试、导出模板化 Excel、现场性能与多屏触摸验收仍留到后续批次。
- AvaloniaShell 启动协调器测试存在 xUnit1051 提醒，属于测试取消令牌响应建议，不影响当前结果。
