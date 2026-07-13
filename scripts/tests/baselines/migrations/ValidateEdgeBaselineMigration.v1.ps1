[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Validate', 'Describe')]
    [string]$Mode = 'Validate',
    [string]$RepositoryRoot,
    [Parameter(Mandatory)]
    [string]$TrustedBaseRevision,
    [string]$CandidateRevision = 'HEAD',
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [string]$OutputPath,
    [string]$MigrationId,
    [string]$RuleIdsCsv,
    [string]$Owner,
    [string]$ApprovedBy,
    [string]$Reason,
    [string]$IssuedAtUtc,
    [string]$ExpiresAtUtc,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RuleId = 'EDGE-BASELINE-MIG-001'
$script:ReceiptSchemaVersion = '1.0'
$script:BaselinePath = 'scripts/tests/baselines/edge-test-governance.baseline.json'
$script:PolicyPath = 'scripts/tests/TestEdgeTestGovernancePolicy.ps1'
$script:PendingRoot = 'scripts/tests/baselines/migrations/pending/'
$script:ConsumedRoot = 'scripts/tests/baselines/migrations/consumed/'
$script:CancelledRoot = 'scripts/tests/baselines/migrations/cancelled/'
$script:ValidatorPath = 'scripts/tests/baselines/migrations/ValidateEdgeBaselineMigration.v1.ps1'
$script:TrustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeEdgeBaselineMigrationFromTrustedBase.v1.ps1'
$script:SelfTestPath = 'scripts/tests/baselines/migrations/TestEdgeBaselineMigrationValidator.v1.ps1'
$script:SchemaPath = 'scripts/tests/baselines/migrations/edge-baseline-migration-receipt.schema.json'
$script:TrustImplementationPaths = @(
    $script:TrustedWrapperPath,
    $script:SelfTestPath,
    $script:ValidatorPath,
    $script:SchemaPath
)
$script:MaximumReceiptLifetime = [TimeSpan]::FromDays(7)
$script:MaximumReceiptBytes = 1MB
$script:MaximumReceiptChanges = 5000
$script:JsonSerializerOptions = [Text.Json.JsonSerializerOptions]::new()
$script:ApprovedOwners = @(
    'Edge.Architecture',
    'Edge.Persistence',
    'Edge.Tests',
    'Edge.Deployment',
    'Edge.Security'
)
$script:ApprovedApprovers = @('ShuJinHao')
$script:TrustUpgradeRuleId = 'EDGE-BASELINE-TRUST-UPGRADE-001'
$script:ApprovedGovernedRuleIds = @(
    'EDGE-ARCH-001',
    'EDGE-PERSIST-001',
    'EDGE-TEST-GOV-001',
    'EDGE-TEST-PHYSICAL-001',
    'WSARCH001', 'WSARCH003', 'WSARCH004', 'WSARCH005', 'WSARCH008',
    'DDD001', 'DDD002', 'DDD003', 'DDD004', 'DDD005', 'DDD006', 'DDD007',
    'DATA001', 'DATA002', 'DATA003', 'DATA004', 'DATA005', 'DATA006', 'DATA007',
    'PLUG001', 'PLUG002', 'PLUG003', 'PLUG004', 'PLUG005', 'PLUG006',
    'EDGEOUT001',
    'EDGEPLCOWN001',
    'EDGEASYNC001', 'EDGEASYNC002',
    'EDGEFRIEND001',
    'EDGEPLUGCON001',
    'QUALITY001',
    'EDGE-BASELINE-TRUST-UPGRADE-001'
)

function Stop-MigrationValidation {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )

    throw "$($script:RuleId)-$Code $Message"
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function ConvertFrom-StrictUtf8Bytes {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    }
    catch {
        Stop-MigrationValidation -Code $Code -Message "$Context is not valid UTF-8."
    }
}

function Get-OrdinalSortedStrings {
    param(
        [AllowEmptyCollection()][object[]]$Values,
        [switch]$Unique
    )

    $items = [Collections.Generic.List[string]]::new()
    foreach ($value in @($Values)) { $items.Add([string]$value) }
    $items.Sort([StringComparer]::Ordinal)
    if (-not $Unique) { return @($items) }

    $result = [Collections.Generic.List[string]]::new()
    $previous = $null
    foreach ($item in $items) {
        if ($null -eq $previous -or -not [StringComparer]::Ordinal.Equals($previous, $item)) {
            $result.Add($item)
            $previous = $item
        }
    }
    return @($result)
}

function Get-OrdinalSortedChangeRecords {
    param([AllowEmptyCollection()][object[]]$Values)

    $items = [Collections.Generic.List[object]]::new()
    foreach ($value in @($Values)) { $items.Add($value) }
    $comparer = [Collections.Generic.Comparer[object]]::Create(
        [Comparison[object]]{
            param($left, $right)
            return [StringComparer]::Ordinal.Compare([string]$left.path, [string]$right.path)
        })
    $items.Sort($comparer)
    return @($items)
}

function Invoke-GitBytes {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($script:RepositoryRoot)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Stop-MigrationValidation -Code 'GIT' -Message "could not start git $($Arguments -join ' ')."
    }

    $errorTask = $process.StandardError.ReadToEndAsync()
    $buffer = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Bytes = $buffer.ToArray()
            Error = $errorText
        }
    }
    finally {
        $buffer.Dispose()
        $process.Dispose()
    }

    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        Stop-MigrationValidation -Code 'GIT' -Message (
            "git {0} failed with exit code {1}: {2}" -f
            ($Arguments -join ' '), $result.ExitCode, $result.Error)
    }

    return $result
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $result = Invoke-GitBytes -Arguments $Arguments -AllowFailure:$AllowFailure
    return [pscustomobject]@{
        ExitCode = $result.ExitCode
        Text = [Text.Encoding]::UTF8.GetString($result.Bytes)
        Error = $result.Error
    }
}

function Resolve-Commit {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Revision) -or $Revision -match '^0{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name must be a non-zero commit revision."
    }

    $result = Invoke-GitText -Arguments @('rev-parse', '--verify', "$Revision^{commit}") -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name is not an available commit: $Revision."
    }

    $resolved = $result.Text.Trim()
    if ($resolved -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name did not resolve to one full commit SHA."
    }

    return $resolved
}

function Assert-Ancestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $result = Invoke-GitText -Arguments @('merge-base', '--is-ancestor', $Ancestor, $Descendant) -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message "$Context requires $Ancestor to be an ancestor of $Descendant."
    }
}

function Assert-FirstParentAncestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $history = (Invoke-GitText -Arguments @('rev-list', '--first-parent', $Descendant)).Text
    $firstParentCommits = @($history -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($Ancestor -cnotin $firstParentCommits) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message (
            "$Context requires $Ancestor on the first-parent chain of $Descendant.")
    }
}

