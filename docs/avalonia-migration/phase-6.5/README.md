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

## Phase 6.5-D Navigation Business Pages Pass

Phase 6.5-D applies the shared visual system to the remaining Navigation business pages and the Homogenization module display page:

- Production pages now use `EdgeSectionHeader`, `EdgeCard`, and `EdgeKpiCard` for query areas, KPI summaries, tables, and monitor summaries.
- Recipe, Param, and Homogenization pages now use shared section headers and card surfaces while preserving existing commands and bindings.
- Hardware IO, IO mapping, and PLC task binding pages now use shared card/status surfaces for the major business sections while keeping high-density matrix and DataGrid layouts where they are operationally useful.
- D-scope pages no longer contain page-local hex colors, `CornerRadius="8"` / `CornerRadius="999"`, or old status brushes such as `Edge.Success`, `Edge.Danger`, `Edge.Warning`, and `Edge.Info`.
- No ViewModel, service, DI, startup, module runtime, PLC/MES/Cloud/cache/retry/dead-letter logic was changed.

Verification performed:

```powershell
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
Select-String -Path $targets -Pattern '#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}'
Select-String -Path $targets -Pattern 'CornerRadius="8|CornerRadius="999'
Select-String -Path $targets -Pattern 'Edge\.Success|Edge\.Danger|Edge\.Warning|Edge\.Info'
Select-String -Path $targets -Pattern 'materialDesign:|PackIcon|System\.Windows|MessageBox|DialogHost|Dock\.Avalonia|Material\.Avalonia'
git diff --name-only HEAD -- "src\Edge\IIoT.Edge.Launcher" "src\Edge\IIoT.Edge.Shell" "src\Presentation\IIoT.Edge.Presentation.Shell" "src\Presentation\IIoT.Edge.Presentation.Panels" "src\Presentation\IIoT.Edge.Presentation.Navigation\Features\Dashboard" "src\Core" "src\Application" "src\Runtime" "src\Infrastructure" "src\Edge\IIoT.Edge.Host.Bootstrap" "src\Modules\IIoT.Edge.Module.Homogenization\Config" "src\Modules\IIoT.Edge.Module.Homogenization\Runtime" "src\Modules\IIoT.Edge.Module.Homogenization\Integration" "src\Modules\IIoT.Edge.Module.Homogenization\Payload"
```

Notes:

- Visual screenshots were not captured in this pass.
- `src/Tests/IIoT.Edge.NonUiRegressionTests/edge_test_output.txt` remains untracked and is not part of this phase.

## Phase 6.5-E Launcher Visual Pass

Phase 6.5-E applies the shared light visual system to the Avalonia Launcher only:

- `App.axaml` now requests the light theme and loads `UI.Shared`'s `EdgeTheme.axaml` before Launcher-local resources.
- `LauncherTheme.axaml` no longer owns a dark standalone palette; Launcher-local brushes now map to the shared `Edge.*` color semantics.
- `LauncherControls.axaml` now uses shared radius, thickness, font, accent, and text tokens for launcher surfaces, chips, inputs, and buttons.
- `MainWindow.axaml` keeps the existing login, profile selection, and launch bindings while presenting them through the shared light visual language.
- `ChangePasswordWindow.axaml` keeps the existing four password inputs and button bindings while using the shared light card styling.
- No `.cs` file, project file, JSON configuration, authentication service, password hash logic, Shell launch service, or profile catalog was changed.

Verification performed:

