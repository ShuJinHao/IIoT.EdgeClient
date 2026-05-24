# Phase 7 WPF Cleanup

## Summary

Phase 7 removes the remaining WPF technical stack after the Avalonia migration. This phase does not attempt to improve the current UI quality; it only establishes a pure Avalonia baseline for the next visual rework batch.

## Removed WPF Layers

- Removed `<UseWPF>true</UseWPF>` from UI.Shared, Host.Bootstrap, Navigation, Panels, and Homogenization.
- Removed `SKIP_EDGE_WPF_PROJECTS` transition settings from Shell and Presentation projects.
- Removed `MaterialDesignThemes` and AvalonDock package references and central package versions.
- Deleted old `.xaml` files and WPF code-behind under `src`.
- Deleted UI.Shared WPF view helpers, WPF localization behavior, and the WPF control-based notify base class.
- Deleted the old WPF `INavigationService` and Host.Bootstrap `NavigationService`.
- Kept `IViewRegistry`, `ViewRegistry`, `HostViewRegistry`, and `ModuleViewRegistry` because the active Avalonia route path depends on them.

## Test Cleanup

- Removed WPF STA dispatcher test infrastructure.
- Replaced old NavigationService tests with `IViewRegistry` registration contract tests.
- Updated language resource tests to read `.axaml` dictionaries.
- Removed obsolete WPF page/window instantiation tests.

## Verification

Builds:

```powershell
dotnet build "IIoT.EdgeClient.slnx" /m:1 /p:UseSharedCompilation=false
dotnet build "src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj" /m:1 /p:UseSharedCompilation=false
dotnet build "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" /m:1 /p:UseSharedCompilation=false
```

Results:

- Solution build: 0 warnings, 0 errors.
- Shell build: 0 warnings, 0 errors.
- Launcher build: build passed; standalone Launcher build still reports Avalonia `AVLN3001` warnings for DI-only windows without public parameterless constructors. This is not WPF residue and was not changed in P7.

Tests:

```powershell
dotnet test "src\Tests\IIoT.Edge.Shell.Tests\IIoT.Edge.Shell.Tests.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test "src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test "src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test "src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
```

Results:

- Shell tests: 60 passed.
- Launcher tests: 22 passed.
- Module contract tests: 28 passed.
- Non-UI regression tests: 367 passed.

Terminal cleanup scans returned no output for:

```powershell
rg "<UseWPF>true</UseWPF>|UseWPF.*true" src
rg "MaterialDesignThemes|MaterialDesignInXaml|Dirkster\.AvalonDock|AvalonDock" src Directory.Packages.props
Get-ChildItem -Path src -Filter *.xaml -Recurse
rg "System\.Windows\.(Application|Controls|Threading|Data|Markup|Media)|using System\.Windows;" src
rg "INavigationService|NavigationService" src
rg "IIoT\.Edge\.UI\.Avalonia|\.Legacy|AddEdgeHostAvaloniaBootstrap|AvaloniaShellStartupCoordinator|AvaloniaShellBootstrapOptionsFactory|IAvalonia[A-Z]" src
rg "CommunityToolkit\.Mvvm|ReactiveUI|Prism\.|Dock\.Avalonia|SukiUI|DialogHost\.Avalonia|Material\.Avalonia" src Directory.Packages.props
```

## Remaining Work

- UI quality is explicitly out of scope for P7. The current visual result should be handled by a separate UI visual rework batch.
- No WPF fallback or compatibility layer remains.
