# Avalonia 12 第十批迁移记录：受控 PLC 写入闸门 + I/O 操作闭环

## 范围

- 继续只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本实施。
- 默认启动仍是 UI-only；只有显式传入 `--start-runtime` 才允许 I/O 页面申请写入运行时缓冲。
- 本批不直接写 PLC，不绕过 PLC 块规划器，不做 Cloud/MES 清理、重试、删除，不改数据库结构、Cloud/MES API、PLC 策略或业务规则。

## 本批完成项

- Avalonia I/O 生产注册从 no-op 写入端口切换为 `RuntimeBufferIoViewSafeInteractionPort`。
- 写入申请增加完整闸门：运行链路已启动、用户具备硬件配置权限、设备已绑定、PLC 已连接、交互行存在 HostSignals、写入值在 `0..65535`、用户确认。
- 写入只调用 `IPlcDataStore` 的运行时缓冲，兼容 SignalKey 和 StartIndex 两条缓冲写入入口；未调用 PLC 直接写入 API。
- I/O 页面新增写入闸门状态、最近写入值和最近写入结果展示；成功文案明确“写入运行时缓冲，实际 PLC 写入由运行链路按块策略处理”。
- 新增 I/O 写入闸门内存审计存储，记录接受和拒绝结果，供 Diagnostics 只读展示。
- Equipment 面板新增 I/O 写入闸门状态行，区分 UI-only、无权限、PLC 未连接和可申请写入。
- Diagnostics 增加 “I/O 写入闸门” 只读页签，不提供清理、重试、删除或强制写入按钮。
- Log 面板继续通过现有日志服务接收写入申请日志，不写业务数据库。

## 验证重点

- UI-only 默认启动下，写入被拒绝且不访问运行时缓冲。
- 无硬件配置权限时，写入被拒绝且不弹确认。
- 用户取消确认时，不写入运行时缓冲。
- 条件满足并确认后，只写入运行时缓冲，不调用 PLC 直接写入 API。
- Avalonia 项目边界扫描未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared` 或 SukiUI 残留。
- I/O Avalonia 代码扫描未发现 `ReadDataAsync`、`WriteDataAsync` 或逐点 PLC 读写实现。
- AvaloniaShell 依赖图中 preview/prerelease 仍仅为已批准的 SkiaSharp 系列；漏洞扫描无未处理告警。

## 已执行验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive`
- `dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive`

## 未纳入本批

- 真实现场 PLC 写入成功率验证。
- PLC 块规划策略调整。
- Cloud/MES 清理、重试、删除或补偿链路调整。
- WPF 主线切换或清理。
