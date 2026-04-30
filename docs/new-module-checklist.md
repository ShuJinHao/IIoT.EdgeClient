# New Module Checklist

Use this checklist whenever a new device/process module is added under `src/Modules`.

Before implementation, read `docs/插件开发约定.md` and follow the single `IEdgeProcessModule` contract.

## Client/cloud separation gate

- First confirm whether the task is client-only or has explicit current-task approval for cloud alignment.
- For client-only work, the final diff must not contain `IIoT.CloudPlatform` paths.
- Do not treat Cloud upload, end-to-end closure, review assumptions, or audit suggestions as permission to modify cloud code.
- Client-side Cloud upload work may add or adjust generic channel, payload mapping, retry, fallback, and dead-letter integration, but must not add cloud endpoints, events, workers, SQL, migrations, or query registration.
- If the cloud contract is not ready, the client implementation must be configurable or explicitly skipped without posting to a missing endpoint or filling retry buffers with permanent failures.
- Stacking and future process startup entries must be added through `scripts/edge-runtime.publish.json`, generated `launcher.profiles.json`, and the matching machine profile; do not add process-specific launcher XAML.

## Required implementation pieces

- Module entry implementing `IEdgeProcessModule`.
- Register services, views, runtime factories, CellData, hardware profiles, and uploaders through `IEdgeProcessModuleBuilder`.
- Keep module data under the module: `Payload`, snapshots, PLC signal profile, module options, and module `Context`.
- Put shared runtime state and factories under `Runtime/`; put tasks under `Runtime/Tasks/`.
- Use explicit `switch (Step)` task machines for trigger/ack PLC workflows; reserve `HeartbeatMirrorPlcTaskBase` and `PeriodicSnapshotUploadTaskBase<TSnapshot>` for heartbeat and periodic snapshot tasks.
- Prefer `CloudUploadChannelBase<TCellData, TPayload>` and `MesScenarioChannelBase<TCellData, ...>` for uploaders.
- Use standard navigation registration extensions before adding custom ViewModel wrappers.
- Add module-focused tests for runtime behavior, upload behavior, registration, and route restrictions.

## New process execution order

Use this order when adding a new process module:

1. Confirm the task is client-only. Do not modify `IIoT.CloudPlatform` unless the user explicitly approves a separate cloud task.
2. Use the homogenization module as the structural reference for plugin shape, but keep the new process business model independent.
3. Create or update the process module entry and keep registration behind `IEdgeProcessModule` / `EdgeProcessModuleBase<TCellData>`.
4. Add process-owned folders and files under the plugin: `Payload`, `Runtime`, `Runtime/Tasks`, `Integration`, `Config/Hardware`, and `Samples`.
5. Add or update the machine profile file `appsettings.machine.<Profile>.json` so `Modules:Enabled` contains only the intended process modules for that runtime.
6. Add or update the launcher profile through `scripts/edge-runtime.publish.json`; regenerate `launcher.profiles.json` with the existing script instead of editing launcher XAML.
7. Add tests for module registration, upload behavior, runtime task behavior, route restrictions, and launcher/runtime profile generation when profile data changes.

## Launcher profile rules

- Launcher cards come from `launcher.profiles.json`.
- `launcher.profiles.json` is generated from `scripts/edge-runtime.publish.json`; do not hardcode process cards in `IIoT.Edge.Launcher` XAML.
- A new process profile must define `runtimeId`, `profileId`, `machineProfile`, `outputDirectory`, `machineConfig`, `moduleIds`, `displayName`, `description`, `imagePath`, `iconKind`, and `accentColor`.
- `Shell__MachineProfile` selects `appsettings.machine.<Profile>.json`; that machine profile controls `Modules:Enabled`.
- Keep profile display text Chinese. Icon, accent color, and image should match the existing launcher card style.

## Stacking process standard

- Stacking uses `ModuleId = Stacking`, `ProcessType = Stacking`, display name `叠片`, and machine profile `StackingLine`.
- Stacking should follow the homogenization module's plugin structure, but its business model follows stacking rules: multi-cell data, PLC ID mapping, barcode duplicate handling, and stacking-specific PLC tasks.
- Stacking startup is added by profile configuration only. Do not create a stacking-specific launcher page, button, or XAML branch.

## Required behavior rules

- Module IDs and process types must stay unique.
- Module views must use `<ModuleId>.*`.
- Do not register `Core.*` routes from a module.
- Do not place module-specific runtime, upload, payload, or hardware logic back into host core.
- Do not duplicate host/shared infrastructure inside a plugin when a shared base class or builder API exists.
- Do not mix Cloud and MES upload, retry, diagnostics, or SQLite compensation channels.
- New device/process support starts as a new module, not as a host `if/else`.
- The host supports one plugin entry contract only: `IEdgeProcessModule`.
- Do not bypass `IEdgeProcessModuleBuilder`.
- Do not modify cloud projects from a client-only module task.
- Do not add process-specific launcher XAML when a launcher profile can represent the startup item.

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
