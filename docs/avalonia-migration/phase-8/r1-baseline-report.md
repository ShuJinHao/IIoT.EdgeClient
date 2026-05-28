# Phase 8 R1 Baseline Report

## 1. T0.1 修正结论

- 旧 T0 报告作废：之前的 `feature/phase-8-r1-shell-pilot` 从 `main@ea3d726e36d2aa3c9a04938c6899906a98b9312a` 创建，但该基线不包含 R1 计划所需的 Avalonia `.axaml` 文件。
- 旧 worktree 已删除：`C:\Users\jinha\Desktop\产线系统架构升级\1\IIoT.EdgeClient.phase8-r1`
- 旧本地分支已删除：`feature/phase-8-r1-shell-pilot`
- 本报告为修正后的 T0 基线报告。

## 2. R1 基线

- R1 branch: `feature/phase-8-r1-shell-pilot`
- R1 worktree: `C:\Users\jinha\Desktop\产线系统架构升级\1\IIoT.EdgeClient.phase8-r1`
- Baseline source branch: `codex/edgeclient-local-homogenization-sync`
- Baseline commit: `04cf17534973f88eec2984459d4fec76de51c25e`
- Baseline commit subject: `edge: migrate client UI to Avalonia`
- T0 report path: `docs/avalonia-migration/phase-8/r1-baseline-report.md`

This branch was created from the committed Avalonia baseline, not from the dirty working tree. Existing dirty work remains isolated in the original `IIoT.EdgeClient` worktree.

## 3. Dirty Work Disposition

- Original worktree: `C:\Users\jinha\Desktop\产线系统架构升级\1\IIoT.EdgeClient`
- Original branch: `codex/edgeclient-local-homogenization-sync`
- Dirty status count after rebuilding R1: 69 porcelain entries
- Tracked diff stat after rebuilding R1: 65 files changed, 968 insertions, 4076 deletions
- Reset paths: none
- Stashed paths: none
- Discarded paths: none

Disposition: all existing dirty changes stay parked in the original worktree and must go through their own PR, parked-branch decision, or explicit owner discard outside Phase 8 R1.

Observed dirty buckets:

- Launcher visual/login surface
- Host Bootstrap and Shell migration cleanup
- Navigation business page polish
- Panels migration cleanup
- Presentation Shell legacy surface
- Homogenization module migration
- Shared UI cleanup
- Tests and contract updates
- Phase 6.5/Phase 7 docs and generated test output

## 4. R1 Target File Check

The corrected baseline contains the files required by T1-T7:

- `src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeTheme.axaml`
- `src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeControls.axaml`
- `src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/EdgeCard.cs`
- `src/Edge/IIoT.Edge.Shell/MainWindow.axaml`
- `src/Edge/IIoT.Edge.Shell/MainWindow.axaml.cs`
- `src/Edge/IIoT.Edge.Shell/App.axaml.cs`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/CoreViewIds.cs`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/NavigationRailView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/ViewModels/NavigationRailViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Shell/Views/ShellHeaderView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Shell/Views/ShellHeaderView.axaml.cs`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Dashboard/Views/DashboardView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/NavigationHostView.axaml.cs`

## 5. Gate Result

- T0.1 completed as a baseline correction only.
- T1-T7 have not started.
- No Launcher, Cloud, PLC, MES, runtime chain, business service, module, or business ViewModel code was changed by this T0.1 correction.
- Next step after review: start T1 from this corrected clean R1 worktree.
