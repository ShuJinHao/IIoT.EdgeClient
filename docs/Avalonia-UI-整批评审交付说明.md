# Avalonia UI 整批评审交付说明

## 交付目标

本批将 Avalonia 迁移 UI 的 Phase 0-7、zip 计划落地、设计系统固化和全局 skill 同步整理为可评审状态。评审重点不是继续扩大 UI，而是确认当前未提交 diff 是否都属于 `IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia 迁移范围，并确认真实数据底线没有被破坏。

## 改动分类

- Launcher：正式入口收口为现场单入口，保留真实运行启动语义，不恢复 UI-only 正式入口。
- Shell / Monitor：默认进入 Monitor，Header/Footer、右侧状态区、右侧日志常驻区按现有真实状态派生。
- 日志 / 状态：文件日志时间不回退当前时间；PLC/MES/Cloud/缓存状态卡和错误日志只来自现有真实来源。
- 宿主页：五个标准宿主页完成浅色工业上位机风格细腻化，并通过直接承载 helper 做 `1366x768` 验收。
- 主题 / 设计系统：`Edge.*` 作为主客户端 canonical token，`Ind.*` 作为兼容层保留，Launcher 状态语义与主客户端对齐。
- 测试：补充 Launcher、Shell、资源卫生、布局 smoke、宿主页直接承载和 Phase 7 设计系统约束。
- 文档：zip 计划 UTF-8 落地，新增阶段索引、设计系统文档、验收清单和本交付说明。
- 全局 skill：新增 `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\`，用于后续 Edge Avalonia HMI 任务。

## 未触碰边界

- 未修改 Cloud、旧 WPF EdgeClient、AICopilot。
- 未修改 Application、Domain、Runtime、Infrastructure。
- 未修改 Modules runtime、PLC/MES/Cloud 集成、缓存、上传、重试、死信链路。
- 未新增 Cloud/API/Runtime/PLC/MES 公共接口。
- 未修改 `C:\Users\jinha\.codex\skills\iiot-frontend-polish\`。

## 真实数据底线

- 不 fake 日志、日志时间、告警、通知。
- 不 fake 产量、良率、节拍、NG。
- 不 fake PLC 在线、I/O、MES 上传、Cloud 同步或缓存队列。
- 没有真实来源的状态必须显示未知、空态、停止或失败，不能显示正常。
- 匀浆插件正式路由会覆盖标准 `DataViewPage`；标准宿主页截图必须明确为直接承载验收。

## 截图资产

截图目录：`%TEMP%\iiot-avalonia-visual-check\`

已确认 `1366x768` 文件存在、尺寸正确且非空：

- `launcher-1366x768.png`
- `shell-runtime-1366x768-monitor.png`
- `shell-host-pages-1366x768-printwindow.png`
- `host-page-direct-1366x768-capacity.png`
- `host-page-direct-1366x768-data.png`
- `host-page-direct-1366x768-recipe.png`
- `host-page-direct-1366x768-param.png`
- `host-page-direct-1366x768-plc-task-binding.png`

受当前本机 `1440x900` 屏幕限制，以下文件实际输出约 `1455x915`，只能作为受限截图记录，不能标记为完整通过：

- `launcher-1600x1000.png`
- `launcher-1900x1200.png`
- `shell-runtime-1600x1000.png`
- `shell-runtime-1900x1200.png`

## 封版验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，65 passed，0 failed。
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- `python C:\Users\jinha\.codex\skills\.system\skill-creator\scripts\quick_validate.py C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish`：通过，`Skill is valid!`。
- 文档/skill 乱码片段扫描：通过，未命中本轮关注的常见乱码片段。
- `git diff --check`：通过，仅有工作区 LF/CRLF 提示，无空白错误。
- diff 范围检查：通过，未发现 Cloud、旧 WPF EdgeClient、AICopilot、Application、Domain、Runtime、Infrastructure、PLC/MES/Cloud 运行链路文件误入。

## 剩余人工验收

- `1600x1000` 和 `1900x1200` 必须在目标显示器或工控机上做完整真实窗口人工验收。
- Headless、PrintWindow 和 `1366x768` 截图能确认窗口可创建、关键区域存在、截图非空；中文细节、DPI 缩放、真实现场数据密度和长文本仍需人工看图确认。
