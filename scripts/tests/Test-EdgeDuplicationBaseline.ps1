[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$BaselinePath,
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot 'baselines/edge-duplication-baseline.json'
} elseif (-not [IO.Path]::IsPathRooted($BaselinePath)) {
    $BaselinePath = Join-Path $RepositoryRoot $BaselinePath
}

$exactWindow = 16
$nearWindow = 24
$keywords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($keyword in @(
    'abstract','as','async','await','base','bool','break','byte','case','catch','char','checked','class','const',
    'continue','decimal','default','delegate','do','double','else','enum','event','explicit','extern','false',
    'finally','fixed','float','for','foreach','goto','if','implicit','in','int','interface','internal','is','lock',
    'long','namespace','new','null','object','operator','out','override','params','partial','private','protected',
    'public','readonly','record','ref','required','return','sbyte','sealed','short','sizeof','stackalloc','static',
    'string','struct','switch','this','throw','true','try','typeof','uint','ulong','unchecked','unsafe','ushort',
    'using','var','virtual','void','volatile','while','with','yield')) {
    [void]$keywords.Add($keyword)
}

function Get-RelativePath([string]$Path) {
    return [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\', '/')
}

function Get-Scope([string]$RelativePath) {
    return 'production'
}

function ConvertTo-ExactLine([string]$Line) {
    $value = [regex]::Replace($Line, '//.*$', '').Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or
        $value -match '^[{};,]+$' -or
        $value -match '^(?:global\s+)?using\s+' -or
        $value -match '^namespace\s+' -or
        $value -match '^\[[A-Za-z]') {
        return $null
    }
    return [regex]::Replace($value, '\s+', ' ')
}

function ConvertTo-NearLine([string]$Line) {
    $value = [regex]::Replace($Line, '"(?:\\.|[^"\\])*"', '"$str"')
    $value = [regex]::Replace($value, "'(?:\\.|[^'\\])'", "'`$char'")
    $value = [regex]::Replace($value, '(?<![A-Za-z_])(?:0[xX][0-9A-Fa-f]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?[uUlLfFdDmM]?)(?![A-Za-z_])', '$num')
    $value = [regex]::Replace($value, '\b[A-Za-z_][A-Za-z0-9_]*\b', {
        param($match)
        if ($keywords.Contains($match.Value)) { return $match.Value }
        return '$id'
    })
    return $value
}

function Get-Hash([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash)
}

function Add-Windows(
    [Collections.Generic.List[object]]$Target,
    [string]$Scope,
    [string]$Mode,
    [string]$Path,
    [Collections.Generic.List[object]]$Lines,
    [int]$WindowSize) {
    if ($Lines.Count -lt $WindowSize) {
        return
    }

    for ($index = 0; $index -le $Lines.Count - $WindowSize; $index++) {
        $slice = $Lines.GetRange($index, $WindowSize)
        $normalized = if ($Mode -eq 'near') {
            ($slice | ForEach-Object { [string]$_.Near }) -join "`n"
        } else {
            ($slice | ForEach-Object { [string]$_.Text }) -join "`n"
        }
        $Target.Add([pscustomobject]@{
            Scope = $Scope
            Mode = $Mode
            Hash = Get-Hash $normalized
            Path = $Path
            StartLine = [int]$slice[0].Line
            EndLine = [int]$slice[$slice.Count - 1].Line
        })
    }
}

$windows = [Collections.Generic.List[object]]::new()
$sourceRoot = Join-Path $RepositoryRoot 'src'
$sourceFiles = @(Get-ChildItem $sourceRoot -Recurse -Filter '*.cs' -File |
    Where-Object {
        $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' -and
        (Get-RelativePath $_.FullName) -notmatch '^src/(?:Tests|Testing)/'
    } |
    Sort-Object FullName)

foreach ($file in $sourceFiles) {
    $relativePath = Get-RelativePath $file.FullName
    $scope = Get-Scope $relativePath
    $lines = [Collections.Generic.List[object]]::new()
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        $normalized = ConvertTo-ExactLine $line
        if ($null -ne $normalized) {
            $lines.Add([pscustomobject]@{
                Line = $lineNumber
                Text = $normalized
                Near = ConvertTo-NearLine $normalized
            })
        }
    }
    Add-Windows $windows $scope 'exact' $relativePath $lines $exactWindow
    Add-Windows $windows $scope 'near' $relativePath $lines $nearWindow
}

$groups = [Collections.Generic.List[object]]::new()
foreach ($group in @($windows | Group-Object Scope, Mode, Hash)) {
    $distinctFiles = @($group.Group.Path | Sort-Object -Unique)
    if ($distinctFiles.Count -lt 2) {
        continue
    }
    $first = $group.Group[0]
    $instances = @($group.Group | Sort-Object Path, StartLine | ForEach-Object {
        [ordered]@{
            path = $_.Path
            startLine = $_.StartLine
            endLine = $_.EndLine
        }
    })
    $groups.Add([pscustomobject][ordered]@{
        key = "$($first.Scope)|$($first.Mode)|$($first.Hash)"
        scope = $first.Scope
        mode = $first.Mode
        hash = $first.Hash
        instanceCount = $instances.Count
        distinctFileCount = $distinctFiles.Count
        instances = $instances
    })
}

