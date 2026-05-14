# Avalonia 12 第十九批迁移记录

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不自动切默认入口，不新增业务功能，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- WPF Launcher/WPF Shell 继续作为生产回退线。

## 本批完成项

- `SwitchAvaloniaDefaultEntry.ps1` 增加受控 `-Apply` 模式；默认仍为 Preview。
- `-Apply` 仅在 readiness 达到 `ApprovedForDefaultEntrySwitch` 且签字字段完整时允许执行。
- `-Apply` 只修改发布包内 `avalonia-launcher/launcher.profiles.json` 默认入口元数据，并生成 `rollback-snapshot/`。
- 新增 `RestoreAvaloniaDefaultEntry.ps1`，从 snapshot 恢复发布包内 Launcher profile。
- 发布包、FullGate、Diagnostics 和文档同步补充真实切换与回退说明。

## 判定口径

- `P1Pending`、缺人工签字、缺 WPF 回退验证时，`-Apply` 必须失败且不能修改 profile。
- `-Preview` 必须只生成报告，不修改 profile。
- `-Apply` 成功后必须生成 apply summary 和 rollback snapshot。
- 回退脚本只能恢复发布包内 profile，不删除发布包、不清理日志、不触碰 Cloud/MES/PLC。

## 验证记录

- 已通过第十九批脚本、发布布局和 Diagnostics 相关测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaDefaultEntryReadinessScriptTests|AvaloniaFieldPackageScriptTests|AvaloniaSwitchReadinessTests|AvaloniaPublishLayoutPreflightTests|ProductionViewModelBehaviorTests" -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

- 结果：38 条通过，0 失败。

- 已通过完整 AvaloniaShell 测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

- 结果：77 条通过，0 失败。

- 已通过完整候选包门禁：

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate
```

- 结果：通过；临时证据包复审仍保持 `P1Pending`，未误判为可切默认入口。

- 已完成边界扫描：
  - Avalonia 项目无 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI`。
  - Avalonia I/O 页无直接 `ReadDataAsync` / `WriteDataAsync`。
  - 相关脚本无数据库删除、Cloud/MES 清理/重试/删除、PLC 直接读写或 `Remove-Item`。
  - 本批新增和修改文本未发现常见中文乱码标记。
