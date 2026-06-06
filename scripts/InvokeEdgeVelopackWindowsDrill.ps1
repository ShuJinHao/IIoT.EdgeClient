param(
    [ValidateSet(
        'PrintChecklist',
        'PrintMachineIdentityPath',
        'ValidateMachineIdentity',
        'ConfigureSource',
        'SnapshotBeforeUpdate',
        'VerifyAfterUpdate',
        'SnapshotBeforeRollback',
        'VerifyAfterRollback'
    )]
    [string]$Step = 'PrintChecklist',

    [string]$UpdateSource,

    [string]$ProgramDataRoot,

    [string]$SnapshotPath,

    [string]$Channel = 'homogenization',

    [string]$MachineProfile = 'HomogenizationLine'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$invariantScript = Join-Path $PSScriptRoot 'TestEdgeProgramDataInvariant.ps1'

function Get-EdgeDrillProgramDataRoot {
    if (-not [string]::IsNullOrWhiteSpace($ProgramDataRoot)) {
        return $ProgramDataRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($env:IIOT_EDGE_PROGRAM_DATA_ROOT)) {
        return $env:IIOT_EDGE_PROGRAM_DATA_ROOT
    }

    return [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
}

function Get-EdgeDrillConfigRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Join-Path $RootPath 'IIoT\EdgeClient'
}

function Get-EdgeDrillMachineConfigPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Join-Path `
        (Join-Path (Get-EdgeDrillConfigRoot -RootPath $RootPath) "profiles\$MachineProfile") `
        "appsettings.machine.$MachineProfile.json"
}

function Get-EdgeDrillSnapshotPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not [string]::IsNullOrWhiteSpace($SnapshotPath)) {
        return $SnapshotPath
    }

    return Join-Path (Get-EdgeDrillConfigRoot -RootPath $RootPath) $Name
}

function Write-EdgeDrillMachineIdentityPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $configPath = Get-EdgeDrillMachineConfigPath -RootPath $RootPath
    Write-Host "Machine identity config: $configPath"
    Write-Host "Fill these keys before Cloud bootstrap is expected to work:"
    Write-Host "  CloudApi:ClientCode"
    Write-Host "  CloudApi:BootstrapSecret"
}

function Assert-EdgeDrillMachineIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $configPath = Get-EdgeDrillMachineConfigPath -RootPath $RootPath
    if (-not (Test-Path $configPath)) {
        throw "Machine config was not found. Launch Shell once to seed it, then fill machine identity: $configPath"
    }

    $config = Get-Content -Raw -Encoding UTF8 -Path $configPath | ConvertFrom-Json
    if ($null -eq $config.CloudApi) {
        throw "Machine config does not contain CloudApi section: $configPath"
    }

    foreach ($key in @('ClientCode', 'BootstrapSecret')) {
        $property = $config.CloudApi.PSObject.Properties[$key]
        $value = if ($null -eq $property) { $null } else { [string]$property.Value }
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Machine identity CloudApi:$key is not configured in $configPath"
        }
    }

    Write-Host "Machine identity check passed: $configPath"
}

function Resolve-EdgeDrillUpdateSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $trimmedSource = $Source.Trim()
    $uri = $null
    if ([Uri]::TryCreate($trimmedSource, [UriKind]::Absolute, [ref]$uri)) {
        if ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https') {
            return $trimmedSource
        }

        if ($uri.Scheme -eq 'file') {
            return $uri.LocalPath
        }
    }

    if (Test-Path $trimmedSource) {
        return (Resolve-Path $trimmedSource).Path
    }

    return $trimmedSource
}

function Assert-EdgeDrillUpdateSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedSource
    )

    $uri = $null
    if ([Uri]::TryCreate($ResolvedSource, [UriKind]::Absolute, [ref]$uri) -and
        ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')) {
        return
    }

    if (-not (Test-Path $ResolvedSource)) {
        throw "Local update source was not found: $ResolvedSource"
    }

    $releaseMetadataPath = Join-Path $ResolvedSource "releases.$Channel.json"
    if (-not (Test-Path $releaseMetadataPath)) {
        throw "Local update source does not contain release metadata: $releaseMetadataPath"
    }
}

function Write-EdgeDrillUpdateConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedSource
    )

    $launcherDirectory = Join-Path (Get-EdgeDrillConfigRoot -RootPath $RootPath) 'launcher'
    $configPath = Join-Path $launcherDirectory 'launcher.update.json'
    New-Item -Path $launcherDirectory -ItemType Directory -Force | Out-Null

    [ordered]@{
        Source = $ResolvedSource
        UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -Path $configPath

    Write-Host "Update source configured: $configPath"
    Write-Host "Source: $ResolvedSource"
}

