# Phase 3：日志与设备状态真实化小计划

> 日志和设备状态是上位机核心能力。
> 本阶段只允许把真实来源展示清楚，不允许制造“看起来在运行”的假内容。

---

## 1. 阶段目标

1. 运行日志默认常驻。
2. 日志只来自真实 `ILogService.EntryAdded`、`ILogDisplayService.Entries` 或真实日志文件。
3. 启动诊断和运行日志必须分离展示。
4. 设备状态只来自真实设备、诊断、I/O、Cloud/MES 状态。
5. UI 不再把所有状态都画成 success。

---

## 2. 允许修改的文件类别

允许修改：

- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/ViewModels/LogViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/LogView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/ViewModels/EquipmentViewModel.cs`，仅限展示投影/状态分类
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/EquipmentView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Resources/Languages/*.xaml`
- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/**`
- `src/Tests/IIoT.Edge.AvaloniaShell.Tests/**`

---

## 3. 禁止修改的文件类别

禁止修改：

- `src/Runtime/**`
- `src/Infrastructure/**`
- `src/Application/**`，除非只为已有抽象补资源化文案且单独说明
- `src/Modules/**/Runtime/**`
- `src/Modules/**/Integration/**`
- PLC/MES/Cloud/上传/重试/死信业务
- 日志写入业务规则
- 数据库结构

---

## 4. 日志具体要求

### 4.1 日志来源

允许来源：

- `ILogService.EntryAdded`
- `ILogDisplayService.Entries`
- 真实 `.log` 文件

禁止来源：

- 写死数组
- UI 初始化时生成假日志
- 用启动诊断摘要伪装日志行
- 用当前时间伪造日志文件中每一行的时间

### 4.2 文件日志时间处理

如果日志文件行能解析出时间：

- 使用文件行中的真实时间。

如果解析不出时间：

- 显示“时间未知”或不显示时间。
- 可以显示文件名/行来源。
- 禁止使用 `DateTime.Now` 让旧日志看起来像刚发生。

### 4.3 空态处理

没有日志时显示：

```text
当前未收到运行日志。请确认运行链路是否已启动，或查看启动诊断。
```

空态必须和日志行视觉区分，不进入日志列表。

### 4.4 启动诊断处理

启动诊断可以作为独立卡片显示：

- 模块数
- PLC 设备数
- 阻断问题数
- 诊断文件位置

但标题必须叫“启动诊断”，不能叫“运行日志”。

### 4.5 日志操作

刷新：重新读取缓存和日志文件。
清空：只清当前 UI 展示缓存，不删除真实日志文件。
删除真实日志文件：本阶段禁止。

---

## 5. 设备状态具体要求

### 5.1 状态来源

允许来源：

- `IEquipmentPanelService.GetHardwareStatusAsync()`
- `IDeviceService.CurrentDevice`
- `IDeviceService.CurrentUploadGate`
- `IEdgeSyncDiagnosticsQuery.GetCurrentAsync()`
- `IAvaloniaRuntimeState`
- `IPlcIoWriteTraceStore`

禁止来源：

- 写死“正常”
- 写死“在线”
- 所有 chip 都 success
- fake PLC 连接数
- fake Cloud/MES 成功率

### 5.2 状态分类

至少区分：

- 正常 / success
- 注意 / warning
- 异常 / error
- 未启动 / neutral
- UI-only 开发模式 / development
- 未知 / muted

示例规则：

- Runtime 未启动：neutral，不是 success。
- PLC 未连接：warning 或 error，不能 success。
- MES 有死信：warning 或 error。
- Cloud 上传闸门关闭：warning。
- 最近 PLC 写入失败：error。

### 5.3 右侧面板可解释性

右侧设备面板必须能回答：

- 当前运行链路是否启动？
- 当前设备是否寻址？
- Cloud/MES 是否有积压或死信？
- PLC 是否连接？
- I/O 写入闸门为什么允许或禁止？
- 最近 PLC 写入是否成功？

---

## 6. UI 细节要求

日志：

- 列表密度适中。
- INFO/WARN/ERROR/FATAL 视觉清楚。
- 长日志自动换行或截断，不撑破右侧面板。
- 支持滚动。
- 最新日志在上方。

设备状态：

- 卡片不要都长一样。
- chip 颜色跟真实状态一致。
- 说明文案短而明确。
- 卡片之间留白，不用硬分隔线。

---

## 7. 测试要求

必须覆盖：

1. `LogViewModel` 收到 `EntryAdded` 后追加日志。
2. 没有日志时 `IsLogEmpty = true`，显示空态。
3. 启动诊断摘要不进入日志 Entries。
4. 文件日志无法解析时间时不使用当前时间伪装。
5. Equipment 状态分类不是所有项 success。
6. Runtime 未启动时 I/O 写入闸门显示禁止。
7. PLC 未连接时状态不显示 success。

---

## 8. 验收标准

本阶段通过条件：

- 日志默认可见。
- 日志真实来源清楚。
- 空态真实。
- 启动诊断和运行日志分离。
- 设备状态能解释当前运行情况。
- 不出现 fake 正常状态。
- 不改业务链路。

---

## 9. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 10. 停止条件

必须停止：

- 需要新增 fake 日志。
- 需要改 Runtime 才能显示设备状态。
- 无法确认 Cloud/MES 状态来源。
- 无法确认日志文件格式且准备伪造时间。
- 需要直接访问 Infrastructure 实现。
