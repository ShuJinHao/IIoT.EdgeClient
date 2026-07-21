[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidencePath,
    [string]$RepositoryRoot,
    [string]$LedgerPath = 'eng/baselines/edge-plugin-contract-ledger.json',
    [string]$SchemaPath = 'eng/edge-phase-close-evidence.schema.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else { $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -LiteralPath $PathValue -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-NoReparsePath {
    param([Parameter(Mandatory = $true)][string]$FullPath)
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $FullPath)
    $current = $RepositoryRoot
    foreach ($segment in $relative.Split(
            [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
            throw "EDGE-SPLIT-EVIDENCE-001 evidence paths must not traverse symlink/reparse points: $current."
        }
    }
}

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $fullPath = if ([IO.Path]::IsPathRooted($PathValue)) {
        [IO.Path]::GetFullPath($PathValue)
    }
    else { [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue)) }
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $fullPath)
    if ($relative -eq '..' -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "EDGE-SPLIT-EVIDENCE-001 path must stay inside the repository: $PathValue."
    }
    Assert-NoReparsePath $fullPath
    return $fullPath
}

function ConvertTo-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return [IO.Path]::GetRelativePath($RepositoryRoot, [IO.Path]::GetFullPath($PathValue)).Replace('\', '/')
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = & git -C $RepositoryRoot @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-SPLIT-EVIDENCE-001 git command failed: git $($Arguments -join ' ')`n$output"
    }
    return $output.Trim()
}

function Get-GitBlobSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$RepositoryPath
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('show')
    $startInfo.ArgumentList.Add("$Commit`:$RepositoryPath")
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $memory = [IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) { throw 'could not start git show' }
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "EDGE-SPLIT-EVIDENCE-001 git show failed for $Commit`:$RepositoryPath`: $errorText"
        }
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant()
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

