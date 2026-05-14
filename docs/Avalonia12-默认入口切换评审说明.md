# Avalonia 12 默认入口切换评审说明

## 使用口径

- 本说明只用于 Avalonia 旁路迁移的默认入口切换评审，不代表已经允许切换生产入口。
- 第十七批只做评审门禁和 preview 预演，不修改 Launcher profile、不改发布链路、不改 WPF 默认入口。
- WPF Launcher/WPF Shell 必须继续保留为回退线。
- 不开放 Cloud/MES 清理、重试、删除，不执行 PLC 直接读写，不修改数据库、配置 JSON 或业务规则。

## 进入评审的前置条件

- `TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate` 通过。
- `ReviewAvaloniaTrialEvidence.ps1 -RequireCompletedAcceptance -RequireScreenshots` 输出 `ReadyForDefaultEntryReview`。
- `NewAvaloniaDefaultEntryDecisionPackage.ps1` 已生成决策材料。
- 决策材料中必须由人工填写：允许切换结论、决策人、决策时间、回退负责人。

## 评审门禁命令

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaDefaultEntryReadiness.ps1 `
  -DecisionPackagePath .\.artifacts\avalonia-default-entry-decision\<PackageName>\default-entry-decision-package.json
```

输出状态：

- `ApprovedForDefaultEntrySwitch`：材料完整，可进入后续独立“真实切默认入口”批次。
- `DefaultEntrySwitchRejected`：材料不完整或仍有阻断，不允许切换默认入口。

## Preview 预演命令

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\SwitchAvaloniaDefaultEntry.ps1 `
  -ReadinessSummaryPath .\.artifacts\avalonia-default-entry-readiness\<ReviewName>\default-entry-readiness-summary.json `
  -ReleaseRoot .\publish\avalonia-migration\Release `
  -Preview
```

Preview 只生成以下材料：

- 当前 WPF 默认入口。
- 目标 Avalonia Launcher/Shell 入口。
- UI-only 和 `--start-runtime` profile。
- WPF 回退入口和回退负责人。

Preview 不写入任何 profile，不修改发布目录，不改变生产启动行为。

## Apply 执行口径

- `SwitchAvaloniaDefaultEntry.ps1` 默认仍是 Preview；只有显式传入 `-Apply` 才允许写发布包内 profile。
- `-Apply` 必须读取 `ApprovedForDefaultEntrySwitch` 的 readiness summary，且签字字段完整。
- `-Apply` 只修改发布包内 `avalonia-launcher/launcher.profiles.json`，不会改源码、WPF 项目、原仓或业务链路。
- `-Apply` 执行前必须生成 `rollback-snapshot/`，用于 `RestoreAvaloniaDefaultEntry.ps1` 回退。
- 没有用户明确批准真实切换时，只允许执行 Preview，不允许执行 Apply。
