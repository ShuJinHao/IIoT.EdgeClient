[CmdletBinding()]
param(
    [string]$ValidatorPath = (Join-Path $PSScriptRoot 'ValidateEdgeBaselineMigration.v1.ps1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ValidatorPath = (Resolve-Path $ValidatorPath).Path
$TrustedWrapperPath = (Resolve-Path (Join-Path $PSScriptRoot 'InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1')).Path
$TrustedWrapperRelativePath = 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1'
$SchemaPath = (Resolve-Path (Join-Path $PSScriptRoot 'edge-baseline-migration-receipt.schema.json')).Path
$SelfPath = $MyInvocation.MyCommand.Path
$Now = [DateTimeOffset]::UtcNow
$IssuedAtUtc = $Now.AddMinutes(-5).ToString('yyyy-MM-ddTHH:mm:ssZ')
$ExpiresAtUtc = $Now.AddDays(6).ToString('yyyy-MM-ddTHH:mm:ssZ')
$MigrationId = 'EDGE-BASELINE-MIG-SELFTEST-001'
$ReceiptRelativePath = "scripts/tests/baselines/migrations/pending/$MigrationId.json"
$ConsumedRelativePath = "scripts/tests/baselines/migrations/consumed/$MigrationId.json"
$CancelledRelativePath = "scripts/tests/baselines/migrations/cancelled/$MigrationId.json"
$script:Passed = 0
$script:Failed = 0
$script:TempRoots = [Collections.Generic.List[string]]::new()

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $directory = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertFrom-TestJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-TestJsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-TestJsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            return $Element.GetDecimal()
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default { throw "Unsupported JSON value kind '$($Element.ValueKind)' in self-test." }
    }
}

function ConvertFrom-TestJson {
    param([Parameter(Mandatory)][string]$Json)

    $document = [Text.Json.JsonDocument]::Parse($Json)
    try { return ConvertFrom-TestJsonElement -Element $document.RootElement }
    finally { $document.Dispose() }
}

function Get-SmokeWorkflowContent {
    return @'
name: edge-smoke-build

on:
  pull_request:
    branches:
      - main
  push:
    branches:
      - main

env:
  DOTNET_NOLOGO: true

jobs:
  smoke-build:
    runs-on: windows-latest
    timeout-minutes: 25
    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          ref: ${{ github.event.pull_request.head.sha || github.sha }}
      - name: Validate trusted baseline migration
        shell: pwsh
        run: |
          # EDGE-BASELINE-MIG-TRUSTED-EXECUTOR-V1
          $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
          $candidate = '${{ github.event.pull_request.head.sha || github.sha }}'
          $trustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1'
          $entry = (git ls-tree $trustedBase -- $trustedWrapperPath | Out-String).Trim()
          $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($trustedWrapperPath) + '$'
          if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
            throw 'Trusted base does not contain the reviewed migration wrapper.'
          }
          $temporaryWrapper = Join-Path $env:RUNNER_TEMP 'edge-baseline-migration-wrapper.ps1'
          try {
            & git cat-file blob $Matches.ObjectId > $temporaryWrapper
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract the trusted migration wrapper.' }
            $extractedObjectId = (git hash-object --no-filters -- $temporaryWrapper | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $Matches.ObjectId) {
              throw 'Extracted migration wrapper differs from the trusted Git blob.'
            }
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryWrapper `
              -RepositoryRoot . `
              -TrustedBaseRevision $trustedBase `
              -CandidateRevision $candidate `
              -AnchorRelationship BaseAncestorOfHead
            if ($LASTEXITCODE -ne 0) { throw "Trusted migration validation failed with exit code $LASTEXITCODE." }
          }
          finally {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
          }
      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          global-json-file: global.json
'@
}

function Get-PackWorkflowContent {
    return @'
name: edge-runtime-package

on:
  push:
    tags:
      - 'edge-v*'
      - 'v*'
  workflow_dispatch:
    inputs:
      version:
        description: Edge client release version
        required: true
        default: 1.0.0
      release_notes:
        description: Edge client release notes shown in Cloud and Launcher
        required: true

env:
  DOTNET_NOLOGO: true
  EDGE_RELEASE_VERSION: ${{ github.event.inputs.version || '1.0.0' }}
  EDGE_RELEASE_CHANNEL: stable
  EDGE_PACK_ID: IIoT.EdgeClient

jobs:
  validate-runtime:
    runs-on: windows-latest
    timeout-minutes: 25
    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          submodules: recursive
          fetch-depth: 0
      - name: Validate trusted baseline migration
        shell: pwsh
        run: |
          # EDGE-BASELINE-MIG-TRUSTED-EXECUTOR-V1
          git fetch origin main --no-tags
          $trustedMain = (git rev-parse origin/main | Out-String).Trim()
          if ($env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and $env:GITHUB_REF -ne 'refs/heads/main') {
            throw 'Manual Edge release validation must run from refs/heads/main.'
          }
          $trustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1'
          $entry = (git ls-tree $trustedMain -- $trustedWrapperPath | Out-String).Trim()
          $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($trustedWrapperPath) + '$'
          if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
            throw 'Trusted main does not contain the reviewed migration wrapper.'
          }
          $temporaryWrapper = Join-Path $env:RUNNER_TEMP 'edge-baseline-migration-wrapper.ps1'
          try {
            & git cat-file blob $Matches.ObjectId > $temporaryWrapper
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract the trusted migration wrapper.' }
            $extractedObjectId = (git hash-object --no-filters -- $temporaryWrapper | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $Matches.ObjectId) {
              throw 'Extracted migration wrapper differs from the trusted Git blob.'
            }
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryWrapper `
              -RepositoryRoot . `
              -TrustedBaseRevision $trustedMain `
              -CandidateRevision '${{ github.sha }}' `
              -AnchorRelationship HeadAncestorOfBase
            if ($LASTEXITCODE -ne 0) { throw "Trusted migration validation failed with exit code $LASTEXITCODE." }
          }
          finally {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
          }
      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          global-json-file: global.json
'@
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$Capture
    )

    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in ${Root}: $($output -join [Environment]::NewLine)"
    }
    if ($Capture) { return ($output | Out-String).Trim() }
}

