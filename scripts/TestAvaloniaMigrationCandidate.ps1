[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$RepositoryRoot,

    [string]$OutputRoot = 'publish\avalonia-migration',

    [string]$EvidenceOutputRoot = '.artifacts\avalonia-candidate-evidence',

    [string]$PackageName,

    [switch]$SkipPublish,

    [switch]$RunRegressionTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-CandidateRepositoryRoot {
    param([string]$InputRoot)

    if (-not [string]::IsNullOrWhiteSpace($InputRoot)) {
        return [System.IO.Path]::GetFullPath($InputRoot)
    }

    return [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
}

function Join-CandidateUnicodeName {
    param(
        [Parameter(Mandatory = $true)]
        [int[]]$CodePoints
    )

    return [string]::Concat([char[]]$CodePoints)
}

function Resolve-CandidateFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Invoke-CandidateCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [System.Collections.Generic.List[object]]$Results
    )

    Write-Host "==> $Name"
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }

    $Results.Add([PSCustomObject]@{
        name = $Name
        command = "$Executable $($Arguments -join ' ')"
    }) | Out-Null
}

function Assert-CandidateRequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file was not found: $path"
    }
}

function Get-CandidatePrereleasePackages {
    param([Parameter(Mandatory = $true)][string[]]$ProjectPaths)

    $packages = @{}
    foreach ($projectPath in $ProjectPaths) {
        $assetsPath = Join-Path (Split-Path -Path $projectPath -Parent) 'obj\project.assets.json'
        if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
            throw "NuGet assets file was not found: $assetsPath"
        }

        $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($library in $assets.libraries.PSObject.Properties) {
            $parts = $library.Name -split '/', 2
            if ($parts.Count -ne 2 -or $parts[1] -notmatch '-') {
                continue
            }

            $key = $library.Name
            if (-not $packages.ContainsKey($key)) {
                $packages[$key] = [PSCustomObject]@{
                    name = $parts[0]
                    version = $parts[1]
                }
            }
        }
    }

    return @($packages.Values | Sort-Object name, version)
}

function Assert-CandidatePreviewPolicy {
    param([Parameter(Mandatory = $true)][string[]]$ProjectPaths)

    $allowed = @(
        'SkiaSharp',
        'SkiaSharp.NativeAssets.Linux',
        'SkiaSharp.NativeAssets.macOS',
        'SkiaSharp.NativeAssets.WebAssembly',
        'SkiaSharp.NativeAssets.Win32'
    )

    $detected = Get-CandidatePrereleasePackages -ProjectPaths $ProjectPaths
    $unexpected = @($detected | Where-Object { $allowed -notcontains $_.name })
    if ($unexpected.Count -gt 0) {
        $text = $unexpected | ForEach-Object { "$($_.name)/$($_.version)" }
        throw "Unexpected preview/prerelease packages: $($text -join ', ')"
    }

    return $detected
}

function Assert-CandidateNoSourceMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string[]]$RelativeDirectories,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    foreach ($relativeDirectory in $RelativeDirectories) {
        $directory = Join-Path $Root $relativeDirectory
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            continue
        }

        $matches = Get-ChildItem -LiteralPath $directory -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch '\\(bin|obj|publish|\.artifacts)\\' -and
                @('.cs', '.csproj', '.axaml', '.xaml', '.json', '.props', '.targets', '.xml').Contains($_.Extension)
            } |
            Select-String -Pattern $Pattern -ErrorAction SilentlyContinue
        if ($matches) {
            $first = $matches | Select-Object -First 1
            throw "$FailureMessage First match: $($first.Path):$($first.LineNumber)"
        }
    }
}

