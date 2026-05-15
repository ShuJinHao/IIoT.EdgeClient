# Avalonia 12 现场证据回收操作说明

## 使用口径

- 本说明只用于 Avalonia 旁路迁移试运行证据回收，不改变 WPF 生产入口。
- 证据复审只读取发布包、日志、截图、验收记录和摘要文件，不读取业务数据库。
- 不执行 Cloud/MES 清理、重试、删除，不执行 PLC 直接写入，不修改现场运行目录。
- 即使复审结果为 `ReadyForDefaultEntryReview`，也只代表可进入切默认入口评审；最终是否切默认入口必须人工签字。

## 证据回收顺序

1. 使用 UI-only profile 启动 Avalonia Launcher/Shell，完成窗口、菜单、Dock、语言切换和 DataGrid 检查。
2. 使用运行联调 profile 或 `StartAvaloniaTrialRun.ps1 -StartRuntime` 启动 `--start-runtime`。
3. 在 IOView 完成只读快照读取，确认未启动、无快照、设备未绑定等状态提示可区分。
4. 在满足权限和运行状态后申请一次 I/O 运行时缓冲写入，并等待扫描周期产生 PLC 写入轨迹。
5. 填写 `docs/Avalonia12-现场试运行验收记录.md`，并在文件内加入一行：`验收记录状态：已完成`。
6. 补齐关键截图，放入证据包 `screenshots/` 目录。
7. 执行证据复审脚本，确认 P0/P1 状态。
8. 只有复审为 `ReadyForDefaultEntryReview` 时，才生成切默认入口评审材料。

## 关键截图命名

证据包必须包含以下截图。文件名可以追加时间戳，但必须保留下面的英文 token，便于脚本识别。

| 截图 | 建议文件名 |
| --- | --- |
| Diagnostics 现场摘要 | `01-diagnostics-summary.png` |
| I/O 写入闸门 | `02-io-write-gate.png` |
| PLC 写入轨迹 | `03-plc-write-trace.png` |
| WPF 回退验证 | `04-wpf-fallback.png` |

## 复审命令

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ReviewAvaloniaTrialEvidence.ps1 `
  -EvidencePath .\publish\field-evidence\<证据包目录> `
  -RequireCompletedAcceptance `
  -RequireScreenshots
```

复审结果：

- `P0Blocked`：缺 manifest、WPF 回退、runtime profile 等硬阻断，不允许继续评审。
- `P1Pending`：P0 为零，但验收记录或关键截图不足，不允许进入切默认入口评审。
- `ReadyForDefaultEntryReview`：P0 为零，验收记录已填写，关键截图齐全，可以生成评审材料。

## 决策包生成命令

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\NewAvaloniaDefaultEntryDecisionPackage.ps1 `
  -CandidateSummaryPath .\publish\avalonia-migration\Release\candidate-validation-summary.json `
  -TrialReviewSummaryPath .\.artifacts\avalonia-trial-review\<ReviewName>\trial-review-summary.json
```

生成的 `default-entry-decision-package.md/json` 不会自动写“允许切默认入口”。人工评审人必须在材料中签字后，后续独立批次才能讨论 Launcher 默认入口或生产发布链路切换。
