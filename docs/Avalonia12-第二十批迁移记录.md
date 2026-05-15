# Avalonia 12 第二十批迁移记录：审核反馈闭环 + 长期分支同步规范

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不新增业务功能，不切默认入口，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- PR #44 当前作为 Draft 审核入口；长期迁移继续以 `codex/avalonia-default-entry-review` 为主线。

## 本批完成项

- 新增长期分支与审核快照同步规范，明确长期分支、审核快照分支和 PR #44 的职责边界。
- 新增 `SyncAvaloniaReviewSnapshot.ps1`，用于从长期迁移副本重新生成基于 `main` 的审核快照分支。
- 新增 Claude/GitHub 审核清单，覆盖架构边界、安全边界、发布证据、默认入口门禁、文本和依赖。
- 增加脚本/文档静态测试，确认同步脚本排除生成目录、执行 `git diff --check`、默认不推送、并保留受控 `-Commit -Push` 流程。

## PR 与 CI 状态

- PR：`https://github.com/ShuJinHao/IIoT.EdgeClient/pull/44`
- 长期迁移分支：`codex/avalonia-default-entry-review`
- 审核快照分支：`codex/avalonia-default-entry-review-pr`
- 当前 PR 状态：Draft，merge state clean。
- 当前自动化状态：`smoke-build` 和 `validate-runtime` 已通过，`package-runtime` 跳过；暂无 review 评论。

## 验证记录

- `git diff --check`
- `dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaReviewSnapshotWorkflowTests|AvaloniaFieldPackageScriptTests|ResourceEncodingHygieneTests" -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate`
- PR 同步前需确认审核快照 staged 文件不包含 `bin`、`obj`、`.artifacts`、`.vs`、`publish`、`TestResults`、`node_modules`、`dist`。

## 剩余风险

- 真实现场验收记录和关键截图仍未提供，证据复审必须继续保持 `P1Pending`。
- PR #44 尚未完成 Claude/GitHub 审核，后续反馈必须先对齐影响范围再处理。
- 默认入口真实切换仍需后续独立批次和用户明确批准。

## 下一阶段准入条件

- Claude/GitHub 完成 PR #44 审核，或给出明确可处理反馈。
- FullGate 和回归继续通过。
- 现场证据包如已提供，必须通过只读导入、复审、决策包生成和 readiness 预审，不得跳过 P0/P1 判定。
