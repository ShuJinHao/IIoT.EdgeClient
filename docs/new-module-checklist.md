# New Module Checklist

Use this checklist whenever a new device/process module is added under `src/Modules`.

Before implementation, read `docs/插件开发约定.md` and follow the single `IEdgeProcessModule` contract.

## Client/cloud separation gate

- First confirm whether the task is client-only or has explicit current-task approval for cloud alignment.
- For client-only work, the final diff must not contain `IIoT.CloudPlatform` paths.
- Do not treat Cloud upload, end-to-end closure, review assumptions, or audit suggestions as permission to modify cloud code.
- Client-side Cloud upload work may add or adjust generic channel, payload mapping, retry, fallback, and dead-letter integration, but must not add cloud endpoints, events, workers, SQL, migrations, or query registration.
- If the cloud contract is not ready, the client implementation must be configurable or explicitly skipped without posting to a missing endpoint or filling retry buffers with permanent failures.
- Future process startup entries must be added through `scripts/edge-runtime.publish.json`, generated `launcher.profiles.json`, and the matching machine profile; do not add process-specific launcher XAML.

## Cloud contract placeholder rule

- When a process has no confirmed cloud contract, the uploader must return `CloudCallResult.Success()` from its pre-check and record a disabled/skipped diagnostic status.
- Do not return `Failure` for a disabled or unimplemented cloud uploader. That path is reserved for retryable upload failures and will fill the Cloud retry buffer.
- The current homogenization disabled-upload behavior is the reference pattern: skip locally, do not call HTTP, do not create Cloud retry records.

## Required implementation pieces

- Module entry implementing `IEdgeProcessModule`.
- Register services, views, runtime factories, CellData, PLC signal profiles, hardware profiles, development samples, and uploaders through `IEdgeProcessModuleBuilder`.
- Keep module data under the module: `Payload`, snapshots, PLC signal enum/profile implementation, module options, and module `Context`.
- Put shared runtime state and factories under `Runtime/`; put tasks under `Runtime/Tasks/`.
- Put PLC signal enums and profile implementations under `Config/Hardware/`; interfaces, base classes, accessors, and offset calculation belong to `Application` / `Runtime`, not plugin projects.
- Put module parameter enums under `Config/Parameters/` with exactly three files: `MesParam.cs`, `CloudParam.cs`, and `BusinessParam.cs`.
- Register parameter enums through `builder.RegisterParameters<MesParam, CloudParam, BusinessParam>()`; do not place process parameter names in host code.
- Use explicit `switch (Step)` task machines for trigger/ack PLC workflows; reserve `HeartbeatMirrorPlcTaskBase` and `PeriodicSnapshotUploadTaskBase<TSnapshot>` for heartbeat and periodic snapshot tasks.
- Runtime PLC read/write must use `ILogicalSignalAccessor<TSignalKey>` and plugin signal enums. Do not add static profile fields, string label runtime calls, JSON point seeds, or task-local offset calculation.
- Prefer `CloudUploadChannelBase<TCellData, TPayload>` and `MesScenarioChannelBase<TCellData, ...>` for uploaders.
- Use standard navigation registration extensions before adding custom ViewModel wrappers.
- Add module-focused tests for runtime behavior, upload behavior, registration, and route restrictions.

## New process execution order

Use this order when adding a new process module:

1. Confirm the task is client-only. Do not modify `IIoT.CloudPlatform` unless the user explicitly approves a separate cloud task.
2. Use the homogenization module as the structural reference for plugin shape, but keep the new process business model independent.
3. Create or update the process module entry and keep registration behind `IEdgeProcessModule` / `EdgeProcessModuleBase<TCellData>`.
4. Add process-owned folders and files under the plugin: `Payload`, `Runtime`, `Runtime/Tasks`, `Integration`, `Config/Hardware`, and `Samples`.
5. Add `Config/Parameters/MesParam.cs`, `Config/Parameters/CloudParam.cs`, and `Config/Parameters/BusinessParam.cs`; register them from the module entry.
6. Add or update the machine profile file `appsettings.machine.<Profile>.json` so `Modules:Enabled` contains only the intended process modules for that runtime.
7. Add or update the launcher profile through `scripts/edge-runtime.publish.json`; regenerate `launcher.profiles.json` with the existing script instead of editing launcher XAML.
8. Add tests for module registration, parameter enum registration, upload behavior, runtime task behavior, route restrictions, and launcher/runtime profile generation when profile data changes.

## Module parameter rules

