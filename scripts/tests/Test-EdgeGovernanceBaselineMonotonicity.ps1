[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$BaseRef
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = (& git -C $RepositoryRoot @Arguments 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "TEST-GOV-BASE-001 git $($Arguments -join ' ') failed with exit code ${exitCode}:`n$output"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

if ([string]::IsNullOrWhiteSpace($BaseRef) -or $BaseRef -match '^0+$') {
    $remoteMain = Invoke-GitText -Arguments @('rev-parse', '--verify', 'origin/main') -AllowFailure
    if ($remoteMain.ExitCode -eq 0) {
        $BaseRef = (Invoke-GitText -Arguments @('merge-base', 'origin/main', 'HEAD')).Output.Trim()
    } else {
        $BaseRef = (Invoke-GitText -Arguments @('rev-parse', 'HEAD^')).Output.Trim()
    }
}

$resolvedBaseRef = (Invoke-GitText -Arguments @('rev-parse', '--verify', "${BaseRef}^{commit}")).Output.Trim()
$candidateHead = (Invoke-GitText -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')).Output.Trim()
if ($resolvedBaseRef -ceq $candidateHead) {
    throw 'TEST-GOV-BASE-001 BaseRef must identify the pre-change commit, not candidate HEAD.'
}
$ancestorCheck = Invoke-GitText -Arguments @('merge-base', '--is-ancestor', $resolvedBaseRef, $candidateHead) -AllowFailure
if ($ancestorCheck.ExitCode -ne 0) {
    throw "TEST-GOV-BASE-001 BaseRef must be an ancestor of candidate HEAD: base=$resolvedBaseRef head=$candidateHead."
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string]$Name,
        $Default = $null
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Get-Collection {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    $value = Get-PropertyValue -Object $Object -Name $Name
    if ($null -eq $value) { return @() }
    return @($value)
}

function Resolve-AnchorRef {
    param([Parameter(Mandatory)][string]$RelativePath)

    $baseObject = Invoke-GitText -Arguments @('cat-file', '-e', "${resolvedBaseRef}:$RelativePath") -AllowFailure
    if ($baseObject.ExitCode -eq 0) {
        return $resolvedBaseRef
    }

    $history = Invoke-GitText -Arguments @(
        'log', '--reverse', '--format=%H', '--diff-filter=A',
        "${resolvedBaseRef}..HEAD", '--', $RelativePath)
    $addition = @($history.Output -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($addition)) {
        throw "TEST-GOV-BASE-001 baseline '$RelativePath' has no committed base or bootstrap anchor; same-change bootstrap is forbidden."
    }
    return $addition.Trim()
}

function Get-AnchoredJson {
    param([Parameter(Mandatory)][string]$RelativePath)

    $anchorRef = Resolve-AnchorRef $RelativePath
    $document = (Invoke-GitText -Arguments @('show', "${anchorRef}:$RelativePath")).Output |
        ConvertFrom-Json -Depth 64
    return [pscustomobject]@{
        AnchorRef = $anchorRef
        Document = $document
    }
}

function Get-CurrentJson {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "TEST-GOV-BASE-001 current baseline is missing: $RelativePath"
    }
    return Get-Content $path -Raw | ConvertFrom-Json -Depth 64
}

function Assert-RateNotLower {
    param(
        [Parameter(Mandatory)][double]$Prior,
        [Parameter(Mandatory)][double]$Current,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Current -lt $Prior) {
        throw "TEST-GOV-BASE-001 $Label weakened: $Prior -> $Current. Baselines may only stay equal or tighten."
    }
}

function Assert-CountNotHigher {
    param(
        [Parameter(Mandatory)][int]$Prior,
        [Parameter(Mandatory)][int]$Current,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Current -gt $Prior) {
        throw "TEST-GOV-BASE-001 $Label expanded: $Prior -> $Current. Baselines may only stay equal or tighten."
    }
}

function Assert-CoverageBaseline {
    param(
        [Parameter(Mandatory)][object]$Prior,
        [Parameter(Mandatory)][object]$Current
    )

    if ($Prior.schemaVersion -ne $Current.schemaVersion -or
        $Prior.ruleId -cne $Current.ruleId -or
        $Prior.collector -cne $Current.collector) {
        throw 'TEST-GOV-BASE-001 coverage schema, rule, or collector pin changed.'
    }
    # Current-tree inventory, discovery, TRX and coverage reconciliation prove
    # the exact runner/report set. Historical quality monotonicity must not turn
    # that set into a permanent project-count floor after a real test migration
    # or physical feature retirement.
    Assert-RateNotLower ([double]$Prior.overall.lineRate) ([double]$Current.overall.lineRate) 'coverage overall line rate'
    Assert-RateNotLower ([double]$Prior.overall.branchRate) ([double]$Current.overall.branchRate) 'coverage overall branch rate'

    $currentComponents = @{}
    foreach ($entry in @(Get-Collection $Current 'components')) {
        $currentComponents[[string]$entry.component] = $entry.metrics
    }
    foreach ($entry in @(Get-Collection $Prior 'components')) {
        $name = [string]$entry.component
        if (-not $currentComponents.ContainsKey($name)) {
            $remainingSources = @(Get-ChildItem (Join-Path $RepositoryRoot "src/$name") -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })
            if ($remainingSources.Count -gt 0) {
                throw "TEST-GOV-BASE-001 covered component '$name' disappeared from the baseline while production sources remain."
            }
            continue
        }
        $metrics = $currentComponents[$name]
        Assert-RateNotLower ([double]$entry.metrics.lineRate) ([double]$metrics.lineRate) "coverage component $name line rate"
        Assert-RateNotLower ([double]$entry.metrics.branchRate) ([double]$metrics.branchRate) "coverage component $name branch rate"
    }

    $currentThresholds = @{}
    foreach ($entry in @(Get-Collection $Current 'highRiskThresholds')) {
        $currentThresholds[[string]$entry.path] = $entry
    }
    foreach ($entry in @(Get-Collection $Prior 'highRiskThresholds')) {
        $path = [string]$entry.path
        if (-not $currentThresholds.ContainsKey($path)) {
            if (Test-Path (Join-Path $RepositoryRoot $path) -PathType Leaf) {
                throw "TEST-GOV-BASE-001 high-risk coverage threshold disappeared while source remains: $path"
            }
            continue
        }
        $threshold = $currentThresholds[$path]
        Assert-RateNotLower ([double]$entry.minimumLineRate) ([double]$threshold.minimumLineRate) "high-risk $path minimum line rate"
        Assert-RateNotLower ([double]$entry.minimumBranchRate) ([double]$threshold.minimumBranchRate) "high-risk $path minimum branch rate"
    }
}

function Assert-DuplicationBaseline {
    param(
        [Parameter(Mandatory)][object]$Prior,
        [Parameter(Mandatory)][object]$Current
    )

    if ($Prior.schemaVersion -ne $Current.schemaVersion -or $Prior.ruleId -cne $Current.ruleId) {
        throw 'TEST-GOV-BASE-001 duplication schema or rule pin changed.'
    }
    if ([int]$Current.algorithm.exactMeaningfulLineWindow -gt [int]$Prior.algorithm.exactMeaningfulLineWindow -or
        [int]$Current.algorithm.nearMeaningfulLineWindow -gt [int]$Prior.algorithm.nearMeaningfulLineWindow -or
        [int]$Current.algorithm.minimumDistinctFiles -gt [int]$Prior.algorithm.minimumDistinctFiles) {
        throw 'TEST-GOV-BASE-001 duplication detection algorithm was weakened.'
    }

    foreach ($metric in @('production.exact', 'production.near', 'testSupport.exact', 'testSupport.near', 'tests.exact', 'tests.near')) {
        $priorMetric = $Prior.metrics.PSObject.Properties[$metric].Value
        $currentMetric = $Current.metrics.PSObject.Properties[$metric].Value
        Assert-CountNotHigher ([int]$priorMetric.groupCount) ([int]$currentMetric.groupCount) "duplication $metric groups"
        Assert-CountNotHigher ([int]$priorMetric.instanceCount) ([int]$currentMetric.instanceCount) "duplication $metric instances"
    }

    $priorGroups = @{}
    foreach ($group in @(Get-Collection $Prior 'groups')) { $priorGroups[[string]$group.key] = $group }
    foreach ($group in @(Get-Collection $Current 'groups')) {
        $key = [string]$group.key
        if (-not $priorGroups.ContainsKey($key)) {
            throw "TEST-GOV-BASE-001 duplication baseline self-authorized new clone group: $key"
        }
        $old = $priorGroups[$key]
        Assert-CountNotHigher ([int]$old.instanceCount) ([int]$group.instanceCount) "duplication group $key instances"
        Assert-CountNotHigher ([int]$old.distinctFileCount) ([int]$group.distinctFileCount) "duplication group $key files"
    }
}

function Assert-MutationBaseline {
    param(
        [Parameter(Mandatory)][object]$Prior,
        [Parameter(Mandatory)][object]$Current
    )

    foreach ($property in @('schemaVersion', 'ruleId', 'mode', 'testRunner', 'tool', 'targetProject', 'testProject')) {
        if ([string](Get-PropertyValue $Prior $property '') -cne [string](Get-PropertyValue $Current $property '')) {
            throw "TEST-GOV-BASE-001 mutation baseline identity changed: $property"
        }
    }
    foreach ($pattern in @(Get-Collection $Prior 'mutate')) {
        if (@(Get-Collection $Current 'mutate') -cnotcontains [string]$pattern) {
            throw "TEST-GOV-BASE-001 mutation scope was removed: $pattern"
        }
    }
    foreach ($test in @(Get-Collection $Prior 'requiredSemanticTests')) {
        if (@(Get-Collection $Current 'requiredSemanticTests') -cnotcontains [string]$test) {
            throw "TEST-GOV-BASE-001 mutation semantic test was removed: $test"
        }
    }
    # The report-only runner separately reconciles the candidate report with
    # the current baseline. Source edits legitimately change mutant identities
    # and absolute status counts, so history only ratchets the quality score and
    # fixed semantic scope instead of freezing those incidental counts.
    Assert-RateNotLower ([double]$Prior.mutationScore) ([double]$Current.mutationScore) 'mutation score'
}

function Get-EvidenceKey {
    param([Parameter(Mandatory)][object]$Evidence)
    return "$([string]$Evidence.path)|$([string]$Evidence.pattern)"
}

function Assert-EvidenceNotExpanded {
    param(
        [Parameter(Mandatory)][object[]]$Prior,
        [Parameter(Mandatory)][object[]]$Current,
        [Parameter(Mandatory)][string]$Label
    )

    $priorByKey = @{}
    foreach ($evidence in $Prior) { $priorByKey[(Get-EvidenceKey $evidence)] = $evidence }
    $currentByKey = @{}
    foreach ($evidence in $Current) {
        $key = Get-EvidenceKey $evidence
        $currentByKey[$key] = $evidence
        if (-not $priorByKey.ContainsKey($key)) {
            throw "TEST-GOV-BASE-001 $Label added consumer evidence '$key'; compatibility consumers may not grow."
        }
        Assert-CountNotHigher ([int]$priorByKey[$key].expectedOccurrences) ([int]$evidence.expectedOccurrences) "$Label evidence $key"
    }

    foreach ($key in $priorByKey.Keys) {
        if ($currentByKey.ContainsKey($key)) { continue }
        $evidence = $priorByKey[$key]
        $path = Join-Path $RepositoryRoot ([string]$evidence.path)
        if (Test-Path $path -PathType Leaf) {
            $remaining = [regex]::Matches((Get-Content $path -Raw), [string]$evidence.pattern).Count
            if ($remaining -gt 0) {
                throw "TEST-GOV-BASE-001 $Label removed evidence '$key' while $remaining matching caller(s) remain."
            }
        }
    }
}

function Get-SymbolKey {
    param([Parameter(Mandatory)][object]$Entry)
    $kind = [string](Get-PropertyValue $Entry 'kind' 'type')
    $declarationPattern = [string](Get-PropertyValue $Entry 'declarationPattern' '')
    return "$(([string]$Entry.token).ToLowerInvariant())|$($kind.ToLowerInvariant())|$([string]$Entry.symbol)|$([string]$Entry.declarationPath)|$declarationPattern"
}

function Assert-DeadlineUnchanged {
    param(
        [Parameter(Mandatory)][object]$Prior,
        [Parameter(Mandatory)][object]$Current,
        [Parameter(Mandatory)][string]$Label
    )
    $oldDeadline = [string](Get-PropertyValue $Prior 'latestRemovalBatch' '')
    if ([string]::IsNullOrWhiteSpace($oldDeadline)) { return }
    $newDeadline = [string](Get-PropertyValue $Current 'latestRemovalBatch' '')
    if ($newDeadline -cne $oldDeadline) {
        throw "TEST-GOV-BASE-001 $Label changed deletion deadline '$oldDeadline' -> '$newDeadline'; deadline relaxation is forbidden."
    }
}

function Assert-CompatibilityBaseline {
    param(
        [Parameter(Mandatory)][object]$Prior,
        [Parameter(Mandatory)][object]$Current
    )

    if ($Prior.schemaVersion -ne $Current.schemaVersion -or $Prior.ruleId -cne $Current.ruleId) {
        throw 'TEST-GOV-BASE-001 compatibility schema or rule pin changed.'
    }
    $currentTokens = @((Get-Collection $Current 'scanTokens') | ForEach-Object { ([string]$_).ToLowerInvariant() })
    foreach ($token in @(Get-Collection $Prior 'scanTokens')) {
        if ($currentTokens -cnotcontains ([string]$token).ToLowerInvariant()) {
            throw "TEST-GOV-BASE-001 compatibility scan token was removed: $token"
        }
    }

    $priorDispositions = @{}
    foreach ($entry in @(Get-Collection $Prior 'candidateDispositions')) { $priorDispositions[[string]$entry.id] = $entry }
    foreach ($entry in @(Get-Collection $Current 'candidateDispositions')) {
        $id = [string]$entry.id
        if (-not $priorDispositions.ContainsKey($id)) {
            if ([string]$entry.status -cne 'OrdinaryAbstraction') {
                throw "TEST-GOV-BASE-001 compatibility baseline introduced a new non-ordinary candidate disposition: $id"
            }
            # The required lane separately runs the current-tree TEST-COMPAT-001
            # gate to prove the declaration and its executable call evidence.
            # Historical monotonicity must not turn a
            # normal Adapter/Wrapper name into a permanently frozen compatibility
            # surface merely because it was not present at the PR base.
            continue
        }
        $old = $priorDispositions[$id]
        if ([string]$old.status -ceq 'MigrationWindow') {
            if ([string]$entry.status -cne 'MigrationWindow') {
                throw "TEST-GOV-BASE-001 candidate '$id' weakened from MigrationWindow to '$($entry.status)'."
            }
            Assert-CountNotHigher ([int](Get-PropertyValue $old 'candidateCount' 0)) ([int](Get-PropertyValue $entry 'candidateCount' 0)) "candidate $id file count"
            Assert-CountNotHigher ([int](Get-PropertyValue $old 'occurrenceCount' 0)) ([int](Get-PropertyValue $entry 'occurrenceCount' 0)) "candidate $id occurrence count"
            Assert-EvidenceNotExpanded (Get-Collection $old 'callEvidence') (Get-Collection $entry 'callEvidence') "candidate $id"
            Assert-DeadlineUnchanged $old $entry "candidate $id"
            continue
        }
        if ([string]$entry.status -cne 'OrdinaryAbstraction') {
            throw "TEST-GOV-BASE-001 ordinary candidate '$id' was reclassified as '$($entry.status)'."
        }
    }

    $priorWindows = @{}
    foreach ($entry in @(Get-Collection $Prior 'migrationWindows')) { $priorWindows[[string]$entry.id] = $entry }
    foreach ($entry in @(Get-Collection $Current 'migrationWindows')) {
        $id = [string]$entry.id
        if (-not $priorWindows.ContainsKey($id)) {
            throw "TEST-GOV-BASE-001 compatibility baseline self-authorized new migration window: $id"
        }
        $old = $priorWindows[$id]
        $priorConsumerCount = @(Get-Collection $old 'currentConsumers').Count
        $currentConsumerCount = @(Get-Collection $entry 'currentConsumers').Count
        Assert-CountNotHigher $priorConsumerCount $currentConsumerCount "migration $id consumer count"
        Assert-EvidenceNotExpanded (Get-Collection $old 'callEvidence') (Get-Collection $entry 'callEvidence') "migration $id"
        Assert-DeadlineUnchanged $old $entry "migration $id"
    }

    $priorSymbols = @{}
    foreach ($entry in @(Get-Collection $Prior 'symbolCandidates')) { $priorSymbols[(Get-SymbolKey $entry)] = $entry }
    foreach ($entry in @(Get-Collection $Current 'symbolCandidates')) {
        $key = Get-SymbolKey $entry
        if (-not $priorSymbols.ContainsKey($key)) {
            if ([string]$entry.status -cne 'OrdinaryAbstraction') {
                throw "TEST-GOV-BASE-001 compatibility baseline introduced a new non-ordinary symbol candidate: $key"
            }
            continue
        }
        $old = $priorSymbols[$key]
        if ([string]$old.status -ceq 'MigrationWindow') {
            if ([string]$entry.status -cne 'MigrationWindow') {
                throw "TEST-GOV-BASE-001 symbol '$key' weakened from MigrationWindow to '$($entry.status)'."
            }
            Assert-EvidenceNotExpanded (Get-Collection $old 'callEvidence') (Get-Collection $entry 'callEvidence') "symbol $key"
            Assert-DeadlineUnchanged $old $entry "symbol $key"
            continue
        }
        if ([string]$entry.status -cne 'OrdinaryAbstraction') {
            throw "TEST-GOV-BASE-001 ordinary symbol '$key' was reclassified as '$($entry.status)'."
        }
    }
}

$coveragePath = 'scripts/tests/baselines/edge-coverage-baseline.json'
$duplicationPath = 'scripts/tests/baselines/edge-duplication-baseline.json'
$mutationPath = 'scripts/tests/baselines/edge-mutation-baseline.json'
$compatibilityPath = 'scripts/tests/edge-compatibility-inventory.json'

$coverageAnchor = Get-AnchoredJson $coveragePath
$duplicationAnchor = Get-AnchoredJson $duplicationPath
$mutationAnchor = Get-AnchoredJson $mutationPath
$compatibilityAnchor = Get-AnchoredJson $compatibilityPath

Assert-CoverageBaseline $coverageAnchor.Document (Get-CurrentJson $coveragePath)
Assert-DuplicationBaseline $duplicationAnchor.Document (Get-CurrentJson $duplicationPath)
Assert-MutationBaseline $mutationAnchor.Document (Get-CurrentJson $mutationPath)
Assert-CompatibilityBaseline $compatibilityAnchor.Document (Get-CurrentJson $compatibilityPath)

$global:LASTEXITCODE = 0
Write-Host "Edge governance baseline monotonicity passed: base=$resolvedBaseRef, coverageAnchor=$($coverageAnchor.AnchorRef), duplicationAnchor=$($duplicationAnchor.AnchorRef), mutationAnchor=$($mutationAnchor.AnchorRef), compatibilityAnchor=$($compatibilityAnchor.AnchorRef)."