function Assert-CandidateScriptSafety {
    param([Parameter(Mandatory = $true)][string]$Root)

    $evidenceScript = Join-Path $Root 'scripts\CollectAvaloniaFieldEvidence.ps1'
    $content = Get-Content -LiteralPath $evidenceScript -Raw -Encoding UTF8
    $forbiddenPatterns = @(
        ('Remove' + '-Item'),
        ('DELETE' + ' FROM'),
        ('DROP' + ' TABLE'),
        ('Invoke' + '-Sqlcmd'),
        ('sqlite' + '3'),
        ('dotnet' + ' ef'),
        ('Clear' + 'DeadLetter'),
        ('Retry' + 'DeadLetter')
    )

    foreach ($pattern in $forbiddenPatterns) {
        if ($content.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Evidence script contains forbidden operation '$pattern': $evidenceScript"
        }
    }
}

$repoRoot = Resolve-CandidateRepositoryRoot -InputRoot $RepositoryRoot
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'IIoT.EdgeClient.slnx') -PathType Leaf)) {
    throw "Repository root is invalid: $repoRoot"
}

$fieldChecklistName = 'Avalonia12-' + (Join-CandidateUnicodeName -CodePoints @(0x73B0, 0x573A, 0x8054, 0x8C03, 0x68C0, 0x67E5, 0x6E05, 0x5355)) + '.md'
$nugetExceptionName = 'NuGet' + (Join-CandidateUnicodeName -CodePoints @(0x9884, 0x89C8, 0x4F20, 0x9012, 0x4F9D, 0x8D56, 0x4F8B, 0x5916, 0x8BB0, 0x5F55)) + '.md'
$switchMatrixName = 'Avalonia12-' + (Join-CandidateUnicodeName -CodePoints @(0x5207, 0x6362, 0x524D, 0x5DEE, 0x5F02, 0x77E9, 0x9635)) + '.md'
$switchBlockerName = 'Avalonia12-' + (Join-CandidateUnicodeName -CodePoints @(0x5207, 0x6362, 0x963B, 0x65AD, 0x6E05, 0x5355)) + '.md'
$publishScript = Join-Path $repoRoot 'scripts\PublishAvaloniaMigration.ps1'
$evidenceScript = Join-Path $repoRoot 'scripts\CollectAvaloniaFieldEvidence.ps1'
$releaseRoot = Join-Path (Resolve-CandidateFullPath -BasePath $repoRoot -PathValue $OutputRoot) $Configuration
$launcherRoot = Join-Path $releaseRoot 'avalonia-launcher'
$shellRoot = Join-Path $releaseRoot 'avalonia-shell'
$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
$launcherProject = Join-Path $repoRoot 'src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj'
$shellProject = Join-Path $repoRoot 'src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj'
$results = [System.Collections.Generic.List[object]]::new()

if (-not $SkipPublish) {
    Invoke-CandidateCommand `
        -Name 'publish Avalonia migration package' `
        -Executable 'powershell' `
        -Arguments @('-ExecutionPolicy', 'Bypass', '-File', $publishScript, '-Configuration', $Configuration, '-RepositoryRoot', $repoRoot, '-OutputRoot', $OutputRoot) `
        -Results $results
}

Assert-CandidateRequiredFile -Root $launcherRoot -RelativePath 'IIoT.Edge.Launcher.Avalonia.exe'
Assert-CandidateRequiredFile -Root $launcherRoot -RelativePath 'launcher.profiles.json'
Assert-CandidateRequiredFile -Root $shellRoot -RelativePath 'IIoT.Edge.AvaloniaShell.exe'
Assert-CandidateRequiredFile -Root (Join-Path $shellRoot 'Modules\Homogenization') -RelativePath 'plugin.json'
Assert-CandidateRequiredFile -Root (Join-Path $shellRoot 'Modules\Homogenization') -RelativePath 'IIoT.Edge.Module.Homogenization.Avalonia.dll'
Assert-CandidateRequiredFile -Root $releaseRoot -RelativePath 'release-manifest.json'
Assert-CandidateRequiredFile -Root (Join-Path $releaseRoot 'docs') -RelativePath $fieldChecklistName
Assert-CandidateRequiredFile -Root (Join-Path $releaseRoot 'docs') -RelativePath $nugetExceptionName
Assert-CandidateRequiredFile -Root (Join-Path $releaseRoot 'docs') -RelativePath $switchMatrixName
Assert-CandidateRequiredFile -Root (Join-Path $releaseRoot 'docs') -RelativePath $switchBlockerName

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.releaseKind -ne 'AvaloniaMigration') {
    throw "release-manifest.json releaseKind is invalid: $($manifest.releaseKind)"
}