function Assert-DirectSingleParentTransition {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    $parents = (Invoke-GitText -Arguments @(
        'rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($parents.Count -ne 2 -or $parents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code $Code -Message (
            "$Context must be one single-parent commit directly after its trusted base.")
    }
}

function Assert-SafeRepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -cne $Path.Trim() -or
        $Path.StartsWith('/') -or
        $Path.Contains('\') -or
        $Path.Contains('//') -or
        $Path -match '(^|/)\.\.?(/|$)' -or
        $Path -match '[\x00-\x1F\x7F]' -or
        $Path.Contains('*') -or
        $Path.Contains('?') -or
        $Path.Contains('[') -or
        $Path.Contains(']') -or
        $Path -match '[<>:"|]') {
        Stop-MigrationValidation -Code 'PATH' -Message "unsafe repository path '$Path'."
    }

    $windowsReservedName = '^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\..*)?$'
    foreach ($segment in $Path.Split('/')) {
        if ($segment.Length -gt 255 -or
            $segment.EndsWith(' ', [StringComparison]::Ordinal) -or
            $segment.EndsWith('.', [StringComparison]::Ordinal) -or
            $segment -match $windowsReservedName) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "repository path '$Path' is not portable to supported Windows worktrees.")
        }
    }
}

function Get-RevisionPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-r', '-z', '--name-only', $Revision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $text = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'PATH' `
        -Context "repository tree at $Revision"
    $nul = [char[]]@([char]0)
    $paths = @($text.TrimEnd($nul).Split($nul, [StringSplitOptions]::None) |
        Where-Object { $_ -ne '' })
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) {
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding repository paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path
    }

    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Get-GitEntry {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowMissing
    )

    Assert-SafeRepositoryPath -Path $Path
    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-z', $Revision, '--', $Path)
    if ($result.Bytes.Length -eq 0) {
        if ($AllowMissing) { return $null }
        Stop-MigrationValidation -Code 'BLOB' -Message "$Revision has no tracked file '$Path'."
    }

    $text = (ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'BLOB' `
        -Context "git entry for '$Path' at $Revision").TrimEnd([char]0)
    $match = [regex]::Match($text, '^([0-9]{6}) ([a-z]+) ([0-9a-f]+)\t(.+)$')
    if (-not $match.Success -or $match.Groups[4].Value -cne $Path) {
        Stop-MigrationValidation -Code 'BLOB' -Message "could not parse git entry for '$Path' at $Revision."
    }
    if ($match.Groups[2].Value -cne 'blob' -or $match.Groups[1].Value -cnotin @('100644', '100755')) {
        Stop-MigrationValidation -Code 'MODE' -Message (
            "'$Path' uses forbidden git type/mode $($match.Groups[2].Value)/$($match.Groups[1].Value).")
    }

    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        ObjectId = $match.Groups[3].Value
        Path = $Path
    }
}

function Get-GitBlobBytes {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    return (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
}

function Get-GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    return [Text.Encoding]::UTF8.GetString((Get-GitBlobBytes -Revision $Revision -Path $Path))
}

function Get-StrictUtf8GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString(
            (Get-GitBlobBytes -Revision $Revision -Path $Path))
    }
    catch {
        Stop-MigrationValidation -Code 'TRUST' -Message "'$Path' is not valid UTF-8 at $Revision."
    }
}

function Get-OrdinalOccurrenceCount {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Value
    )

    if ($Value.Length -eq 0) { return 0 }
    $count = 0
    $offset = 0
    while ($offset -le $Text.Length - $Value.Length) {
        $index = $Text.IndexOf($Value, $offset, [StringComparison]::Ordinal)
        if ($index -lt 0) { break }
        $count++
        $offset = $index + $Value.Length
    }
    return $count
}

function Assert-TargetWorkflowTrustClosure {
    param([Parameter(Mandatory)][string]$Revision)

    $workflowContracts = [ordered]@{
        '.github/workflows/edge-smoke-build.yml' = @'
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
'@
        '.github/workflows/edge-pack-modules.yml' = @'
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
'@
    }

    $workflowHeaders = [ordered]@{
        '.github/workflows/edge-smoke-build.yml' = @'
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
'@
        '.github/workflows/edge-pack-modules.yml' = @'
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
'@
    }

    foreach ($workflowPath in $workflowContracts.Keys) {
        $text = (Get-StrictUtf8GitBlobText -Revision $Revision -Path $workflowPath).
            Replace("`r`n", "`n").Replace("`r", "`n")
        $requiredBlock = ([string]$workflowContracts[$workflowPath]).TrimEnd("`r", "`n")
        $requiredHeader = ([string]$workflowHeaders[$workflowPath]).TrimEnd("`r", "`n")
        $topLevelLines = @($text.Split("`n") | Where-Object {
            $_ -match '^\S' -and -not $_.StartsWith('#', [StringComparison]::Ordinal)
        })
        $expectedTopLevelLines = if ($workflowPath -ceq '.github/workflows/edge-smoke-build.yml') {
            @('name: edge-smoke-build', 'on:', 'env:', 'jobs:')
        } else {
            @('name: edge-runtime-package', 'on:', 'env:', 'jobs:')
        }
        if (-not $text.StartsWith("$requiredHeader`n", [StringComparison]::Ordinal) -or
            ($topLevelLines -join '|') -cne ($expectedTopLevelLines -join '|')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' trigger, environment and top-level trust envelope are not canonical.")
        }
        $jobName = if ($workflowPath -ceq '.github/workflows/edge-smoke-build.yml') {
            'smoke-build'
        } else {
            'validate-runtime'
        }
        $jobHeader = "  ${jobName}:"
        if ([regex]::Matches($text, '(?m)^jobs:\s*$').Count -ne 1 -or
            (Get-OrdinalOccurrenceCount -Text $text -Value "$jobHeader`n") -ne 1) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' must contain one canonical '$jobName' job.")
        }
        $jobStart = $text.IndexOf("$jobHeader`n", [StringComparison]::Ordinal)
        $remainingAfterHeader = $text.Substring($jobStart + $jobHeader.Length + 1)
        $nextJobMatch = [regex]::Match(
            $remainingAfterHeader,
            '(?m)^  [A-Za-z0-9_-]+:\s*$')
        $jobEnd = if ($nextJobMatch.Success) {
            $jobStart + $jobHeader.Length + 1 + $nextJobMatch.Index
        } else {
            $text.Length
        }
        $jobText = $text.Substring($jobStart, $jobEnd - $jobStart)
        $jobDirectLines = @($jobText.Split("`n") | Where-Object {
            $_ -match '^    \S' -and
            -not $_.Substring(4).StartsWith('#', [StringComparison]::Ordinal)
        })
        if (($jobDirectLines -join '|') -cne
            '    runs-on: windows-latest|    timeout-minutes: 25|    steps:') {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' trusted job direct keys are not canonical.")
        }
        $beforeGate = if ($workflowPath -ceq '.github/workflows/edge-smoke-build.yml') {
            @'
  smoke-build:
    runs-on: windows-latest
    timeout-minutes: 25
    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          ref: ${{ github.event.pull_request.head.sha || github.sha }}
'@
        } else {
            @'
  validate-runtime:
    runs-on: windows-latest
    timeout-minutes: 25
    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          submodules: recursive
          fetch-depth: 0
'@
        }
        $afterGate = @'
      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          global-json-file: global.json