function Assert-TrackedRegularBlob {
    param(
        [Parameter(Mandatory = $true)][string]$Head,
        [Parameter(Mandatory = $true)][string]$RepositoryPath,
        [Parameter(Mandatory = $true)][string]$WorktreePath
    )
    $treeEntry = Invoke-GitText @('ls-tree', $Head, '--', $RepositoryPath)
    $expectedSuffix = "`t$RepositoryPath"
    if ($treeEntry -notmatch '^100644 blob [0-9a-f]{40}\t' -or
        -not $treeEntry.EndsWith($expectedSuffix, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-EVIDENCE-001 tracked authority must be an exact 100644 regular blob: $RepositoryPath."
    }
    if ((Get-GitBlobSha256 -Commit $Head -RepositoryPath $RepositoryPath) -cne (Get-Sha256 $WorktreePath)) {
        throw "EDGE-SPLIT-EVIDENCE-001 tracked authority differs between exact evidence HEAD and worktree: $RepositoryPath."
    }
}

function Get-CounterValue {
    param(
        [Parameter(Mandatory = $true)][Xml.XmlElement]$Counters,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $value = $Counters.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return 0 }
    if ($value -notmatch '^\d+$') { throw "EDGE-SPLIT-EVIDENCE-001 invalid TRX counter $Name=$value." }
    return [int]$value
}

function Read-TrxCounters {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($PathValue, $settings)
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    try { $document.Load($reader) }
    finally { $reader.Dispose() }
    $manager = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $manager.AddNamespace('t', [string]$document.DocumentElement.NamespaceURI)
    $nodes = @($document.SelectNodes('//t:ResultSummary/t:Counters', $manager))
    if ($nodes.Count -ne 1) {
        throw "EDGE-SPLIT-EVIDENCE-001 TRX must contain exactly one ResultSummary/Counters node: $PathValue."
    }
    $counters = [Xml.XmlElement]$nodes[0]
    return [pscustomobject][ordered]@{
        discovered = Get-CounterValue $counters 'total'
        executed = Get-CounterValue $counters 'executed'
        passed = Get-CounterValue $counters 'passed'
        failed = Get-CounterValue $counters 'failed'
        skipped = (Get-CounterValue $counters 'notExecuted') +
            (Get-CounterValue $counters 'inconclusive') +
            (Get-CounterValue $counters 'notRunnable')
    }
}

$resolvedEvidencePath = Resolve-RepositoryPath $EvidencePath
$resolvedLedgerPath = Resolve-RepositoryPath $LedgerPath
$resolvedSchemaPath = Resolve-RepositoryPath $SchemaPath
$resolvedValidatorPath = [IO.Path]::GetFullPath($PSCommandPath)
$inventoryPath = Resolve-RepositoryPath 'scripts/tests/edge-test-inventory.json'
$requiredCountsPath = Resolve-RepositoryPath 'scripts/tests/required-test-counts.json'
foreach ($path in @($resolvedEvidencePath, $resolvedLedgerPath, $resolvedSchemaPath, $resolvedValidatorPath, $inventoryPath, $requiredCountsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "EDGE-SPLIT-EVIDENCE-001 required evidence input does not exist: $path."
    }
}
if ([IO.Path]::GetFullPath($resolvedEvidencePath) -ceq [IO.Path]::GetFullPath($resolvedLedgerPath)) {
    throw 'EDGE-SPLIT-EVIDENCE-001 external phase-close evidence must not be carried inside the canonical ledger.'
}

$evidenceRaw = Get-Content -LiteralPath $resolvedEvidencePath -Raw
if (-not ($evidenceRaw | Test-Json -SchemaFile $resolvedSchemaPath -ErrorAction Stop)) {
    throw 'EDGE-SPLIT-EVIDENCE-001 external evidence fails its strict schema.'
}
$evidence = $evidenceRaw | ConvertFrom-Json -Depth 50
$ledger = Get-Content -LiteralPath $resolvedLedgerPath -Raw | ConvertFrom-Json -Depth 100
$currentHead = Invoke-GitText @('rev-parse', 'HEAD')
if ([string]$evidence.evidenceHead -cne $currentHead -or [string]$evidence.source.recordedHead -cne $currentHead) {
    throw 'EDGE-SPLIT-EVIDENCE-001 evidence and its execution source must bind the exact final HEAD.'
}

$canonicalRelativePath = ConvertTo-RepositoryPath $resolvedLedgerPath
$schemaRelativePath = ConvertTo-RepositoryPath $resolvedSchemaPath
$validatorRelativePath = ConvertTo-RepositoryPath $resolvedValidatorPath
Assert-TrackedRegularBlob $currentHead $canonicalRelativePath $resolvedLedgerPath
Assert-TrackedRegularBlob $currentHead $schemaRelativePath $resolvedSchemaPath
Assert-TrackedRegularBlob $currentHead $validatorRelativePath $resolvedValidatorPath
if ([string]$ledger.testInventory.phaseCloseEvidenceProtocol.schemaPath -cne $schemaRelativePath -or
    [string]$ledger.testInventory.phaseCloseEvidenceProtocol.validatorPath -cne $validatorRelativePath -or
    [string]$ledger.testInventory.phaseCloseEvidenceProtocol.schemaSha256 -cne (Get-Sha256 $resolvedSchemaPath) -or
    [string]$ledger.testInventory.phaseCloseEvidenceProtocol.validatorSha256 -cne (Get-Sha256 $resolvedValidatorPath)) {
    throw 'EDGE-SPLIT-EVIDENCE-001 canonical ledger does not bind the exact external evidence schema and validator bytes.'
}

$parents = @(($currentHead | ForEach-Object { Invoke-GitText @('show', '-s', '--format=%P', $_) }).Split(
        ' ', [StringSplitOptions]::RemoveEmptyEntries))
$committedPaths = @((Invoke-GitText @('-c', 'core.quotePath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', $currentHead)) -split "`r?`n" |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($parents.Count -ne 1 -or [string]$parents[0] -cne [string]$ledger.sourceState.head -or
    $committedPaths.Count -ne 1 -or [string]$committedPaths[0] -cne $canonicalRelativePath) {
    throw 'EDGE-SPLIT-EVIDENCE-001 final evidence HEAD must be the direct ledger-only child of the recorded implementation HEAD.'
}
$status = (& git -C $RepositoryRoot -c core.quotePath=false status --porcelain=v1 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace($status)) {
    throw 'EDGE-SPLIT-EVIDENCE-001 final external evidence validation requires a clean tracked/unignored worktree.'
}

$ledgerSha256 = Get-Sha256 $resolvedLedgerPath
if ([string]$evidence.canonicalLedger.path -cne $canonicalRelativePath -or
    [string]$evidence.canonicalLedger.sha256 -cne $ledgerSha256 -or
    [string]$evidence.canonicalLedger.batchId -cne [string]$ledger.batchId -or
    [string]$evidence.batchId -cne [string]$ledger.batchId) {
    throw 'EDGE-SPLIT-EVIDENCE-001 external evidence does not bind the exact canonical ledger bytes and batch.'
}
if ([string]$evidence.source.type -ceq 'required-ci') {
    $runUrlLeaf = ([Uri][string]$evidence.source.workflowRunUrl).Segments[-1].Trim('/')
    if ($runUrlLeaf -cne [string]$evidence.source.workflowRunId) {
        throw 'EDGE-SPLIT-EVIDENCE-001 required-CI run URL and immutable run ID disagree.'
    }
}

$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json -Depth 30
$requiredCounts = Get-Content -LiteralPath $requiredCountsPath -Raw | ConvertFrom-Json -Depth 30
if ([string]$ledger.testInventory.inventorySha256 -cne (Get-Sha256 $inventoryPath) -or
    [string]$ledger.testInventory.requiredCountsSha256 -cne (Get-Sha256 $requiredCountsPath)) {
    throw 'EDGE-SPLIT-EVIDENCE-001 canonical ledger test inventories differ from exact final HEAD bytes.'
}
$expectedCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
foreach ($project in @($requiredCounts.projects)) {
    $projectName = [IO.Path]::GetFileNameWithoutExtension([string]$project.projectPath)
    if (-not $expectedCounts.TryAdd($projectName, [int]$project.discovered)) {
        throw "EDGE-SPLIT-EVIDENCE-001 required count inventory has duplicate runner ID: $projectName."
    }
}
$requiredRunnerNames = @($inventory.projects | Where-Object required | ForEach-Object { [string]$_.projectName })
if ($requiredRunnerNames.Count -ne $expectedCounts.Count) {
    throw 'EDGE-SPLIT-EVIDENCE-001 required runner inventory and count inventory disagree.'
}

$artifactPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$runnerNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$totals = [ordered]@{ discovered = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
foreach ($artifact in @($evidence.source.artifacts)) {
    $runnerId = [string]$artifact.runnerId
    if (-not $expectedCounts.ContainsKey($runnerId) -or -not $runnerNames.Add($runnerId) -or
        -not $artifactPaths.Add([string]$artifact.path)) {
        throw "EDGE-SPLIT-EVIDENCE-001 artifact runner/path is unknown, duplicate, or Windows-colliding: $runnerId|$($artifact.path)."
    }
    $artifactPath = Resolve-RepositoryPath ([string]$artifact.path)
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf) -or
        [long]$artifact.size -ne (Get-Item -LiteralPath $artifactPath).Length -or
        [string]$artifact.sha256 -cne (Get-Sha256 $artifactPath)) {
        throw "EDGE-SPLIT-EVIDENCE-001 TRX artifact bytes differ from the external manifest: $($artifact.path)."
    }
    $counters = Read-TrxCounters $artifactPath
    if ([int]$counters.discovered -ne $expectedCounts[$runnerId] -or
        [int]$counters.discovered -ne [int]$counters.executed -or
        [int]$counters.executed -ne [int]$counters.passed -or
        [int]$counters.failed -ne 0 -or [int]$counters.skipped -ne 0) {
        throw "EDGE-SPLIT-EVIDENCE-001 TRX does not reconcile for $runnerId."
    }
    foreach ($name in @('discovered', 'executed', 'passed', 'failed', 'skipped')) {
        $totals[$name] = [int]$totals[$name] + [int]$counters.$name
    }
}
if ($runnerNames.Count -ne $expectedCounts.Count) {
    throw 'EDGE-SPLIT-EVIDENCE-001 external artifact manifest does not cover every required runner exactly once.'
}

$reconciliation = $evidence.reconciliation
if ([int]$reconciliation.requiredRunnerCount -ne $expectedCounts.Count -or
    [int]$reconciliation.artifactCount -ne @($evidence.source.artifacts).Count -or
    [int]$reconciliation.requiredRunnerCount -ne [int]$ledger.testInventory.requiredRunnerCount -or
    [int]$reconciliation.discovered -ne [int]$requiredCounts.caseCount -or
    [int]$reconciliation.discovered -ne [int]$ledger.testInventory.discoveredCaseCount -or
    [int]$reconciliation.discovered -ne [int]$totals.discovered -or
    [int]$reconciliation.executed -ne [int]$totals.executed -or
    [int]$reconciliation.passed -ne [int]$totals.passed -or
    [int]$reconciliation.failed -ne 0 -or [int]$reconciliation.skipped -ne 0 -or
    [int]$totals.discovered -ne [int]$totals.executed -or
    [int]$totals.executed -ne [int]$totals.passed -or
    [int]$totals.failed -ne 0 -or [int]$totals.skipped -ne 0) {
    throw 'EDGE-SPLIT-EVIDENCE-001 external phase-close totals do not satisfy discovered=executed=passed and failed=skipped=0.'
}

Write-Host "Edge external phase-close evidence passed: batch=$($evidence.batchId), head=$currentHead, source=$($evidence.source.type), runners=$($runnerNames.Count), passed=$($totals.passed)."
