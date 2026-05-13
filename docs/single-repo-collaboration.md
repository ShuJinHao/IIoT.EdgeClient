# IIoT.EdgeClient Single-Repo Collaboration Guide

This repository is the only Edge collaboration entry point. Modules stay pluginized in code and at runtime, but day-to-day Git maintenance stays inside this repository.

## Current rule set

- Keep the host loading boundary inside `src/Edge/IIoT.Edge.Host.Bootstrap`; keep plugin public contracts inside `src/Application/IIoT.Edge.Application`.
- Keep `src/Shared` limited to `IIoT.Edge.SharedKernel` and `IIoT.Edge.UI.Shared`.
- Keep device/process behavior inside dedicated module projects under `src/Modules`.
- Keep production support scripts under `scripts`; do not keep runnable tool projects under `src`.

## Path ownership

- Host core:
  - `src/Edge/IIoT.Edge.Host.Bootstrap`
  - `src/Application/IIoT.Edge.Application`
  - `src/Shared/IIoT.Edge.SharedKernel`
  - `src/Shared/IIoT.Edge.UI.Shared`
- Module code:
  - `src/Modules/IIoT.Edge.Module.Homogenization`
During the adaptation phase, `CODEOWNERS` routes all approvals to `@ShuJinHao`. When module owners are stable, replace the module entries with their real GitHub usernames.

## Pull request rules

- Protect `main`; do not push directly.
- Require PRs for every change.
- Use `.github/pull_request_template.md` for every PR.
- If a PR touches host core and a module, review it as a host-core change.
- New device/process modules should be created directly under `src/Modules`.
- Do not place runnable tool projects under `src`.
- Any module-contract change must explain:
  - why the current contract is insufficient
  - which modules are affected
  - whether existing module loading or runtime registration behavior is impacted

## Required checks for `main`

Configure these required status checks in GitHub branch protection:

- `edge-smoke-build / smoke-build`
- `edge-runtime-package / validate-runtime`

The exact job names come from the workflows under `.github/workflows`.

## Manual GitHub setup

Apply these settings in GitHub repository settings:

1. Settings -> Branches -> Add branch protection rule.
2. Branch name pattern: `main`.
3. Enable:
   - Require a pull request before merging
   - Require approvals
   - Require review from Code Owners
   - Require status checks to pass before merging
   - Do not allow bypassing the above settings
4. Select the required checks listed above.

## Release behavior

- Host, launcher, product modules, Application contracts, SharedKernel, and UI.Shared all stay in this repository.
- Directory-style runtime publish is the official delivery path.
- Official production behavior still follows the main Edge shell inside this repository.

## Recommended release rehearsal

Before any formal release, run:

- `pwsh scripts/TestEdgeRuntimePublish.ps1 -Configuration Release`

This validates the directory-style runtime publish output without reintroducing NuGet package feeds.
