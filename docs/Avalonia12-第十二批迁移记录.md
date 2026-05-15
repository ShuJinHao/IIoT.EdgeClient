# Avalonia 12 第十二批迁移记录：发布候选包脚本 + 现场联调证据包

## 范围

- 本批继续只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本内实施，不回写原 WPF 主线。
- 新增 Avalonia 独立发布脚本和现场证据采集脚本，不修改现有 WPF 发布链路。
- Cloud、MES、数据库、配置 JSON、PLC 块规划策略和业务规则均未修改。

## 已完成

- 新增 `scripts/PublishAvaloniaMigration.ps1`：
  - 发布 `IIoT.Edge.Launcher.Avalonia` 和 `IIoT.Edge.AvaloniaShell` 到独立时间戳目录。
  - 验证 Launcher profiles、AvaloniaShell、匀浆 Avalonia 插件 DLL、`plugin.json`、模块配置和联调文档齐全。
  - 生成 `release-manifest.json`，记录 commit、构建时间、输出位置、项目清单、验证命令和 SkiaSharp preview 例外。

- 新增 `scripts/CollectAvaloniaFieldEvidence.ps1`：
  - 只读复制 Launcher profile、现场联调清单、预览依赖例外记录、运行诊断日志和 Diagnostics 文本/JSON。
  - 生成 `field-evidence-summary.json` 和截图占位说明。
  - 不读取或修改业务数据库，不执行 Cloud/MES 清理、重试、删除或 PLC 写入。

- Diagnostics 增加“现场联调摘要”只读页签：
  - 汇总 UI-only / `--start-runtime` 状态、启动诊断、最近 I/O 写入申请、最近 PLC 块写入轨迹、Cloud/MES 当前只读状态。
  - 只展示和记录，不提供清理、重试、删除、强制写入或补偿按钮。

- 新增脚本预检和 Diagnostics 摘要测试：
  - 验证发布脚本只引用 Avalonia Launcher/Shell，不引用 WPF Shell 发布入口。
  - 验证证据采集脚本不包含数据库删除、清理或 EF 操作命令。
  - 验证 Launcher profile 保持 UI-only 默认入口和显式 `--start-runtime` 联调入口。

## 使用方式

发布 Avalonia 联调包：

```powershell
.\scripts\PublishAvaloniaMigration.ps1 -Configuration Release
```

采集现场证据：

```powershell
.\scripts\CollectAvaloniaFieldEvidence.ps1 -CreateZip
```

如现场使用指定发布目录，可显式传入：

```powershell
.\scripts\CollectAvaloniaFieldEvidence.ps1 `
  -AvaloniaLauncherDirectory "C:\EdgeAvalonia\avalonia-launcher" `
  -AvaloniaShellDirectory "C:\EdgeAvalonia\avalonia-shell" `
  -CreateZip
```

## 安全口径

- 默认 Launcher profile 仍为 UI-only，不启动运行链路。
- 运行联调必须显式使用 `--start-runtime`。
- I/O 写入仍只进入运行时缓冲，真实 PLC 物理写入由既有扫描任务和块规划器负责。
- Cloud/MES 当前仅展示只读状态，不开放人工清理、重试、删除。
- SkiaSharp preview 仅作为 Avalonia 12 传递依赖例外保留，后续稳定版发布后必须收回例外并重新验证依赖图。

## 待验证

- 在现场机器执行发布包脚本并确认输出目录完整。
- 以 UI-only 和 `--start-runtime` 两种 profile 启动 Avalonia 客户端。
- 在 Diagnostics 中截图保存“现场联调摘要”“I/O 写入闸门”“PLC 写入轨迹”。
- 执行证据采集脚本生成目录包或 zip 包，交由后续审核。