'@
        $trustedJobPrefix = "$(([string]$beforeGate).TrimEnd("`r", "`n"))`n$requiredBlock`n$(([string]$afterGate).TrimEnd("`r", "`n"))"
        if (-not $jobText.StartsWith($trustedJobPrefix, [StringComparison]::Ordinal) -or
            $jobText.Length -le $trustedJobPrefix.Length -or
            $jobText[$trustedJobPrefix.Length] -cne "`n") {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' trusted checkout/gate/setup prefix is not canonical.")
        }
        if ((Get-OrdinalOccurrenceCount -Text $jobText -Value "    steps:`n") -ne 1 -or
            (Get-OrdinalOccurrenceCount -Text $jobText -Value "      - name: Checkout`n") -ne 1 -or
            (Get-OrdinalOccurrenceCount -Text $jobText -Value 'uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7') -ne 1 -or
            [regex]::IsMatch($jobText, '(?m)^    (?:if|continue-on-error)\s*:')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' trusted job structure is not canonical.")
        }
        if ((Get-OrdinalOccurrenceCount -Text $text -Value $requiredBlock) -ne 1 -or
            (Get-OrdinalOccurrenceCount -Text $text -Value 'EDGE-BASELINE-MIG-TRUSTED-EXECUTOR-V1') -ne 1 -or
            (Get-OrdinalOccurrenceCount -Text $text -Value $script:TrustedWrapperPath) -ne 1) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' must contain exactly one reviewed trusted-executor block.")
        }
        if ($workflowPath -ceq '.github/workflows/edge-smoke-build.yml' -and
            (Get-OrdinalOccurrenceCount `
                -Text $text `
                -Value "ref: `${{ github.event.pull_request.head.sha || github.sha }}") -ne 1) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' must check out the exact candidate SHA validated by the gate.")
        }
        $blockIndex = $jobText.IndexOf($requiredBlock, [StringComparison]::Ordinal)
        $setupIndex = $jobText.IndexOf(
            'uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5',
            [StringComparison]::Ordinal)
        $checkoutIndex = $jobText.LastIndexOf(
            'uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7',
            $blockIndex,
            [StringComparison]::Ordinal)
        if ($checkoutIndex -lt 0 -or $setupIndex -lt 0 -or $blockIndex -gt $setupIndex) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' must run the trusted-executor block after checkout and before setup-dotnet.")
        }
        $betweenCheckoutAndGate = $jobText.Substring(
            $checkoutIndex,
            $blockIndex - $checkoutIndex)
        if ([regex]::IsMatch($betweenCheckoutAndGate, '(?m)^      -(?:\s|$)')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' cannot run another workflow step between checkout and the trusted-executor block.")
        }
    }
}

function Get-FileState {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path -AllowMissing
    if ($null -eq $entry) {
        return [pscustomobject]@{ Mode = $null; Sha256 = $null }
    }

    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    return [pscustomobject]@{
        Mode = $entry.Mode
        Sha256 = Get-Sha256Hex -Bytes $bytes
    }
}

function Get-DiffRecords {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision
    )

    $result = Invoke-GitBytes -Arguments @(
        'diff-tree', '-r', '--no-commit-id', '--name-status', '--no-renames', '-z',
        $BaseRevision, $TargetRevision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $nul = [char[]]@([char]0)
    $diffText = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'DIFF' `
        -Context "git diff $BaseRevision..$TargetRevision"
    $parts = @($diffText.TrimEnd($nul).Split($nul, [StringSplitOptions]::None))
    if ($parts.Count % 2 -ne 0) {
        Stop-MigrationValidation -Code 'DIFF' -Message 'git diff-tree returned an invalid name-status stream.'
    }

    $records = [Collections.Generic.List[object]]::new()
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $parts.Count; $index += 2) {
        $status = $parts[$index]
        $path = $parts[$index + 1]
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'DIFF' -Message "unsupported git status '$status' for '$path'."
        }
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding diff paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path

        $before = Get-FileState -Revision $BaseRevision -Path $path
        $after = Get-FileState -Revision $TargetRevision -Path $path
        if (($status -ceq 'A' -and ($null -ne $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'M' -and ($null -eq $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'D' -and ($null -eq $before.Mode -or $null -ne $after.Mode))) {
            Stop-MigrationValidation -Code 'DIFF' -Message "status '$status' disagrees with '$path' blob presence."
        }

        $records.Add([pscustomobject][ordered]@{
            path = $path
            status = $status
            beforeMode = $before.Mode
            beforeSha256 = $before.Sha256
            afterMode = $after.Mode
            afterSha256 = $after.Sha256
        })
    }

    return @(Get-OrdinalSortedChangeRecords -Values @($records))
}

function Test-IsReceiptStatePath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:ConsumedRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:CancelledRoot, [StringComparison]::Ordinal)
}

function Test-IsTrustImplementationPath {
    param([Parameter(Mandatory)][string]$Path)

    return $script:TrustImplementationPaths -ccontains $Path
}

function Assert-TrustImplementationAssets {
    param([Parameter(Mandatory)][string]$Revision)

    foreach ($path in $script:TrustImplementationPaths) {
        try {
            $entry = Get-GitEntry -Revision $Revision -Path $path
        }
        catch {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' is missing at $Revision.")
        }
        if ($entry.Mode -cne '100644') {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' must use mode 100644 at $Revision.")
        }
    }
}

function Test-IsProtectedPath {
    param([Parameter(Mandatory)][string]$Path)

    $exactPaths = @(
        '.github/CODEOWNERS',
        '.gitattributes',
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'NuGet.Config',
        'IIoT.EdgeClient.slnx',
        '.config/dotnet-tools.json',
        'scripts/CheckEdgeUiSharedBaseline.ps1',
        'scripts/TestEdgePackageVulnerabilities.ps1',
        $script:PolicyPath,
        'scripts/tests/TestEdgeTestGovernanceBehavior.ps1'
    )
    if ($Path -in $exactPaths) { return $true }
    if ($Path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('.github/actions/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('scripts/tests/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/Analyzers/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/Architecture/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/Testing/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/Tests/', [StringComparison]::Ordinal) -or
        $Path.Equals('.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals('packages.lock.json', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/packages.lock.json', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals('NuGet.Config', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/NuGet.Config', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $extension = [IO.Path]::GetExtension($Path)
    return $extension -in @('.csproj', '.fsproj', '.vbproj', '.props', '.targets', '.rsp')
}

function Get-ProtectedManifestDigest {
    param([Parameter(Mandatory)][string]$Revision)

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($path in (Get-RevisionPaths -Revision $Revision)) {
        if (-not (Test-IsProtectedPath -Path $path) -or (Test-IsReceiptStatePath -Path $path)) {
            continue
        }
        $state = Get-FileState -Revision $Revision -Path $path
        $lines.Add("$($state.Mode)`0$($state.Sha256)`0$path`n")
    }

    return Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes(($lines -join '')))
}

