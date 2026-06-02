# Visual Test Data Wiring - 2026-06-02

## Scope

This record covers the temporary UI visual-acceptance data wiring for `IIoT.EdgeClient`.

The change is intentionally limited to:

- `IIoT.Edge.Presentation.VisualTestData`
- Shell DI composition
- Shell UI configuration

It does not change Application handlers, Domain models, Runtime tasks, Infrastructure, PLC, MES, Cloud, cache, upload, retry, dead-letter, or production persistence chains.

## Behavior

`UI:VisualTestData:Enabled` controls the temporary binding source.

When enabled, Shell replaces only these display-layer facades:

- `IEquipmentPanelService`
- `ICapacityQueryFacade`
- `IProductionDataQueryFacade`
- `IMonitorSnapshotQueryFacade`

The replacements return ViewModel-ready snapshots for visual inspection. They do not write databases, do not call cloud APIs, do not start PLC reads, do not write runtime context, and do not publish upload state.

## Database Seed Boundary

Database-backed hardware pages continue to use the existing development sample path:

- `DevelopmentSamples:Enabled`
- `Modules:Homogenization:DeviceSeed:Enabled`

That path seeds the local Homogenization PLC device and IO mappings only. It remains controlled by the existing plugin configuration.

## Restore Rule

To restore real data bindings, set all active configuration overlays to:

```json
"UI": {
  "VisualTestData": {
    "Enabled": false
  }
}
```

For full cleanup after visual acceptance, remove:

- `src/Presentation/IIoT.Edge.Presentation.VisualTestData/`
- the Host.Bootstrap project reference and DI call
- the Shell.Tests project reference
- the solution entry
- the temporary `UI:VisualTestData` configuration entries
