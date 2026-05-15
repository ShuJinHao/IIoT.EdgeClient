# Avalonia 12 现场证据包预审最小样例说明

## 目的

本说明用于现场回传 Avalonia 试运行证据包前自查。它只定义最小证据形状，不代表现场验收已经通过。

## 最小目录结构

```text
AvaloniaFieldEvidence/
  release-manifest.json
  candidate-validation-summary.json
  launcher/
    launcher.profiles.json
  docs/
    Avalonia12-现场试运行验收记录.md
  screenshots/
    01-diagnostics-summary.png
    02-io-write-gate.png
    03-plc-write-trace.png
    04-wpf-fallback.png
  logs/
    startup-or-runtime.log
  diagnostics/
    diagnostics-summary.json
```

## 必填证据

- `release-manifest.json`：候选包版本、构建时间、输出目录和 SkiaSharp preview 例外记录。
- `candidate-validation-summary.json`：FullGate、WPF 回退构建和依赖/漏洞扫描结果。
- `launcher.profiles.json`：必须能看出 UI-only 默认入口和显式 `--start-runtime` 运行联调入口。
- `Avalonia12-现场试运行验收记录.md`：必须由现场填写，不能只回传模板。
- 四张关键截图：Diagnostics 摘要、I/O 写入闸门、PLC 写入轨迹、WPF 回退。
- 日志或诊断摘要：用于证明启动、只读快照、写入申请和回退过程可追溯。

## 预审判定

- 缺少 manifest、candidate summary、Launcher profile 或 WPF 回退证据时，判定为 `P0Blocked`。
- P0 为零但缺少已填写验收记录或关键截图时，判定为 `P1Pending`。
- 只有 P0 为零、现场验收记录已填写、关键截图齐全时，才允许进入 `ReadyForDefaultEntryReview`。
- 即使达到 `ReadyForDefaultEntryReview`，仍不能自动切默认入口；还必须经过人工签字和 `ApprovedForDefaultEntrySwitch` 门禁。

## 禁止事项

- 不得修改证据包原件后再送审。
- 不得把截图占位说明当成真实截图。
- 不得把模板验收记录当成现场已填写记录。
- 不得通过证据脚本执行数据库删除、Cloud/MES 清理、重试、删除或 PLC 直接读写。
