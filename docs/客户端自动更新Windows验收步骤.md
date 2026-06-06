# 客户端自动更新 Windows 验收步骤

本文用于验证 Velopack 自动更新在真实 Windows 安装态下是否满足红线：

- `Setup.exe` 可首装。
- Launcher 可从本地 release feed 检查并人工确认更新。
- 更新和回滚只替换程序，不覆盖 ProgramData 里的现场配置、账号、密钥、数据库、日志和运行数据。
- `homogenization/IIoT.Edge.Shell` 在 Velopack `current` 目录下能被 Launcher 正常拉起。

以下命令都在 `IIoT.EdgeClient` 目录执行。建议全程使用同一个 PowerShell 窗口；如果中途换窗口，需要重新设置 `$old` 和 `$new`。

## 1. 准备 old/new 两套 release feed

```powershell
dotnet tool restore --tool-manifest .config\dotnet-tools.json

$old = Join-Path $PWD 'publish\edge-acceptance\old'
$new = Join-Path $PWD 'publish\edge-acceptance\new'

powershell -ExecutionPolicy Bypass -File scripts\PackEdgeClientVelopack.ps1 `
  -Version 0.0.15 `
  -Channel homogenization `
  -OutputRoot $old `
  -CleanOutput

powershell -ExecutionPolicy Bypass -File scripts\TestEdgeVelopackPackage.ps1 `
  -Version 0.0.15 `
  -Channel homogenization `
  -OutputRoot $old

powershell -ExecutionPolicy Bypass -File scripts\PackEdgeClientVelopack.ps1 `
  -Version 0.0.15 `
  -Channel homogenization `
  -OutputRoot $new `
  -CleanOutput

powershell -ExecutionPolicy Bypass -File scripts\PackEdgeClientVelopack.ps1 `
  -Version 0.0.16 `
  -Channel homogenization `
  -OutputRoot $new

powershell -ExecutionPolicy Bypass -File scripts\TestEdgeVelopackPackage.ps1 `
  -Version 0.0.16 `
  -Channel homogenization `
  -OutputRoot $new `
  -RequireDelta
```

预期结果：

- `$old` 里有 `0.0.15` 的 `Setup.exe` 和 release 元数据。
- `$new` 里有 `0.0.16` 的 release 元数据，并包含 `0.0.15 -> 0.0.16` 的 delta 包。
- 两次 `TestEdgeVelopackPackage.ps1` 均通过。

## 2. 首装旧版本

双击安装：

```powershell
& "$old\IIoT.EdgeClient.Homogenization-homogenization-Setup.exe"
```

安装后启动 Launcher，并启动一次 Shell，让客户端把 ProgramData 机器配置 seed 出来。

## 3. 填写并校验机器身份

打印机器配置路径：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step PrintMachineIdentityPath `
  -MachineProfile HomogenizationLine
```

打开上面打印的 `appsettings.machine.HomogenizationLine.json`，填写：

```json
"CloudApi": {
  "ClientCode": "现场真实客户端编码",
  "BootstrapSecret": "现场真实 bootstrap 密钥"
}
```

校验已填写：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step ValidateMachineIdentity `
  -MachineProfile HomogenizationLine
```

预期结果：输出 `Machine identity check passed`。

## 4. 更新到新版本并校验 ProgramData 不变

配置更新源为新版本 feed：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step ConfigureSource `
  -UpdateSource $new `
  -Channel homogenization
```

更新前创建保护快照：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step SnapshotBeforeUpdate
```

在 Launcher 里点击检查更新并确认应用更新。更新完成后 Launcher 应重启，版本应变为 `0.0.16`。

校验 ProgramData 不变：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step VerifyAfterUpdate
```

预期结果：

- Launcher 版本变化。
- Shell 仍能启动。
- 输出 `ProgramData invariant check passed`。

## 5. 回滚到旧版本并再次校验

配置更新源为旧版本 feed：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step ConfigureSource `
  -UpdateSource $old `
  -Channel homogenization
```

回滚前创建保护快照：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step SnapshotBeforeRollback
```

在 Launcher 里检查更新并确认回滚。回滚完成后 Launcher 应重启，版本应回到 `0.0.15`。

校验 ProgramData 不变：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 `
  -Step VerifyAfterRollback
```

预期结果：

- Launcher 版本回到旧版本。
- Shell 仍能启动。
- 输出 `ProgramData invariant check passed`。

## 6. 验收通过标准

全部满足才算自动更新功能真正通过：

- old/new feed 打包和包内容校验通过。
- 旧版 `Setup.exe` 可安装。
- 首装后机器身份配置能定位、能填写、能校验。
- 从 old 更新到 new 成功。
- 从 new 回滚到 old 成功。
- 两次 ProgramData 快照对比均通过。
- 更新和回滚后 Shell 都能正常启动。
- 没有真实 `launcher.accounts.json`、`launcher.update.json`、数据库、日志、recipe、excel 被打进安装包。
