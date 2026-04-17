# IIoT.EdgeClient Single-Repo Collaboration Guide

This repository is the single collaboration entry point for the Edge client during the adaptation phase.

## Current rule set

- Keep one GitHub repository: `IIoT.EdgeClient`.
- Keep the host boundary inside `src/Edge/IIoT.Edge.Host.Bootstrap` and `src/Shared/IIoT.Edge.Module.Abstractions`.
- Keep device/process behavior inside `src/Modules/IIoT.Edge.Module.*`.
- Keep `IIoT.Edge.Integration.DefaultClient` as a package-assembly validation sample, not as a daily-maintained production repository.

## Path ownership

- Host core:
  - `src/Edge/IIoT.Edge.Host.Bootstrap`
  - `src/Shared/IIoT.Edge.Module.Abstractions`
- Module code:
  - `src/Modules/IIoT.Edge.Module.Injection`
  - `src/Modules/IIoT.Edge.Module.Stacking`

During the adaptation phase, `CODEOWNERS` routes all approvals to `@ShuJinHao`. When module owners are stable, replace the module entries with their real GitHub usernames.

## Pull request rules

- Protect `main`; do not push directly.
- Require PRs for every change.
- Use `.github/pull_request_template.md` for every PR.
- If a PR touches host core and a module, review it as a host-core change.
- Any module-contract change must explain:
  - why the current contract is insufficient
  - which modules are affected
  - whether Injection historical compatibility is impacted

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

## Release behavior in the single-repo phase

- Daily development stays in this repository.
- Package generation stays enabled through `scripts/PackEdgePackages.ps1`.
- Package-only assembly validation stays in `IIoT.Edge.Integration.DefaultClient`.
- Official production behavior still follows the main Edge shell inside this repository until the team is ready for a dedicated integration repository workflow.

## Recommended release rehearsal

Before any formal release, run:

- `pwsh scripts/RunSingleRepoReleaseRehearsal.ps1`

This runs shell build, shell contract tests, non-UI regression tests, package generation, and package-only integration build in one sequence.
