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
$gatePath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgeCompatibilityInventory.ps1'
if (-not (Test-Path $gatePath -PathType Leaf)) {
    throw "TEST-COMPAT-FIXTURE-001 compatibility gate does not exist: $gatePath"
}
$scanTokens = @('alias', 'adapter', 'wrapper', 'compat', 'legacy', 'shadow', 'obsolete', 'fallback', '双写', '影子')

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Value)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function New-Disposition {
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][int]$Occurrences,
        [Parameter(Mandatory)][string]$ConsumerPattern,
        [Parameter(Mandatory)][int]$ExpectedConsumers
    )
    return [pscustomobject][ordered]@{
        id = "FIXTURE-$($Token.ToUpperInvariant())"
        token = $Token
        pathPattern = '^src/Feature\.cs$'
        status = 'OrdinaryAbstraction'
        rationale = 'Fixture disposition with explicit consumer evidence.'
        candidateCount = 1
        occurrenceCount = $Occurrences
        manifestSha256 = Get-Sha256 "$Token|src/Feature.cs|$Occurrences"
        callEvidence = @([pscustomobject][ordered]@{
            path = 'src/Feature.cs'
            pattern = $ConsumerPattern
            expectedOccurrences = $ExpectedConsumers
        })
    }
}

function Invoke-ExpectedFailure {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][object[]]$Dispositions,
        [object[]]$SymbolCandidates = @(),
        [Parameter(Mandatory)][string]$ExpectedMessage
    )
    $root = Join-Path ([IO.Path]::GetTempPath()) "edge-compat-fixture-$([Guid]::NewGuid().ToString('N'))"
    try {
        [void](New-Item (Join-Path $root 'src') -ItemType Directory -Force)
        [IO.File]::WriteAllText(
            (Join-Path $root 'src/Feature.cs'),
            $Source,
            [Text.UTF8Encoding]::new($false))
        $inventory = [pscustomobject][ordered]@{
            schemaVersion = 3
            ruleId = 'TEST-COMPAT-001'
            scanTokens = $scanTokens
            symbolCandidates = $SymbolCandidates
            candidateDispositions = $Dispositions
            migrationWindows = @()
        }
        $inventoryPath = Join-Path $root 'inventory.json'
        [IO.File]::WriteAllText(
            $inventoryPath,
            (($inventory | ConvertTo-Json -Depth 20) + "`n"),
            [Text.UTF8Encoding]::new($false))

        $output = & pwsh -NoProfile -File $gatePath `
            -RepositoryRoot $root `
            -InventoryPath $inventoryPath `
            -SourceRoot src 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $output -notmatch $ExpectedMessage) {
            throw "TEST-COMPAT-FIXTURE-001 '$Name' did not fail with '$ExpectedMessage'. Output: $output"
        }
        Write-Host "Compatibility fixture passed: $Name"
    } finally {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-ExpectedSuccess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][object[]]$Dispositions,
        [Parameter(Mandatory)][object[]]$SymbolCandidates
    )
    $root = Join-Path ([IO.Path]::GetTempPath()) "edge-compat-fixture-$([Guid]::NewGuid().ToString('N'))"
    try {
        [void](New-Item (Join-Path $root 'src') -ItemType Directory -Force)
        [IO.File]::WriteAllText(
            (Join-Path $root 'src/Feature.cs'),
            $Source,
            [Text.UTF8Encoding]::new($false))
        $inventory = [pscustomobject][ordered]@{
            schemaVersion = 3
            ruleId = 'TEST-COMPAT-001'
            scanTokens = $scanTokens
            symbolCandidates = $SymbolCandidates
            candidateDispositions = $Dispositions
            migrationWindows = @()
        }
        $inventoryPath = Join-Path $root 'inventory.json'
        [IO.File]::WriteAllText(
            $inventoryPath,
            (($inventory | ConvertTo-Json -Depth 20) + "`n"),
            [Text.UTF8Encoding]::new($false))

        $output = & pwsh -NoProfile -File $gatePath `
            -RepositoryRoot $root `
            -InventoryPath $inventoryPath `
            -SourceRoot src 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "TEST-COMPAT-FIXTURE-001 '$Name' unexpectedly failed. Output: $output"
        }
        Write-Host "Compatibility fixture passed: $Name"
    } finally {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-SymbolCandidate {
    param(
        [Parameter(Mandatory)][string]$Symbol,
        [Parameter(Mandatory)][string]$ConsumerPattern,
        [Parameter(Mandatory)][int]$ExpectedConsumers,
        [string]$Token = 'fallback',
        [ValidateSet(
            'type', 'delegate', 'method', 'property', 'field', 'const', 'event', 'alias',
            'enum-member', 'record-property', 'primary-constructor-parameter')][string]$Kind = 'type'
    )
    return [pscustomobject][ordered]@{
        token = $Token
        kind = $Kind
        symbol = $Symbol
        declarationPath = 'src/Feature.cs'
        status = 'OrdinaryAbstraction'
        rationale = 'Fixture exact per-symbol registration.'
        callEvidence = @([pscustomobject][ordered]@{
            path = 'src/Feature.cs'
            pattern = $ConsumerPattern
            expectedOccurrences = $ExpectedConsumers
        })
    }
}

$unregisteredSource = @'
public sealed class ExampleAdapter { }
public sealed class FallbackValue { }
public sealed class Consumer { public FallbackValue UseFallbackValue() => new(); }
'@
Invoke-ExpectedFailure `
    -Name 'unregistered-candidate' `
    -Source $unregisteredSource `
    -Dispositions @((New-Disposition -Token fallback -Occurrences 3 -ConsumerPattern 'UseFallbackValue' -ExpectedConsumers 1)) `
    -ExpectedMessage 'unclassified=1'

$zeroConsumerSource = @'
public sealed class ExampleAdapter { }
'@
Invoke-ExpectedFailure `
    -Name 'zero-consumer' `
    -Source $zeroConsumerSource `
    -Dispositions @((New-Disposition -Token adapter -Occurrences 1 -ConsumerPattern 'new\s+ExampleAdapter' -ExpectedConsumers 1)) `
    -ExpectedMessage 'expected=1 actual=0'

$consumerGrowthSource = @'
public sealed class ExampleAdapter { }
public sealed class Consumer
{
    public object First() => new ExampleAdapter();
    public object Second() => new ExampleAdapter();
}
'@
Invoke-ExpectedFailure `
    -Name 'new-consumer-growth' `
    -Source $consumerGrowthSource `
    -Dispositions @((New-Disposition -Token adapter -Occurrences 3 -ConsumerPattern 'new\s+ExampleAdapter' -ExpectedConsumers 1)) `
    -ExpectedMessage 'expected=1 actual=2'

$broadDispositionGapSource = @'
public sealed class LiveFallback { }
public sealed class DeadFallback { }
public sealed class Consumer { public LiveFallback Use() => new LiveFallback(); }
'@
Invoke-ExpectedFailure `
    -Name 'broad-disposition-symbol-gap' `
    -Source $broadDispositionGapSource `
    -Dispositions @((New-Disposition -Token fallback -Occurrences 4 -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -SymbolCandidates @((New-SymbolCandidate -Symbol LiveFallback -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -ExpectedMessage 'unregisteredSymbols=1'

$broadDispositionMemberGapSource = @'
public sealed class LiveFallback { }
public sealed class Consumer
{
    public LiveFallback Use() => new LiveFallback();
    private static string DeadFallback() => "--";
}
'@
Invoke-ExpectedFailure `
    -Name 'broad-disposition-member-gap' `
    -Source $broadDispositionMemberGapSource `
    -Dispositions @((New-Disposition -Token fallback -Occurrences 4 -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -SymbolCandidates @((New-SymbolCandidate -Symbol LiveFallback -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -ExpectedMessage 'unregisteredSymbols=1'

$broadDispositionDataMemberGapSource = @'
public sealed class LiveFallback { }
public sealed class Consumer
{
    private readonly LiveFallback _deadFallback;
    public string TitleFallback { get; } = "--";
    public LiveFallback Use() => new LiveFallback();
}
public sealed record Snapshot(string RecordFallback);
'@
Invoke-ExpectedFailure `
    -Name 'broad-disposition-data-member-gap' `
    -Source $broadDispositionDataMemberGapSource `
    -Dispositions @((New-Disposition -Token fallback -Occurrences 7 -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -SymbolCandidates @((New-SymbolCandidate -Symbol LiveFallback -ConsumerPattern 'new\s+LiveFallback' -ExpectedConsumers 1)) `
    -ExpectedMessage 'unregisteredSymbols=3'

$commentStringDeclarationOnlySource = @'
public sealed class ExampleAdapter { }
public sealed class Consumer
{
    private const string Description = "ExampleAdapter";
    // ExampleAdapter is intentionally not a runtime consumer.
}
'@
Invoke-ExpectedFailure `
    -Name 'comment-string-declaration-only-is-not-evidence' `
    -Source $commentStringDeclarationOnlySource `
    -Dispositions @((New-Disposition -Token adapter -Occurrences 3 -ConsumerPattern '\bExampleAdapter\b' -ExpectedConsumers 1)) `
    -SymbolCandidates @((New-SymbolCandidate -Token adapter -Symbol ExampleAdapter -ConsumerPattern '\bExampleAdapter\b' -ExpectedConsumers 1)) `
    -ExpectedMessage 'expected=1 actual=0'

$realReferenceSource = @'
public sealed class ExampleAdapter { }
public sealed class Consumer { public ExampleAdapter Create() => new ExampleAdapter(); }
'@
Invoke-ExpectedSuccess `
    -Name 'real-reference-is-evidence' `
    -Source $realReferenceSource `
    -Dispositions @((New-Disposition -Token adapter -Occurrences 3 -ConsumerPattern 'new\s+ExampleAdapter' -ExpectedConsumers 1)) `
    -SymbolCandidates @((New-SymbolCandidate -Token adapter -Symbol ExampleAdapter -ConsumerPattern 'new\s+ExampleAdapter' -ExpectedConsumers 1))

$extendedDeclarationKindsSource = @'
using FallbackAlias = System.String;
public delegate void FallbackDelegate();
public enum State { FallbackMode }
public sealed class Consumer(string fallbackValue)
{
    public FallbackAlias Value { get; } = fallbackValue;
    public FallbackDelegate Handler { get; } = static () => { };
    public State Current { get; } = State.FallbackMode;
}
'@
$extendedCandidates = @(
    (New-SymbolCandidate -Symbol FallbackAlias -Kind alias -ConsumerPattern '\bFallbackAlias\b' -ExpectedConsumers 1),
    (New-SymbolCandidate -Token alias -Symbol FallbackAlias -Kind alias -ConsumerPattern '\bFallbackAlias\b' -ExpectedConsumers 1),
    (New-SymbolCandidate -Symbol FallbackDelegate -Kind delegate -ConsumerPattern '\bFallbackDelegate\b' -ExpectedConsumers 1),
    (New-SymbolCandidate -Symbol FallbackMode -Kind enum-member -ConsumerPattern 'State\.FallbackMode' -ExpectedConsumers 1),
    (New-SymbolCandidate -Symbol fallbackValue -Kind primary-constructor-parameter -ConsumerPattern '=\s*fallbackValue' -ExpectedConsumers 1))
Invoke-ExpectedSuccess `
    -Name 'extended-declaration-kinds-have-real-references' `
    -Source $extendedDeclarationKindsSource `
    -Dispositions @(
        (New-Disposition -Token fallback -Occurrences 8 -ConsumerPattern '\bFallbackAlias\b' -ExpectedConsumers 1),
        (New-Disposition -Token alias -Occurrences 2 -ConsumerPattern '\bFallbackAlias\b' -ExpectedConsumers 1)) `
    -SymbolCandidates $extendedCandidates

Write-Host 'Edge compatibility inventory fixtures passed: invalid=7, valid=2, declarationKinds=4.'
