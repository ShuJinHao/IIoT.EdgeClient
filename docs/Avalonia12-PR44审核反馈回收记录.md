# Avalonia 12 PR #44 审核反馈回收记录

## 当前状态

- PR：`https://github.com/ShuJinHao/IIoT.EdgeClient/pull/44`
- 长期迁移分支：`codex/avalonia-default-entry-review`
- 审核快照分支：`codex/avalonia-default-entry-review-pr`
- PR 状态：Draft。
- CI 状态：`smoke-build` 通过，`validate-runtime` 通过，`package-runtime` 跳过。
- 当前 review 评论：暂无。
- 当前 review decision：暂无。

## 处理规则

- 如果 Claude 或 GitHub review 给出反馈，必须先记录反馈内容、影响范围、拟处理方式和是否需要用户确认。
- 反馈处理只允许修正审核意见、CI 问题、文档问题或测试覆盖问题。
- 不允许借反馈处理新增 Cloud/MES 清理、重试、删除入口。
- 不允许新增 PLC 直接 `ReadDataAsync` / `WriteDataAsync` 调用。
- 不允许把 Avalonia 切为生产默认入口。
- 如反馈涉及业务规则、PLC 策略、Cloud/MES 契约或默认入口切换，必须先和用户单独确认。

## 回收表

| 编号 | 来源 | 反馈内容 | 影响范围 | 处理结论 | 复验命令 | 是否需用户确认 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R-001 | 当前检查 | 暂无 review 评论或 requested changes | 无代码影响 | 等待 Claude/GitHub 审核 | `gh pr view 44 --repo ShuJinHao/IIoT.EdgeClient --json comments,reviews,reviewDecision` | 否 | 待审核 |

## 下一次更新要求

- 每处理一条反馈，都要把状态从“待审核”更新为“处理中 / 已处理 / 需用户确认 / 延期”之一。
- 已处理反馈必须写清复验命令和结果。
- 不得把“无评论”写成“审核通过”；只有 review decision 或用户明确结论才能作为审核通过依据。
