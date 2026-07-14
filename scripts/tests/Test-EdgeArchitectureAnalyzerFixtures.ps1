[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$fixtureSourceRoot = Join-Path $PSScriptRoot 'fixtures/edge-architecture'
$workingRoot = Join-Path $RepositoryRoot 'obj/EdgeArchitectureFixtures'
$architectureIdPattern = '\b(?:WSARCH|DDD|DATA|PLUG|EDGEOUT|EDGEPLCOWN|EDGEASYNC)\d{3}\b'

function Initialize-FixtureWorkspace {
    if (-not (Test-Path $fixtureSourceRoot -PathType Container)) {
        throw "EDGE-ARCH-FIXTURE-001 fixture source root does not exist: $fixtureSourceRoot"
    }

    if (Test-Path $workingRoot) {
        Remove-Item $workingRoot -Recurse -Force
    }
    [void](New-Item $workingRoot -ItemType Directory -Force)
    Copy-Item (Join-Path $fixtureSourceRoot '*') $workingRoot -Recurse -Force

    foreach ($template in @(Get-ChildItem $workingRoot -Recurse -File -Filter '*.fixture')) {
        $targetPath = $template.FullName.Substring(0, $template.FullName.Length - '.fixture'.Length)
        Move-Item $template.FullName $targetPath
    }
}

function Invoke-FixtureBuild {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][bool]$ShouldSucceed,
        [string[]]$ExpectedDiagnosticIds = @(),
        [string[]]$AdditionalBuildArguments = @()
    )

    $resolvedProject = Join-Path $workingRoot $ProjectPath
    if (-not (Test-Path $resolvedProject -PathType Leaf)) {
        throw "EDGE-ARCH-FIXTURE-001 fixture project does not exist: $resolvedProject"
    }

    $buildArguments = @(
        'build',
        $resolvedProject,
        '-c',
        $Configuration,
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse'
    ) + $AdditionalBuildArguments
    $buildOutput = @(& dotnet @buildArguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $outputText = $buildOutput -join "`n"
    $actualIds = @([regex]::Matches($outputText, $architectureIdPattern) |
        ForEach-Object { $_.Value } |
        Sort-Object -Unique)
    $expectedIds = @($ExpectedDiagnosticIds | Sort-Object -Unique)

    if ($ShouldSucceed) {
        if ($exitCode -ne 0) {
            throw "EDGE-ARCH-FIXTURE-001 valid fixture '$Name' failed with exit code ${exitCode}:`n$outputText"
        }
        if ($actualIds.Count -ne 0) {
            throw "EDGE-ARCH-FIXTURE-001 valid fixture '$Name' emitted architecture diagnostics: $($actualIds -join ', ')."
        }
    } else {
        if ($exitCode -eq 0) {
            throw "EDGE-ARCH-FIXTURE-001 invalid fixture '$Name' unexpectedly built successfully."
        }
        if (($actualIds -join '|') -ne ($expectedIds -join '|')) {
            throw "EDGE-ARCH-FIXTURE-001 invalid fixture '$Name' diagnostics mismatch: actual=[$($actualIds -join ', ')], expected=[$($expectedIds -join ', ')].`n$outputText"
        }
    }

    Write-Host "Edge architecture fixture passed: name=$Name, exitCode=$exitCode, diagnostics=[$($actualIds -join ', ')]."
}