function Invoke-EdgeDrillInvariantSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue,

        [Parameter(Mandatory = $true)]
        [bool]$Create
    )

    if (-not (Test-Path $invariantScript)) {
        throw "ProgramData invariant script was not found: $invariantScript"
    }

    if ($Create) {
        & $invariantScript `
            -ProgramDataRoot $RootPath `
            -SnapshotPath $PathValue `
            -CreateSnapshot
        return
    }

    & $invariantScript `
        -ProgramDataRoot $RootPath `
        -SnapshotPath $PathValue `
        -CompareSnapshot
}

function Write-EdgeDrillChecklist {
    Write-Host "IIoT Edge Velopack Windows drill"
    Write-Host ""
    Write-Host "1. Install the old version Setup.exe and launch Launcher once."
    Write-Host "2. Launch Shell once to seed ProgramData machine config, then fill machine identity:"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step PrintMachineIdentityPath -MachineProfile $MachineProfile"
    Write-Host "   # edit CloudApi:ClientCode and CloudApi:BootstrapSecret in the printed file"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step ValidateMachineIdentity -MachineProfile $MachineProfile"
    Write-Host "3. Point Launcher to the new release feed:"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step ConfigureSource -UpdateSource C:\edge-releases\new -Channel $Channel"
    Write-Host "4. Snapshot protected ProgramData before update:"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step SnapshotBeforeUpdate"
    Write-Host "5. In Launcher, click check update, then download and restart update."
    Write-Host "6. Verify protected ProgramData after update:"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step VerifyAfterUpdate"
    Write-Host "7. For rollback, configure source to the old compatible feed, snapshot, apply update, then verify:"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step ConfigureSource -UpdateSource C:\edge-releases\old -Channel $Channel"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step SnapshotBeforeRollback"
    Write-Host "   # apply rollback from Launcher"
    Write-Host "   powershell -ExecutionPolicy Bypass -File scripts\InvokeEdgeVelopackWindowsDrill.ps1 -Step VerifyAfterRollback"
    Write-Host ""
    Write-Host "Pass criteria: Launcher version changes, Shell starts, and protected ProgramData comparison passes."
}

$resolvedProgramDataRoot = Get-EdgeDrillProgramDataRoot

switch ($Step) {
    'PrintChecklist' {
        Write-EdgeDrillChecklist
    }

    'PrintMachineIdentityPath' {
        Write-EdgeDrillMachineIdentityPath -RootPath $resolvedProgramDataRoot
    }

    'ValidateMachineIdentity' {
        Assert-EdgeDrillMachineIdentity -RootPath $resolvedProgramDataRoot
    }

    'ConfigureSource' {
        if ([string]::IsNullOrWhiteSpace($UpdateSource)) {
            throw "-UpdateSource is required for ConfigureSource."
        }

        $resolvedSource = Resolve-EdgeDrillUpdateSource -Source $UpdateSource
        Assert-EdgeDrillUpdateSource -ResolvedSource $resolvedSource
        Write-EdgeDrillUpdateConfig `
            -RootPath $resolvedProgramDataRoot `
            -ResolvedSource $resolvedSource
    }

    'SnapshotBeforeUpdate' {
        Invoke-EdgeDrillInvariantSnapshot `
            -RootPath $resolvedProgramDataRoot `
            -PathValue (Get-EdgeDrillSnapshotPath `
                -RootPath $resolvedProgramDataRoot `
                -Name 'edge-programdata.before-update.snapshot.json') `
            -Create $true
    }

    'VerifyAfterUpdate' {
        Invoke-EdgeDrillInvariantSnapshot `
            -RootPath $resolvedProgramDataRoot `
            -PathValue (Get-EdgeDrillSnapshotPath `
                -RootPath $resolvedProgramDataRoot `
                -Name 'edge-programdata.before-update.snapshot.json') `
            -Create $false
    }

    'SnapshotBeforeRollback' {
        Invoke-EdgeDrillInvariantSnapshot `
            -RootPath $resolvedProgramDataRoot `
            -PathValue (Get-EdgeDrillSnapshotPath `
                -RootPath $resolvedProgramDataRoot `
                -Name 'edge-programdata.before-rollback.snapshot.json') `
            -Create $true
    }

    'VerifyAfterRollback' {
        Invoke-EdgeDrillInvariantSnapshot `
            -RootPath $resolvedProgramDataRoot `
            -PathValue (Get-EdgeDrillSnapshotPath `
                -RootPath $resolvedProgramDataRoot `
                -Name 'edge-programdata.before-rollback.snapshot.json') `
            -Create $false
    }
}
