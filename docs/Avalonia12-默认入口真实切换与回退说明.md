# Avalonia 12 默认入口真实切换与回退说明

## 使用口径

- 本说明只适用于旁路副本生成的 Avalonia 联调发布包。
- 真实切换必须先由 `TestAvaloniaDefaultEntryReadiness.ps1` 输出 `ApprovedForDefaultEntrySwitch`。
- 没有完整现场证据、P0/P1 清零、WPF 回退验证和人工签字时，不允许执行 `-Apply`。
- `-Apply` 只修改发布包内 `avalonia-launcher/launcher.profiles.json`，不改源码、不改 WPF 项目、不改原仓。

## Apply 命令

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\SwitchAvaloniaDefaultEntry.ps1 `
  -ReadinessSummaryPath .\.artifacts\avalonia-default-entry-readiness\<ReviewName>\default-entry-readiness-summary.json `
  -ReleaseRoot .\publish\avalonia-migration\Release `
  -Apply
```

执行成功后会生成：

- `default-entry-switch-apply-summary.json`
- `default-entry-switch-apply-summary.md`
- `rollback-snapshot/launcher.profiles.json`
- `rollback-snapshot/release-manifest.json`
- `rollback-snapshot/candidate-validation-summary.json`
- `rollback-snapshot/default-entry-readiness-summary.json`

## 回退命令

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\RestoreAvaloniaDefaultEntry.ps1 `
  -RollbackSnapshotPath .\.artifacts\avalonia-default-entry-switch\<ApplyName>\rollback-snapshot `
  -ReleaseRoot .\publish\avalonia-migration\Release
```

回退只恢复发布包内 `avalonia-launcher/launcher.profiles.json`，并生成 `default-entry-restore-summary.json/md`。

## 禁止事项

- 不删除发布包。
- 不清理日志。
- 不读取或修改业务数据库。
- 不调用 Cloud/MES 清理、重试、删除。
- 不执行 PLC 直接读写。
