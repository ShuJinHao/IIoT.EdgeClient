# Avalonia 12 第十八批迁移记录

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不切默认入口，不新增业务功能，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- WPF Launcher/WPF Shell 继续作为生产回退线。

## 本批完成项

- 新增 `ImportAvaloniaFieldEvidence.ps1`，支持导入现场证据目录或 zip，生成文件 hash、缺失项和统一复审 bundle。
- 导入脚本串联试运行证据复审、默认入口决策包草案、readiness 预审，并在 readiness 通过时只生成 Preview 报告。
- 发布包携带现场证据导入预审说明和导入脚本。
- 试运行问题回收清单补齐 P1 关闭证据映射，并明确证据不足不得关闭 P1。
- Diagnostics 增加现场证据导入与预审命令提示，不提供切换按钮。

## 判定口径

- 缺少已填写验收记录、关键截图或 WPF 回退证据时，复审必须保持 `P1Pending` 或 `P0Blocked`。
- 没有人工签字的决策包时，readiness 必须保持 `DefaultEntrySwitchRejected`。
- 即使 evidence import 预审达到 `ApprovedForDefaultEntrySwitch`，真实修改 Launcher 默认入口或生产发布链路仍必须另起独立批次并获得明确批准。

## 验证记录

- 已通过第十八批新增链路和相关发布/诊断测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaFieldEvidenceImportScriptTests|AvaloniaFieldPackageScriptTests|AvaloniaSwitchReadinessTests|AvaloniaPublishLayoutPreflightTests|ProductionViewModelBehaviorTests" -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

- 结果：30 条通过，0 失败。

- 已通过完整 AvaloniaShell 测试：

```powershell
dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

- 结果：72 条通过，0 失败。

- 已通过完整候选包门禁：

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate
```

- 结果：通过；临时证据包复审结果保持 `P1Pending`，未误判为可切默认入口。

- 已完成边界扫描：
  - Avalonia 项目无 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI`。
  - Avalonia I/O 页无直接 `ReadDataAsync` / `WriteDataAsync`。
  - 新增/相关脚本无数据库删除、Cloud/MES 清理/重试/删除或 PLC 直接读写命令。
  - 本批新增和修改文本未发现常见中文乱码标记。