function Get-SolutionProjectPaths {
    param([Parameter(Mandatory)][string]$Revision)

    try {
        [xml]$solution = Get-GitBlobText -Revision $Revision -Path 'IIoT.EdgeClient.slnx'
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse IIoT.EdgeClient.slnx at ${Revision}: $($_.Exception.Message)"
    }

    $projects = @($solution.SelectNodes("//*[local-name()='Project']") | ForEach-Object {
        [string]$_.Path
    })
    foreach ($project in $projects) { Assert-SafeRepositoryPath -Path $project }
    return @(Get-OrdinalSortedStrings -Values $projects -Unique)
}

function Get-BaselineState {
    param([Parameter(Mandatory)][string]$Revision)

    $baselineBytes = Get-GitBlobBytes -Revision $Revision -Path $script:BaselinePath
    try {
        $baselineJson = [Text.UTF8Encoding]::new($false, $true).GetString($baselineBytes)
        $baseline = ConvertFrom-StrictJson -Json $baselineJson -Location "$($script:BaselinePath)@$Revision"
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse baseline at ${Revision}: $($_.Exception.Message)"
    }

    if (@($baseline.PSObject.Properties.Name) -cnotcontains 'projects' -or
        $baseline.projects -isnot [Array]) {
        Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects must be a JSON array.'
    }
    $projects = @($baseline.projects)
    $projectPathLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $declarations = [long]0
    $executionTemplates = [long]0
    $projectedCases = [long]0
    $runnerCases = [long]0
    foreach ($project in $projects) {
        if ($null -eq $project -or $project -is [Array] -or $project -is [string] -or
            $project -is [ValueType]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects[] must be a JSON object.'
        }
        $requiredNames = @(
            'projectPath', 'baselineDeclarations', 'baselineExecutionTemplates',
            'baselineProjectedCases', 'baselineRunnerCases')
        $actualNames = @($project.PSObject.Properties.Name)
        foreach ($requiredName in $requiredNames) {
            if ($actualNames -cnotcontains $requiredName) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline project is missing '$requiredName'.")
            }
        }
        if ($project.projectPath -isnot [string]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline projectPath must be a JSON string.'
        }
        $projectPath = [string]$project.projectPath
        Assert-SafeRepositoryPath -Path $projectPath
        if ($projectPathLedger.ContainsKey($projectPath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline contains duplicate or case-colliding project '$projectPath'.")
        }
        $projectPathLedger[$projectPath] = $projectPath
        foreach ($countName in $requiredNames[1..4]) {
            $countValue = $project.$countName
            if (($countValue -isnot [long] -and $countValue -isnot [int]) -or
                [long]$countValue -lt 0) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline $projectPath.$countName must be a non-negative integer.")
            }
        }
        $declarations += [long]$project.baselineDeclarations
        $executionTemplates += [long]$project.baselineExecutionTemplates
        $projectedCases += [long]$project.baselineProjectedCases
        $runnerCases += [long]$project.baselineRunnerCases
    }
    $revisionPaths = @(Get-RevisionPaths -Revision $Revision)
    $solutionProjects = @(Get-SolutionProjectPaths -Revision $Revision)
    $testProjects = @(Get-OrdinalSortedStrings -Values @(
        $projects | ForEach-Object { [string]$_.projectPath }) -Unique)
    $counts = [ordered]@{
        repositoryProjects = $solutionProjects.Count
        testProjects = $testProjects.Count
        testSourceFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('src/Tests/', [StringComparison]::Ordinal) -and
            $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
        }).Count
        declarations = $declarations
        executionTemplates = $executionTemplates
        projectedCases = $projectedCases
        runnerCases = $runnerCases
    }

    return [pscustomobject]@{
        State = [pscustomobject][ordered]@{
            baselineSha256 = Get-Sha256Hex -Bytes $baselineBytes
            protectedManifestSha256 = Get-ProtectedManifestDigest -Revision $Revision
            counts = [pscustomobject]$counts
        }
        RepositoryProjects = $solutionProjects
        TestProjects = $testProjects
    }
}

function Get-ProjectChanges {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    $sourceProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.RepositoryProjects), [StringComparer]::Ordinal)
    $targetProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.RepositoryProjects), [StringComparer]::Ordinal)
    $sourceTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.TestProjects), [StringComparer]::Ordinal)
    $targetTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.TestProjects), [StringComparer]::Ordinal)
    return [pscustomobject][ordered]@{
        added = @(Get-OrdinalSortedStrings -Values @(
            $Target.RepositoryProjects | Where-Object { -not $sourceProjects.Contains($_) }))
        removed = @(Get-OrdinalSortedStrings -Values @(
            $Source.RepositoryProjects | Where-Object { -not $targetProjects.Contains($_) }))
        addedTests = @(Get-OrdinalSortedStrings -Values @(
            $Target.TestProjects | Where-Object { -not $sourceTests.Contains($_) }))
        removedTests = @(Get-OrdinalSortedStrings -Values @(
            $Source.TestProjects | Where-Object { -not $targetTests.Contains($_) }))
    }
}

function Assert-NoDuplicateJsonKeys {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate JSON key '$($property.Name)' at $Location."
            }
            Assert-NoDuplicateJsonKeys -Element $property.Value -Location "$Location.$($property.Name)"
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateJsonKeys -Element $item -Location "$Location[$index]"
            $index++
        }
    }
}

function ConvertFrom-StrictJson {
    param(
        [Parameter(Mandatory)][string]$Json,
        [Parameter(Mandatory)][string]$Location
    )

    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($Json)
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "JSON root at $Location must be an object."
        }
        Assert-NoDuplicateJsonKeys -Element $document.RootElement -Location $Location
        return ConvertFrom-JsonElement -Element $document.RootElement
    }
    catch {
        if ($_.Exception.Message.StartsWith($script:RuleId, [StringComparison]::Ordinal)) { throw }
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid JSON at ${Location}: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function ConvertFrom-JsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-JsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-JsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            $decimal = [decimal]0
            if ($Element.TryGetDecimal([ref]$decimal)) { return $decimal }
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON number '$($Element.GetRawText())'."
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Location
    )

    if ($null -eq $Object -or $Object -is [Array] -or $Object -is [string] -or
        $Object -is [ValueType]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON object."
    }
    $actual = @($Object.PSObject.Properties.Name)
    $expectedSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Expected, [StringComparer]::Ordinal)
    $actualSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$actual, [StringComparer]::Ordinal)
    $missing = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $unknown = @($actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($missing.Count -gt 0 -or $unknown.Count -gt 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "$Location properties mismatch; missing=[$($missing -join ',')] unknown=[$($unknown -join ',')].")
    }
}

