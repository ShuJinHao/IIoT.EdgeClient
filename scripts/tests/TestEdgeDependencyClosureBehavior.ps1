param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$gate = Join-Path $scriptsRoot 'TestEdgeDependencyClosure.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "edge-dependency-closure-test-$([System.Guid]::NewGuid().ToString('N'))")
$passed = 0

function New-DependencyLayout {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [switch]$OmitReferenceLibrary
    )

    foreach ($application in @(
        @{ Name = 'IIoT.Edge.Launcher'; Directory = 'launcher' },
        @{ Name = 'IIoT.Edge.Shell'; Directory = 'host' }
    )) {
        $applicationRoot = Join-Path $Root $application.Directory
        New-Item -ItemType Directory -Force -Path (Join-Path $applicationRoot 'zh-Hans') |
            Out-Null
        foreach ($fileName in @(
            "$($application.Name).dll",
            'Velopack.dll',
            'IIoT.Edge.UI.Shared.dll',
            'IIoT.Edge.Module.Contracts.dll',
            'native-runtime.dll'
        )) {
            Set-Content -Encoding UTF8 -LiteralPath (Join-Path $applicationRoot $fileName) `
                -Value $fileName
        }
        Set-Content -Encoding UTF8 `
            -LiteralPath (Join-Path $applicationRoot 'zh-Hans/Runtime.resources.dll') `
            -Value 'resource'

        $target = [ordered]@{
            "$($application.Name)/$Version" = [ordered]@{
                runtime = [ordered]@{
                    "$($application.Name).dll" = [ordered]@{}
                }
            }
            'Runtime.Dependencies/1.0.0' = [ordered]@{
                runtime = [ordered]@{
                    'lib/net10.0/Velopack.dll' = [ordered]@{}
                    'lib/net10.0/IIoT.Edge.UI.Shared.dll' = [ordered]@{}
                    'lib/net10.0/IIoT.Edge.Module.Contracts.dll' = [ordered]@{}
                }
                native = [ordered]@{
                    'runtimes/win-x64/native/native-runtime.dll' = [ordered]@{}
                }
                resources = [ordered]@{
                    'lib/net10.0/Runtime.resources.dll' = [ordered]@{
                        locale = 'zh-Hans'
                    }
                }
            }
        }
        if (-not $OmitReferenceLibrary) {
            $target['Reference.Runtime/1.0.0'] = [ordered]@{
                runtime = [ordered]@{
                    'lib/net10.0/Reference.Runtime.dll' = [ordered]@{}
                }
            }
            Set-Content -Encoding UTF8 `
                -LiteralPath (Join-Path $applicationRoot 'Reference.Runtime.dll') `
                -Value 'reference'
        }

        $manifest = [ordered]@{
            runtimeTarget = [ordered]@{ name = 'fixture/win-x64'; signature = '' }
            targets = [ordered]@{ 'fixture/win-x64' = $target }
            libraries = [ordered]@{}
        }
        $manifest | ConvertTo-Json -Depth 12 |
            Set-Content -Encoding UTF8 `
                -LiteralPath (Join-Path $applicationRoot "$($application.Name).deps.json")
    }
}

function Assert-Passes {
    param([Parameter(Mandatory = $true)][scriptblock]$Action)
    & $Action
    $script:passed++
}

function Assert-ThrowsContaining {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$ExpectedText
    )

    $caught = $null
    try {
        & $Action
    }
    catch {
        $caught = $_
    }
    if ($null -eq $caught) {
        throw "Expected dependency closure check to fail containing: $ExpectedText"
    }
    if (-not $caught.Exception.Message.Contains(
            $ExpectedText,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Dependency closure failure did not contain '$ExpectedText'. Actual: $($caught.Exception.Message)"
    }
    $script:passed++
}

try {
    $referenceRoot = Join-Path $temporaryRoot 'reference'
    $currentRoot = Join-Path $temporaryRoot 'current'
    New-DependencyLayout -Root $referenceRoot -Version '2.0.8'
    New-DependencyLayout -Root $currentRoot -Version '2.0.10'

    Assert-Passes {
        & $gate `
            -SourcePath $currentRoot `
            -LayoutRoot '.' `
            -ReferenceSourcePath $referenceRoot `
            -ReferenceLayoutRoot '.' `
            -RequireReferenceComparison
    }

    Remove-Item -LiteralPath (Join-Path $currentRoot 'launcher/Velopack.dll') -Force
    Assert-ThrowsContaining -ExpectedText 'Velopack.dll' -Action {
        & $gate `
            -SourcePath $currentRoot `
            -LayoutRoot '.' `
            -ReferenceSourcePath $referenceRoot `
            -ReferenceLayoutRoot '.' `
            -RequireReferenceComparison
    }

    Remove-Item -LiteralPath $currentRoot -Recurse -Force
    New-DependencyLayout -Root $currentRoot -Version '2.0.10' -OmitReferenceLibrary
    Assert-ThrowsContaining -ExpectedText 'Dependencies disappeared relative to runnable 2.0.8' -Action {
        & $gate `
            -SourcePath $currentRoot `
            -LayoutRoot '.' `
            -ReferenceSourcePath $referenceRoot `
            -ReferenceLayoutRoot '.' `
            -RequireReferenceComparison
    }

    Write-Host "Edge dependency closure behavior tests passed: $passed"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