- The host owns parameter persistence, cache invalidation, type conversion, and parameter pages.
- Plugins only declare enum members and `ModuleParamAttribute` metadata.
- Do not add process business parameters to `SharedKernel` enums. `DeviceParamKey` and `SystemConfigKey` style host parameter enums are not allowed for new process work.
- The parameter page has only three entries: MES, cloud, and business. Business parameters must come from the plugin `BusinessParam` enum; do not add a separate device-parameter tab or host-generated device parameter candidates.
- Runtime tasks must read parameters through `IModuleParamProvider<MesParam, CloudParam, BusinessParam>` and keep the returned snapshot in memory for the current operation.
- Parameter snapshot calls must use the generic API only: `p.Mes<T>(...)`, `p.Cloud<T>(...)`, and `p.Business<T>(...)`. Do not add or keep `Bool/String/Int/Decimal` helper methods.
- Do not query the database from plugin tasks just to read a module parameter.
- Do not introduce parameter-specific cache services. New cache scenarios must reuse `IEdgeCacheService.GetOrCreateAsync`.
- Future high-frequency reads such as capacity summaries, diagnostics summaries, and device metadata may use the same cache model, but should be introduced in their own focused task.

## Hardware configuration page rules

- The hardware page owns only network devices, serial devices, and IO mappings.
- Do not add module protocol summary text blocks to the hardware page. Template availability is determined by whether the selected PLC has a registered module hardware template.
- Applying a module template must import plugin standard points from the plugin hardware profile and must not overwrite maintained local addresses.
- IO mapping truth is `IoMappingEntity` saved per PLC `NetworkDeviceId`; IO interaction pages must read that selected PLC mapping and must not read plugin templates or JSON point seeds as runtime addresses.
- IO mapping pages group only by `Category`. `GroupName` may describe business meaning inside the row, but must not create extra top-level headings such as `信号交互 - 心跳交互`.
- Plugin `*PlcSignalProfile` is only the default template for applying templates and development seeding. Do not create JSON point seeds. Fixed development sample PLC devices should be defined in the sample contributor code; add a JSON seed only after the user confirms there are multiple sample sets worth configuring.
- Plugin `*PlcSignalProfile` must split standard IO points by the fixed categories `信号交互`, `单点读数据`, and `连续读数据`, then expose one `Signals` aggregate for templates/seeding. Do not maintain a flat point dump or a second JSON point list.
- Plugin registration must use `builder.RegisterPlcSignalProfile<TSignalKey, TProfile>()`, `builder.RegisterHardwareProfile<TProvider>()`, and `builder.RegisterDevelopmentSample<TContributor>()`. Do not directly register these host abstractions from plugin code.
- PLC IO scanning, read/write merge, block planning, reconnect backoff, and buffer transport belong to `IIoT.Edge.Runtime`; infrastructure projects should only provide concrete PLC communication and status reporting.
- Realtime scanning must only process `信号交互`. `单点读数据` and `连续读数据` are read by business tasks or manual debug reads and must not be added to the realtime loop.
- Plugin hardware profiles own IO runtime policy such as `SignalLoopIntervalMs`, `MaxSignalBlockWordCount`, `WriteGapPolicy`, and business read lengths. The host must not provide global production point defaults.
- IO mappings are loaded for the selected device as one full list and shown with table scrolling, not host-side paging.
- New IO buttons must be explicit: `新增信号交互` creates a paired read/write interaction group, and `新增数据点` creates only `单点读数据` or `连续读数据`. The category must not silently default to the wrong IO class.

## Development-stage cleanup rules

- The current project stage is active development. Do not keep old APIs, old enums, old ViewModels, old resource keys, compatibility adapters, or transition branches unless the user explicitly asks for that compatibility.
- A shared capability should have one entrypoint, one calling style, and one cache strategy.
- Prefer generics, interfaces, base-class templates, and decorator/AOP-style wrappers for shared concerns. Do not scatter logging, cache, retry, permission, or diagnostics logic through process tasks.
- New or modified comments must be Chinese and must explain business intent, debugging clues, or non-obvious design constraints.

## Launcher profile rules

- Launcher cards come from `launcher.profiles.json`.
- `launcher.profiles.json` is generated from `scripts/edge-runtime.publish.json`; do not hardcode process cards in `IIoT.Edge.Launcher` XAML.
- A new process profile must define `runtimeId`, `profileId`, `machineProfile`, `outputDirectory`, `machineConfig`, `moduleIds`, `displayName`, `description`, `imagePath`, `iconKind`, and `accentColor`.
- `Shell__MachineProfile` selects `appsettings.machine.<Profile>.json`; that machine profile controls `Modules:Enabled`.
- Keep profile display text Chinese. Icon, accent color, and image should match the existing launcher card style.

## Shared UI dependency rules

- `MaterialDesignThemes` is a shared UI dependency and must be referenced directly only by `IIoT.Edge.UI.Shared`.
- Launcher, Shell, Presentation projects, and plugin projects must consume MaterialDesign through `IIoT.Edge.UI.Shared`; do not add duplicate direct package references.
- Third-party package fonts such as `Resources/Noto` and `Resources/Roboto` must not be copied into project source trees or publish output.
- If a new WPF feature needs shared icons, fonts, or theme resources, add the reusable asset under `IIoT.Edge.UI.Shared/Assets/` and document why it is needed.

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
