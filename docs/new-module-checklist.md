# New Module Checklist

Use this checklist whenever a new device/process module is added under `src/Modules`.

Do not place runnable developer tools or sample shells under `src/Tests`; those belong in `src/Tools`.

## Required implementation pieces

- Module entry implementing `IEdgeStationModule`
- `CellData` registration
- PLC runtime factory registration
- Cloud uploader registration
- Hardware profile provider registration
- Navigation and view registration
- Module-focused tests

## Required behavior rules

- Module IDs and process types must stay unique.
- Module views must use `<ModuleId>.*`.
- Do not register `Core.*` routes from a module.
- Do not place module-specific runtime, upload, or hardware logic back into host core.
- New device/process support starts as a new module, not as a host `if/else`.

## Required verification

- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -p:BuildInParallel=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -p:BuildInParallel=false --disable-build-servers`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -p:BuildInParallel=false --disable-build-servers`
- `pwsh scripts/RunSingleRepoReleaseRehearsal.ps1 -SkipIntegrationValidation`

## When a contract changes

Document all of the following in the PR:

- Why the existing module contract is not enough
- Which existing modules must change
- Whether historical Injection compatibility changes
- Whether package assembly validation still passes