Invoke-CandidateCommand `
    -Name 'field evidence preflight' `
    -Executable 'powershell' `
    -Arguments @('-ExecutionPolicy', 'Bypass', '-File', $evidenceScript, '-AvaloniaShellDirectory', $shellRoot, '-AvaloniaLauncherDirectory', $launcherRoot, '-PreflightOnly') `
    -Results $results

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "AvaloniaCandidateEvidence-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

Invoke-CandidateCommand `
    -Name 'collect temporary field evidence package' `
    -Executable 'powershell' `
    -Arguments @('-ExecutionPolicy', 'Bypass', '-File', $evidenceScript, '-AvaloniaShellDirectory', $shellRoot, '-AvaloniaLauncherDirectory', $launcherRoot, '-OutputRoot', $EvidenceOutputRoot, '-PackageName', $PackageName, '-CreateZip') `
    -Results $results

Invoke-CandidateCommand `
    -Name 'list AvaloniaShell transitive packages' `
    -Executable 'dotnet' `
    -Arguments @('list', $shellProject, 'package', '--include-transitive') `
    -Results $results

Invoke-CandidateCommand `
    -Name 'scan AvaloniaShell vulnerable packages' `
    -Executable 'dotnet' `
    -Arguments @('list', $shellProject, 'package', '--vulnerable', '--include-transitive') `
    -Results $results

$previewPackages = Assert-CandidatePreviewPolicy -ProjectPaths @($launcherProject, $shellProject)
Assert-CandidateNoSourceMatches `
    -Root $repoRoot `
    -RelativeDirectories @(
        'src\Edge\IIoT.Edge.AvaloniaShell',
        'src\Edge\IIoT.Edge.Host.Bootstrap.Avalonia',
        'src\Presentation\IIoT.Edge.Presentation.Navigation.Avalonia',
        'src\Presentation\IIoT.Edge.Presentation.Panels.Avalonia',
        'src\Presentation\IIoT.Edge.Presentation.Shell.Avalonia',
        'src\Shared\IIoT.Edge.UI.Avalonia',
        'src\Modules\IIoT.Edge.Module.Homogenization.Avalonia'
    ) `
    -Pattern 'System\.Windows|UseWPF|IIoT\.Edge\.UI\.Shared|SukiUI' `
    -FailureMessage 'Avalonia candidate contains forbidden WPF/SukiUI reference.'

Assert-CandidateNoSourceMatches `
    -Root $repoRoot `
    -RelativeDirectories @('src\Presentation\IIoT.Edge.Presentation.Navigation.Avalonia\Features\Hardware\IOView') `
    -Pattern 'ReadDataAsync|WriteDataAsync' `
    -FailureMessage 'Avalonia IOView contains direct PLC read/write call.'

Assert-CandidateScriptSafety -Root $repoRoot

if ($RunRegressionTests) {
    foreach ($testProject in @(
        'src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj',
        'src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj',
        'src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj',
        'src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj',
        'src\Tests\IIoT.Edge.Shell.Tests\IIoT.Edge.Shell.Tests.csproj'
    )) {
        Invoke-CandidateCommand `
            -Name "test $testProject" `
            -Executable 'dotnet' `
            -Arguments @('test', (Join-Path $repoRoot $testProject), '-m:1', '/p:UseSharedCompilation=false') `
            -Results $results
    }
}

$summary = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    releaseRoot = $releaseRoot
    manifest = $manifestPath
    previewPackages = $previewPackages
    commands = @($results)
    regressionTestsRequested = [bool]$RunRegressionTests
}

$summaryPath = Join-Path $releaseRoot 'candidate-validation-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host 'Avalonia migration candidate validation passed.'
Write-Host "  Release: $releaseRoot"
Write-Host "  Summary: $summaryPath"
