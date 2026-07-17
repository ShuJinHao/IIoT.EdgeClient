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

$gatePath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgeRetiredFeatureEvidence.ps1'
$canonicalLedgerPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/edge-regression-ledger.json'
$canonicalDiscoveredPath = Join-Path $RepositoryRoot 'scripts/tests/discovered-test-inventory.json'
$canonicalInventoryPath = Join-Path $RepositoryRoot 'scripts/tests/edge-test-inventory.json'
$canonicalValidatorPath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgeRegressionLedger.ps1'

foreach ($requiredPath in @(
    $gatePath,
    $canonicalLedgerPath,
    $canonicalDiscoveredPath,
    $canonicalInventoryPath,
    $canonicalValidatorPath)) {
    if (-not (Test-Path $requiredPath -PathType Leaf)) {
        throw "EDGE-RETIRED-FEATURE-FIXTURE-001 required file does not exist: $requiredPath"
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    [void](New-Item (Split-Path $Path -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 64) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

function Copy-AllowedActiveInputs {
    param([Parameter(Mandatory)][string]$ActiveInputRoot)

    $validatorTarget = Join-Path $ActiveInputRoot 'scripts/tests/Test-EdgeRegressionLedger.ps1'
    $ledgerTarget = Join-Path $ActiveInputRoot 'scripts/tests/baselines/edge-regression-ledger.json'
    [void](New-Item (Split-Path $validatorTarget -Parent) -ItemType Directory -Force)
    [void](New-Item (Split-Path $ledgerTarget -Parent) -ItemType Directory -Force)
    Copy-Item $canonicalValidatorPath $validatorTarget -Force
    Copy-Item $canonicalLedgerPath $ledgerTarget -Force
}

function Invoke-GateFixture {
    param(
        [Parameter(Mandatory)][string]$Name,
        [scriptblock]$MutateLedger,
        [scriptblock]$MutateDiscovered,
        [scriptblock]$MutateActiveInputs,
        [switch]$ExpectSuccess
    )

    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-retired-feature-$([Guid]::NewGuid().ToString('N'))"
    $inputRoot = Join-Path $fixtureRoot 'inputs'
    $activeInputRoot = Join-Path $fixtureRoot 'active'
    try {
        [void](New-Item $inputRoot -ItemType Directory -Force)
        [void](New-Item $activeInputRoot -ItemType Directory -Force)
        Copy-AllowedActiveInputs -ActiveInputRoot $activeInputRoot

        $ledger = Get-Content $canonicalLedgerPath -Raw | ConvertFrom-Json -Depth 64
        $discovered = Get-Content $canonicalDiscoveredPath -Raw | ConvertFrom-Json -Depth 64
        if ($null -ne $MutateLedger) { & $MutateLedger $ledger }
        if ($null -ne $MutateDiscovered) { & $MutateDiscovered $discovered $ledger }
        if ($null -ne $MutateActiveInputs) { & $MutateActiveInputs $activeInputRoot $ledger }

        $ledgerPath = Join-Path $inputRoot 'ledger.json'
        $discoveredPath = Join-Path $inputRoot 'discovered.json'
        Write-JsonFile -Path $ledgerPath -Value $ledger
        Write-JsonFile -Path $discoveredPath -Value $discovered

        $output = (& pwsh -NoLogo -NoProfile -File $gatePath `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $ledgerPath `
            -DiscoveredInventoryPath $discoveredPath `
            -InventoryPath $canonicalInventoryPath `
            -ActiveInputRoot $activeInputRoot 2>&1 | Out-String).TrimEnd()
        $exitCode = $LASTEXITCODE
        $global:LASTEXITCODE = 0

        if ($ExpectSuccess) {
            if ($exitCode -ne 0) {
                throw "EDGE-RETIRED-FEATURE-FIXTURE-001 '$Name' unexpectedly failed: $output"
            }
        } elseif ($exitCode -eq 0) {
            throw "EDGE-RETIRED-FEATURE-FIXTURE-001 '$Name' unexpectedly passed."
        }
        Write-Host "Retired feature evidence fixture passed: $Name"
    } finally {
        Remove-Item $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-GateFixture -Name 'canonical-evidence' -ExpectSuccess

Invoke-GateFixture -Name 'active-source-return' -MutateActiveInputs {
    param($activeInputRoot, $ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidate = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })[0]
    $path = Join-Path $activeInputRoot 'src/ReturnedFeature.cs'
    [void](New-Item (Split-Path $path -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText($path, "// $($candidate.oldKey)`n", [Text.UTF8Encoding]::new($false))
}

Invoke-GateFixture -Name 'third-governance-file' -MutateActiveInputs {
    param($activeInputRoot, $ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidate = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })[0]
    $path = Join-Path $activeInputRoot 'scripts/tests/RetiredFeatureNote.json'
    [void](New-Item (Split-Path $path -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText($path, "{ `"oldKey`": `"$($candidate.oldKey)`" }`n", [Text.UTF8Encoding]::new($false))
}

Invoke-GateFixture -Name 'expected-count-40' -MutateLedger {
    param($ledger)
    @($ledger.retirementEvidence)[0].expectedDeclarationCount = 40
}

Invoke-GateFixture -Name 'expected-count-42' -MutateLedger {
    param($ledger)
    @($ledger.retirementEvidence)[0].expectedDeclarationCount = 42
}

Invoke-GateFixture -Name 'duplicate-old-key' -MutateLedger {
    param($ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidates = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })
    $candidates[1].oldKey = [string]$candidates[0].oldKey
}

Invoke-GateFixture -Name 'wrong-disposition' -MutateLedger {
    param($ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidate = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })[0]
    $candidate.disposition = "$($evidence.disposition)-wrong"
}

Invoke-GateFixture -Name 'wrong-decision' -MutateLedger {
    param($ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidate = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })[0]
    $candidate.replacement = 'decision:EDGE-UNRELATED-RETIRE-001'
}

Invoke-GateFixture -Name 'old-declaration-return' -MutateDiscovered {
    param($discovered, $ledger)
    $evidence = @($ledger.retirementEvidence)[0]
    $candidate = @($ledger.entries | Where-Object {
        [string]$_.disposition -ceq [string]$evidence.disposition -and
        [string]$_.replacement -ceq [string]$evidence.replacement
    })[0]
    $discovered.cases = @($discovered.cases) + @([pscustomobject][ordered]@{
        identity = [string]$candidate.oldKey
        regressionId = ''
    })
}

Write-Host 'Edge retired feature evidence fixtures passed: success=1, negative=8.'
