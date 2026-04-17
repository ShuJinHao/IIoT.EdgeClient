param(
    [string]$Configuration = 'Release',
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'IIoT.Edge.PackageValidationClient.slnx'
$nugetConfig = Join-Path $repoRoot 'NuGet.Config'
$resolvedConfig = Join-Path $repoRoot '.artifacts\NuGet.resolved.Config'
$localFeed = Join-Path $repoRoot '.artifacts\nuget'
$projectAssets = Join-Path $repoRoot 'src\IIoT.Edge.PackageValidationClient\obj\project.assets.json'
$packagesCache = Join-Path $repoRoot '.artifacts\packages-cache'

function Install-LocalPackageToCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetCacheRoot,
        [Parameter(Mandatory = $true)]
        [string]$SourcePath
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "Package '$PackagePath' does not contain a nuspec."
        }

        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $packageId = $nuspec.package.metadata.id.ToString()
        $packageVersion = $nuspec.package.metadata.version.ToString()
        $packageIdLower = $packageId.ToLowerInvariant()
        $targetRoot = Join-Path $TargetCacheRoot (Join-Path $packageIdLower $packageVersion)

        if (Test-Path $targetRoot) {
            Remove-Item -Path $targetRoot -Recurse -Force -ErrorAction SilentlyContinue
        }

        New-Item -Path $targetRoot -ItemType Directory -Force | Out-Null

        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            if ($entry.FullName -eq '[Content_Types].xml' `
                -or $entry.FullName.StartsWith('_rels/', [StringComparison]::OrdinalIgnoreCase) `
                -or $entry.FullName.StartsWith('package/', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $destinationPath = Join-Path $targetRoot $entry.FullName
            $destinationDir = Split-Path -Parent $destinationPath
            if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
                New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
            }

            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destinationPath, $true)
        }

        $packageFileName = "$packageIdLower.$packageVersion.nupkg"
        $cachedPackagePath = Join-Path $targetRoot $packageFileName
        Copy-Item -Path $PackagePath -Destination $cachedPackagePath -Force

        $sha512 = [System.Security.Cryptography.SHA512]::Create()
        try {
            $stream = [System.IO.File]::OpenRead($PackagePath)
            try {
                $hashBytes = $sha512.ComputeHash($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $sha512.Dispose()
        }

        $contentHash = [Convert]::ToBase64String($hashBytes)
        Set-Content -Path (Join-Path $targetRoot "$packageFileName.sha512") -Value $contentHash -Encoding ascii

        $metadata = [ordered]@{
            version = 2
            contentHash = $contentHash
            source = $SourcePath
        } | ConvertTo-Json

        Set-Content -Path (Join-Path $targetRoot '.nupkg.metadata') -Value $metadata -Encoding UTF8
    }
    finally {
        $archive.Dispose()
    }
}

& (Join-Path $PSScriptRoot 'SyncLocalPackages.ps1')

New-Item -Path (Split-Path -Parent $resolvedConfig) -ItemType Directory -Force | Out-Null

$configContent = Get-Content $nugetConfig -Raw
$globalPackagesLine = dotnet nuget locals global-packages --list
$globalPackagesPath = (($globalPackagesLine -split ':', 2)[1]).Trim()

$configContent = $configContent.Replace('.\.artifacts\nuget', $localFeed)
$configContent = [regex]::Replace(
    $configContent,
    '\s*<add key="nuget\.org" value="https://api\.nuget\.org/v3/index\.json" />\s*',
    [Environment]::NewLine)
if ([string]::IsNullOrWhiteSpace($env:EDGE_SHARED_NUGET_FEED)) {
    $configContent = [regex]::Replace(
        $configContent,
        '\s*<add key="edge-shared" value="%EDGE_SHARED_NUGET_FEED%" />\s*',
        [Environment]::NewLine)
}
else {
    $configContent = $configContent.Replace('%EDGE_SHARED_NUGET_FEED%', $env:EDGE_SHARED_NUGET_FEED)
}

if (-not [string]::IsNullOrWhiteSpace($globalPackagesPath) -and (Test-Path $globalPackagesPath)) {
    $fallbackSection = @"
  <fallbackPackageFolders>
    <clear />
    <add key="global-packages" value="$globalPackagesPath" />
  </fallbackPackageFolders>
"@
    $configContent = $configContent.Replace('</configuration>', "$fallbackSection`r`n</configuration>")
}

Set-Content -Path $resolvedConfig -Value $configContent -Encoding UTF8

if (Test-Path $packagesCache) {
    Remove-Item -Path $packagesCache -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -Path $packagesCache -ItemType Directory -Force | Out-Null
$env:NUGET_PACKAGES = $packagesCache

Get-ChildItem -Path $localFeed -Filter 'IIoT.Edge*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' } |
    ForEach-Object {
        Install-LocalPackageToCache -PackagePath $_.FullName -TargetCacheRoot $packagesCache -SourcePath $localFeed
    }

dotnet restore $solutionPath `
    --configfile $resolvedConfig `
    --packages $packagesCache `
    --no-cache `
    -p:BuildInParallel=false `
    -p:RestoreDisableParallel=true
dotnet build $solutionPath --configuration $Configuration --no-restore -p:BuildInParallel=false

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    return
}

if (-not (Test-Path $ManifestPath)) {
    throw "Release manifest not found: $ManifestPath"
}

if (-not (Test-Path $projectAssets)) {
    throw "Restore assets file not found: $projectAssets"
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$manifestPackages = @{}
foreach ($package in $manifest.packages) {
    $manifestPackages[$package.id.ToString().ToLowerInvariant()] = $package.version.ToString()
}

$assets = Get-Content $projectAssets -Raw | ConvertFrom-Json
$consumedPackages = @(
    foreach ($libraryName in $assets.libraries.PSObject.Properties.Name) {
        if ($libraryName -notlike 'iiot.edge.*/*') {
            continue
        }

        $parts = $libraryName -split '/', 2
        [pscustomobject]@{
            Id = $parts[0].ToLowerInvariant()
            Version = $parts[1]
        }
    }
)

$missingPackages = @()
$mismatchedPackages = @()
foreach ($package in $consumedPackages) {
    if (-not $manifestPackages.ContainsKey($package.Id)) {
        $missingPackages += "$($package.Id)/$($package.Version)"
        continue
    }

    if ($manifestPackages[$package.Id] -ne $package.Version) {
        $mismatchedPackages += "$($package.Id): expected $($manifestPackages[$package.Id]), actual $($package.Version)"
    }
}

if ($missingPackages.Count -gt 0 -or $mismatchedPackages.Count -gt 0) {
    $messages = @()
    if ($missingPackages.Count -gt 0) {
        $messages += "Missing from manifest: $([string]::Join(', ', $missingPackages))"
    }

    if ($mismatchedPackages.Count -gt 0) {
        $messages += "Version mismatch: $([string]::Join(', ', $mismatchedPackages))"
    }

    throw "Integration build consumed IIoT.Edge packages that do not match the release manifest. $([string]::Join(' | ', $messages))"
}