```powershell
dotnet build "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
rg "RequestedThemeVariant=`"Dark`"|#0F1518|#161E22|#1D272C|#202B31|#344148|#0B1013|#131C21|#0E151A" "src\Edge\IIoT.Edge.Launcher" --glob "*.axaml"
rg "#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}" "src\Edge\IIoT.Edge.Launcher\MainWindow.axaml" "src\Edge\IIoT.Edge.Launcher\ChangePasswordWindow.axaml" "src\Edge\IIoT.Edge.Launcher\App.axaml"
rg "MaterialDesign|PackIcon|System\.Windows|MessageBox|DialogHost|Dock\.Avalonia|Material\.Avalonia" "src\Edge\IIoT.Edge.Launcher"
git diff --name-only HEAD -- "Directory.Packages.props" "src\Edge\IIoT.Edge.Launcher\App.axaml.cs" "src\Edge\IIoT.Edge.Launcher\MainWindow.axaml.cs" "src\Edge\IIoT.Edge.Launcher\ChangePasswordWindow.axaml.cs" "src\Edge\IIoT.Edge.Launcher\ViewModels" "src\Edge\IIoT.Edge.Launcher\Services" "src\Edge\IIoT.Edge.Launcher\Configuration" "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" "src\Edge\IIoT.Edge.Launcher\launcher.accounts.sample.json" "src\Edge\IIoT.Edge.Launcher\launcher.accounts.json" "src\Edge\IIoT.Edge.Launcher\launcher.profiles.json" "src\Core" "src\Application" "src\Runtime" "src\Infrastructure" "src\Edge\IIoT.Edge.Host.Bootstrap" "src\Modules"
```

Notes:

- Visual screenshots were not captured in this pass.
- The Launcher build reports the existing Avalonia `AVLN3001` warnings for DI-only window constructors; this pass intentionally did not modify `.cs` files to suppress them.

## Phase 6.5-F Final Visual Acceptance Pass

Phase 6.5-F closes the visual polish phase with real application screenshots, small screenshot-driven fixes, and a P7 cleanup candidate list.

The pass stayed within visual-only boundaries:

- no business service, ViewModel, DI, startup, PLC/MES/Cloud/cache/retry/dead-letter path changed;
- no dependency, project, JSON, password hash, account catalog, profile catalog, or package cleanup was performed;
- no old WPF files were deleted in this phase.

Screenshot-driven micro-fixes applied:

- `ShellHeaderView.axaml`: replaced the visual-only menu `Button` wrapper with a transparent non-hit-test `Border`, removing the gray button block visible in Shell screenshots.
- `Launcher/MainWindow.axaml`: tightened the default launcher window size and login layout for 1366x768, wrapped the login title, avoided profile-card header collision, and converted the right hero metadata chips to a vertical stack to prevent clipping.

Final screenshots captured from real running programs:

- `screenshots/1366x768/launcher-login.png`
- `screenshots/1366x768/launcher-selection.png`
- `screenshots/1366x768/launcher-change-password.png`
- `screenshots/1366x768/shell-dashboard.png`
- `screenshots/1366x768/shell-recipe.png`
- `screenshots/1366x768/shell-param.png`
- `screenshots/1366x768/shell-diagnostics.png`
- `screenshots/1366x768/shell-hardware.png`
- `screenshots/1366x768/shell-homogenization.png`
- `screenshots/1366x768/shell-crash-dialog.png`
- `screenshots/1600x1000/launcher-login.png`
- `screenshots/1600x1000/launcher-selection.png`
- `screenshots/1600x1000/launcher-change-password.png`
- `screenshots/1600x1000/shell-dashboard.png`
- `screenshots/1900x1200/shell-dashboard.png`

Verification performed:

```powershell
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
dotnet build "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
Get-ChildItem -Path "src\Edge","src\Presentation","src\Modules" -Recurse -Filter *.axaml |
  Select-String -Pattern '#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}|RequestedThemeVariant="Dark"|materialDesign:|PackIcon|DialogHost|Dock\.Avalonia|Material\.Avalonia'
Get-ChildItem -Path "src\Edge","src\Presentation","src\Modules" -Recurse -Filter *.axaml |
  Select-String -Pattern 'Edge\.Success|Edge\.Danger|Edge\.Warning|Edge\.Info|CornerRadius="8|CornerRadius="999'
git diff --name-only HEAD -- "Directory.Packages.props" "src\Core" "src\Application" "src\Runtime" "src\Infrastructure" "src\Edge\IIoT.Edge.Host.Bootstrap" "src\Modules\IIoT.Edge.Module.Homogenization\Config" "src\Modules\IIoT.Edge.Module.Homogenization\Runtime" "src\Modules\IIoT.Edge.Module.Homogenization\Integration" "src\Modules\IIoT.Edge.Module.Homogenization\Payload"
```

Results:

- Shell build: passed with 0 warnings and 0 errors.
- Launcher build: passed with 0 warnings and 0 errors.
- Visual residual scan: no hex colors, dark theme flag, MaterialDesign, PackIcon, DialogHost, Dock, or Material.Avalonia residuals in `.axaml`.
- Old status/corner scan: one `CornerRadius="8,0,0,8"` remains in `ShellNavRailView.axaml`; it is not referenced by the active Shell path and is recorded in `p7-cleanup-candidates.md`.
- Frozen path diff: empty for packages, Core, Application, Runtime, Infrastructure, Host.Bootstrap, and Homogenization business folders.

Phase 6.5 visual polish is complete. Cleanup-only work moves to Phase 7.
