# Avalonia 12 第十一批迁移记录：PLC 写入轨迹只读展示收口

## 完成内容

- 新增 PLC I/O 块写入轨迹存储，用于记录扫描任务块写入的尝试、成功和失败。
- 在现有 PLC I/O 扫描任务块写入外围补诊断记录，不改变块规划、写入顺序、重试或连接策略。
- Avalonia I/O 页面增加“PLC 写入轨迹”展示：写入运行时缓冲后先显示“已进入运行时缓冲，等待扫描任务按块写入”，后续展示最近一次相关块写入结果。
- Equipment 面板新增“最近 PLC 块写入”只读状态。
- Diagnostics 页面新增“PLC 写入轨迹”只读页签，与 “I/O 写入闸门” 并列展示。
- 新增现场联调检查清单，固定 UI-only、`--start-runtime`、手动读取、申请缓冲写入、查看 PLC 写入轨迹和保存日志证据的流程。

## 改动边界

- 只修改 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本。
- 不修改原 `IIoT.EdgeClient` 主仓。
- 不修改 Cloud/MES API、数据库结构、业务配置 JSON、业务规则文档或 PLC 块规划策略。
- 不新增直接 PLC 单点写入；实际物理写入仍由现有扫描任务按块策略执行。
- 诊断页只读展示，不提供清理、重试、删除或强制写入按钮。

## 实现说明

- `IPlcIoWriteTraceStore` 放在 Application 抽象层，供 Runtime 记录和 Avalonia 读取。
- Runtime 内存实现只保存最近轨迹，不持久化、不参与补偿、不参与业务判断。
- `PlcIoScanTaskBase.WriteBufferToPlcAsync` 在每个块写入前记录尝试，成功后记录成功，异常时记录失败并继续按原语义抛出。
- Avalonia I/O 页通过信号 key 关联最近 PLC 块写入轨迹，不直接引用 PLC 服务。
- Equipment 和 Diagnostics 均从轨迹存储读取只读快照。

## 验证记录

- 已执行：`dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -c Release -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 42，失败 0，跳过 0。
  - 覆盖：发布候选输出 `publish/Release/avalonia-launcher`、`publish/Release/avalonia-shell`、匀浆 Avalonia 插件输出、Launcher UI-only / `--start-runtime` profile、AvaloniaShell 运行目录模板、I/O 写入闸门与 PLC 写入轨迹只读展示。
- 已执行：`dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 警告，0 错误。
- 已执行：`dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 42，失败 0，跳过 0。
- 已执行：Avalonia Navigation/Panels 边界扫描未发现直接 `ReadDataAsync` / `WriteDataAsync`。
- 已执行：I/O、Equipment、Diagnostics 目标视图扫描未发现新增清理、重试、删除或强制写入按钮。
- 已执行：`dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 警告，0 错误。
- 已执行：`dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过，0 警告，0 错误。
- 已执行：`dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 25，失败 0，跳过 0。
- 已执行：`dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 28，失败 0，跳过 0。
- 已执行：`dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 373，失败 0，跳过 0。
- 已执行：`dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 结果：通过 71，失败 0，跳过 0；仅存在测试假实现事件未使用的既有警告。
- 已执行：Avalonia 项目边界扫描未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、SukiUI。
- 已执行：源码和文档乱码扫描未发现新增乱码。
- 已执行：`dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive`
  - 结果：preview 依赖仅包含已批准的 `SkiaSharp 3.119.4-preview.1.1` 系列。
- 已执行：`dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive`
  - 结果：未发现易受攻击的包。

## 剩余风险

- 第十一批只建立现场联调证据链，不自动判断 PLC 物理侧最终状态。
- 任何 PLC 写入失败只展示和记录，不自动清理缓冲、不自动重试。
- Cloud/MES 操作闭环仍未进入 Avalonia 主线，后续单独规划。

## 下一阶段进入条件

- 本批构建、测试、边界扫描、依赖漏洞扫描通过。
- 现场联调包预检通过，确认 Launcher、Shell、插件和运行目录模板齐全。
- 由人工确认是否进入 Cloud/MES 只读操作闭环或现场真实写入联调批次。
