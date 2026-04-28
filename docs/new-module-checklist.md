# New Module Checklist

Use this checklist whenever a new device/process module is added under `src/Modules`.

Before implementation, read `docs/插件开发约定.md` and follow the single `IEdgeProcessModule` contract.

## Required implementation pieces

- Module entry implementing `IEdgeProcessModule`.
- Register services, views, runtime factories, CellData, hardware profiles, and uploaders through `IEdgeProcessModuleBuilder`.
- Keep module data under the module: `Payload`, snapshots, PLC signal profile, module options, and module `Context`.
- Put shared runtime state and factories under `Runtime/`; put tasks under `Runtime/Tasks/`.
- Use explicit `switch (Step)` task machines for trigger/ack PLC workflows; reserve `HeartbeatMirrorPlcTaskBase` and `PeriodicSnapshotUploadTaskBase<TSnapshot>` for heartbeat and periodic snapshot tasks.
- Prefer `ProcessCloudUploaderBase<TCellData, TPayload>` and `ProcessMesUploaderBase<TCellData>` for uploaders.
- Use standard navigation registration extensions before adding custom ViewModel wrappers.
- Add module-focused tests for runtime behavior, upload behavior, registration, and route restrictions.

## Required behavior rules

- Module IDs and process types must stay unique.
- Module views must use `<ModuleId>.*`.
- Do not register `Core.*` routes from a module.
- Do not place module-specific runtime, upload, payload, or hardware logic back into host core.
- Do not duplicate host/shared infrastructure inside a plugin when a shared base class or builder API exists.
- Do not mix Cloud and MES upload, retry, diagnostics, or SQLite compensation channels.
- New device/process support starts as a new module, not as a host `if/else`.
- The host supports one plugin entry contract only: `IEdgeProcessModule`.

## Required verification

- `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -p:BuildInParallel=false`
- `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -p:BuildInParallel=false --disable-build-servers`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -p:BuildInParallel=false --disable-build-servers`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -p:BuildInParallel=false --disable-build-servers`

## When a contract changes

Document all of the following in the PR:

- Why the existing module contract is not enough.
- Which existing modules must change.
- Whether old-interface compatibility is being added. Default answer should be no unless the user explicitly approved it.
- Whether package assembly validation still passes.
