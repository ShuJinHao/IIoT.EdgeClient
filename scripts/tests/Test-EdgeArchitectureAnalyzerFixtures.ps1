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
$inventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json'
$expectedMainProjectCount = [int]((Get-Content $inventoryPath -Raw | ConvertFrom-Json -Depth 16).solutionProjectCount)
$fixtureGraphProjectCount = @(
    Select-Xml -Path (Join-Path $fixtureSourceRoot 'graph-valid/EdgeArchitecture.Valid.slnx.fixture') -XPath '/Solution/Project'
).Count

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
        [string]$ExpectedOutputPattern,
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
        if (-not [string]::IsNullOrWhiteSpace($ExpectedOutputPattern) -and
            $outputText -notmatch $ExpectedOutputPattern) {
            throw "EDGE-ARCH-FIXTURE-001 invalid fixture '$Name' did not emit required output '$ExpectedOutputPattern'.`n$outputText"
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
    if (-not $targetsText.Contains("'`$(IsTestProject)' != 'true'", [StringComparison]::Ordinal) -or
        -not $targetsText.Contains("'`$(MSBuildProjectFullPath)' != '`$(EdgeArchitectureAnalyzerProject)'", [StringComparison]::Ordinal) -or
        -not $targetsText.Contains("'`$(IsEdgePluginTestFixture)' != 'true'", [StringComparison]::Ordinal) -or
        $targetsText.Contains("'`$(MSBuildProjectName)' != 'IIoT.Edge.Architecture.Analyzers'", [StringComparison]::Ordinal) -or
        $targetsText.Contains('[/\\]src[/\\]Tests[/\\]', [StringComparison]::Ordinal)) {
        throw 'EDGE-ARCH-FIXTURE-001 Analyzer exclusions must remain tied to validated test/fixture roles and the exact Analyzer path.'
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
    $mainCountPattern = "\bprojects=$expectedMainProjectCount(?:,|\b)"
    $fixtureCountPattern = "\bprojects=$fixtureGraphProjectCount(?:,|\b)"
    if ($exitCode -ne 0 -or
        $outputText -notmatch $mainCountPattern -or
        $outputText -match $fixtureCountPattern) {
        throw "EDGE-ARCH-FIXTURE-001 Shell graph CLI override bypass check failed with exit code ${exitCode}:`n$outputText"
    }

    Write-Host "Edge architecture Shell graph bypass check passed: CLI override still validated the $expectedMainProjectCount-project main solution."
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
    -ExpectedDiagnosticIds @('EDGEASYNC001', 'EDGEASYNC002') `
    -AdditionalBuildArguments @('-p:UseEdgeArchitectureAnalyzer=false')
Invoke-FixtureBuild -Name 'graph-valid' `
    -ProjectPath 'graph-valid/Host/IIoT.Edge.Host.Bootstrap.csproj' `
    -ShouldSucceed $true
Invoke-FixtureBuild -Name 'graph-production-test-invalid' `
    -ProjectPath 'graph-production-test-invalid/src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH005', 'WSARCH006')
Invoke-FixtureBuild -Name 'graph-layer-invalid' `
    -ProjectPath 'graph-layer-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-cycle-invalid' `
    -ProjectPath 'graph-cycle-invalid/TestsA/IIoT.Edge.Host.CycleA.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH001')
Invoke-FixtureBuild -Name 'graph-plugin-metadata-invalid' `
    -ProjectPath 'graph-plugin-metadata-invalid/IIoT.Edge.Module.InvalidFixture.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('PLUG004')
Invoke-FixtureBuild -Name 'graph-pure-network-invalid' `
    -ProjectPath 'graph-pure-network-invalid/src/Tests/IIoT.Edge.Plc.PureNetworkInvalidTests/IIoT.Edge.Plc.PureNetworkInvalidTests.csproj' `
    -ShouldSucceed $false `
    -ExpectedOutputPattern '\bWSTEST009\b'
Invoke-FixtureBuild -Name 'graph-compound-condition-invalid' `
    -ProjectPath 'graph-compound-condition-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-imported-layer-invalid' `
    -ProjectPath 'graph-imported-layer-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-test-support-transitive-invalid' `
    -ProjectPath 'graph-test-support-transitive-invalid/src/Tests/IIoT.Edge.Domain.AggregateTests/IIoT.Edge.Domain.AggregateTests.csproj' `
    -ShouldSucceed $false `
    -ExpectedOutputPattern '\bWSTEST010\b'
Invoke-FixtureBuild -Name 'graph-pure-support-persistence-invalid' `
    -ProjectPath 'graph-pure-support-persistence-invalid/src/Tests/IIoT.Edge.PersistenceBoundary.UnitTests/IIoT.Edge.PersistenceBoundary.UnitTests.csproj' `
    -ShouldSucceed $false `
    -ExpectedOutputPattern '\bWSTEST007\b'
Invoke-FixtureBuild -Name 'graph-pure-resource-valid' `
    -ProjectPath 'graph-pure-resource-valid/src/Tests/IIoT.Edge.ResourceBoundary.UnitTests/IIoT.Edge.ResourceBoundary.UnitTests.csproj' `
    -ShouldSucceed $true
Invoke-FixtureBuild -Name 'graph-wall-clock-invalid' `
    -ProjectPath 'graph-wall-clock-invalid/src/Tests/IIoT.Edge.WallClock.UnitTests/IIoT.Edge.WallClock.UnitTests.csproj' `
    -ShouldSucceed $false `
    -ExpectedOutputPattern '\bWSTEST008\b'
Invoke-FixtureBuild -Name 'graph-wall-clock-valid' `
    -ProjectPath 'graph-wall-clock-valid/src/Tests/IIoT.Edge.WallClockGuard.UnitTests/IIoT.Edge.WallClockGuard.UnitTests.csproj' `
    -ShouldSucceed $true
Invoke-FixtureBuild -Name 'graph-external-compile-link-invalid' `
    -ProjectPath 'graph-external-compile-link-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH007')
Invoke-FixtureBuild -Name 'graph-external-compile-no-link-invalid' `
    -ProjectPath 'graph-external-compile-no-link-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH007')
Invoke-FixtureBuild -Name 'graph-external-compile-glob-invalid' `
    -ProjectPath 'graph-external-compile-glob-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH007')
Invoke-FixtureBuild -Name 'graph-external-compile-import-invalid' `
    -ProjectPath 'graph-external-compile-import-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH007')
Invoke-FixtureBuild -Name 'graph-imported-test-package-invalid' `
    -ProjectPath 'graph-imported-test-package-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH003', 'WSARCH005', 'WSARCH006')
Invoke-FixtureBuild -Name 'graph-target-item-edge-invalid' `
    -ProjectPath 'graph-target-item-edge-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-target-property-edge-invalid' `
    -ProjectPath 'graph-target-property-edge-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-analyzer-suppression-invalid' `
    -ProjectPath 'graph-analyzer-suppression-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH006')
Invoke-FixtureBuild -Name 'graph-unknown-role-invalid' `
    -ProjectPath 'graph-unknown-role-invalid/Misc/IIoT.Edge.Utility.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH004')
Invoke-FixtureBuild -Name 'graph-analyzer-exclusion-invalid' `
    -ProjectPath 'graph-analyzer-exclusion-invalid/Application/IIoT.Edge.Application.csproj' `
    -ShouldSucceed $false `
    -ExpectedDiagnosticIds @('WSARCH006')

Write-Host 'Edge architecture analyzer/build fixtures passed: valid=4, invalid=21, bypass-checks=2.'
