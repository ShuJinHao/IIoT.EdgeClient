# Phase 7 Cleanup Candidates

Phase 7 has processed the WPF cleanup candidates that were recorded during Phase 6.5-F.

## Processed In Phase 7

- Deleted the inactive `ShellNavRailView.axaml` and code-behind after confirming `rg "ShellNavRailView" src` only found self-references.
- Deleted old WPF `.xaml` resource dictionaries where matching `.axaml` dictionaries are now the active path.
- Deleted old WPF view files and code-behind under Panels and UI.Shared.
- Removed `<UseWPF>true</UseWPF>` and `SKIP_EDGE_WPF_PROJECTS` transition settings.
- Removed `MaterialDesignThemes` and AvalonDock package references and central versions.
- Deleted the old WPF `INavigationService` and `NavigationService` path.
- Reworked WPF-only test helpers and assertions to use Avalonia resources, `IViewRegistry`, or static `.axaml` checks.

## Not P7 Work

- Visual quality problems from P6.5 are intentionally not handled here. They belong to the later UI visual rework batch.
- `src/Tests/IIoT.Edge.NonUiRegressionTests/edge_test_output.txt` remains an existing untracked test output and is not part of this cleanup.
