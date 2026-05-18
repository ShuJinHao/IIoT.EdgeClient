# SKILL：Avalonia 工业上位机前端规则

本规则已经同步为全局 Codex skill：

- `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\SKILL.md`
- `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\references\phase-status.md`
- `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\references\design-system.md`
- `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\references\visual-acceptance.md`

后续凡是在 `IIoT.EdgeClient.AvaloniaMigration` 中修改 Avalonia Launcher、Shell、Presentation、主题、资源、日志、设备状态或视觉验收，都应使用该专用 skill。不要使用 `iiot-frontend-polish` 处理 Edge Avalonia；该 skill 继续只服务 Cloud/Vue 和 AICopilot。

## 当前执行口径

- Phase 0-7 已进入可评审状态，不能默认从 zip 原 Phase 计划重新执行。
- 每轮先读 `00_INDEX.md` 和 `docs/Avalonia12-第二十三批迁移记录.md`，确认当前事实，再决定是否需要代码、文档、验收或记录收口。
- `1600x1000` 和 `1900x1200` 真实窗口完整验收仍依赖目标屏幕或工控机，不能用当前 1440x900 受限截图冒充通过。
- `HomogenizationDataPage` 会在匀浆正式路由中覆盖标准 `DataViewPage`，标准宿主页验收必须明确是直接承载还是正式路由。

## 硬性边界

禁止 fake：

- 日志、日志时间、告警、通知；
- 产量、良率、节拍、NG；
- PLC 在线、I/O、MES 上传、Cloud 同步；
- 缓存队列、状态卡、正常/在线/运行中文案。

未单独批准时禁止修改：

- Application、Domain、Runtime、Infrastructure；
- Modules runtime、PLC/MES/Cloud 集成；
- 缓存、上传、重试、死信链路；
- 插件加载规则、模块注册、MachineProfile 语义。

如果 UI 需求必须触碰以上边界，停止并输出影响范围，不要偷扩实现。

## 设计系统口径

- `Edge.*` 是主客户端 canonical token。
- `Ind.*` 只作为兼容层保留，不新增。
- Launcher 可以保留 `Launcher.*`，但状态语义必须与 `Edge.*` 对齐。
- 优先复用 card、KPI、状态卡、表格、日志、chip、空态、表单、右侧面板等共享 class。
- 非主题 XAML 不新增十六进制颜色。
- 用户可见文案必须进入所属项目或模块的 `zh-CN.xaml` / `en-US.xaml`，中文为默认语言。

## 验收口径

默认验证命令：

```powershell
dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

文档或 skill 批次至少要做乱码片段扫描、`git diff --check` 和相关测试项目回归。

## 停止条件

遇到以下情况必须停止：

- 需要 fake 数据；
- 无法确认真实数据来源；
- 需要改业务链路或运行时链路；
- 当前 Phase 状态与索引或迁移记录冲突；
- 目标窗口尺寸无法在当前屏幕真实采集。

停止时输出：请求内容、命中的边界、可能影响的层或文件、需要人工决策的问题。
