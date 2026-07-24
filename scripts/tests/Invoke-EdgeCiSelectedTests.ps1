[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$SelectionPath = 'artifacts/ci-selection.json',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/ci-test-results',
    [switch]$NoBuild,
    [switch]$CollectCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $RepositoryRoot).Path
$resolvedSelection = if ([IO.Path]::IsPathRooted($SelectionPath)) {
    [IO.Path]::GetFullPath($SelectionPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $SelectionPath))
}
if (-not (Test-Path $resolvedSelection -PathType Leaf)) {
    throw "CI selection is missing: $resolvedSelection"
}
$selection = Get-Content $resolvedSelection -Raw | ConvertFrom-Json
if ([int]$selection.schemaVersion -ne 2) {
    throw "Unsupported Edge CI selection schema: $($selection.schemaVersion)"
}
$projects = @($selection.selectedDotNetProjects)
if ($projects.Count -eq 0) {
    throw 'CI selection contains no .NET test projects.'
}
$mode = [string]$selection.mode
$allowedCategories = @('Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')
$allowedByMode = @{
    Default = @('Architecture', 'Security', 'Business', 'DeploymentContract')
    Deployment = @('Architecture', 'Security', 'DeploymentContract')
    Quality = @('Architecture', 'Security', 'Quality')
    CrossProject = @('CrossProject')
    Full = $allowedCategories
}
if (-not $allowedByMode.ContainsKey($mode)) {
    throw "Unsupported Edge CI selection mode: $mode"
}
if ($CollectCoverage -and $mode -notin @('Quality', 'Full')) {
    throw "Coverage is a Quality operation and is forbidden for Edge CI mode '$mode'."
}
foreach ($project in $projects) {
    $categories = @($project.categories)
    if ($categories.Count -eq 0 -or @($categories | Where-Object {
                $_ -notin $allowedCategories -or $_ -notin $allowedByMode[$mode]
            }).Count -gt 0) {
        throw "Edge CI selection contains invalid categories for mode ${mode}: project=$($project.path) categories=$($categories -join ',')"
    }
    if ($categories -contains 'DeploymentContract' -and -not [bool]$selection.deploymentAffected) {
        throw "Edge DeploymentContract selection is not bound to an affected deployment path: $($project.path)"
    }
}

$resolvedResults = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $ResultsDirectory))
}
[void](New-Item $resolvedResults -ItemType Directory -Force)
$results = [Collections.Generic.List[object]]::new()
$head = ((& git -C $root rev-parse HEAD 2>&1) -join "`n").Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw "Edge CI test execution is not bound to a full Git HEAD: $head"
}

foreach ($project in $projects) {
    $projectPath = Join-Path $root ([string]$project.path)
    if (-not (Test-Path $projectPath -PathType Leaf)) {
        throw "Selected test project is missing: $($project.path)"
    }
    $projectResults = Join-Path $resolvedResults ([string]$project.projectName)
    [void](New-Item $projectResults -ItemType Directory -Force)
    $arguments = @(
        'test',
        $projectPath,
        '-c', $Configuration,
        '--no-restore',
        '-m:1',
        '-p:BuildInParallel=false',
        '--disable-build-servers',
        '--nologo',
        "-p:SourceRevisionId=$head",
        '--logger', "trx;LogFileName=$($project.projectName).trx",
        '--results-directory', $projectResults
    )
    if ($CollectCoverage) {
        $arguments += @('--collect', 'XPlat Code Coverage')
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$project.testFilter)) {
        $arguments += @('--filter', [string]$project.testFilter)
    }
    if ($NoBuild) {
        $arguments += '--no-build'
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Selected Edge test runner failed: $($project.path)"
    }

    $trxFiles = @(Get-ChildItem $projectResults -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) {
        throw "Expected one TRX for $($project.projectName), found $($trxFiles.Count)."
    }
    [xml]$trx = Get-Content $trxFiles[0].FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $notExecuted = [int]$counters.notExecuted
    if ($total -le 0 -or $total -ne $executed -or $total -ne $passed -or
        $failed -ne 0 -or $notExecuted -ne 0) {
        throw "$($project.projectName) current discovery did not reconcile: discovered=$total executed=$executed passed=$passed failed=$failed skipped=$notExecuted"
    }
    $results.Add([ordered]@{
        projectName = [string]$project.projectName
        projectPath = [string]$project.path
        categories = @($project.categories)
        testFilter = [string]$project.testFilter
        runtime = [string]$project.runtime
        discovered = $total
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $notExecuted
        trx = [IO.Path]::GetRelativePath($root, $trxFiles[0].FullName).Replace('\', '/')
    })
}

$selectionChangedFileSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($changedFile in @($selection.changedFiles)) {
    [void]$selectionChangedFileSet.Add($changedFile.ToString())
}
[string[]]$selectionChangedFiles = @($selectionChangedFileSet)
[Array]::Sort($selectionChangedFiles, [StringComparer]::Ordinal)
$selectionScopeBytes = [Text.UTF8Encoding]::new($false).GetBytes(
    [string]::Join("`n", $selectionChangedFiles))
$selectionScopeSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($selectionScopeBytes)).ToLowerInvariant()
$inventoryPath = Join-Path $resolvedResults 'current-discovery.json'
$discoveredTotal = [int](($results |
        ForEach-Object { [int]$_['discovered'] } |
        Measure-Object -Sum).Sum)
[ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    selectionMode = [string]$selection.mode
    sourceRevision = $head
    selectedCategories = @($selection.selectedCategories | Sort-Object -Unique)
    selectionScope = [ordered]@{
        kind = 'changed-files'
        count = $selectionChangedFiles.Count
        sha256 = $selectionScopeSha256
    }
    selectedProjects = $results.Count
    discovered = $discoveredTotal
    projects = $results
} | ConvertTo-Json -Depth 8 | Set-Content $inventoryPath -Encoding utf8

Write-Host "EDGE_CI_TESTS_OK projects=$($results.Count) discovered=$discoveredTotal output=$inventoryPath"
