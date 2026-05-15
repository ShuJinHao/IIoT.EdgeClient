# Avalonia 12 长期分支与审核同步规范

## 分支口径

- `codex/avalonia-default-entry-review` 是 Avalonia 迁移长期主线，后续实现继续以本地 `IIoT.EdgeClient.AvaloniaMigration` 为准。
- `codex/avalonia-default-entry-review-pr` 是基于远端 `main` 生成的审核快照分支，只用于 GitHub PR 展示可审核 diff。
- PR #44 保持 Draft，直到 Claude/GitHub 审核、FullGate、现场证据和默认入口评审门禁全部满足。
- 原 `IIoT.EdgeClient` 继续作为 WPF 主线，不承载 Avalonia 迁移代码。

## 同步流程

1. 在长期迁移副本确认当前分支为 `codex/avalonia-default-entry-review`。
2. 运行 FullGate 和必要回归，确认 P1 现场证据仍按真实状态记录，不把缺失证据误判为已关闭。
3. 执行 `scripts\SyncAvaloniaReviewSnapshot.ps1 -Commit -Push` 生成并推送审核快照。
4. 同步脚本会从远端 `main` 新建临时工作树，镜像长期迁移副本，并排除 `.git`、`bin`、`obj`、`.artifacts`、`.vs`、`publish`、`TestResults`、`node_modules`、`dist`。
5. 推送前必须通过 `git diff --check`、`git diff --cached --check` 和 staged 生成目录扫描。
6. 审核快照分支是可再生分支，脚本只对 `codex/avalonia-default-entry-review-pr` 使用 `--force-with-lease` 更新；长期迁移分支不得被强推。

## 审核反馈处理

- 如果 PR #44 出现 review 评论或 CI 失败，先记录问题、影响范围和拟处理方式，再在长期迁移分支修复。
- 修复后重新运行门禁，再通过同步脚本刷新审核快照分支。
- 审核反馈只处理反馈、CI、文档和测试补强；不得借机新增 Cloud/MES 操作、PLC 直接读写或默认入口切换。

## 默认入口边界

- 没有真实现场证据、P0/P1 清零、人工签字和 `ApprovedForDefaultEntrySwitch` 时，不允许执行真实默认入口切换。
- `SwitchAvaloniaDefaultEntry.ps1 -Apply` 只允许修改发布包内 Launcher profile，不允许修改源码、WPF 项目或原仓。
- WPF Launcher/WPF Shell 继续保留为回退线。
