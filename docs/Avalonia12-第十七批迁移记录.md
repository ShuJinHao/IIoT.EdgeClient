# Avalonia 12 第十七批迁移记录

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不切默认入口，不新增业务功能，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- WPF Launcher/WPF Shell 继续作为回退线。

## 本批完成项

- 新增 `TestAvaloniaDefaultEntryReadiness.ps1`，统一检查默认入口切换评审材料。
- 新增 `SwitchAvaloniaDefaultEntry.ps1`，只生成默认入口切换 preview 报告，不修改 profile 或发布目录。
- 发布包携带默认入口切换评审说明、preview 报告模板和第十七批两个脚本。
- 切换阻断清单新增“未通过默认入口评审门禁”作为 P0。
- 切默认入口决策包模板补充人工签字字段：允许切换结论、决策人、决策时间、回退负责人。
- Diagnostics 增加“默认入口切换门禁”只读提示，不提供切换按钮。

## 判定口径

- `TestAvaloniaDefaultEntryReadiness.ps1` 只有在试运行复审为 `ReadyForDefaultEntryReview`、FullGate 通过、WPF 回退验证通过、P0/P1 清零且人工签字完整时，才输出 `ApprovedForDefaultEntrySwitch`。
- `SwitchAvaloniaDefaultEntry.ps1` 只接受 `ApprovedForDefaultEntrySwitch` 的 readiness summary，并且本批只输出 preview 报告。
- 即使 preview 通过，真实修改 Launcher 默认入口或生产发布链路仍必须另起独立批次并获得明确批准。

## 已验证

- 运行第十七批相关测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaDefaultEntryReadinessScriptTests|AvaloniaFieldPackageScriptTests|AvaloniaSwitchReadinessTests|AvaloniaPublishLayoutPreflightTests|ProductionViewModelBehaviorTests" -m:1 /p:UseSharedCompilation=false
```

- 结果：32 个测试通过。
- 运行完整 AvaloniaShell 测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false
```

- 结果：68 个测试通过。
- 运行完整候选门禁：

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate
```

- 结果：发布、证据预检、依赖/漏洞扫描、WPF 回退构建和全部回归测试通过；临时证据复审输出 `P1Pending`，符合“未提供真实现场验收记录和关键截图时不得进入默认入口评审”的口径。
- 已完成 Avalonia 边界扫描、I/O 直接 PLC 调用扫描和脚本危险操作扫描，未发现本批违规项。
