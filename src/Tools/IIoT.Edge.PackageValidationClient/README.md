# IIoT.Edge.PackageValidationClient

Internal package validation shell for the Edge client. This is not the day-to-day production client entry point.

This tool stays inside the main `IIoT.EdgeClient` repository and is used only to validate package-only composition before release.

## Rules

- Only compose the client from NuGet packages.
- Do not add project references back to the Host or Module source projects.
- Lock package versions in `Directory.Packages.props`.
- Ship only artifacts produced from the main Edge client release flow.
- Do not treat this shell as a second business client or a separate daily maintenance target.

## Local validation

1. From the `IIoT.EdgeClient` repository root, pack host and module packages into the local feed:
   `pwsh scripts/PackEdgePackages.ps1 -Group all -Configuration Release -CleanOutput`
2. From the same repository root, build the package validation tool from packages only:
   `pwsh src/Tools/IIoT.Edge.PackageValidationClient/scripts/BuildRelease.ps1`

`SyncLocalPackages.ps1` copies package artifacts from the main repository feed at `.artifacts\nuget` into this tool's local feed before restore/build.
