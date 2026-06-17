param(
    [string]$SolutionPath = 'IIoT.EdgeClient.slnx'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedSolutionPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SolutionPath))
if (-not (Test-Path $resolvedSolutionPath)) {
    throw "Solution was not found: $resolvedSolutionPath"
}

$output = & dotnet list $resolvedSolutionPath package --vulnerable --include-transitive --format json 2>&1
if ($LASTEXITCODE -ne 0) {
    $message = ($output -join [Environment]::NewLine)
    throw "dotnet vulnerable package scan failed with exit code $LASTEXITCODE.`n$message"
}

$report = ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
$findings = [System.Collections.Generic.List[string]]::new()

foreach ($project in @($report.projects)) {
    $frameworks = @($project.frameworks | Where-Object { $null -ne $_ })
    foreach ($framework in $frameworks) {
        foreach ($packageGroupName in @('topLevelPackages', 'transitivePackages')) {
            $packageGroup = $framework.PSObject.Properties[$packageGroupName]
            if ($null -eq $packageGroup) {
                continue
            }

            foreach ($package in @($packageGroup.Value)) {
                $vulnerabilities = @($package.vulnerabilities)
                if ($vulnerabilities.Count -eq 0) {
                    continue
                }

                foreach ($vulnerability in $vulnerabilities) {
                    $findings.Add(('{0} [{1}] {2} {3}: {4} {5}' -f `
                        $project.path,
                        $framework.framework,
                        $package.id,
                        $package.resolvedVersion,
                        $vulnerability.severity,
                        $vulnerability.advisoryUrl))
                }
            }
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Error "Vulnerable NuGet packages were found:`n$($findings -join [Environment]::NewLine)"
    exit 1
}

Write-Host "No vulnerable NuGet packages were found for: $resolvedSolutionPath"
