# Avalonia 12 第十六批迁移记录

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不切默认入口，不新增业务功能，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- WPF Launcher/WPF Shell 继续作为回退线。

## 本批完成项

- `ReviewAvaloniaTrialEvidence.ps1` 增加 `-RequireCompletedAcceptance` 和 `-RequireScreenshots`。
- 证据复审输出增加 `p1Evidence`、`readyForDefaultEntryReview` 和关键截图判定。
- 新增 `NewAvaloniaDefaultEntryDecisionPackage.ps1`，生成 `default-entry-decision-package.md/json`。
- 新增 `Avalonia12-现场证据回收操作说明.md`，明确验收记录填写、截图命名、复审命令和决策包生成命令。
- 更新试运行问题回收清单，把两个 P1 项改为“可由证据关闭”，并写明关闭条件。
- 发布脚本和证据采集脚本同步携带第十六批新脚本和新文档。
- Diagnostics 增加“P1 关闭证据要求”只读提示，不提供任何切默认入口、Cloud/MES 运维或 PLC 强制写入操作。

## 判定口径

- `P0Blocked`：缺少 manifest、WPF 回退、UI-only/runtime profile 等硬证据。
- `P1Pending`：P0 为零，但缺已填写验收记录或关键截图。
- `ReadyForDefaultEntryReview`：P0 为零、验收记录已填写、关键截图齐全。
- 即使进入 `ReadyForDefaultEntryReview`，脚本也不会自动写“允许切默认入口”；最终决策留空等待人工签字。

## 已验证

- 运行小范围测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaTrialEvidenceReviewScriptTests|AvaloniaFieldPackageScriptTests|AvaloniaSwitchReadinessTests|AvaloniaPublishLayoutPreflightTests|ProductionViewModelBehaviorTests" -m:1 /p:UseSharedCompilation=false
```

- 结果：32 个测试通过。
- 运行完整 AvaloniaShell 测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false
```

- 结果：61 个测试通过。
- 运行完整候选门禁：

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate
```

- 结果：通过；WPF 回退构建通过，回归测试通过，临时证据包复审为 `P1Pending`，符合“无真实现场记录/截图时不进入默认入口评审”的预期。
- FullGate 覆盖结果：
  - `AvaloniaShell.Tests`：61 通过。
  - `Launcher.Tests`：25 通过。
  - `Module.ContractTests`：28 通过。
  - `NonUiRegressionTests`：373 通过。
  - `Shell.Tests`：71 通过。
- 边界扫描：
  - Avalonia 项目未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI`。
  - Avalonia IOView 未发现直接 `ReadDataAsync` / `WriteDataAsync`。
  - 第十六批新增/相关脚本未发现数据库删除、Cloud/MES 清理/重试/删除或 PLC 直接读写命令。

## 待完整门禁

- 真实现场证据包尚未提供；P1 关闭仍需已填写验收记录和四类关键截图。
- 即使后续证据包复审输出 `ReadyForDefaultEntryReview`，切默认入口仍需独立批次和人工签字。