function Assert-JsonString {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [string]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON string."
    }
}

function Assert-JsonArray {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [Array]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON array."
    }
}

function Assert-HashOrNull {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][bool]$MustExist,
        [Parameter(Mandatory)][string]$Location
    )

    if ($MustExist) {
        Assert-JsonString -Value $Value -Location $Location
        if ([string]$Value -cnotmatch '^[0-9a-f]{64}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be one lowercase SHA-256."
        }
    }
    elseif ($null -ne $Value) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be null."
    }
}

function Assert-SortedUniqueStrings {
    param(
        [AllowNull()][object]$Values,
        [Parameter(Mandatory)][string]$Location,
        [switch]$Paths
    )

    Assert-JsonArray -Value $Values -Location $Location
    $items = @($Values)
    foreach ($item in $items) { Assert-JsonString -Value $item -Location "$Location[]" }
    if ($Paths) {
        $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($item in $items) {
            $path = [string]$item
            if ($caseLedger.ContainsKey($path)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message (
                    "$Location contains duplicate or case-colliding path '$path'.")
            }
            $caseLedger[$path] = $path
        }
    }
    $expected = @(Get-OrdinalSortedStrings -Values @(
        $items | ForEach-Object { [string]$_ }) -Unique)
    if ($items.Count -ne $expected.Count) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location contains duplicates."
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
        if ([string]$items[$index] -cne $expected[$index]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be ordinal-sorted and unique."
        }
        if ($Paths) { Assert-SafeRepositoryPath -Path ([string]$items[$index]) }
    }
}

function ConvertTo-UtcTimestamp {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        $Value,
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed)) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must use UTC yyyy-MM-ddTHH:mm:ssZ."
    }
    return $parsed.ToUniversalTime()
}

function Assert-StateShape {
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][string]$Location
    )

    Assert-ExactProperties -Object $State -Expected @(
        'baselineSha256', 'protectedManifestSha256', 'counts') -Location $Location
    Assert-HashOrNull -Value $State.baselineSha256 -MustExist $true -Location "$Location.baselineSha256"
    Assert-HashOrNull -Value $State.protectedManifestSha256 -MustExist $true -Location "$Location.protectedManifestSha256"
    Assert-ExactProperties -Object $State.counts -Expected @(
        'repositoryProjects', 'testProjects', 'testSourceFiles', 'declarations',
        'executionTemplates', 'projectedCases', 'runnerCases') -Location "$Location.counts"
    foreach ($name in @(
        'repositoryProjects', 'testProjects', 'testSourceFiles', 'declarations',
        'executionTemplates', 'projectedCases', 'runnerCases')) {
        $value = $State.counts.$name
        if ($value -isnot [long] -and $value -isnot [int]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name must be an integer."
        }
        if ([long]$value -lt 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name cannot be negative."
        }
    }
}

function Assert-TestEvidenceDoesNotDecrease {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    foreach ($name in @(
        'testProjects', 'testSourceFiles', 'declarations', 'executionTemplates',
        'projectedCases', 'runnerCases')) {
        if ([long]$Target.counts.$name -lt [long]$Source.counts.$name) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "target $name cannot decrease during a v1 baseline migration; " +
                "source=$($Source.counts.$name) target=$($Target.counts.$name).")
        }
    }
}