function Export-TestGitBlob {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string]$Destination
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-C', $Root, 'cat-file', 'blob', $ObjectId)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Could not start trusted wrapper extraction.' }
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $stream = [IO.File]::Open(
            $Destination,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try { $process.StandardOutput.BaseStream.CopyTo($stream) }
        finally { $stream.Dispose() }
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) {
            throw "Trusted wrapper extraction failed: $errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Commit-All {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Message
    )

    Invoke-Git -Root $Root -Arguments @('add', '--all')
    Invoke-Git -Root $Root -Arguments @('commit', '--quiet', '-m', $Message)
    return Invoke-Git -Root $Root -Arguments @('rev-parse', 'HEAD') -Capture
}

function New-BaseFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "edge-migration-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $script:TempRoots.Add($root)
    Invoke-Git -Root $root -Arguments @('init', '--quiet', '--initial-branch=main', '--object-format=sha1')
    Invoke-Git -Root $root -Arguments @('config', 'user.name', 'Edge Migration Self Test')
    Invoke-Git -Root $root -Arguments @('config', 'user.email', 'edge-migration@example.invalid')
    Invoke-Git -Root $root -Arguments @('config', 'commit.gpgsign', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.autocrlf', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.safecrlf', 'true')
    $emptyHooks = Join-Path $root '.empty-git-hooks'
    [IO.Directory]::CreateDirectory($emptyHooks) | Out-Null
    Invoke-Git -Root $root -Arguments @('config', 'core.hooksPath', $emptyHooks)

    Write-Utf8File -Path (Join-Path $root 'IIoT.EdgeClient.slnx') -Content @'
<Solution>
  <Project Path="src/Tests/Sample.Tests/Sample.Tests.csproj" />
</Solution>
'@
    Write-Utf8File -Path (Join-Path $root 'src/Tests/Sample.Tests/Sample.Tests.csproj') -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-Utf8File -Path (Join-Path $root 'src/Tests/Sample.Tests/SampleTests.cs') -Content "public sealed class SampleTests { }`n"
    Write-Utf8File -Path (Join-Path $root '.github/workflows/edge-smoke-build.yml') -Content "name: old`n"
    Write-Utf8File -Path (Join-Path $root '.github/workflows/edge-pack-modules.yml') -Content "name: old-pack`n"
    Write-Utf8File -Path (Join-Path $root 'src/App/Program.cs') -Content "internal static class Program { }`n"
    Write-Utf8File -Path (Join-Path $root 'scripts/tests/baselines/edge-test-governance.baseline.json') -Content @'
{
  "schemaVersion": "1.0",
  "ruleId": "EDGE-TEST-GOV-001",
  "projects": [
    {
      "projectPath": "src/Tests/Sample.Tests/Sample.Tests.csproj",
      "baselineDeclarations": 1,
      "baselineExecutionTemplates": 1,
      "baselineProjectedCases": 1,
      "baselineRunnerCases": 1
    }
  ]
}
'@
    $validatorTarget = Join-Path $root 'scripts/tests/baselines/migrations/ValidateEdgeBaselineMigration.v1.ps1'
    $wrapperTarget = Join-Path $root 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1'
    $selfTarget = Join-Path $root 'scripts/tests/baselines/migrations/TestEdgeBaselineMigrationValidator.v1.ps1'
    $schemaTarget = Join-Path $root 'scripts/tests/baselines/migrations/edge-baseline-migration-receipt.schema.json'
    [IO.Directory]::CreateDirectory((Split-Path $validatorTarget -Parent)) | Out-Null
    [IO.File]::Copy($ValidatorPath, $validatorTarget, $true)
    [IO.File]::Copy($TrustedWrapperPath, $wrapperTarget, $true)
    [IO.File]::Copy($SelfPath, $selfTarget, $true)
    [IO.File]::Copy($SchemaPath, $schemaTarget, $true)

    $base = Commit-All -Root $root -Message 'base'
    return [pscustomobject]@{ Root = $root; Base = $base }
}

function New-TemplateCandidate {
    param([Parameter(Mandatory)][object]$Fixture)

    Write-Utf8File -Path (Join-Path $Fixture.Root '.github/workflows/edge-smoke-build.yml') -Content "$(Get-SmokeWorkflowContent)`n"
    Write-Utf8File -Path (Join-Path $Fixture.Root '.github/workflows/edge-pack-modules.yml') -Content "$(Get-PackWorkflowContent)`n"
    Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Program.cs') -Content "internal static class Program { internal const int Version = 2; }`n"
    $template = Commit-All -Root $Fixture.Root -Message 'template candidate'
    $Fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $Fixture
}

function New-ReceiptJson {
    param([Parameter(Mandatory)][object]$Fixture)

    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $ValidatorPath,
        '-Mode', 'Describe',
        '-RepositoryRoot', $Fixture.Root,
        '-TrustedBaseRevision', $Fixture.Base,
        '-CandidateRevision', $Fixture.Template,
        '-MigrationId', $MigrationId,
        '-RuleIdsCsv', 'EDGE-ARCH-001',
        '-Owner', 'Edge.Architecture',
        '-ApprovedBy', 'ShuJinHao',
        '-Reason', 'Self-test receipt for one exact workflow migration.',
        '-IssuedAtUtc', $IssuedAtUtc,
        '-ExpiresAtUtc', $ExpiresAtUtc)
    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Describe failed: $($output -join [Environment]::NewLine)"
    }
    return ($output | Out-String).Trim()
}

function Invoke-DescribeResult {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$RuleIdsCsv
    )

    return Invoke-PowerShellResult -Arguments @(
        '-File', $ValidatorPath,
        '-Mode', 'Describe',
        '-RepositoryRoot', $Fixture.Root,
        '-TrustedBaseRevision', $Fixture.Base,
        '-CandidateRevision', $Fixture.Template,
        '-MigrationId', $MigrationId,
        '-RuleIdsCsv', $RuleIdsCsv,
        '-Owner', 'Edge.Architecture',
        '-ApprovedBy', 'ShuJinHao',
        '-Reason', 'Self-test receipt for one exact workflow migration.',
        '-IssuedAtUtc', $IssuedAtUtc,
        '-ExpiresAtUtc', $ExpiresAtUtc)
}

