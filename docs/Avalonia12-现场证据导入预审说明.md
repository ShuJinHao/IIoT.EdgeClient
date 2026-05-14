# Avalonia 12 现场证据导入预审说明

## 使用口径

- 本说明用于把现场回传的 Avalonia 证据包导入旁路副本，并串联证据复审、决策包草案和默认入口预审。
- 导入脚本只读取现场证据包原件，写出导入副本和审查材料；不修改证据原件。
- 本流程不会修改 Launcher profile、发布链路或 WPF 默认入口。
- 本流程不会读取业务数据库，不调用 Cloud/MES 清理、重试、删除，也不调用 PLC 读写命令。

## 导入与预审命令

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ImportAvaloniaFieldEvidence.ps1 `
  -EvidencePath <现场证据包目录或zip> `
  -OutputRoot .\.artifacts\avalonia-field-evidence-inbox
```

导入后会生成：

- `evidence-import-summary.json/md`：导入摘要、文件 hash、缺失项和各阶段状态。
- `evidence-file-inventory.json`：现场证据文件清单和 SHA256。
- `field-evidence-review-bundle/review/`：试运行证据复审报告。
- `field-evidence-review-bundle/decision/`：切默认入口决策包草案。
- `field-evidence-review-bundle/readiness/`：默认入口评审门禁结果。
- `field-evidence-review-bundle/switch-preview/`：只有 readiness 通过时才生成默认入口切换预演报告。

## P1 关闭证据

P1 只能由现场证据关闭，至少包括：

- 已填写的 `Avalonia12-现场试运行验收记录.md`，并标记“验收记录状态：已完成”。
- `diagnostics-summary` 截图，展示 Diagnostics 现场联调摘要。
- `io-write-gate` 截图，展示 I/O 写入闸门申请结果。
- `plc-write-trace` 截图，展示 PLC 块写入轨迹。
- `wpf-fallback` 截图，展示 WPF Launcher/WPF Shell 回退验证。
- 多屏、高 DPI、触摸、窗口拖拽、DataGrid 滚动的人工验收记录。

证据不足时不得关闭 P1；`ReviewAvaloniaTrialEvidence.ps1 -RequireCompletedAcceptance -RequireScreenshots` 必须保持 `P1Pending` 或 `P0Blocked`。

## 默认入口预审

- 没有完整现场记录、关键截图、P0/P1 清零和人工签字时，`TestAvaloniaDefaultEntryReadiness.ps1` 必须输出拒绝。
- 如果证据包内携带已签字的 `default-entry-decision-package.json`，导入脚本会用它进行 readiness 预审。
- 即使 readiness 输出 `ApprovedForDefaultEntrySwitch`，本批也只生成 Preview 报告；真实默认入口切换必须放到后续独立批次，并获得明确批准。