function Assert-ReceiptShape {
    param(
        [Parameter(Mandatory)][object]$Receipt,
        [Parameter(Mandatory)][string]$ExpectedPath,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    Assert-ExactProperties -Object $Receipt -Expected @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
        'ruleIds', 'source', 'target', 'projectChanges', 'changes') -Location 'receipt'
    foreach ($name in @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason')) {
        Assert-JsonString -Value $Receipt.$name -Location "receipt.$name"
    }
    if ([string]$Receipt.schemaVersion -cne $script:ReceiptSchemaVersion -or
        [string]$Receipt.ruleId -cne $script:RuleId) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'unsupported schemaVersion or ruleId.'
    }
    if ([string]$Receipt.migrationId -cnotmatch '^EDGE-BASELINE-MIG-[A-Z0-9][A-Z0-9-]{2,80}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid migrationId '$($Receipt.migrationId)'."
    }
    $expectedPendingPath = "$($script:PendingRoot)$($Receipt.migrationId).json"
    if ($ExpectedPath -cne $expectedPendingPath) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "receipt path '$ExpectedPath' must equal '$expectedPendingPath'.")
    }
    if ([string]$Receipt.issuedAgainstRevision -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'issuedAgainstRevision must be one full lowercase commit SHA.'
    }
    if ([string]$Receipt.owner -cnotin $script:ApprovedOwners -or
        [string]$Receipt.approvedBy -cnotin $script:ApprovedApprovers) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'owner or approvedBy is not in the reviewed registry.'
    }
    $reasonText = [string]$Receipt.reason
    if ($reasonText -cne $reasonText.Trim() -or $reasonText.Length -lt 20 -or $reasonText.Length -gt 500) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'reason must be trimmed and contain 20-500 characters.'
    }

    $issued = ConvertTo-UtcTimestamp -Value ([string]$Receipt.issuedAtUtc) -Location 'receipt.issuedAtUtc'
    $expires = ConvertTo-UtcTimestamp -Value ([string]$Receipt.expiresAtUtc) -Location 'receipt.expiresAtUtc'
    if ($expires -le $issued -or $expires - $issued -gt $script:MaximumReceiptLifetime) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt lifetime must be positive and no longer than 7 days.'
    }
    if (-not $AllowExpired -and ($issued -gt $Now.AddMinutes(5) -or $expires -lt $Now)) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt is not currently valid.'
    }

    Assert-JsonArray -Value $Receipt.ruleIds -Location 'receipt.ruleIds'
    $ruleIdsValue = @($Receipt.ruleIds)
    if ($ruleIdsValue.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'ruleIds cannot be empty.'
    }
    Assert-SortedUniqueStrings -Values $ruleIdsValue -Location 'receipt.ruleIds'
    foreach ($receiptRuleId in $ruleIdsValue) {
        if ([string]$receiptRuleId -cnotmatch '^[A-Z][A-Z0-9-]{2,80}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid governed rule ID '$receiptRuleId'."
        }
        if ([string]$receiptRuleId -cnotin $script:ApprovedGovernedRuleIds) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                "governed rule ID '$receiptRuleId' is not in the reviewed registry.")
        }
    }

    Assert-StateShape -State $Receipt.source -Location 'receipt.source'
    Assert-StateShape -State $Receipt.target -Location 'receipt.target'
    Assert-TestEvidenceDoesNotDecrease -Source $Receipt.source -Target $Receipt.target
    Assert-ExactProperties -Object $Receipt.projectChanges -Expected @(
        'added', 'removed', 'addedTests', 'removedTests') -Location 'receipt.projectChanges'
    foreach ($name in @('added', 'removed', 'addedTests', 'removedTests')) {
        Assert-SortedUniqueStrings -Values $Receipt.projectChanges.$name -Location "receipt.projectChanges.$name" -Paths
    }
    $addedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.added), [StringComparer]::OrdinalIgnoreCase)
    $removedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.removed), [StringComparer]::OrdinalIgnoreCase)
    if (@($addedProjects | Where-Object { $removedProjects.Contains($_) }).Count -ne 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            'projectChanges.added and projectChanges.removed must be disjoint.')
    }
    foreach ($addedTest in @($Receipt.projectChanges.addedTests)) {
        if (-not $addedProjects.Contains([string]$addedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.addedTests must be a subset of projectChanges.added.')
        }
    }
    foreach ($removedTest in @($Receipt.projectChanges.removedTests)) {
        if (-not $removedProjects.Contains([string]$removedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.removedTests must be a subset of projectChanges.removed.')
        }
    }

    Assert-JsonArray -Value $Receipt.changes -Location 'receipt.changes'
    $changes = @($Receipt.changes)
    if ($changes.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes cannot be empty.'
    }
    if ($changes.Count -gt $script:MaximumReceiptChanges) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "changes exceeds the $($script:MaximumReceiptChanges)-path limit.")
    }
    $changePaths = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $hasProtectedChange = $false
    $hasTrustImplementationChange = $false
    $hasOrdinaryChange = $false
    $hasBaselineChange = $false
    $hasPolicyChange = $false
    $lastPath = $null
    foreach ($change in $changes) {
        Assert-ExactProperties -Object $change -Expected @(
            'path', 'status', 'beforeMode', 'beforeSha256', 'afterMode', 'afterSha256') -Location 'receipt.changes[]'
        Assert-JsonString -Value $change.path -Location 'receipt.changes[].path'
        Assert-JsonString -Value $change.status -Location "receipt.changes[$($change.path)].status"
        $path = [string]$change.path
        Assert-SafeRepositoryPath -Path $path
        if ($null -ne $lastPath -and [StringComparer]::Ordinal.Compare($lastPath, $path) -ge 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes must be ordinal-sorted by unique path.'
        }
        $lastPath = $path
        if ($changePaths.ContainsKey($path)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate or case-colliding change path '$path'."
        }
        $changePaths[$path] = $path
        if (Test-IsReceiptStatePath -Path $path) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'pending/consumed receipt moves are implicit and cannot appear in changes.'
        }
        if (Test-IsTrustImplementationPath -Path $path) {
            $hasTrustImplementationChange = $true
        } else {
            $hasOrdinaryChange = $true
        }
        if (Test-IsProtectedPath -Path $path) { $hasProtectedChange = $true }
        if ($path -ceq $script:BaselinePath) { $hasBaselineChange = $true }
        if ($path -ceq $script:PolicyPath) { $hasPolicyChange = $true }

        $status = [string]$change.status
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid change status '$status' for '$path'."
        }
        $beforeExists = $status -cin @('M', 'D')
        $afterExists = $status -cin @('A', 'M')
        if ((Test-IsTrustImplementationPath -Path $path) -and
            ($status -ceq 'D' -or [string]$change.afterMode -cne '100644')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' must remain present with mode 100644.")
        }
        if ($beforeExists) { Assert-JsonString -Value $change.beforeMode -Location "change[$path].beforeMode" }
        if ($afterExists) { Assert-JsonString -Value $change.afterMode -Location "change[$path].afterMode" }
        if ($beforeExists -and [string]$change.beforeMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid beforeMode for '$path'."
        }
        if (-not $beforeExists -and $null -ne $change.beforeMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "beforeMode for added '$path' must be null."
        }
        if ($afterExists -and [string]$change.afterMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid afterMode for '$path'."
        }
        if (-not $afterExists -and $null -ne $change.afterMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "afterMode for deleted '$path' must be null."
        }
        Assert-HashOrNull -Value $change.beforeSha256 -MustExist $beforeExists -Location "change[$path].beforeSha256"
        Assert-HashOrNull -Value $change.afterSha256 -MustExist $afterExists -Location "change[$path].afterSha256"
    }
    if ($hasBaselineChange -and $hasPolicyChange) {
        Stop-MigrationValidation -Code 'POLICY' -Message (
            'baseline and governance policy must be migrated in separate receipts.')
    }
    if (-not $hasProtectedChange) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'a migration receipt must govern at least one protected asset change.'
    }
    $isTrustUpgrade = $ruleIdsValue.Count -eq 1 -and
        [string]$ruleIdsValue[0] -ceq $script:TrustUpgradeRuleId
    if ($hasTrustImplementationChange -and (-not $isTrustUpgrade -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'trusted implementation changes require one isolated EDGE-BASELINE-TRUST-UPGRADE-001 receipt.')
    }
    if ($isTrustUpgrade -and (-not $hasTrustImplementationChange -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'EDGE-BASELINE-TRUST-UPGRADE-001 may change only trusted implementation assets.')
    }
}

function Assert-ObjectJsonEqual {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory)][string]$Location
    )

    $expectedJson = ConvertTo-CanonicalJson -Value $Expected
    $actualJson = ConvertTo-CanonicalJson -Value $Actual
    if ($expectedJson -cne $actualJson) {
        Stop-MigrationValidation -Code 'MISMATCH' -Message (
            "$Location differs from the receipt. expected=$expectedJson actual=$actualJson")
    }
}

function ConvertTo-CanonicalJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $properties = [Collections.Generic.List[object]]::new()
            foreach ($property in $Element.EnumerateObject()) {
                $properties.Add([pscustomobject]@{
                    Name = $property.Name
                    Value = $property.Value.Clone()
                })
            }
            $comparer = [Collections.Generic.Comparer[object]]::Create(
                [Comparison[object]]{
                    param($left, $right)
                    return [StringComparer]::Ordinal.Compare([string]$left.Name, [string]$right.Name)
                })
            $properties.Sort($comparer)
            $members = @($properties | ForEach-Object {
                $nameJson = [Text.Json.JsonSerializer]::Serialize(
                    [string]$_.Name,
                    $script:JsonSerializerOptions)
                "$nameJson`:$(ConvertTo-CanonicalJsonElement -Element $_.Value)"
            })
            return "{$($members -join ',')}"
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = @($Element.EnumerateArray() | ForEach-Object {
                ConvertTo-CanonicalJsonElement -Element $_
            })
            return "[$($items -join ',')]"
        }
        ([Text.Json.JsonValueKind]::String) {
            return [Text.Json.JsonSerializer]::Serialize(
                $Element.GetString(),
                $script:JsonSerializerOptions)
        }
        ([Text.Json.JsonValueKind]::Number) { return $Element.GetRawText() }
        ([Text.Json.JsonValueKind]::True) { return 'true' }
        ([Text.Json.JsonValueKind]::False) { return 'false' }
        ([Text.Json.JsonValueKind]::Null) { return 'null' }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function ConvertTo-CanonicalJson {
    param([AllowNull()][object]$Value)

    $json = ConvertTo-Json -InputObject $Value -Depth 100 -Compress
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        return ConvertTo-CanonicalJsonElement -Element $document.RootElement
    }
    finally {
        $document.Dispose()
    }
}

