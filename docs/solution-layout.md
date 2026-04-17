# IIoT.EdgeClient Solution Layout

This repository has one production client and several supporting projects with different roles. Use the paths below as the default placement rules.

## Production entry point

- `src/Edge/IIoT.Edge.Shell`
  - The only day-to-day production client entry point.
- `src/Edge/IIoT.Edge.Host.Bootstrap`
  - Host startup, module composition, diagnostics, and lifecycle wiring.

## Business modules

Keep real device/process modules in `src/Modules` only.

- `src/Modules/IIoT.Edge.Module.Injection`
- `src/Modules/IIoT.Edge.Module.Stacking`

New device support should start as a new module here.

## Tools

Keep runnable developer tools and validation shells in `src/Tools`.

- `src/Tools/IIoT.Edge.TestSimulator`
  - Hardware-free simulator for local development.
- `src/Tools/IIoT.Edge.PackageValidationClient`
  - Package-only restore/build validation shell.
  - This is not a second production client.
- `src/Tools/ModuleSamples/IIoT.Edge.Module.DryRun`
  - Internal sample module for scaffold validation, contract tests, and release rehearsal.
  - This is not a formal business module.

## Automated tests

Keep only automated test projects in `src/Tests`.

- `src/Tests/IIoT.Edge.Shell.Tests`
- `src/Tests/IIoT.Edge.NonUiRegressionTests`
- `src/Tests/IIoT.Edge.Module.ContractTests`

Runnable tools must not be placed in `src/Tests`.

## Quick placement rules

- New production shell features: `src/Edge`
- New process/device modules: `src/Modules`
- New local developer tools: `src/Tools`
- New sample/fixture modules: `src/Tools/ModuleSamples`
- New automated tests: `src/Tests`