function New-AuthorizationFixture {
    param(
        [scriptblock]$MutateReceipt,
        [scriptblock]$MutateTemplate,
        [switch]$AddAuthorizationNoise,
        [switch]$AddSecondPending
    )

    $fixture = New-TemplateCandidate -Fixture (New-BaseFixture)
    if ($null -ne $MutateTemplate) {
        Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Template)
        & $MutateTemplate $fixture.Root
        Invoke-Git -Root $fixture.Root -Arguments @('add', '--all')
        Invoke-Git -Root $fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $fixture.Template = Invoke-Git -Root $fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    }
    $receiptJson = New-ReceiptJson -Fixture $fixture
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    if ($null -ne $MutateReceipt) {
        $receiptJson = & $MutateReceipt $receiptJson
    }
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$receiptJson`n"
    if ($AddAuthorizationNoise) {
        Write-Utf8File -Path (Join-Path $fixture.Root 'docs/noise.md') -Content "authorization noise`n"
    }
    if ($AddSecondPending) {
        $second = $receiptJson.Replace($MigrationId, 'EDGE-BASELINE-MIG-SELFTEST-002')
        Write-Utf8File -Path (Join-Path $fixture.Root 'scripts/tests/baselines/migrations/pending/EDGE-BASELINE-MIG-SELFTEST-002.json') -Content "$second`n"
    }
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize migration'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function New-TrustTemplateFixture {
    $fixture = New-BaseFixture
    Write-Utf8File -Path (Join-Path $fixture.Root '.github/workflows/edge-smoke-build.yml') -Content "$(Get-SmokeWorkflowContent)`n"
    Write-Utf8File -Path (Join-Path $fixture.Root '.github/workflows/edge-pack-modules.yml') -Content "$(Get-PackWorkflowContent)`n"
    $fixture.Base = Commit-All -Root $fixture.Root -Message 'integrated trusted workflow base'
    [IO.File]::AppendAllText(
        (Join-Path $fixture.Root 'scripts/tests/baselines/migrations/ValidateEdgeBaselineMigration.v1.ps1'),
        "# reviewed trust upgrade candidate`n",
        [Text.UTF8Encoding]::new($false))
    $template = Commit-All -Root $fixture.Root -Message 'trust upgrade template'
    $fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $fixture
}