function Get-ReceiptAtRevision {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    $sizeText = (Invoke-GitText -Arguments @('cat-file', '-s', $entry.ObjectId)).Text.Trim()
    $blobSize = [long]0
    if (-not [long]::TryParse($sizeText, [ref]$blobSize) -or $blobSize -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt exceeds $($script:MaximumReceiptBytes) bytes: $Path."
    }
    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    try {
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt is not valid UTF-8: $Path."
    }
    $receipt = ConvertFrom-StrictJson -Json $json -Location $Path
    Assert-ReceiptShape -Receipt $receipt -ExpectedPath $Path -Now $Now -AllowExpired:$AllowExpired
    return [pscustomobject]@{ Receipt = $receipt; Bytes = $bytes }
}

function Get-PendingReceiptPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $paths = @((Get-RevisionPaths -Revision $Revision) | Where-Object {
        $_.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -and
        $_.EndsWith('.json', [StringComparison]::Ordinal)
    })
    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Assert-MigrationIdNotFinalized {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$MigrationId
    )

    $consumedPath = "$($script:ConsumedRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $consumedPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already consumed."
    }
    $cancelledPath = "$($script:CancelledRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $cancelledPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already cancelled."
    }
}

function Test-AuthorizationOnlyTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    if ($Diff.Count -ne 1 -or [string]$Diff[0].status -cne 'A' -or
        -not ([string]$Diff[0].path).StartsWith($script:PendingRoot, [StringComparison]::Ordinal)) {
        return $false
    }
    if ([string]$Diff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'pending receipt must use git mode 100644.'
    }

    $targetParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($targetParents.Count -ne 2 -or $targetParents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'authorization must be one single-parent commit directly after its issued base.'
    }

    if (@(Get-PendingReceiptPaths -Revision $BaseRevision).Count -ne 0) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'cannot authorize a second pending receipt.'
    }
    $path = [string]$Diff[0].path
    $loaded = Get-ReceiptAtRevision -Revision $TargetRevision -Path $path -Now $Now
    $receipt = $loaded.Receipt
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId ([string]$receipt.migrationId)
    if ([string]$receipt.issuedAgainstRevision -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'issuedAgainstRevision must equal the authorization commit parent.'
    }
    $baseState = Get-BaselineState -Revision $BaseRevision
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $baseState.State -Location 'authorization source state'
    Write-Host "Edge baseline migration authorization recorded: $($receipt.migrationId)"
    return $true
}

function Test-CancellationTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1 -or $Diff.Count -ne 2) { return $false }
    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CANCEL' `
        -Context 'receipt cancellation'

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now -AllowExpired
    $migrationIdValue = [string]$loaded.Receipt.migrationId
    $cancelledPath = "$($script:CancelledRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue
    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $cancelledAdd = @($Diff | Where-Object { $_.path -ceq $cancelledPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $cancelledAdd.Count -ne 1) { return $false }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$cancelledAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'pending and cancelled receipts must use git mode 100644.'
    }
    $cancelledBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $cancelledPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $cancelledBytes)) {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'cancelled receipt blob differs from pending receipt.'
    }

    Write-Host "Edge baseline migration receipt cancelled: $migrationIdValue"
    return $true
}

function Assert-ConsumptionTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CONSUME' `
        -Context 'receipt consumption'

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message "trusted base must contain exactly one pending receipt; found $($pendingPaths.Count)."
    }

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now
    $receipt = $loaded.Receipt
    $migrationIdValue = [string]$receipt.migrationId
    $consumedPath = "$($script:ConsumedRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue

    $baseParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $BaseRevision)).Text.Trim().Split(' ')
    if ($baseParents.Count -ne 2 -or $baseParents[1] -cne [string]$receipt.issuedAgainstRevision) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'trusted base must be the isolated authorization commit for this receipt.'
    }
    $authorizationDiff = @(Get-DiffRecords -BaseRevision $baseParents[1] -TargetRevision $BaseRevision)
    if ($authorizationDiff.Count -ne 1 -or
        [string]$authorizationDiff[0].status -cne 'A' -or
        [string]$authorizationDiff[0].path -cne $pendingPath -or
        [string]$authorizationDiff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'authorization commit must add only the pending receipt.'
    }

    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $consumedAdd = @($Diff | Where-Object { $_.path -ceq $consumedPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $consumedAdd.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'candidate must atomically move pending receipt to consumed.'
    }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$consumedAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'pending and consumed receipt must use git mode 100644.'
    }
    $consumedBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $consumedPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $consumedBytes)) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'consumed receipt blob differs from pending receipt.'
    }

    $actualChanges = @($Diff | Where-Object {
        $_.path -cne $pendingPath -and $_.path -cne $consumedPath
    })
    Assert-ObjectJsonEqual -Expected @($receipt.changes) -Actual $actualChanges -Location 'candidate changes'
    Assert-TargetWorkflowTrustClosure -Revision $TargetRevision

    $sourceState = Get-BaselineState -Revision ([string]$receipt.issuedAgainstRevision)
    $targetState = Get-BaselineState -Revision $TargetRevision
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $sourceState.State -Location 'source state'
    Assert-ObjectJsonEqual -Expected $receipt.target -Actual $targetState.State -Location 'target state'
    $projectChanges = Get-ProjectChanges -Source $sourceState -Target $targetState
    Assert-ObjectJsonEqual -Expected $receipt.projectChanges -Actual $projectChanges -Location 'project changes'

    Write-Host "Edge baseline migration receipt consumed: $migrationIdValue"
}

