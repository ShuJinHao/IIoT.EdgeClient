# Avalonia 12 现场证据采集阶段记录

## 完成内容

- 新增并校验 `scripts/CollectAvaloniaFieldEvidence.ps1`。
- 脚本支持从 AvaloniaShell 运行目录只读采集日志、Diagnostics 摘要、Launcher profile、PLC 写入轨迹截图占位说明和现场联调清单。
- 脚本支持目录输出和 zip 输出。
- 脚本兼容两组参数：
  - `-RuntimeRoot`、`-LauncherProfilesPath`、`-Zip`。
  - `-AvaloniaShellDirectory`、`-AvaloniaLauncherDirectory`、`-CreateZip`。

## 改动边界

- 只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本实施。
- 不修改原 `IIoT.EdgeClient`。
- 不修改 Cloud/MES API、业务数据库、业务配置、PLC 块规划策略或运行链路。
- 证据采集只复制诊断日志、诊断文本、Launcher profile、联调文档和用户提供的截图。
- 不读取业务数据库文件，不复制 `db`、`context`、`recipe`、`excel` 运行数据目录。
- 不提供 Cloud/MES 清理、重试、补传、删除或现场恢复动作。

## 验证命令

```powershell
$tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile('scripts/CollectAvaloniaFieldEvidence.ps1',[ref]$tokens,[ref]$errors) | Out-Null; if ($errors.Count -gt 0) { $errors | Format-List; exit 1 }; 'PowerShell syntax parse passed.'
```

结果：通过。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\CollectAvaloniaFieldEvidence.ps1 -RuntimeRoot .\src\Edge\IIoT.Edge.AvaloniaShell -LauncherProfilesPath .\src\Edge\IIoT.Edge.Launcher.Avalonia\launcher.profiles.json -PreflightOnly
```

结果：通过，识别 2 个 Launcher profile，其中 1 个 UI-only、1 个 `--start-runtime` 运行联调入口。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\CollectAvaloniaFieldEvidence.ps1 -AvaloniaShellDirectory .\src\Edge\IIoT.Edge.AvaloniaShell -AvaloniaLauncherDirectory .\src\Edge\IIoT.Edge.Launcher.Avalonia -PreflightOnly
```

结果：通过。

```powershell
$out = Join-Path $env:TEMP ('AvaloniaFieldEvidenceCheck-' + (Get-Date -Format 'yyyyMMddHHmmss')); powershell -ExecutionPolicy Bypass -File .\scripts\CollectAvaloniaFieldEvidence.ps1 -AvaloniaShellDirectory .\src\Edge\IIoT.Edge.AvaloniaShell -AvaloniaLauncherDirectory .\src\Edge\IIoT.Edge.Launcher.Avalonia -OutputRoot $out -PackageName Smoke -DiagnosticsSummary '模块数：0；PLC 设备数：0；阻断问题数：0；运行目录：静态预检夹具' -CreateZip
```

结果：通过，在 `%TEMP%` 下生成目录证据包和 zip，未写入仓库。

```powershell
Select-String -Path .\scripts\CollectAvaloniaFieldEvidence.ps1 -Pattern 'Remove-Item','Invoke-Sqlcmd','sqlite3','DROP TABLE','TRUNCATE','DELETE FROM','dotnet ef' -SimpleMatch
```

结果：未命中运行数据删除、数据库命令或破坏性 SQL/EF 片段。

```powershell
dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --filter FullyQualifiedName~AvaloniaFieldPackageScriptTests -m:1 /p:UseSharedCompilation=false
```

结果：通过 3，失败 0，跳过 0。

## 剩余风险

- 现场 Diagnostics UI 摘要仍需要人工通过参数复制，或通过截图补证。
- 脚本不会判断 PLC 物理侧最终状态，仍需现场 PLC 侧截图或运维确认。
- 脚本已通过静态与临时包验证，但尚未接入主线 CI。

## 下一阶段进入条件

- 主线 owner 确认是否把现场证据采集脚本纳入发布前静态预检。
- 现场联调前确认 AvaloniaShell 运行目录、Avalonia Launcher 目录和截图保存目录。
- 真实现场执行后回收证据包目录或 zip，再审核日志、Diagnostics 摘要和截图。
