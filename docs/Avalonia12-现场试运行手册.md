# Avalonia 12 现场试运行手册

## 试运行目标

本手册用于现场人工验证 Avalonia 迁移客户端是否具备进入试运行的条件。试运行只验证 UI、只读诊断、运行时启动入口、I/O 运行时缓冲写入申请和证据采集闭环，不把 Avalonia 切成生产默认入口。

## 准备条件

- 使用 `publish\avalonia-migration\Release` 目录中的发布包。
- 保留 WPF Launcher 和 WPF Shell 可用，作为回退入口。
- 确认现场账号、设备配置、PLC 连接、Cloud/MES 配置由现有运行目录提供。
- 试运行前先执行 `scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback`，确认候选包和 WPF 回退线均通过。

## 步骤

1. 运行 UI-only 启动：
   `powershell -ExecutionPolicy Bypass -File .\scripts\StartAvaloniaTrialRun.ps1 -ReleaseRoot .\publish\avalonia-migration\Release -Target Shell`
2. 确认 Avalonia Shell 打开，Header/Footer 显示 UI-only 状态。
3. 检查窗口拖拽、最大化、Dock 停靠、菜单切换、语言切换、DataGrid 滚动和触摸点击。
4. 退出 UI-only，运行显式联调入口：
   `powershell -ExecutionPolicy Bypass -File .\scripts\StartAvaloniaTrialRun.ps1 -ReleaseRoot .\publish\avalonia-migration\Release -Target Shell -StartRuntime`
5. 在 Diagnostics 查看启动摘要、运行目录、I/O 写入申请、PLC 块写入轨迹、Cloud/MES 只读状态。
6. 在 IOView 执行只读快照读取，确认未启动、无快照、设备未绑定、PLC 未连接等状态提示清楚。
7. 在满足现场授权和运行链路已启动时，执行一次受控 I/O 缓冲写入申请，确认 UI 文案明确“进入运行时缓冲，等待扫描任务按块写入”。
8. 截图留存：Diagnostics 现场联调摘要、I/O 写入闸门、PLC 写入轨迹、IOView 目标行、Equipment 最近 PLC 块写入。
9. 执行证据采集：
   `powershell -ExecutionPolicy Bypass -File .\scripts\CollectAvaloniaFieldEvidence.ps1 -AvaloniaShellDirectory .\publish\avalonia-migration\Release\avalonia-shell -AvaloniaLauncherDirectory .\publish\avalonia-migration\Release\avalonia-launcher -CreateZip`
10. 回退验证：启动 WPF Launcher/WPF Shell，确认仍可打开生产入口。

## 禁止事项

- 不把 Avalonia Launcher 设置为生产默认入口。
- 不在 Diagnostics 执行 Cloud/MES 清理、重试、删除或补偿。
- 不直接调用 PLC 单点写入。
- 不修改数据库结构、配置 JSON、Cloud/MES API 或 PLC 块规划策略。
- 试运行失败时不清理运行目录，不删除证据包。

## 回退口径

- UI-only 启动失败：保存 Avalonia 日志和截图，回退 WPF Launcher/WPF Shell。
- `--start-runtime` 启动失败：保存启动失败详情和诊断日志，不继续 I/O 写入申请。
- I/O 写入轨迹失败：不自动重试、不清理缓冲，由现场人工结合 PLC 侧状态判断。
- Cloud/MES 状态异常：只记录，不在 Avalonia 内执行清理、重试、删除或补偿。