$orderedGroups = @($groups | Sort-Object key)
$metrics = [ordered]@{}
foreach ($scope in @('production')) {
    foreach ($mode in @('exact', 'near')) {
        $matching = @($orderedGroups | Where-Object { $_.scope -eq $scope -and $_.mode -eq $mode })
        $sum = if ($matching.Count -eq 0) {
            0
        } else {
            ($matching | Measure-Object -Property instanceCount -Sum).Sum
        }
        $metrics["$scope.$mode"] = [ordered]@{
            groupCount = $matching.Count
            instanceCount = [int]$sum
        }
    }
}

$actual = [ordered]@{
    schemaVersion = 1
    ruleId = 'TEST-GOV-006'
    algorithm = [ordered]@{
        exactMeaningfulLineWindow = $exactWindow
        nearMeaningfulLineWindow = $nearWindow
        minimumDistinctFiles = 2
    }
    sourceFileCount = $sourceFiles.Count
    metrics = $metrics
    groups = $orderedGroups
}

if ($Update) {
    $parent = Split-Path $BaselinePath -Parent
    [void](New-Item $parent -ItemType Directory -Force)
    $actual | ConvertTo-Json -Depth 12 | Set-Content $BaselinePath -Encoding utf8
    Write-Host "Edge duplication baseline updated: files=$($sourceFiles.Count), groups=$($orderedGroups.Count), path=$BaselinePath."
    return
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "TEST-GOV-006 duplication baseline does not exist: $BaselinePath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 32
if ($baseline.schemaVersion -ne 1 -or $baseline.ruleId -ne 'TEST-GOV-006') {
    throw 'TEST-GOV-006 duplication baseline schemaVersion/ruleId is invalid.'
}
$expectedMetricNames = @('production.exact', 'production.near')
$actualMetricNames = @($baseline.metrics.PSObject.Properties.Name | Sort-Object)
$invalidBaselineGroups = @($baseline.groups | Where-Object {
    [string]$_.scope -cne 'production' -or
    [string]$_.mode -notin @('exact', 'near') -or
    -not ([string]$_.key).StartsWith("production|$([string]$_.mode)|", [StringComparison]::Ordinal)
})
if (($actualMetricNames -join '|') -cne (($expectedMetricNames | Sort-Object) -join '|') -or
    $invalidBaselineGroups.Count -gt 0) {
    throw 'TEST-GOV-006 duplication baseline must contain production.exact/production.near metrics and production groups only.'
}
if ($baseline.algorithm.exactMeaningfulLineWindow -ne $exactWindow -or
    $baseline.algorithm.nearMeaningfulLineWindow -ne $nearWindow) {
    throw 'TEST-GOV-006 duplication algorithm changed; regenerate only after explicit baseline review.'
}

$baselineByKey = @{}
foreach ($group in @($baseline.groups)) {
    $baselineByKey[[string]$group.key] = $group
}
$findings = [Collections.Generic.List[string]]::new()
foreach ($group in $orderedGroups) {
    if (-not $baselineByKey.ContainsKey($group.key)) {
        $paths = @($group.instances.path | Sort-Object -Unique) -join ', '
        $findings.Add("new $($group.scope)/$($group.mode) clone group $($group.hash) in $paths")
        continue
    }
    $prior = $baselineByKey[$group.key]
    if ($group.instanceCount -gt [int]$prior.instanceCount -or
        $group.distinctFileCount -gt [int]$prior.distinctFileCount) {
        $findings.Add("expanded $($group.scope)/$($group.mode) clone group $($group.hash): instances $($prior.instanceCount)->$($group.instanceCount), files $($prior.distinctFileCount)->$($group.distinctFileCount)")
    }
}

foreach ($metricName in $metrics.Keys) {
    $priorMetric = $baseline.metrics.$metricName
    $actualMetric = $metrics[$metricName]
    if ($actualMetric.groupCount -gt [int]$priorMetric.groupCount -or
        $actualMetric.instanceCount -gt [int]$priorMetric.instanceCount) {
        $findings.Add("aggregate $metricName increased: groups $($priorMetric.groupCount)->$($actualMetric.groupCount), instances $($priorMetric.instanceCount)->$($actualMetric.instanceCount)")
    }
}

if ($findings.Count -gt 0) {
    throw "TEST-GOV-006 duplication ratchet failed:`n - $($findings -join "`n - ")"
}

Write-Host "Edge duplication ratchet passed: files=$($sourceFiles.Count), exact/near groups=$($orderedGroups.Count), newGroups=0, expandedGroups=0."