function Assert-ArchitectureGateTextIsPinned {
    $targetsPath = Join-Path $RepositoryRoot 'Directory.Build.targets'
    $targetsText = Get-Content $targetsPath -Raw

    if ($targetsText.Contains('UseEdgeArchitectureAnalyzer', [StringComparison]::Ordinal)) {
        throw 'EDGE-ARCH-FIXTURE-001 production Analyzer wiring must not expose UseEdgeArchitectureAnalyzer.'
    }
    if (-not $targetsText.Contains("'`$(MSBuildProjectName)' != 'IIoT.Edge.Architecture.Analyzers'", [StringComparison]::Ordinal) -or
        -not $targetsText.Contains("'`$(MSBuildProjectName)' != 'IIoT.Edge.TestPlugin'", [StringComparison]::Ordinal) -or
        -not $targetsText.Contains('[/\\]src[/\\]Tests[/\\]', [StringComparison]::Ordinal)) {
        throw 'EDGE-ARCH-FIXTURE-001 Analyzer exclusions must remain the exact Analyzer/test/TestPlugin roles.'
    }

    [xml]$targets = $targetsText
    $shellTarget = $targets.SelectSingleNode("/Project/Target[@Name='ValidateEdgeArchitectureProjectGraph']")
    $shellExec = if ($null -eq $shellTarget) {
        $null
    } else {
        $shellTarget.SelectSingleNode('Exec')
    }
    if ($null -eq $shellExec) {
        throw 'EDGE-ARCH-FIXTURE-001 pinned Shell project-graph target is missing.'
    }
    $shellCommand = ([System.Xml.XmlElement]$shellExec).GetAttribute('Command')
    if (-not $shellCommand.Contains('-RepositoryRoot "$(MSBuildThisFileDirectory)"', [StringComparison]::Ordinal) -or
        -not $shellCommand.Contains('-SolutionPath "$(MSBuildThisFileDirectory)IIoT.EdgeClient.slnx"', [StringComparison]::Ordinal) -or
        $shellCommand.Contains('$(EdgeArchitectureGraphRepositoryRoot)', [StringComparison]::Ordinal) -or
        $shellCommand.Contains('$(EdgeArchitectureGraphSolutionPath)', [StringComparison]::Ordinal)) {
        throw 'EDGE-ARCH-FIXTURE-001 Shell project-graph paths must be pinned to the current repository and solution.'
    }

    $fixtureTarget = $targets.SelectSingleNode("/Project/Target[@Name='ValidateEdgeArchitectureProjectGraphFixture']")
    if ($null -eq $fixtureTarget -or
        -not ([System.Xml.XmlElement]$fixtureTarget).GetAttribute('Condition').Contains(
            "'`$(IsEdgeArchitectureGraphFixture)' == 'true'",
            [StringComparison]::Ordinal)) {
        throw 'EDGE-ARCH-FIXTURE-001 isolated graph overrides must remain limited to explicit fixture projects.'
    }

    Write-Host 'Edge architecture gate text passed: Analyzer is mandatory and Shell graph paths are pinned.'
}

function Assert-ShellGraphCliOverrideCannotBypass {
    $shellProject = Join-Path $RepositoryRoot 'src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj'
    $benignRoot = Join-Path $workingRoot 'graph-valid'
    $benignSolution = Join-Path $benignRoot 'EdgeArchitecture.Valid.slnx'
    $arguments = @(
        'msbuild',
        $shellProject,
        '-target:ValidateEdgeArchitectureProjectGraph',
        '-property:Configuration=Release',
        "-property:EdgeArchitectureGraphRepositoryRoot=$benignRoot",
        "-property:EdgeArchitectureGraphSolutionPath=$benignSolution",
        '-nologo',
        '-noAutoResponse'
    )
    $output = @(& dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $outputText = $output -join "`n"
    if ($exitCode -ne 0 -or
        -not $outputText.Contains('projects=32', [StringComparison]::Ordinal) -or
        $outputText.Contains('projects=5', [StringComparison]::Ordinal)) {
        throw "EDGE-ARCH-FIXTURE-001 Shell graph CLI override bypass check failed with exit code ${exitCode}:`n$outputText"
    }

    Write-Host 'Edge architecture Shell graph bypass check passed: CLI override still validated the 32-project main solution.'
}

Initialize-FixtureWorkspace
Assert-ArchitectureGateTextIsPinned
Assert-ShellGraphCliOverrideCannotBypass

Invoke-FixtureBuild -Name 'analyzer-valid' `
    -ProjectPath 'analyzer-valid/IIoT.Edge.Installer.ValidFixture.csproj' `
    -ShouldSucceed $true
Invoke-FixtureBuild -Name 'analyzer-invalid' `
    -ProjectPath 'analyzer-invalid/IIoT.Edge.Installer.InvalidFixture.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('EDGEASYNC002') `
    -AdditionalBuildArguments @('-p:UseEdgeArchitectureAnalyzer=false')
Invoke-FixtureBuild -Name 'graph-valid' `
    -ProjectPath 'graph-valid/Host/IIoT.Edge.Host.Bootstrap.csproj' `
    -ShouldSucceed $true
Invoke-FixtureBuild -Name 'graph-production-test-invalid' `
    -ProjectPath 'graph-production-test-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH003')
Invoke-FixtureBuild -Name 'graph-layer-invalid' `
    -ProjectPath 'graph-layer-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-cycle-invalid' `
    -ProjectPath 'graph-cycle-invalid/TestsA/IIoT.Edge.CycleA.Tests.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH001')
Invoke-FixtureBuild -Name 'graph-plugin-metadata-invalid' `
    -ProjectPath 'graph-plugin-metadata-invalid/IIoT.Edge.Module.InvalidFixture.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('PLUG004')

Write-Host 'Edge architecture analyzer/build fixtures passed: valid=2, invalid=5, bypass-checks=2.'
