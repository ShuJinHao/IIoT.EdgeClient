param(
    [string]$ProgramDataRoot,

    [string]$SnapshotPath,

    [switch]$CreateSnapshot,

    [switch]$CompareSnapshot
)

$ErrorActionPreference = 'Stop'

if ($CreateSnapshot -and $CompareSnapshot) {
    throw "Use either -CreateSnapshot or -CompareSnapshot, not both."
}

if (-not $CreateSnapshot -and -not $CompareSnapshot) {
    throw "Specify -CreateSnapshot before update or -CompareSnapshot after update."
}

if ([string]::IsNullOrWhiteSpace($ProgramDataRoot)) {
    $ProgramDataRoot = if (-not [string]::IsNullOrWhiteSpace($env:IIOT_EDGE_PROGRAM_DATA_ROOT)) {
        $env:IIOT_EDGE_PROGRAM_DATA_ROOT
    }
    else {
        [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    }
}

if ([string]::IsNullOrWhiteSpace($SnapshotPath)) {
    $SnapshotPath = Join-Path $ProgramDataRoot 'IIoT\EdgeClient\edge-programdata.snapshot.json'
}

$configRoot = Join-Path $ProgramDataRoot 'IIoT\EdgeClient'
$dataRoot = Join-Path $ProgramDataRoot 'IIoT\EdgeData'

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    return [System.IO.Path]::GetRelativePath($BasePath, $PathValue).Replace('\', '/')
}

function Get-FileSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string[]]$IncludePatterns
    )

    if (-not (Test-Path $RootPath)) {
        return @()
    }

    $files = Get-ChildItem -Path $RootPath -File -Recurse |
        Where-Object {
            $relativePath = Get-RelativePath -BasePath $RootPath -PathValue $_.FullName
            foreach ($pattern in $IncludePatterns) {
                if ($relativePath -like $pattern) {
                    return $true
                }
            }

            return $false
        } |
        Sort-Object FullName

    return @($files | ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -Path $_.FullName
        [pscustomobject]@{
            RelativePath = Get-RelativePath -BasePath $RootPath -PathValue $_.FullName
            Length = $_.Length
            Sha256 = $hash.Hash
        }
    })
}

function New-ProgramDataSnapshot {
    $configPatterns = @(
        'launcher/launcher.accounts.json',
        'launcher/language.json',
        'launcher/launcher.update.json',
        'profiles/*/appsettings.machine.*.json'
    )
    $dataPatterns = @(
        'profiles/*/db/*.db',
        'profiles/*/db/*.db-wal',
        'profiles/*/db/*.db-shm',
        'profiles/*/context/*',
        'profiles/*/recipe/*',
        'profiles/*/excel/*',
        'profiles/*/diagnostics/*',
        'profiles/*/diagnostics/logs/*',
        'profiles/*/device_cache.json'
    )

    return [pscustomobject]@{
        CreatedAt = [DateTimeOffset]::UtcNow.ToString('O')
        ProgramDataRoot = $ProgramDataRoot
        ConfigRoot = $configRoot
        DataRoot = $dataRoot
        ConfigFiles = @(Get-FileSnapshot -RootPath $configRoot -IncludePatterns $configPatterns)
        DataFiles = @(Get-FileSnapshot -RootPath $dataRoot -IncludePatterns $dataPatterns)
    }
}

function Get-FileMap {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [array]$Files
    )

    $map = @{}
    foreach ($file in $Files) {
        $map[$file.RelativePath] = $file
    }

    return $map
}

function Compare-FileGroup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GroupName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [array]$ExpectedFiles,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [array]$ActualFiles
    )

    $expectedMap = Get-FileMap -Files $ExpectedFiles
    $actualMap = Get-FileMap -Files $ActualFiles
    $differences = [System.Collections.Generic.List[string]]::new()

    foreach ($key in ($expectedMap.Keys | Sort-Object)) {
        if (-not $actualMap.ContainsKey($key)) {
            $differences.Add("$GroupName missing after update: $key")
            continue
        }

        $expected = $expectedMap[$key]
        $actual = $actualMap[$key]
        if ($expected.Length -ne $actual.Length -or $expected.Sha256 -ne $actual.Sha256) {
            $differences.Add("$GroupName changed after update: $key")
        }
    }

    foreach ($key in ($actualMap.Keys | Sort-Object)) {
        if (-not $expectedMap.ContainsKey($key)) {
            $differences.Add("$GroupName added after update: $key")
        }
    }

    return $differences
}

if ($CreateSnapshot) {
    $snapshot = New-ProgramDataSnapshot
    $snapshotDirectory = Split-Path -Parent $SnapshotPath
    if (-not [string]::IsNullOrWhiteSpace($snapshotDirectory)) {
        New-Item -Path $snapshotDirectory -ItemType Directory -Force | Out-Null
    }

    $snapshot | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $SnapshotPath
    Write-Host "ProgramData snapshot created: $SnapshotPath"
    Write-Host "Config files: $(@($snapshot.ConfigFiles).Count)"
    Write-Host "Data files: $(@($snapshot.DataFiles).Count)"
    return
}

if (-not (Test-Path $SnapshotPath)) {
    throw "Snapshot file was not found: $SnapshotPath"
}

$expectedSnapshot = Get-Content -Raw -Encoding UTF8 -Path $SnapshotPath | ConvertFrom-Json
$actualSnapshot = New-ProgramDataSnapshot
$differences = [System.Collections.Generic.List[string]]::new()

foreach ($difference in Compare-FileGroup `
    -GroupName 'Config file' `
    -ExpectedFiles @($expectedSnapshot.ConfigFiles) `
    -ActualFiles @($actualSnapshot.ConfigFiles)) {
    $differences.Add($difference)
}

foreach ($difference in Compare-FileGroup `
    -GroupName 'Data file' `
    -ExpectedFiles @($expectedSnapshot.DataFiles) `
    -ActualFiles @($actualSnapshot.DataFiles)) {
    $differences.Add($difference)
}

if ($differences.Count -gt 0) {
    foreach ($difference in $differences) {
        Write-Error $difference
    }

    throw "ProgramData invariant check failed. Differences: $($differences.Count)"
}

Write-Host "ProgramData invariant check passed: $SnapshotPath"
