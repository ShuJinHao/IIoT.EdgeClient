# Phase 6.5-A Visual System Pilot

## Summary

Phase 6.5-A builds the first shared Avalonia visual-system layer in `IIoT.Edge.UI.Shared` and applies it only to the Equipment panel as the pilot page.

No business data sources, ViewModels, Launcher files, runtime chains, module runtime code, or startup paths were changed.

## Shared Tokens

`EdgeTheme.axaml` now defines:

- raw color tokens with Lime `#C6F432` as the primary accent starting point;
- surface, border, text, accent, and semantic status brushes;
- status brushes for `Running`, `Idle`, `Stopped`, `Offline`, `Info`, `Cache`, `Warning`, and `Error`;
- corner radius, spacing thickness, font size, font family, and shadow tokens.

## Shared Controls

The first shared control set is under `UI.Shared/Avalonia/Controls`:

- `EdgeCard`
- `EdgeStatusDot`
- `EdgeStatusChip`
- `EdgeSectionHeader`
- `EdgeKpiCard`

These controls are UI-only. They do not access services, runtime state, PLC, MES, Cloud, logs, or fake data.

## Pilot Page

The only pilot page is:

`src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/Views/EquipmentView.axaml`

The page now uses shared card, section header, and status chip controls while keeping the existing real bindings.

## Objective Metrics

Equipment pilot metrics:

| Metric | Before | Target | Notes |
|---|---:|---:|---|
| Hex colors | 0 | 0 | Page stays token-based. |
| `CornerRadius` occurrences | 9 | 0 | Shared controls own radius token usage. |
| `BorderBrush` occurrences | 7 | 0 | Card borders are centralized in `EdgeCard`. |
| Handwritten status dots | 0 | 0 | No page-local dot structure remains. |
| Handwritten status chips | 4 | 0 | Connection and recipe status use `EdgeStatusChip`. |

## Verification

Required checks:

```powershell
cd "C:\Users\jinha\Desktop\产线系统架构升级\1\IIoT.EdgeClient"
Test-Path "docs\avalonia-migration\phase-6.5\visual-inventory.md"
dotnet restore "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj"
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
rg "IIoT\.Edge\.UI\.Avalonia|\.Legacy|IAvalonia[A-Z]|AddEdgeHostAvaloniaBootstrap|AvaloniaShellStartupCoordinator|AvaloniaShellBootstrapOptionsFactory" src
rg "CommunityToolkit\.Mvvm|ReactiveUI|Prism\.|Dock\.Avalonia|Material\.Avalonia|Material\.Icons\.Avalonia|DialogHost\.Avalonia|LucideAvalonia" Directory.Packages.props src
rg "#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}" src\Edge src\Presentation src\Modules --glob "*.axaml"
rg "CornerRadius" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\Equipment\Views\EquipmentView.axaml"
rg "BorderBrush=" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\Equipment\Views\EquipmentView.axaml"
git diff -- src\Core src\Application src\Runtime src\Infrastructure src\Edge\IIoT.Edge.Host.Bootstrap
```

## Remaining Work

- Visual screenshots were not captured in this pass.
- Shell frame, Dashboard, Navigation pages, Homogenization, and Launcher visual alignment remain in later Phase 6.5 sub-phases.
- Launcher's dark theme is intentionally untouched until P6.5-E.

## Phase 6.5-B Shell Frame Pass

Phase 6.5-B applies the shared visual system to the Shell outer frame only:

- Main window frame now opens maximized with a 1280x720 minimum and keeps the fixed 320px right rail.
- Header now uses the left-group / spacer / right-group layout with shared `EdgeStatusChip` controls for running status, local mode, and profile.
- Notification is visual-only and does not expose a click path before a real alert source exists.
- Footer now shows system status, version, Edge ID, and a local-time display driven by an Avalonia `DispatcherTimer`.
- NavRail selected state now uses a full Lime tile with dark icon/text instead of the previous left accent bar.
- Existing dynamic NavRail routing remains unchanged; the top brand mark is intentionally preserved for the P6.5-F final pass.

Verification performed:

```powershell
dotnet restore "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj"
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
rg "#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}" "src\Edge\IIoT.Edge.Shell" "src\Presentation\IIoT.Edge.Presentation.Shell\Views" "src\Presentation\IIoT.Edge.Presentation.Navigation\Features\Shell"
rg "IIoT\.Edge\.UI\.Avalonia|\.Legacy|IAvalonia[A-Z]|AddEdgeHostAvaloniaBootstrap|AvaloniaShellStartupCoordinator|AvaloniaShellBootstrapOptionsFactory" src
git diff -- src\Core src\Application src\Runtime src\Infrastructure src\Edge\IIoT.Edge.Host.Bootstrap
```

Notes:

- Visual screenshots were not captured in this pass.
- `System.Windows.Input.ICommand` remains in Navigation ViewModel code as the existing command interface and is not treated as WPF UI surface.

## Phase 6.5-C Dashboard And Right Rail Pass

Phase 6.5-C applies the shared visual system to the Dashboard, right rail panels, and startup failure dialog:

- Added `EdgeStatusListItem` as a shared UI-only status row for Dashboard devices, Equipment hardware status, and log entries.
- Added `LogLevelToEdgeVisualStatusConverter` for UI-only log level color mapping.
- Dashboard now uses `EdgeSectionHeader`, `EdgeKpiCard`, `EdgeCard`, and `EdgeStatusListItem`; no fake trend chart or fake KPI data was introduced.
- Equipment right rail no longer uses DataGrid for hardware or recipe parameters; both are vertical lists suited to the fixed 320px rail.
- Log right rail keeps binding to real `Entries` and `ClearCommand`; the clear button still only clears the UI collection.
- `ShellCrashDialog` now uses a borderless card layout with drag support and preserves the existing startup-failure close behavior.

Verification performed:

```powershell
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
rg "#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}" "src\Presentation\IIoT.Edge.Presentation.Navigation\Features\Dashboard" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\Equipment\Views\EquipmentView.axaml" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\SysLog\Views\LogView.axaml" "src\Edge\IIoT.Edge.Shell\ShellCrashDialog.axaml"
rg 'CornerRadius="8|CornerRadius="999|Edge\.Success|Edge\.Danger' "src\Presentation\IIoT.Edge.Presentation.Navigation\Features\Dashboard" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\Equipment\Views\EquipmentView.axaml" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\SysLog\Views\LogView.axaml"
rg "DataGrid" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\Equipment\Views\EquipmentView.axaml" "src\Presentation\IIoT.Edge.Presentation.Panels\Features\SysLog\Views\LogView.axaml"
git diff -- src\Core src\Application src\Runtime src\Infrastructure src\Edge\IIoT.Edge.Host.Bootstrap
```

Notes:

- Visual screenshots were not captured in this pass.
- The broad `.Legacy` scan still matches the existing non-UI test string `Homogenization.Legacy`; this is not a parallel UI project.
