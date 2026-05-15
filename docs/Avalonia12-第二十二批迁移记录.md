# Avalonia 12 第二十二批迁移记录

## 范围

- 只在 `IIoT.EdgeClient.AvaloniaMigration` 长期迁移副本内修改。
- 本批回到 Avalonia 代码功能收口，不新增审核记录、现场证据模板或评审流程。
- 未修改 Cloud/MES API、数据库结构、PLC 块规划策略、业务规则文档或原 `IIoT.EdgeClient`。

## 完成内容

- Diagnostics 接入现有 `IDeadLetterMaintenanceService`，补齐 Cloud/MES 死信列表、详情、重新入队和删除入口。
- 死信运维保持 Cloud/MES 两条补偿链路分离；重新入队和删除都需要 Avalonia 确认弹窗。
- 死信操作权限沿用本地管理员口径；非管理员只能查看，不能执行重新入队或删除。
- 新增 `IAvaloniaDataExportService`，统一 DataView 和 Capacity 的导出入口，当前实现复用已有 UTF-8 CSV 落盘能力。
- DataView/Capacity 导出失败、空数据和成功结果都返回中文反馈，不吞异常。
- 清理 Diagnostics 中过期的现场证据、试运行审查、默认入口门禁提示，保留当前真实功能文案。
- IOView 安全策略保持不变：只通过运行时缓冲链路，不直接调用 PLC 单点读写接口。

## 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`

## 边界检查

- Avalonia Shell、Launcher、Bootstrap、Presentation、UI Shared 和匀浆 Avalonia 插件未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI` 引用。
- Avalonia IOView 未发现直接 `ReadDataAsync` 或 `WriteDataAsync` 调用。
- 本批未新增脚本，既有发布脚本中的清理命令只作用于发布输出目录。
