[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$ruleId = 'EDGE-SOURCE-QUALITY-001'
$findings = [Collections.Generic.List[string]]::new()
$skipSegments = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($segment in @('bin', 'obj', 'publish', '.git', '.vs', '.dotnet', '.artifacts')) {
    [void]$skipSegments.Add($segment)
}

function Get-RepositoryPath([string]$Path) {
    return [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\', '/')
}

function Test-SkippedPath([string]$Path) {
    foreach ($segment in $Path.Split([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) {
        if ($skipSegments.Contains($segment)) { return $true }
    }
    return $false
}

function Get-SourceFiles([string]$Root, [string]$Filter) {
    if (-not (Test-Path $Root -PathType Container)) { return @() }
    return @(Get-ChildItem $Root -Recurse -File -Filter $Filter |
        Where-Object { -not (Test-SkippedPath $_.FullName) } |
        Sort-Object FullName)
}

function Add-LiteralFindings([IO.FileInfo[]]$Files, [string[]]$ForbiddenValues, [string]$Description) {
    foreach ($file in $Files) {
        $text = [IO.File]::ReadAllText($file.FullName)
        foreach ($value in $ForbiddenValues) {
            if ($text.Contains($value, [StringComparison]::Ordinal)) {
                $findings.Add("$(Get-RepositoryPath $file.FullName) contains $Description '$value'.")
            }
        }
    }
}

$productionFiles = @(Get-SourceFiles (Join-Path $RepositoryRoot 'src') '*.cs' |
    Where-Object { (Get-RepositoryPath $_.FullName) -notlike 'src/Tests/*' })
Add-LiteralFindings $productionFiles @('Debug.WriteLine', 'System.Diagnostics.Debug.WriteLine') 'debug output'
Add-LiteralFindings $productionFiles @(
    '[DataPipeline]', '[ContextStore]', '[Retry-Cloud]', '[Retry-MES]', '[DeviceLogSync]',
    '[RecipeSync]', '[CapacitySync]', 'Initialized and started', 'Task failed',
    'timeout_exceeded', 'consumer_returned_false') 'retired visible log prefix'

$abstractionFiles = @(Get-SourceFiles (
        Join-Path $RepositoryRoot 'src/Application/IIoT.Edge.Application/Abstractions') '*.cs')
$abstractionImplementationPatterns = [ordered]@{
    'static implementation class' = '\b(?:internal\s+|public\s+)?static\s+class\b'
    'helper implementation class' = '\bclass\s+\w*Helper\b'
    'direct filesystem implementation' = '\b(?:File|Directory)\.'
    'cryptographic implementation' = '\bSHA256\s*\.(?:Create|HashData|TryHashData)\b'
    'timer implementation' = '\bTask\.Delay\b'
}
foreach ($file in $abstractionFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($entry in $abstractionImplementationPatterns.GetEnumerator()) {
        if ($text -match $entry.Value) {
            $findings.Add("$(Get-RepositoryPath $file.FullName) contains $($entry.Key).")
        }
    }
}

$testFiles = @(Get-SourceFiles (Join-Path $RepositoryRoot 'src/Tests') '*.cs')
$longDelayPattern = [regex]::new(
    'Task\.Delay\(\s*(?:1\d{2,}|\d{4,}|TimeSpan\.FromMilliseconds\(\s*(?:1\d{2,}|\d{4,}))',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)
foreach ($file in $testFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($longDelayPattern.IsMatch($text)) {
        $findings.Add("$(Get-RepositoryPath $file.FullName) uses a long fixed Task.Delay for synchronization.")
    }
}

$textExtensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @('.cs', '.csproj', '.props', '.targets', '.ps1', '.json', '.config', '.slnx', '.axaml', '.xaml', '.md', '.yml', '.yaml')) {
    [void]$textExtensions.Add($extension)
}
$activeTextFiles = @(Get-ChildItem $RepositoryRoot -Recurse -File |
    Where-Object { -not (Test-SkippedPath $_.FullName) -and $textExtensions.Contains($_.Extension) } |
    Sort-Object FullName)
Add-LiteralFindings $activeTextFiles @(
    "`u{FFFD}", "`u{6D93}`u{5D85}", "`u{6D60}`u{64B3}", "`u{93CD}`u{572D}",
    "`u{6D30}`u{8930}", "`u{6DC7}`u{6FE7}", "`u{93C8}`u{E061}", "`u{9359}`u{6A58}",
    "`u{9356}`u{E15C}", "`u{8FBE}`u{64B3}", "`u{93C3}`u{72B3}", "`u{7039}`u{6C56}") 'mojibake marker'

$credentialFiles = @(
    Get-ChildItem (Join-Path $RepositoryRoot 'src/Edge/IIoT.Edge.Shell') -File -Filter 'appsettings*.json'
    Get-Item (Join-Path $RepositoryRoot 'src/Edge/IIoT.Edge.Launcher/launcher.accounts.sample.json') -ErrorAction SilentlyContinue
) | Where-Object { $null -ne $_ }
$credentialPatterns = [ordered]@{
    'committed JWT' = 'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+'
    'committed SHA256 password hash' = '"PasswordHash"\s*:\s*"[0-9A-Fa-f]{64}"'
    'committed default password' = '"Password"\s*:\s*"123456"'
    'retired LicenseKey setting' = '"LicenseKey"\s*:'
}
foreach ($file in $credentialFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($entry in $credentialPatterns.GetEnumerator()) {
        if ($text -match $entry.Value) {
            $findings.Add("$(Get-RepositoryPath $file.FullName) contains $($entry.Key).")
        }
    }
}

if ($findings.Count -gt 0) {
    throw "$ruleId source-quality policy failed:`n - $($findings -join "`n - ")"
}

Write-Host "$ruleId source-quality policy passed: productionFiles=$($productionFiles.Count), testFiles=$($testFiles.Count), activeTextFiles=$($activeTextFiles.Count)."
