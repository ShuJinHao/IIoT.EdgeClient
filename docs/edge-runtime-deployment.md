# Edge Runtime Deployment

Use this guide when assembling the multi-process Edge runtime package for local launcher deployments.

## Runtime package layout

The runtime package is now a directory tree, not a single shared runtime root.

Recommended layout:

- `launcher/IIoT.Edge.Launcher.exe`
- `launcher/launcher.profiles.json`
- `launcher/launcher.accounts.sample.json`
- `launcher/Assets/Profiles/*`
- `homogenization/IIoT.Edge.Shell.exe`
- `homogenization/appsettings.machine.HomogenizationLine.json`
- `homogenization/Modules/Homogenization/*`

`launcher.profiles.json` is generated from `scripts/edge-runtime.publish.json`. Do not hand-maintain a second card list.

Each process runtime keeps only its own modules. Process isolation is achieved by:

- separate runtime directory
- `Shell__MachineProfile`
- `Shell:RuntimeDataRoot`

## Publish steps

Publish the full runtime layout:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\PublishEdgeRuntime.ps1 -Configuration Release -CleanOutput
```

Run the smoke test before shipping:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\TestEdgeRuntimePublish.ps1 -Configuration Release
```

If you only need to refresh module payloads inside a specific runtime root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\PublishEdgeModules.ps1 `
  -Configuration Release `
  -TargetModulesRoot .\publish\homogenization\Modules `
  -ModuleIds Homogenization `
  -CleanModulesDirectory
```

## Local debug layout

Visual Studio local builds now synchronize the same relative layout under:

- `publish\Debug\launcher`
- `publish\Debug\homogenization`

Launcher cards resolve relative executable paths such as:

- `..\homogenization\IIoT.Edge.Shell.exe`

This keeps local F5 behavior aligned with the shipped runtime package.

## Launcher accounts

The repository keeps only `launcher.accounts.sample.json`. Do not commit a real `launcher.accounts.json`.

To generate a password hash:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\NewLauncherPasswordHash.ps1 -Password 'ChangeMe123!'
```

Create a real `launcher.accounts.json` beside `launcher\IIoT.Edge.Launcher.exe` before delivery. You can also inject it during publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\PublishEdgeRuntime.ps1 `
  -Configuration Release `
  -LauncherAccountsSource 'C:\secure\launcher.accounts.json' `
  -CleanOutput
```

If `launcher.accounts.json` is missing at startup, the launcher will stop with a local configuration error instead of entering process selection.

## Adding a new process

When a new process is added:

1. Add `src/Modules/IIoT.Edge.Module.<Process>`
2. Add `src/Edge/IIoT.Edge.Shell/appsettings.machine.<Profile>.json`
3. Add one runtime entry to `scripts/edge-runtime.publish.json`
4. Run the publish script again

Do not add process-specific host `if/else` logic. Launcher cards and runtime directories are generated from the publish manifest.

## Manual validation

Validate all of the following on a packaged runtime directory:

- launcher login success and failure
- Homogenization card displays correctly
- each card resolves to its own relative runtime directory
- different profiles can run side by side on one machine
- the same profile is still single-instance
- `%LocalAppData%\IIoT.Edge\runtime\<Profile>` creates isolated DB, context, and diagnostics roots
- missing launcher target exe produces a launcher-side path error
- bad plugin manifest only affects the corresponding process runtime
