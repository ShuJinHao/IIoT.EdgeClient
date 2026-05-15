# Avalonia 12 第十五批迁移记录：试运行问题回收 + P0/P1 阻断清零 + 切默认入口决策包

## 范围

- 只修改旁路副本 `IIoT.EdgeClient.AvaloniaMigration`。
- 原 `IIoT.EdgeClient`、Cloud、AICopilot 未参与本批改动。
- 本批不新增业务功能，不开放 Cloud/MES 清理、重试、删除，不修改数据库、配置 JSON、Cloud/MES API、PLC 策略或业务规则。
- Avalonia 仍不是生产默认入口；WPF Launcher/WPF Shell 继续作为回退线。

## 本批完成内容

- 新增 `scripts/ReviewAvaloniaTrialEvidence.ps1`，输入证据包目录或 zip，校验发布 manifest、候选验收摘要、Launcher profile、验收记录模板、截图占位说明和诊断摘要，输出 `trial-review-summary.json` 与 `trial-review-report.md`。
- `scripts/CollectAvaloniaFieldEvidence.ps1` 现在会把 `release-manifest.json`、`candidate-validation-summary.json`、问题回收清单和切默认入口决策包模板纳入证据包。
- `scripts/TestAvaloniaMigrationCandidate.ps1` 新增 `-FullGate`，自动串行执行发布、证据采集、依赖/漏洞扫描、WPF 回退构建、全部回归测试和证据审查；summary 记录 `fullGate`、测试通过数和 WPF 回退状态。
- 发布脚本同步携带第十五批新增资料和证据审查脚本。
- 新增 `docs/Avalonia12-试运行问题回收清单.md` 和 `docs/Avalonia12-切默认入口决策包模板.md`。
- 更新差异矩阵和阻断清单的当前判定：P0 技术门禁已可 FullGate 复验，P1 仍需现场证据关闭。
- Diagnostics 的现场联调摘要新增“试运行审查资料”只读提示，不增加切默认入口、Cloud/MES 运维或 PLC 强制写入按钮。

## 验证结果

- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
  - 通过：55
- `powershell -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate`
  - 通过：发布包、证据采集预检、临时证据包、依赖列表、漏洞扫描、WPF Shell 回退构建、WPF Launcher 回退构建、全部回归测试、证据审查
  - 输出：`publish\avalonia-migration\Release\candidate-validation-summary.json`
  - 证据审查状态：`P1Pending`
  - 测试通过数：AvaloniaShell.Tests 55、Launcher.Tests 25、Module.ContractTests 28、NonUiRegressionTests 373、Shell.Tests 71

## 边界检查

- Avalonia Shell、Avalonia Bootstrap、Avalonia Presentation、Avalonia UI Shared、匀浆 Avalonia 插件扫描未发现 `System.Windows`、`UseWPF`、`IIoT.Edge.UI.Shared`、`SukiUI`。
- Avalonia IOView 扫描未发现直接 `ReadDataAsync` / `WriteDataAsync`。
- 证据采集、候选验收、试运行启动和证据审查脚本未发现数据库删除、Cloud/MES 清理、重试、删除或 PLC 直接写入命令。
- 依赖图中 preview/prerelease 仍只允许已批准的 SkiaSharp 系列；漏洞扫描未报告未处理漏洞包。

## 剩余风险

- `P1Pending` 是预期状态：当前没有真实现场截图和已填写验收记录，不能宣称现场 P1 已关闭。
- 真实 `--start-runtime`、PLC 侧状态、多屏、高 DPI、触摸体验仍需现场证据包补齐。
- WPF Launcher/WPF Shell 必须继续保留为回退线，直到切默认入口评审明确批准。

## 下一步建议

- 先让 Claude 审核第十五批 FullGate、证据审查脚本、问题回收清单和切默认入口决策包模板。
- 下一批建议进入“现场试运行执行与证据回收”或“按 Claude 审核意见修正”，不要直接切默认入口。
