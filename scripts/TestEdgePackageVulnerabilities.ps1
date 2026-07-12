param(
    [string]$SolutionPath = 'IIoT.EdgeClient.slnx'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedSolutionPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SolutionPath))
if (-not (Test-Path $resolvedSolutionPath)) {
    throw "Solution was not found: $resolvedSolutionPath"
}

$output = & dotnet list $resolvedSolutionPath package --vulnerable --include-transitive --format json --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    $message = ($output -join [Environment]::NewLine)
    throw "dotnet vulnerable package scan failed with exit code $LASTEXITCODE.`n$message"
}

$report = ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
if ($null -eq $report -or $report -isnot [psobject] -or
    $null -eq $report.PSObject.Properties['version'] -or [int]$report.version -ne 1 -or
    $null -eq $report.PSObject.Properties['parameters'] -or [string]$report.parameters -cne '--vulnerable --include-transitive' -or
    $null -eq $report.PSObject.Properties['sources'] -or
    $null -eq $report.PSObject.Properties['projects']) {
    throw 'dotnet vulnerable package scan returned an unsupported or incomplete JSON report.'
}

$nugetConfigPath = Join-Path $repoRoot 'NuGet.Config'
[xml]$nugetConfig = Get-Content $nugetConfigPath -Raw
$expectedSources = @($nugetConfig.SelectNodes('/configuration/packageSources/add') | ForEach-Object { [string]$_.value } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$reportedSources = @($report.sources | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
if ($expectedSources.Count -eq 0 -or ($expectedSources -join '|') -cne ($reportedSources -join '|')) {
    throw "dotnet vulnerable package scan sources differ from the reviewed NuGet configuration. expected=[$($expectedSources -join ', ')] reported=[$($reportedSources -join ', ')]."
}

[xml]$solution = Get-Content $resolvedSolutionPath -Raw
$solutionDirectory = Split-Path $resolvedSolutionPath -Parent
$expectedProjectPaths = @($solution.SelectNodes('//Project') | ForEach-Object {
    $path = [string]$_.Path
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'Solution contains a project entry without a path.'
    }
    [System.IO.Path]::GetFullPath((Join-Path $solutionDirectory $path))
})
$reportedProjects = @($report.projects | Where-Object { $null -ne $_ })
if ($expectedProjectPaths.Count -eq 0 -or $reportedProjects.Count -eq 0) {
    throw "dotnet vulnerable package scan returned no project coverage; expected=$($expectedProjectPaths.Count), reported=$($reportedProjects.Count)."
}

$pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$expectedProjectSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($path in $expectedProjectPaths) {
    if (-not $expectedProjectSet.Add($path)) {
        throw "Solution contains a duplicate project path: $path"
    }
}
$reportedProjectSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($project in $reportedProjects) {
    if ($null -eq $project.PSObject.Properties['path'] -or [string]::IsNullOrWhiteSpace([string]$project.path)) {
        throw 'dotnet vulnerable package scan returned a project without a path.'
    }
    $reportedPath = if ([System.IO.Path]::IsPathRooted([string]$project.path)) {
        [System.IO.Path]::GetFullPath([string]$project.path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $solutionDirectory ([string]$project.path)))
    }
    if (-not $reportedProjectSet.Add($reportedPath)) {
        throw "dotnet vulnerable package scan returned a duplicate project path: $reportedPath"
    }
}
if (-not $expectedProjectSet.SetEquals($reportedProjectSet)) {
    $missing = @($expectedProjectSet | Where-Object { -not $reportedProjectSet.Contains($_) } | Sort-Object)
    $unexpected = @($reportedProjectSet | Where-Object { -not $expectedProjectSet.Contains($_) } | Sort-Object)
    throw "dotnet vulnerable package scan project coverage differs from the solution. missing=[$($missing -join ', ')] unexpected=[$($unexpected -join ', ')]."
}

$findings = [System.Collections.Generic.List[string]]::new()

foreach ($project in $reportedProjects) {
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
