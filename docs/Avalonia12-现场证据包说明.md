# Avalonia 12 现场证据包说明

## 适用范围

- 仅适用于旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia 现场联调包。
- 证据包用于回收现场运行目录日志、Diagnostics 摘要、Launcher profile、PLC 写入轨迹截图说明和联调清单。
- 证据采集只读，不读取业务数据库，不清理、不重试、不删除 Cloud/MES 数据。

## 采集脚本

脚本路径：

```powershell
scripts\CollectAvaloniaFieldEvidence.ps1
```

推荐先生成 Avalonia 联调包：

```powershell
.\scripts\PublishAvaloniaMigration.ps1 -Configuration Release
```

现场生成目录证据包：

```powershell
.\scripts\CollectAvaloniaFieldEvidence.ps1 `
  -AvaloniaShellDirectory C:\IIoT\Avalonia\avalonia-shell `
  -AvaloniaLauncherDirectory C:\IIoT\Avalonia\avalonia-launcher `
  -OutputRoot C:\IIoT\FieldEvidence
```

现场生成 zip：

```powershell
.\scripts\CollectAvaloniaFieldEvidence.ps1 `
  -AvaloniaShellDirectory C:\IIoT\Avalonia\avalonia-shell `
  -AvaloniaLauncherDirectory C:\IIoT\Avalonia\avalonia-launcher `
  -OutputRoot C:\IIoT\FieldEvidence `
  -CreateZip
```

## 输出内容

- `field-evidence-summary.json`：采集输入、输出和只读边界。
- `runtime-logs/`：运行目录 `data\avalonia-migration\diagnostics\logs` 下的日志副本。
- `diagnostics/`：运行目录 `data\avalonia-migration\diagnostics` 下的文本诊断文件副本。
- `launcher/launcher.profiles.json`：现场使用的 Launcher profile。
- `screenshots/截图占位说明.md`：PLC 写入轨迹、I/O 申请和现场 PLC 状态截图占位说明。
- `docs/Avalonia12-现场联调检查清单.md`：现场联调清单副本。
- `docs/NuGet预览传递依赖例外记录.md`：SkiaSharp preview 传递依赖例外记录。

## 明确排除

- 不复制 `db`、`context`、`recipe`、`excel` 运行数据目录。
- 不读取 SQLite、LiteDB 或其他业务数据库文件。
- 不触发 Cloud/MES 清理、重试、补传或数据删除动作。
- 不修改运行目录、业务配置、PLC 块规划策略或 Launcher profile。
- 不自动判断 PLC 物理侧最终状态，物理侧状态仍以现场 PLC 截图或运维确认记录为准。

## 主线 owner 测试点

- 把 `CollectAvaloniaFieldEvidence.ps1` 纳入发布前静态预检，确认 Launcher profile 同时包含 UI-only 和 `--start-runtime` 运行联调入口。
- 增加脚本禁用动作扫描，确认采集脚本不包含运行数据删除、数据库读取、Cloud/MES 清理或重试入口。
- 用临时运行目录夹具覆盖证据包生成：日志复制、Launcher profile 复制、截图占位说明、清单复制、zip 输出。
- 用含 `db`、`context`、`recipe`、`excel` 的运行目录夹具验证这些目录不会进入证据包。
