# IIoT.EdgeClient Solution Layout

This repository has one production client solution. Use the paths below as the default placement rules.

## Production entry point

- `src/Edge/IIoT.Edge.Shell`
  - The only day-to-day production client entry point.
- `src/Edge/IIoT.Edge.Host.Bootstrap`
  - Host startup, module composition, diagnostics, and lifecycle wiring.

## Business modules

Keep all process modules in `src/Modules`.

- `src/Modules/IIoT.Edge.Module.Injection`
- `src/Modules/IIoT.Edge.Module.Stacking`
- `src/Modules/IIoT.Edge.Module.Homogenization`

New device support should be added as a new module project under `src/Modules`.

## Automated tests

Keep only automated test projects in `src/Tests`.

- `src/Tests/IIoT.Edge.Shell.Tests`
- `src/Tests/IIoT.Edge.NonUiRegressionTests`
- `src/Tests/IIoT.Edge.Module.ContractTests`

Runnable tools must not be placed in `src/Tests`.

## Quick placement rules

- New production shell features: `src/Edge`
- New process/device modules: `src/Modules`
- New automated tests: `src/Tests`