function New-ReceiptDescription {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Issued,
        [Parameter(Mandatory)][DateTimeOffset]$Expires
    )

    if ([string]::IsNullOrWhiteSpace($MigrationId) -or
        [string]::IsNullOrWhiteSpace($RuleIdsCsv) -or
        [string]::IsNullOrWhiteSpace($Owner) -or
        [string]::IsNullOrWhiteSpace($ApprovedBy) -or
        [string]::IsNullOrWhiteSpace($Reason)) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            'Describe requires MigrationId, RuleIdsCsv, Owner, ApprovedBy and Reason.')
    }
    $describedRuleIds = @($RuleIdsCsv.Split(',', [StringSplitOptions]::None))
    if ($describedRuleIds.Count -eq 0 -or @($describedRuleIds | Where-Object {
        [string]::IsNullOrWhiteSpace($_) -or $_ -cne $_.Trim()
    }).Count -ne 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'RuleIdsCsv must be a comma-separated list without whitespace or empty items.'
    }
    $normalizedRuleIds = @(Get-OrdinalSortedStrings -Values $describedRuleIds -Unique)
    $isTrustUpgradeRequest = $normalizedRuleIds.Count -eq 1 -and
        $normalizedRuleIds[0] -ceq $script:TrustUpgradeRuleId

    $diff = @(Get-DiffRecords -BaseRevision $BaseRevision -TargetRevision $TargetRevision)
    if ($diff.Count -eq 0 -or @($diff | Where-Object { Test-IsProtectedPath -Path $_.path }).Count -eq 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'candidate must change at least one protected asset.'
    }
    foreach ($change in $diff) {
        if (Test-IsReceiptStatePath -Path $change.path) {
            Stop-MigrationValidation -Code 'DESCRIBE' -Message "candidate contains forbidden receipt-state change '$($change.path)'."
        }
        $isTrustPath = Test-IsTrustImplementationPath -Path $change.path
        if ($isTrustPath -ne $isTrustUpgradeRequest) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                'trust upgrades must be isolated from all ordinary migration changes.')
        }
    }

    $source = Get-BaselineState -Revision $BaseRevision
    $target = Get-BaselineState -Revision $TargetRevision
    return [pscustomobject][ordered]@{
        schemaVersion = $script:ReceiptSchemaVersion
        ruleId = $script:RuleId
        migrationId = $MigrationId
        issuedAgainstRevision = $BaseRevision
        issuedAtUtc = $Issued.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        expiresAtUtc = $Expires.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        owner = $Owner
        approvedBy = $ApprovedBy
        reason = $Reason.Trim()
        ruleIds = $normalizedRuleIds
        source = $source.State
        target = $target.State
        projectChanges = Get-ProjectChanges -Source $source -Target $target
        changes = $diff
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
}
if ($RemainingArguments.Count -ne 0) {
    Stop-MigrationValidation -Code 'PARAMETER' -Message (
        "unexpected positional arguments: $($RemainingArguments -join ', ').")
}
$script:RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot '.git'))) {
    Stop-MigrationValidation -Code 'ROOT' -Message "RepositoryRoot is not a Git worktree: $script:RepositoryRoot."
}

if ($TrustedBaseRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $CandidateRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $TrustedBaseRevision -match '^0{40}$' -or
    $CandidateRevision -match '^0{40}$') {
    Stop-MigrationValidation -Code 'REVISION' -Message (
        'TrustedBaseRevision and CandidateRevision must be explicit non-zero full 40-character SHAs.')
}

$trustedBase = Resolve-Commit -Revision $TrustedBaseRevision -Name 'TrustedBaseRevision'
$candidate = Resolve-Commit -Revision $CandidateRevision -Name 'CandidateRevision'
$head = Resolve-Commit -Revision 'HEAD' -Name 'HEAD'
$now = [DateTimeOffset]::UtcNow

if ($Mode -eq 'Describe') {
    if ($AnchorRelationship -ne 'BaseAncestorOfHead') {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'Describe only supports BaseAncestorOfHead.'
    }
    Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'Describe'
    Assert-TrustImplementationAssets -Revision $trustedBase
    Assert-TrustImplementationAssets -Revision $candidate
    $issued = if ([string]::IsNullOrWhiteSpace($IssuedAtUtc)) {
        $now
    } else {
        ConvertTo-UtcTimestamp -Value $IssuedAtUtc -Location 'IssuedAtUtc'
    }
    $expires = if ([string]::IsNullOrWhiteSpace($ExpiresAtUtc)) {
        $issued.AddDays(7)
    } else {
        ConvertTo-UtcTimestamp -Value $ExpiresAtUtc -Location 'ExpiresAtUtc'
    }
    $description = New-ReceiptDescription -BaseRevision $trustedBase -TargetRevision $candidate -Issued $issued -Expires $expires
    Assert-ReceiptShape `
        -Receipt $description `
        -ExpectedPath "$($script:PendingRoot)$MigrationId.json" `
        -Now $now
    $json = $description | ConvertTo-Json -Depth 100
    if ([Text.Encoding]::UTF8.GetByteCount("$json`n") -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "generated receipt exceeds the $($script:MaximumReceiptBytes)-byte limit.")
    }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $json
    } else {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        $outputDirectory = Split-Path $resolvedOutput -Parent
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
        }
        [IO.File]::WriteAllText($resolvedOutput, "$json`n", [Text.UTF8Encoding]::new($false))
        Write-Host "Wrote Edge migration receipt description: $resolvedOutput"
    }
    exit 0
}

if ($candidate -cne $head) {
    Stop-MigrationValidation -Code 'REVISION' -Message 'Validate requires CandidateRevision to equal checked-out HEAD.'
}

if ($AnchorRelationship -eq 'HeadAncestorOfBase') {
    Assert-Ancestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-FirstParentAncestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-TrustImplementationAssets -Revision $trustedBase
    $reverseDiff = @(Get-DiffRecords -BaseRevision $candidate -TargetRevision $trustedBase)
    $protectedReverseDiff = @($reverseDiff | Where-Object { Test-IsProtectedPath -Path $_.path })
    if ($protectedReverseDiff.Count -ne 0) {
        Stop-MigrationValidation -Code 'RELEASE' -Message (
            "release commit differs from trusted main in $($protectedReverseDiff.Count) protected asset(s).")
    }
    Write-Host "Edge trusted release anchor passed: head=$candidate trusted=$trustedBase"
    exit 0
}

Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'candidate validation'
Assert-TrustImplementationAssets -Revision $trustedBase
Assert-TrustImplementationAssets -Revision $candidate
$diff = @(Get-DiffRecords -BaseRevision $trustedBase -TargetRevision $candidate)
if (Test-AuthorizationOnlyTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
    exit 0
}

$pendingAtBase = @(Get-PendingReceiptPaths -Revision $trustedBase)
if ($pendingAtBase.Count -gt 0) {
    if (Test-CancellationTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
        exit 0
    }
    Assert-ConsumptionTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now
    exit 0
}
$protectedDiff = @($diff | Where-Object { Test-IsProtectedPath -Path $_.path })
if ($protectedDiff.Count -ne 0) {
    Stop-MigrationValidation -Code 'IMMUTABLE' -Message (
        "protected assets changed without one pending receipt: $(@($protectedDiff.path) -join ', ').")
}

Write-Host "Edge protected baseline transition is immutable: base=$trustedBase candidate=$candidate"
