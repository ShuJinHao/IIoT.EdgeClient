# IIoT.EdgeClient Single-Repo Collaboration Guide

This repository is the only Edge collaboration entry point. Modules stay pluginized in code and at runtime, but day-to-day Git maintenance stays inside this repository.

## Current rule set

- Keep the host boundary inside `src/Edge/IIoT.Edge.Host.Bootstrap` and `src/Shared`.
- Keep device/process behavior inside dedicated module projects under `src/Modules`.
- Keep runnable non-production tools inside `src/Tools`.
- Keep the package validation shell in `src/Tools/IIoT.Edge.PackageValidationClient`; it is a validation tool, not a second production client.

## Path ownership

- Host core:
  - `src/Edge/IIoT.Edge.Host.Bootstrap`
  - `src/Shared/IIoT.Edge.Module.Abstractions`
  - `src/Shared/IIoT.Edge.Module.Contracts`
- Module code:
  - `src/Modules/IIoT.Edge.Module.Injection`
  - `src/Modules/IIoT.Edge.Module.Stacking`
  - `src/Modules/IIoT.Edge.Module.Homogenization`
- Tools and samples:
  - `src/Tools/IIoT.Edge.TestSimulator`
  - `src/Tools/IIoT.Edge.PackageValidationClient`
  - `src/Tools/ModuleSamples/IIoT.Edge.Module.DryRun`

During the adaptation phase, `CODEOWNERS` routes all approvals to `@ShuJinHao`. When module owners are stable, replace the module entries with their real GitHub usernames.

## Pull request rules

- Protect `main`; do not push directly.
- Require PRs for every change.
- Use `.github/pull_request_template.md` for every PR.
- If a PR touches host core and a module, review it as a host-core change.
- New device/process modules should be created directly under `src/Modules`.
- New runnable developer tools go under `src/Tools`.
- Do not place runnable tools under `src/Tests`.
- Any module-contract change must explain:
  - why the current contract is insufficient
  - which modules are affected
  - whether existing module loading or runtime registration behavior is impacted

## Required checks for `main`

Configure these required status checks in GitHub branch protection:

- `edge-smoke-build / smoke-build`
- `edge-pack-sdk / validate-sdk`
- `edge-pack-modules / validate-modules`

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

- Host, launcher, modules, and shared contracts all stay in this repository.
- Directory-style runtime publish is the official delivery path.
- Package-only assembly validation stays in `src/Tools/IIoT.Edge.PackageValidationClient`.
- Official production behavior still follows the main Edge shell inside this repository.

## Recommended release rehearsal

Before any formal release, run:

- `pwsh scripts/RunSingleRepoReleaseRehearsal.ps1`

This runs shell build, shell contract tests, non-UI regression tests, package generation, and package-only integration build in one sequence.
