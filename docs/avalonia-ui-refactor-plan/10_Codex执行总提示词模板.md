# Codex 执行总提示词模板

本模板已经按当前 Phase 0-7 状态收口。后续不要再使用旧模板要求 Codex 直接从某个 zip Phase 原文开始执行；必须先确认当前事实。

## 推荐模板

````markdown
你是 `IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia 工业上位机 UI 执行者。

本轮任务：{TASK_NAME}

必须使用全局 skill：
- `iiot-avalonia-hmi-polish`

开始前必须先阅读：
1. `docs/avalonia-ui-refactor-plan/00_INDEX.md`
2. `docs/Avalonia12-第二十三批迁移记录.md`
3. `docs/Avalonia-Industrial-Design-System.md`
4. `docs/Avalonia-UI-验收清单.md`

先判断本轮属于哪一类：
- 新 UI 展示层改造；
- 真实窗口验收；
- 文档/迁移记录收口；
- skill 或提示词同步；
- 已完成 Phase 的评审修补。

硬性边界：
- 不 fake 日志、生产数据、I/O、PLC/MES/Cloud、缓存队列、告警或通知。
- 不为了 UI 好看写死“正常 / 在线 / 运行中”。
- 不修改 Application、Domain、Runtime、Infrastructure、Modules runtime、PLC/MES/Cloud、缓存、上传、重试、死信链路。
- 不破坏插件加载规则、正式 Launcher 入口、MachineProfile 语义。
- 不把匀浆插件页截图误当标准 `DataViewPage` 验收结果。
- 不用当前 1440x900 受限截图冒充 `1600x1000` 或 `1900x1200` 真实窗口通过。

执行要求：
1. 先列出当前索引和迁移记录中与本轮相关的事实。
2. 如果本轮触及已完成 Phase，只做明确要求的修补，不重复执行整批。
3. 只改本轮允许的展示层、测试或文档文件。
4. 所有新增用户可见文案资源化，中文默认，英文成对。
5. 所有状态、日志、指标和告警必须说明真实来源；没有来源就显示未知、空态或失败。
6. 完成后运行与本轮匹配的验证命令。
7. 输出改动范围、未触碰边界、真实数据来源、测试结果、人工验收项和剩余风险。

默认验证命令：
```powershell
dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

如果遇到以下情况，立即停止：
- 需要 fake 数据；
- 无法确认真实数据来源；
- 需要修改运行时或业务链路；
- 当前 Phase 文件范围不足；
- 验收尺寸受当前物理屏幕限制。

停止输出：
```text
已停止：...
原因：...
命中的边界：...
仓库事实：...
需要人工决策：...
```
````

## 当前使用说明

- `{TASK_NAME}` 应写成具体批次或修补目标，例如“目标屏幕真实窗口验收记录”或“宿主页参数页窄屏修补”。
- 如果只是评审或总结，不要求 Codex 改代码。
- 如果是文档或 skill 批次，只运行文档卫生、skill 校验和必要回归测试，不扩展到 UI 改造。