function New-TrustUpgradeFixture {
    $fixture = New-TrustTemplateFixture
    $result = Invoke-DescribeResult `
        -Fixture $fixture `
        -RuleIdsCsv 'EDGE-BASELINE-TRUST-UPGRADE-001'
    if ($result.ExitCode -ne 0) {
        throw "Trust upgrade Describe failed: $($result.Output)"
    }
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$($result.Output)`n"
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize trust upgrade'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function Complete-Cancellation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$AlterCancelled,
        [switch]$AddExtraPath,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    $pending = Join-Path $Fixture.Root $ReceiptRelativePath
    $cancelled = Join-Path $Fixture.Root $CancelledRelativePath
    [IO.Directory]::CreateDirectory((Split-Path $cancelled -Parent)) | Out-Null
    [IO.File]::Move($pending, $cancelled)
    if ($AlterCancelled) {
        [IO.File]::AppendAllText($cancelled, " `n", [Text.UTF8Encoding]::new($false))
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'docs/cancellation-noise.md') -Content "noise`n"
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $CancelledRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '-m', 'cancel migration')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Complete-Candidate {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$SkipMove,
        [switch]$AlterConsumed,
        [switch]$AddExtraPath,
        [switch]$RemoveExpectedPath,
        [switch]$ModifyCandidateValidator
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    Invoke-Git -Root $Fixture.Root -Arguments @('cherry-pick', '--quiet', $Fixture.Template)
    if ($RemoveExpectedPath) {
        Invoke-Git -Root $Fixture.Root -Arguments @('checkout', "$($Fixture.Authorization)^", '--', '.github/workflows/edge-smoke-build.yml')
    }
    if (-not $SkipMove) {
        $pending = Join-Path $Fixture.Root $ReceiptRelativePath
        $consumed = Join-Path $Fixture.Root $ConsumedRelativePath
        [IO.Directory]::CreateDirectory((Split-Path $consumed -Parent)) | Out-Null
        [IO.File]::Move($pending, $consumed)
        if ($AlterConsumed) {
            [IO.File]::AppendAllText($consumed, " `n", [Text.UTF8Encoding]::new($false))
        }
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Extra.cs') -Content "internal sealed class Extra { }`n"
    }
    if ($ModifyCandidateValidator) {
        [IO.File]::AppendAllText(
            (Join-Path $Fixture.Root 'scripts/tests/baselines/migrations/ValidateEdgeBaselineMigration.v1.ps1'),
            "# candidate bypass`n",
            [Text.UTF8Encoding]::new($false))
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Invoke-Validation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Candidate,
        [string]$Relationship = 'BaseAncestorOfHead'
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Candidate)
    $executorPath = $TrustedWrapperPath
    $temporaryWrapper = $null
    if ($Base -match '^[0-9A-Fa-f]{40}$') {
        & git -C $Fixture.Root cat-file -e "$Base^{commit}" 2>$null
        if ($LASTEXITCODE -eq 0) {
            $entry = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('ls-tree', $Base, '--', $TrustedWrapperRelativePath) `
                -Capture
            $entryMatch = [regex]::Match(
                $entry,
                '^100644 blob (?<ObjectId>[0-9a-f]+)\t' +
                    [regex]::Escape($TrustedWrapperRelativePath) + '$')
            if (-not $entryMatch.Success) {
                throw 'Trusted self-test base does not contain the reviewed wrapper.'
            }
            $temporaryWrapper = Join-Path ([IO.Path]::GetTempPath()) (
                "$([Guid]::NewGuid().ToString('N')).trusted-wrapper.ps1")
            Export-TestGitBlob `
                -Root $Fixture.Root `
                -ObjectId $entryMatch.Groups['ObjectId'].Value `
                -Destination $temporaryWrapper
            $actualObjectId = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('hash-object', '--no-filters', '--', $temporaryWrapper) `
                -Capture
            if ($actualObjectId -cne $entryMatch.Groups['ObjectId'].Value) {
                throw 'Extracted self-test wrapper differs from its trusted Git blob.'
            }
            $executorPath = $temporaryWrapper
        }
    }

    try {
        $arguments = @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $executorPath,
            '-RepositoryRoot', $Fixture.Root,
            '-TrustedBaseRevision', $Base,
            '-CandidateRevision', $Candidate,
            '-AnchorRelationship', $Relationship)
        $output = & pwsh @arguments 2>&1
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String).Trim()
        }
    }
    finally {
        if ($null -ne $temporaryWrapper) {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-PowerShellResult {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & pwsh -NoLogo -NoProfile -NonInteractive @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Amend-AuthorizationReceiptBytes {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    [IO.File]::WriteAllBytes((Join-Path $Fixture.Root $ReceiptRelativePath), $Bytes)
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $ReceiptRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $Fixture.Authorization = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    return $Fixture
}

function Assert-Pass {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action,
        [string]$ExpectedText
    )

    try {
        $result = & $Action
        if ($result.ExitCode -ne 0) {
            throw "expected success but exit=$($result.ExitCode): $($result.Output)"
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedText) -and
            $result.Output -notmatch [regex]::Escape($ExpectedText)) {
            throw "expected output '$ExpectedText' but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedCode,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        $result = & $Action
        if ($result.ExitCode -eq 0) {
            throw "expected rejection but validator passed: $($result.Output)"
        }
        if ($result.Output -notmatch [regex]::Escape("EDGE-BASELINE-MIG-001-$ExpectedCode")) {
            throw "expected $ExpectedCode but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name (rejected $ExpectedCode)"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

try {
    Assert-Pass -Name 'reference schema locks reviewed receipt constants' -Action {
        $schema = ConvertFrom-TestJson -Json ([IO.File]::ReadAllText($SchemaPath))
        $required = @($schema.required)
        $expectedRequired = @(
            'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
            'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
            'ruleIds', 'source', 'target', 'projectChanges', 'changes')
        $actualRequiredText = (@($required | Sort-Object) -join "`n")
        $expectedRequiredText = (@($expectedRequired | Sort-Object) -join "`n")
        if ($schema.additionalProperties -ne $false -or
            $actualRequiredText -cne $expectedRequiredText -or
            [string]$schema.properties.schemaVersion.const -cne '1.0' -or
            [string]$schema.properties.ruleId.const -cne 'EDGE-BASELINE-MIG-001' -or
            [string]$schema.properties.approvedBy.const -cne 'ShuJinHao' -or
            [long]$schema.properties.changes.maxItems -ne 5000 -or
            @($schema.properties.ruleIds.items.enum).Count -ne 37 -or
            @($schema.properties.ruleIds.items.enum) -cnotcontains 'EDGE-BASELINE-TRUST-UPGRADE-001' -or
            -not ([string]$schema.'$comment').Contains('is authoritative', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'reference schema constants drifted from the runtime validator contract.'
        }
        return [pscustomobject]@{ ExitCode = 0; Output = 'schema parity passed' }
    }

    $immutable = New-BaseFixture
    Assert-Pass -Name 'immutable transition' -ExpectedText 'transition is immutable' -Action {
        Invoke-Validation -Fixture $immutable -Base $immutable.Base -Candidate $immutable.Base
    }

    $withoutReceipt = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'protected change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $withoutReceipt -Base $withoutReceipt.Base -Candidate $withoutReceipt.Template
    }

    $describeRules = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'RuleIdsCsv rejects whitespace' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'EDGE-ARCH-001, EDGE-TEST-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects empty item' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'EDGE-ARCH-001,,EDGE-TEST-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects lowercase rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'edge-arch-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects unregistered rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'AAA-001'
    }
    Assert-Pass -Name 'RuleIdsCsv output is ordinal-sorted and unique' -Action {
        $result = Invoke-DescribeResult `
            -Fixture $describeRules `
            -RuleIdsCsv 'EDGE-PERSIST-001,EDGE-ARCH-001,EDGE-ARCH-001'
        if ($result.ExitCode -eq 0) {
            $receipt = ConvertFrom-TestJson -Json $result.Output
            $actual = @($receipt.ruleIds)
            if ($actual.Count -ne 2 -or
                $actual[0] -cne 'EDGE-ARCH-001' -or
                $actual[1] -cne 'EDGE-PERSIST-001') {
                throw "unexpected normalized ruleIds: $($actual -join ',')"
            }
        }
        return $result
    }

    $duplicateBaseline = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('checkout', '--quiet', $duplicateBaseline.Template)
    $baselinePath = Join-Path $duplicateBaseline.Root 'scripts/tests/baselines/edge-test-governance.baseline.json'
    $baselineJson = [IO.File]::ReadAllText($baselinePath).Replace(
        '"projects":',
        '"projects":[],"projects":')
    Write-Utf8File -Path $baselinePath -Content $baselineJson
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('add', '--all')
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $duplicateBaseline.Template = Invoke-Git -Root $duplicateBaseline.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'baseline duplicate JSON key is rejected' -ExpectedCode 'COUNTS' -Action {
        Invoke-DescribeResult -Fixture $duplicateBaseline -RuleIdsCsv 'EDGE-TEST-GOV-001'
    }

    $valid = New-AuthorizationFixture
    Assert-Pass -Name 'authorization-only transition' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Base -Candidate $valid.Authorization
    }
    $valid = Complete-Candidate -Fixture $valid
    Assert-Pass -Name 'receipt consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Authorization -Candidate $valid.Candidate
    }

    $noise = New-AuthorizationFixture -AddAuthorizationNoise
    Assert-Rejected -Name 'authorization commit contains another file' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $noise -Base $noise.Base -Candidate $noise.Authorization
    }

    $second = New-AuthorizationFixture -AddSecondPending
    Assert-Rejected -Name 'two pending receipts in one authorization' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $second -Base $second.Base -Candidate $second.Authorization
    }

    $missing = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.PSObject.Properties.Remove('reason')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'missing receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $missing -Base $missing.Base -Candidate $missing.Authorization
    }

    $unknown = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt | Add-Member -NotePropertyName command -NotePropertyValue 'Invoke-Expression'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'unknown executable receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $unknown -Base $unknown.Base -Candidate $unknown.Authorization
    }

    $duplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"reason":"duplicate","reason":'
    }
    Assert-Rejected -Name 'duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $duplicate -Base $duplicate.Base -Candidate $duplicate.Authorization
    }

    $expired = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'expired receipt' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $expired -Base $expired.Base -Candidate $expired.Authorization
    }

    $tooLong = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.expiresAtUtc = $Now.AddDays(8).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'receipt lifetime exceeds seven days' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $tooLong -Base $tooLong.Base -Candidate $tooLong.Authorization
    }

    $wrongBase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAgainstRevision = '1111111111111111111111111111111111111111'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wrong issued-against revision' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $wrongBase -Base $wrongBase.Base -Candidate $wrongBase.Authorization
    }

    $traversal = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = '../escape.yml'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'path traversal in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $traversal -Base $traversal.Base -Candidate $traversal.Authorization
    }

    $wildcard = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/Tests/*.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wildcard path in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $wildcard -Base $wildcard.Base -Candidate $wildcard.Authorization
    }

    $wrongMode = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterMode = '120000'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'symlink mode in receipt' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongMode -Base $wrongMode.Base -Candidate $wrongMode.Authorization
    }

    $caseCollision = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $clone = ConvertFrom-TestJson -Json ($receipt.changes[0] | ConvertTo-Json -Depth 10)
        $clone.path = ([string]$clone.path).ToUpperInvariant()
        $receipt.changes = @($receipt.changes) + @($clone)
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'case-colliding receipt paths' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $caseCollision -Base $caseCollision.Base -Candidate $caseCollision.Authorization
    }

    $wrongHash = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterSha256 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization accepts reviewed future descriptor' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Base -Candidate $wrongHash.Authorization
    }
    $wrongHash = Complete-Candidate -Fixture $wrongHash
    Assert-Rejected -Name 'consumption rejects wrong file hash' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Authorization -Candidate $wrongHash.Candidate
    }

    $countDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [int]$receipt.target.counts.runnerCases + 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization records future count claim' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Base -Candidate $countDrift.Authorization
    }
    $countDrift = Complete-Candidate -Fixture $countDrift
    Assert-Rejected -Name 'consumption rejects runner count drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Authorization -Candidate $countDrift.Candidate
    }

    $manifestDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.protectedManifestSha256 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        return $receipt | ConvertTo-Json -Depth 100
    }
    $manifestDrift = Complete-Candidate -Fixture $manifestDrift
    Assert-Rejected -Name 'consumption rejects protected manifest drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $manifestDrift -Base $manifestDrift.Authorization -Candidate $manifestDrift.Candidate
    }

    $noMove = Complete-Candidate -Fixture (New-AuthorizationFixture) -SkipMove
    Assert-Rejected -Name 'pending receipt not moved' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noMove -Base $noMove.Authorization -Candidate $noMove.Candidate
    }

    $altered = Complete-Candidate -Fixture (New-AuthorizationFixture) -AlterConsumed
    Assert-Rejected -Name 'consumed receipt blob changed' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $altered -Base $altered.Authorization -Candidate $altered.Candidate
    }

    $extra = Complete-Candidate -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'candidate has extra path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $extra -Base $extra.Authorization -Candidate $extra.Candidate
    }

    $fewer = Complete-Candidate -Fixture (New-AuthorizationFixture) -RemoveExpectedPath
    Assert-Rejected -Name 'candidate omits expected path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $fewer -Base $fewer.Authorization -Candidate $fewer.Candidate
    }

    $candidateBypass = Complete-Candidate -Fixture (New-AuthorizationFixture) -ModifyCandidateValidator
    Assert-Rejected -Name 'candidate validator cannot self-authorize' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $candidateBypass -Base $candidateBypass.Authorization -Candidate $candidateBypass.Candidate
    }

    $wrongPropertyCase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"Reason":'
    }
    Assert-Rejected -Name 'receipt property names are case-sensitive' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongPropertyCase -Base $wrongPropertyCase.Base -Candidate $wrongPropertyCase.Authorization
    }

    $nestedDuplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"baselineSha256"\s*:', '"baselineSha256":"duplicate","baselineSha256":'
    }
    Assert-Rejected -Name 'nested duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nestedDuplicate -Base $nestedDuplicate.Base -Candidate $nestedDuplicate.Authorization
    }

    $scalarRuleIds = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.ruleIds = 'EDGE-ARCH-001'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'ruleIds scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarRuleIds -Base $scalarRuleIds.Base -Candidate $scalarRuleIds.Authorization
    }

    $scalarChanges = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes = $receipt.changes[0]
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'changes scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarChanges -Base $scalarChanges.Base -Candidate $scalarChanges.Authorization
    }

    $nullProjectArray = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.projectChanges.added = $null
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'null project array is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nullProjectArray -Base $nullProjectArray.Base -Candidate $nullProjectArray.Authorization
    }

    foreach ($registryMutation in @(
        @{ Name = 'owner registry is case-sensitive'; Property = 'owner'; Value = 'edge.architecture' },
        @{ Name = 'approver registry is case-sensitive'; Property = 'approvedBy'; Value = 'shujinhao' },
        @{ Name = 'governance rule ID is case-sensitive'; Property = 'ruleId'; Value = 'edge-baseline-mig-001' }
    )) {
        $propertyName = [string]$registryMutation.Property
        $propertyValue = [string]$registryMutation.Value
        $registryCase = New-AuthorizationFixture -MutateReceipt {
            param($json)
            $receipt = ConvertFrom-TestJson -Json $json
            $receipt.$propertyName = $propertyValue
            return $receipt | ConvertTo-Json -Depth 100
        }
        Assert-Rejected -Name ([string]$registryMutation.Name) -ExpectedCode 'RECEIPT' -Action {
            Invoke-Validation -Fixture $registryCase -Base $registryCase.Base -Candidate $registryCase.Authorization
        }
    }

    $windowsReserved = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/Tests/CON.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows reserved path is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $windowsReserved -Base $windowsReserved.Base -Candidate $windowsReserved.Authorization
    }

    $trailingDot = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/Tests/bad./Case.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows trailing-dot segment is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $trailingDot -Base $trailingDot.Base -Candidate $trailingDot.Authorization
    }

    $superscriptDevice = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/Tests/COM¹.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows superscript device name is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $superscriptDevice -Base $superscriptDevice.Base -Candidate $superscriptDevice.Authorization
    }

    $longComponent = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = "src/Tests/$('a' * 256).cs"
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows overlong path component is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $longComponent -Base $longComponent.Base -Candidate $longComponent.Authorization
    }

    $countDecrease = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [long]$receipt.source.counts.runnerCases - 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'target test evidence cannot decrease' -ExpectedCode 'COUNTS' -Action {
        Invoke-Validation -Fixture $countDecrease -Base $countDecrease.Base -Candidate $countDecrease.Authorization
    }

    $invalidUtf8 = New-AuthorizationFixture
    $invalidUtf8 = Amend-AuthorizationReceiptBytes -Fixture $invalidUtf8 -Bytes ([byte[]]@(0x7B, 0xFF, 0x7D))
    Assert-Rejected -Name 'invalid UTF-8 receipt is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $invalidUtf8 -Base $invalidUtf8.Base -Candidate $invalidUtf8.Authorization
    }

    $oversized = New-AuthorizationFixture
    $oversized = Amend-AuthorizationReceiptBytes -Fixture $oversized -Bytes (
        [Text.Encoding]::UTF8.GetBytes(' ' * (1MB + 1)))
    Assert-Rejected -Name 'oversized receipt is rejected before parsing' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $oversized -Base $oversized.Base -Candidate $oversized.Authorization
    }

    $executablePending = New-AuthorizationFixture
    $pendingBytes = [IO.File]::ReadAllBytes((Join-Path $executablePending.Root $ReceiptRelativePath))
    $executablePending = Amend-AuthorizationReceiptBytes -Fixture $executablePending -Bytes $pendingBytes -ExecutableMode
    Assert-Rejected -Name 'pending receipt executable mode is rejected' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $executablePending -Base $executablePending.Base -Candidate $executablePending.Authorization
    }

    $missingTrustMarker = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            'EDGE-BASELINE-MIG-TRUSTED-EXECUTOR-V1',
            'EDGE-BASELINE-MIG-NOT-TRUSTED')
        Write-Utf8File -Path $path -Content $text
    }
    $missingTrustMarker = Complete-Candidate -Fixture $missingTrustMarker
    Assert-Rejected -Name 'workflow missing trusted marker is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $missingTrustMarker -Base $missingTrustMarker.Authorization -Candidate $missingTrustMarker.Candidate
    }

    $preGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted baseline migration',
            "      - name: Pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted baseline migration")
        Write-Utf8File -Path $path -Content $text
    }
    $preGateStep = Complete-Candidate -Fixture $preGateStep
    Assert-Rejected -Name 'workflow step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $preGateStep -Base $preGateStep.Authorization -Candidate $preGateStep.Candidate
    }

    $alternatePreGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted baseline migration',
            "      -`n        name: Alternate pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted baseline migration")
        Write-Utf8File -Path $path -Content $text
    }
    $alternatePreGateStep = Complete-Candidate -Fixture $alternatePreGateStep
    Assert-Rejected -Name 'alternate YAML step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $alternatePreGateStep `
            -Base $alternatePreGateStep.Authorization `
            -Candidate $alternatePreGateStep.Candidate
    }

    $spoofedCheckout = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7',
            "        uses: attacker/checkout@0123456789012345678901234567890123456789`n        # uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7")
        Write-Utf8File -Path $path -Content $text
    }
    $spoofedCheckout = Complete-Candidate -Fixture $spoofedCheckout
    Assert-Rejected -Name 'comment cannot spoof pinned checkout action' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $spoofedCheckout -Base $spoofedCheckout.Authorization -Candidate $spoofedCheckout.Candidate
    }

    $softFailedGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        continue-on-error: true`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $softFailedGate = Complete-Candidate -Fixture $softFailedGate
    Assert-Rejected -Name 'trusted gate cannot continue on error' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $softFailedGate -Base $softFailedGate.Authorization -Candidate $softFailedGate.Candidate
    }

    $conditionalGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        if: false`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $conditionalGate = Complete-Candidate -Fixture $conditionalGate
    Assert-Rejected -Name 'trusted gate cannot be conditional' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $conditionalGate -Base $conditionalGate.Authorization -Candidate $conditionalGate.Candidate
    }

    $changedRunner = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '    runs-on: windows-latest',
            '    runs-on: self-hosted')
        Write-Utf8File -Path $path -Content $text
    }
    $changedRunner = Complete-Candidate -Fixture $changedRunner
    Assert-Rejected -Name 'trusted job runner is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $changedRunner -Base $changedRunner.Authorization -Candidate $changedRunner.Candidate
    }

    $changedWorkflowEnvelope = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "jobs:`n",
            "permissions: write-all`njobs:`n")
        Write-Utf8File -Path $path -Content $text
    }
    $changedWorkflowEnvelope = Complete-Candidate -Fixture $changedWorkflowEnvelope
    Assert-Rejected -Name 'workflow trigger and permissions envelope is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $changedWorkflowEnvelope `
            -Base $changedWorkflowEnvelope.Authorization `
            -Candidate $changedWorkflowEnvelope.Candidate
    }

    $jobLevelEnvironment = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n    env:`n      PATH: candidate-controlled-path`n"
    }
    $jobLevelEnvironment = Complete-Candidate -Fixture $jobLevelEnvironment
    Assert-Rejected -Name 'trusted job rejects trailing job-level environment' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $jobLevelEnvironment `
            -Base $jobLevelEnvironment.Authorization `
            -Candidate $jobLevelEnvironment.Candidate
    }

    $quotedJobLevelEnvironment = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n    `"env`":`n      PATH: candidate-controlled-path`n"
    }
    $quotedJobLevelEnvironment = Complete-Candidate -Fixture $quotedJobLevelEnvironment
    Assert-Rejected -Name 'quoted job-level environment cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedJobLevelEnvironment `
            -Base $quotedJobLevelEnvironment.Authorization `
            -Candidate $quotedJobLevelEnvironment.Candidate
    }

    $quotedTopLevelPermissions = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n`"permissions`": write-all`n"
    }
    $quotedTopLevelPermissions = Complete-Candidate -Fixture $quotedTopLevelPermissions
    Assert-Rejected -Name 'quoted top-level permissions cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedTopLevelPermissions `
            -Base $quotedTopLevelPermissions.Authorization `
            -Candidate $quotedTopLevelPermissions.Candidate
    }

    $disabledTrustedJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-smoke-build.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "  smoke-build:`n    runs-on:",
            "  smoke-build:`n    if : false`n    runs-on:")
        Write-Utf8File -Path $path -Content $text
    }
    $disabledTrustedJob = Complete-Candidate -Fixture $disabledTrustedJob
    Assert-Rejected -Name 'disabled trusted workflow job is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $disabledTrustedJob -Base $disabledTrustedJob.Authorization -Candidate $disabledTrustedJob.Candidate
    }

    $duplicateWrapperReference = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/edge-pack-modules.yml'
        [IO.File]::AppendAllText(
            $path,
            "# scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1`n",
            [Text.UTF8Encoding]::new($false))
    }
    $duplicateWrapperReference = Complete-Candidate -Fixture $duplicateWrapperReference
    Assert-Rejected -Name 'duplicate wrapper reference is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $duplicateWrapperReference -Base $duplicateWrapperReference.Authorization -Candidate $duplicateWrapperReference.Candidate
    }

    $baselinePolicyCoChange = New-BaseFixture
    $baselinePath = Join-Path $baselinePolicyCoChange.Root 'scripts/tests/baselines/edge-test-governance.baseline.json'
    $baselineText = [IO.File]::ReadAllText($baselinePath).Replace(
        '"baselineDeclarations": 1',
        '"baselineDeclarations": 2')
    Write-Utf8File -Path $baselinePath -Content $baselineText
    Write-Utf8File `
        -Path (Join-Path $baselinePolicyCoChange.Root 'scripts/tests/TestEdgeTestGovernancePolicy.ps1') `
        -Content "throw 'candidate policy'`n"
    $baselinePolicyCoChange | Add-Member `
        -NotePropertyName Template `
        -NotePropertyValue (Commit-All -Root $baselinePolicyCoChange.Root -Message 'baseline policy co-change') `
        -Force

    $ordinaryTrustChange = New-TrustTemplateFixture
    Assert-Rejected -Name 'baseline-policy co-change and ordinary trust replacement are rejected' -ExpectedCode 'TRUST' -Action {
        $coChangeResult = Invoke-DescribeResult `
            -Fixture $baselinePolicyCoChange `
            -RuleIdsCsv 'EDGE-ARCH-001'
        if ($coChangeResult.ExitCode -eq 0 -or
            $coChangeResult.Output -notmatch [regex]::Escape('EDGE-BASELINE-MIG-001-POLICY')) {
            throw "expected POLICY for baseline/policy co-change but got: $($coChangeResult.Output)"
        }
        Invoke-DescribeResult -Fixture $ordinaryTrustChange -RuleIdsCsv 'EDGE-ARCH-001'
    }

    $mixedTrustChange = New-TrustTemplateFixture
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('checkout', '--quiet', $mixedTrustChange.Template)
    Write-Utf8File -Path (Join-Path $mixedTrustChange.Root 'src/App/Mixed.cs') -Content "internal sealed class Mixed { }`n"
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('add', '--all')
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $mixedTrustChange.Template = Invoke-Git -Root $mixedTrustChange.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'trust upgrade cannot mix ordinary paths' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $mixedTrustChange `
            -RuleIdsCsv 'EDGE-BASELINE-TRUST-UPGRADE-001'
    }

    $extraTrustAsset = New-TrustTemplateFixture
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('checkout', '--quiet', $extraTrustAsset.Template)
    Write-Utf8File `
        -Path (Join-Path $extraTrustAsset.Root 'scripts/tests/baselines/migrations/FutureTrustBypass.ps1') `
        -Content "throw 'candidate trust bypass'`n"
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('add', '--all')
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $extraTrustAsset.Template = Invoke-Git -Root $extraTrustAsset.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'trust upgrade rejects unregistered implementation path' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $extraTrustAsset `
            -RuleIdsCsv 'EDGE-BASELINE-TRUST-UPGRADE-001'
    }

    $deletedTrustAsset = New-TrustTemplateFixture
    Invoke-Git -Root $deletedTrustAsset.Root -Arguments @(
        'checkout', '--quiet', $deletedTrustAsset.Template)
    Invoke-Git -Root $deletedTrustAsset.Root -Arguments @(
        'rm', '--quiet', '--', $TrustedWrapperRelativePath)
    Invoke-Git -Root $deletedTrustAsset.Root -Arguments @(
        'commit', '--quiet', '--amend', '--no-edit')
    $deletedTrustAsset.Template = Invoke-Git -Root $deletedTrustAsset.Root -Arguments @(
        'rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'trust upgrade cannot delete required trust asset' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $deletedTrustAsset `
            -RuleIdsCsv 'EDGE-BASELINE-TRUST-UPGRADE-001'
    }

    $trustUpgrade = New-TrustUpgradeFixture
    Assert-Pass -Name 'isolated trust upgrade authorization' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Base -Candidate $trustUpgrade.Authorization
    }
    $trustUpgrade = Complete-Candidate -Fixture $trustUpgrade
    Assert-Pass -Name 'isolated trust upgrade consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Authorization -Candidate $trustUpgrade.Candidate
    }

    $cancelled = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Assert-Pass -Name 'pending receipt can be cancelled byte-for-byte' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $cancelled -Base $cancelled.Authorization -Candidate $cancelled.Candidate
    }

    $expiredCancellation = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    $expiredCancellation = Complete-Cancellation -Fixture $expiredCancellation
    Assert-Pass -Name 'expired pending receipt can be cancelled for recovery' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $expiredCancellation -Base $expiredCancellation.Authorization -Candidate $expiredCancellation.Candidate
    }

    $alteredCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AlterCancelled
    Assert-Rejected -Name 'altered cancelled receipt is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $alteredCancellation -Base $alteredCancellation.Authorization -Candidate $alteredCancellation.Candidate
    }

    $noisyCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'cancellation cannot carry another path' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noisyCancellation -Base $noisyCancellation.Authorization -Candidate $noisyCancellation.Candidate
    }

    $executableCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -ExecutableMode
    Assert-Rejected -Name 'cancelled receipt executable mode is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $executableCancellation -Base $executableCancellation.Authorization -Candidate $executableCancellation.Candidate
    }

    $cancelReplay = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $cancelReplay.Root -Arguments @('checkout', '--quiet', $cancelReplay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $cancelReplay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $cancelReplay.Root $CancelledRelativePath),
        (Join-Path $cancelReplay.Root $ReceiptRelativePath),
        $true)
    $cancelReplayAttempt = Commit-All -Root $cancelReplay.Root -Message 'attempt cancelled replay'
    Assert-Rejected -Name 'cancelled migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $cancelReplay -Base $cancelReplay.Candidate -Candidate $cancelReplayAttempt
    }

    $nonDirectAuthorization = New-AuthorizationFixture
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'checkout', '--quiet', $nonDirectAuthorization.Authorization)
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'authorization descendant')
    $authorizationDescendant = Invoke-Git `
        -Root $nonDirectAuthorization.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'authorization must be a direct single-parent commit' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation `
            -Fixture $nonDirectAuthorization `
            -Base $nonDirectAuthorization.Base `
            -Candidate $authorizationDescendant
    }

    $nonDirectConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @('checkout', '--quiet', $nonDirectConsumption.Candidate)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'consumption descendant')
    $consumptionDescendant = Invoke-Git `
        -Root $nonDirectConsumption.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'consumption must be a direct single-parent commit' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation `
            -Fixture $nonDirectConsumption `
            -Base $nonDirectConsumption.Authorization `
            -Candidate $consumptionDescendant
    }

    $mergeConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', '-b', 'side-parent', $mergeConsumption.Authorization)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'side parent')
    $sideParent = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', $mergeConsumption.Candidate)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'synthetic merge shape', $sideParent)
    $mergeCandidate = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'merge-shaped consumption is rejected' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation `
            -Fixture $mergeConsumption `
            -Base $mergeConsumption.Authorization `
            -Candidate $mergeCandidate
    }

    $nonDirectCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @('checkout', '--quiet', $nonDirectCancellation.Candidate)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'cancellation descendant')
    $cancellationDescendant = Invoke-Git `
        -Root $nonDirectCancellation.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'cancellation must be a direct single-parent commit' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation `
            -Fixture $nonDirectCancellation `
            -Base $nonDirectCancellation.Authorization `
            -Candidate $cancellationDescendant
    }

    $replay = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $replay.Root -Arguments @('checkout', '--quiet', $replay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $replay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $replay.Root $ConsumedRelativePath),
        (Join-Path $replay.Root $ReceiptRelativePath),
        $true)
    $replayAttempt = Commit-All -Root $replay.Root -Message 'attempt replay'
    Assert-Rejected -Name 'consumed migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $replay -Base $replay.Candidate -Candidate $replayAttempt
    }

    $zero = New-BaseFixture
    Assert-Rejected -Name 'zero trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base '0000000000000000000000000000000000000000' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'short trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base $zero.Base.Substring(0, 12) -Candidate $zero.Base
    }

    Assert-Rejected -Name 'symbolic trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base 'HEAD' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'unexpected positional argument rejected' -ExpectedCode 'PARAMETER' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $zero.Root,
            '-TrustedBaseRevision', $zero.Base,
            '-CandidateRevision', $zero.Base,
            'unexpected')
    }

    $candidateNotHead = New-BaseFixture
    Write-Utf8File -Path (Join-Path $candidateNotHead.Root 'docs/later.md') -Content "later`n"
    $laterHead = Commit-All -Root $candidateNotHead.Root -Message 'later head'
    Assert-Rejected -Name 'candidate revision must equal checked-out HEAD' -ExpectedCode 'REVISION' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $candidateNotHead.Root,
            '-TrustedBaseRevision', $candidateNotHead.Base,
            '-CandidateRevision', $candidateNotHead.Base)
    }

    $analyzerBypass = New-BaseFixture
    Write-Utf8File -Path (Join-Path $analyzerBypass.Root 'src/Analyzers/Bypass.cs') -Content "internal sealed class Bypass { }`n"
    $analyzerCandidate = Commit-All -Root $analyzerBypass.Root -Message 'attempt analyzer bypass'
    Assert-Rejected -Name 'analyzer source cannot change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $analyzerBypass -Base $analyzerBypass.Base -Candidate $analyzerCandidate
    }

    $wrapperBypass = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $wrapperBypass.Root 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1') `
        -Content "exit 0`n"
    $wrapperCandidate = Commit-All -Root $wrapperBypass.Root -Message 'attempt wrapper bypass'
    Assert-Rejected -Name 'base-extracted wrapper ignores candidate wrapper bypass' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $wrapperBypass -Base $wrapperBypass.Base -Candidate $wrapperCandidate
    }

    $releaseAnchor = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseAnchor.Root 'docs/main-ahead.md') -Content "main ahead`n"
    $trustedMain = Commit-All -Root $releaseAnchor.Root -Message 'unprotected main advance'
    Assert-Pass -Name 'release anchor allows unprotected main advance' -ExpectedText 'trusted release anchor passed' -Action {
        Invoke-Validation `
            -Fixture $releaseAnchor `
            -Base $trustedMain `
            -Candidate $releaseAnchor.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $releaseProtected = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseProtected.Root 'src/Tests/Sample.Tests/NewCase.cs') -Content "internal sealed class NewCase { }`n"
    $protectedMain = Commit-All -Root $releaseProtected.Root -Message 'protected main advance'
    Assert-Rejected -Name 'release anchor rejects protected drift' -ExpectedCode 'RELEASE' -Action {
        Invoke-Validation `
            -Fixture $releaseProtected `
            -Base $protectedMain `
            -Candidate $releaseProtected.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $wrongReleaseDirection = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'release anchor rejects wrong ancestry direction' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation `
            -Fixture $wrongReleaseDirection `
            -Base $wrongReleaseDirection.Base `
            -Candidate $wrongReleaseDirection.Template `
            -Relationship 'HeadAncestorOfBase'
    }

    $sideRelease = New-BaseFixture
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', '-b', 'release-side', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/side-release.md') -Content "side`n"
    $sideReleaseCandidate = Commit-All -Root $sideRelease.Root -Message 'side release candidate'
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/main-first-parent.md') -Content "main`n"
    [void](Commit-All -Root $sideRelease.Root -Message 'main first-parent advance')
    Invoke-Git -Root $sideRelease.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'merge side release', $sideReleaseCandidate)
    $sideReleaseTrustedMain = Invoke-Git -Root $sideRelease.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'release anchor must be on trusted first-parent chain' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation `
            -Fixture $sideRelease `
            -Base $sideReleaseTrustedMain `
            -Candidate $sideReleaseCandidate `
            -Relationship 'HeadAncestorOfBase'
    }

    $orphan = New-BaseFixture
    Invoke-Git -Root $orphan.Root -Arguments @('checkout', '--quiet', '--orphan', 'orphan-line')
    Invoke-Git -Root $orphan.Root -Arguments @('rm', '--quiet', '-f', '-r', '--ignore-unmatch', '.')
    Write-Utf8File -Path (Join-Path $orphan.Root 'orphan.txt') -Content "orphan`n"
    $orphanCandidate = Commit-All -Root $orphan.Root -Message 'orphan commit'
    Assert-Rejected -Name 'same-repository orphan history rejected' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation -Fixture $orphan -Base $orphan.Base -Candidate $orphanCandidate
    }

    $unrelatedLeft = New-BaseFixture
    $unrelatedRight = New-BaseFixture
    Write-Utf8File -Path (Join-Path $unrelatedRight.Root 'src/App/Unrelated.cs') -Content "internal sealed class Unrelated { }`n"
    $unrelatedRight.Base = Commit-All -Root $unrelatedRight.Root -Message 'unrelated history'
    Assert-Rejected -Name 'unrelated trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $unrelatedLeft -Base $unrelatedRight.Base -Candidate $unrelatedLeft.Base
    }
}
finally {
    foreach ($root in $script:TempRoots) {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failed -ne 0) {
    throw "Edge baseline migration validator self-tests failed: passed=$($script:Passed) failed=$($script:Failed)."
}

Write-Host "Edge baseline migration validator self-tests passed: $($script:Passed)/$($script:Passed)."
$global:LASTEXITCODE = 0
exit 0
