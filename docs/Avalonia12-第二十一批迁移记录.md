# Avalonia 12 第二十一批迁移记录：审核反馈处理 + 现场证据预审准备

## 范围

- 只在旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 内实施。
- 本批不新增业务功能，不切默认入口，不修改 Cloud/MES、数据库、配置 JSON、PLC 策略或业务规则。
- 当前以 PR #44 审核闭环和现场证据预审准备为目标。

## 当前 PR 状态

- PR：`https://github.com/ShuJinHao/IIoT.EdgeClient/pull/44`
- PR 状态：Draft。
- merge state：clean。
- CI 状态：`smoke-build` 通过，`validate-runtime` 通过，`package-runtime` 跳过。
- review 评论：暂无。

## 本批完成项

- 新增 PR #44 审核反馈回收记录，明确“无评论不等于审核通过”。
- 新增现场证据包预审最小样例说明，明确证据包目录、关键截图、日志和 P0/P1 判定。
- 增加静态测试，确认审核反馈记录、证据样例说明和 `P1Pending` 门禁文案存在。
- 继续保持默认入口不切换，真实现场证据不足时仍按 `P1Pending` 处理。

## 验证记录

- `git diff --check`
- `dotnet test .\src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --filter "AvaloniaReviewSnapshotWorkflowTests|AvaloniaFieldPackageScriptTests|ResourceEncodingHygieneTests" -m:1 /p:UseSharedCompilation=false --logger "console;verbosity=minimal"`
- `powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestAvaloniaMigrationCandidate.ps1 -Configuration Release -VerifyWpfFallback -FullGate`
- `gh pr checks 44 --repo ShuJinHao/IIoT.EdgeClient`

## 剩余风险

- 真实现场证据包仍未提供，不能关闭 P1。
- PR #44 尚未获得 Claude/GitHub 审核结论，后续反馈必须继续走回收记录。
- 默认入口真实切换仍需后续独立批次和用户明确批准。

## 下一阶段准入条件

- Claude/GitHub 给出审核结论或明确反馈。
- 如现场证据包已回传，必须通过只读导入、复审、决策包生成和 readiness 预审。
- FullGate 和 PR CI 继续通过。
