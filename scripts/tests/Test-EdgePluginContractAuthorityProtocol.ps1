[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$LedgerPath = '.artifacts/phase0-ledger-dev-3.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else { $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }

$modulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
Import-Module $modulePath -Force
Assert-EdgeAuthorityGitEnvironment
$protocolPowerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)
$protocolGitCommand = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
$protocolGitPath = Resolve-EdgeFixedExecutable ([string]$protocolGitCommand.Source)
$requestSchemaPath = Join-Path $RepositoryRoot 'eng/edge-plugin-contract-authority-request.schema.json'
$descriptorSchemaPath = Join-Path $RepositoryRoot 'eng/edge-plugin-contract-authority-descriptor.schema.json'
$receiptSchemaPath = Join-Path $RepositoryRoot 'eng/edge-plugin-contract-authority-receipt.schema.json'
$formalResultSchemaPath = Join-Path $RepositoryRoot `
    'eng/edge-plugin-contract-formal-validation-result.schema.json'
$ledgerSchemaPath = Join-Path $RepositoryRoot 'eng/edge-plugin-contract-ledger.schema.json'
$ledgerFullPath = Resolve-EdgeRepositoryPath $RepositoryRoot $LedgerPath
$receiptRelativePath = ".artifacts/test-temp/edge-authority-protocol-$([Guid]::NewGuid().ToString('N')).json"
$receiptFullPath = Resolve-EdgeRepositoryPath $RepositoryRoot $receiptRelativePath
[void](New-Item -ItemType Directory -Path (Split-Path $receiptFullPath -Parent) -Force)

if (-not (Test-Path -LiteralPath $ledgerFullPath -PathType Leaf)) {
    throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 protocol fixture ledger is missing: $LedgerPath."
}
$ledger = Read-EdgeGeneratorLedger -Path $ledgerFullPath -SchemaPath $ledgerSchemaPath -Name 'protocol-fixture'
$head = (& $protocolGitPath -C $RepositoryRoot rev-parse HEAD 2>&1 | Out-String).Trim()
$tree = (& $protocolGitPath -C $RepositoryRoot rev-parse 'HEAD^{tree}' 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 cannot resolve protocol fixture HEAD/tree.' }

$curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
$privateKey = [Security.Cryptography.ECDsa]::Create($curve)
$publicBytes = $privateKey.ExportSubjectPublicKeyInfo()
$publicKeyBase64 = [Convert]::ToBase64String($publicBytes)
$publicKeySha256 = Get-EdgeSha256Bytes $publicBytes
$challengeBase64 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$runId = [Guid]::NewGuid().ToString('N')
$dirtyManifestSha256 = ('a' * 64)
$issued = [DateTime]::UtcNow
$basePayload = [pscustomobject][ordered]@{
    schemaVersion = 1
    mode = 'development-snapshot'
    formal = $false
    runId = $runId
    challengeBase64 = $challengeBase64
    issuedUtc = $issued.ToString('O')
    expiresUtc = $issued.AddMinutes(20).ToString('O')
    sourceRepositoryRoot = $RepositoryRoot
    authorityRepositoryRoot = $RepositoryRoot
    authorityHead = $head
    authorityTree = $tree
    formalFinalHead = ''
    formalFinalTree = ''
    sourceBaseHead = [string]$ledger.sourceState.head
    sourceBaseTree = [string]$ledger.sourceState.tree
    sourceDirtyManifestSha256 = $dirtyManifestSha256
    ephemeralSnapshotHead = $head
    ephemeralSnapshotTree = $tree
    implementationHead = [string]$ledger.sourceState.head
    implementationTree = [string]$ledger.sourceState.tree
    ledgerPath = $LedgerPath
    ledgerSha256 = Get-EdgeSha256File $ledgerFullPath
    ledgerPayloadSha256 = [string]$ledger.integrity.payloadSha256
    analyzedInputsSha256 = [string]$ledger.integrity.analyzedInputsSha256
    authorityCodeSha256 = Get-EdgeAuthorityCodeDigest $RepositoryRoot
    publicKeySha256 = $publicKeySha256
    authorityCount = 1
    replayCount = 1
    factGroups = @(New-EdgeCandidateFactGroups -Ledger $ledger)
}

function Copy-ProtocolValue {
    param([Parameter(Mandatory)]$Value)
    return ConvertFrom-EdgeJsonText (ConvertTo-EdgeCanonicalJson $Value)
}

function New-ProtocolReceiptUnchecked {
    param(
        [Parameter(Mandatory)]$Payload,
        [Parameter(Mandatory)][Security.Cryptography.ECDsa]$SigningKey
    )
    $signature = $SigningKey.SignData(
        (ConvertTo-EdgeCanonicalBytes $Payload),
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        signatureAlgorithm = 'ECDSA-P256-SHA256-IEEE-P1363'
        payload = $Payload
        signatureBase64 = [Convert]::ToBase64String($signature)
    }
}

function Write-ProtocolReceipt {
    param(
        [AllowNull()][scriptblock]$MutatePayload,
        [AllowNull()][scriptblock]$MutateReceipt,
        [switch]$Unchecked
    )
    $payload = Copy-ProtocolValue $basePayload
    if ($null -ne $MutatePayload) { & $MutatePayload $payload }
    $receipt = if ($Unchecked) {
        New-ProtocolReceiptUnchecked -Payload $payload -SigningKey $privateKey
    }
    else { New-EdgeSignedAuthorityReceipt -Payload $payload -PrivateKey $privateKey }
    if ($null -ne $MutateReceipt) { & $MutateReceipt $receipt }
    [IO.File]::WriteAllBytes($receiptFullPath, (ConvertTo-EdgeCanonicalBytes $receipt))
    return $receipt
}

function Get-ProtocolReceiptArguments {
    param([AllowNull()][Collections.IDictionary]$Overrides)
    $arguments = [ordered]@{
        RepositoryRoot = $RepositoryRoot
        LedgerPath = $LedgerPath
        ReceiptPath = $receiptFullPath
        PublicKeySpkiBase64 = $publicKeyBase64
        ExpectedRunId = $runId
        ExpectedChallengeBase64 = $challengeBase64
        ExpectedSourceRepositoryRoot = $RepositoryRoot
        ExpectedAuthorityHead = $head
        ExpectedAuthorityTree = $tree
        ExpectedFormalFinalHead = ''
        ExpectedFormalFinalTree = ''
        ExpectedSourceBaseHead = [string]$ledger.sourceState.head
        ExpectedSourceBaseTree = [string]$ledger.sourceState.tree
        ExpectedSourceDirtyManifestSha256 = $dirtyManifestSha256
        ExpectedEphemeralSnapshotHead = $head
        ExpectedEphemeralSnapshotTree = $tree
        ExpectedImplementationHead = [string]$ledger.sourceState.head
        ExpectedImplementationTree = [string]$ledger.sourceState.tree
    }
    if ($null -ne $Overrides) {
        foreach ($name in $Overrides.Keys) { $arguments[[string]$name] = $Overrides[$name] }
    }
    return $arguments
}

function Invoke-ProtocolReceiptValidation {
    $arguments = Get-ProtocolReceiptArguments
    return Assert-EdgeAuthorityReceipt @arguments
}

function Assert-ProtocolFormalTransitionFixture {
    param([Parameter(Mandatory)]$Request)
    if ([int]$Request.schemaVersion -ne 1 -or
        [string]$Request.mode -cne 'formal-clean' -or
        [string]$Request.ledgerPath -cne
            'eng/baselines/edge-plugin-contract-ledger.json' -or
        [string]$Request.authorityHead -cne [string]$Request.formalFinalHead -or
        [string]$Request.authorityTree -cne [string]$Request.formalFinalTree -or
        [string]$Request.sourceBaseHead -cne [string]$Request.formalFinalHead -or
        [string]$Request.sourceBaseTree -cne [string]$Request.formalFinalTree -or
        [string]$Request.implementationHead -ceq [string]$Request.formalFinalHead -or
        [string]$Request.implementationTree -ceq [string]$Request.formalFinalTree -or
        -not [string]::IsNullOrEmpty([string]$Request.sourceDirtyManifestSha256) -or
        -not [string]::IsNullOrEmpty([string]$Request.ephemeralSnapshotHead) -or
        -not [string]::IsNullOrEmpty([string]$Request.ephemeralSnapshotTree)) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 formal synthetic I-to-E transition is not clean and parent-bound.'
    }
}

$negativeCount = 0
function Assert-ProtocolRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedCode,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    $matched = $false
    try { & $Action }
    catch {
        if ($_.Exception.Message.Contains($ExpectedCode, [StringComparison]::Ordinal)) { $matched = $true }
        else { throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 negative '$Name' failed for the wrong reason: $($_.Exception.Message)" }
    }
    if (-not $matched) { throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 negative '$Name' was accepted." }
    $script:negativeCount++
}

$staticGuardModulePath = Join-Path $PSScriptRoot 'EdgePluginContractStaticGuard.psm1'
Import-Module $staticGuardModulePath -Force

function Get-ProtocolExactStaticMutation {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Replacement,
        [Parameter(Mandatory)][string]$Name
    )
    $count = [Text.RegularExpressions.Regex]::Matches(
        $Source, [Text.RegularExpressions.Regex]::Escape($Needle),
        [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count
    if ($count -ne 1) {
        throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 static mutation '$Name' needle count is $count, expected exactly one."
    }
    $mutated = $Source.Replace($Needle, $Replacement)
    if ([string]::Equals($mutated, $Source, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 static mutation '$Name' was a no-op."
    }
    return $mutated
}

function Assert-ProtocolStaticMutationRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action,
        [AllowEmptyString()][string]$ExpectedMessage = ''
    )
    try { & $Action }
    catch {
        if (-not [string]::IsNullOrEmpty($ExpectedMessage)) {
            if ([string]$_.Exception.Message -ceq $ExpectedMessage) {
                return
            }
            throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 static mutation '$Name' was rejected by an unexpected semantic owner: $($_.Exception.Message)"
        }
        if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-STATIC-001', [StringComparison]::Ordinal)) {
            return
        }
        throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 static mutation '$Name' failed for the wrong reason: $($_.Exception.Message)"
    }
    throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 static mutation '$Name' was accepted."
}

function Invoke-ProtocolBoundedPowerShell {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [AllowNull()][byte[]]$InputBytes,
        [AllowNull()][Collections.IDictionary]$Environment,
        [ValidateRange(1, 60)][int]$TimeoutSeconds = 20
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:protocolPowerShellPath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $InputBytes
    foreach ($argument in @('-NoLogo', '-NoProfile') + $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    if ($null -ne $Environment) {
        foreach ($name in $Environment.Keys) {
            $startInfo.Environment[[string]$name] = [string]$Environment[$name]
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        if (-not $process.Start()) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bounded production-entry fixture did not start.'
        }
        $started = $true
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardOutput.BaseStream, 4194304)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardError.BaseStream, 4194304)
        if ($null -ne $InputBytes) {
            $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
            $process.StandardInput.Close()
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(50)) {
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted -or
                [DateTimeOffset]::UtcNow -ge $deadline) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bounded production-entry fixture exceeded output/deadline limits.'
            }
        }
        $capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline `
            'EDGE-SPLIT-AUTHORITY-PROTOCOL-001'
        return [pscustomobject][ordered]@{
            exitCode = [int]$process.ExitCode
            stdoutBytes = [byte[]]$capture.stdoutBytes
            stderrBytes = [byte[]]$capture.stderrBytes
            stdout = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stdoutBytes)
            stderr = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stderrBytes)
        }
    }
    finally {
        if ($started -and -not $process.HasExited) {
            try { $process.Kill($true) } catch { }
            [void]$process.WaitForExit(30000)
        }
        $process.Dispose()
    }
}

function Invoke-ProtocolCoordinatorParentBindingFixture {
    $runId = [Guid]::NewGuid().ToString('N')
    $outerRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-parent-binding-$runId"
    $emptyPathRoot = Join-Path $outerRoot "empty-path-$runId"
    $coordinatorRoot = Join-Path $outerRoot "coordinator-$runId"
    $validatorRoot = Join-Path $coordinatorRoot "validator-$runId"
    $replayRoot = Join-Path $coordinatorRoot "replay-$runId"
    $markerPath = Join-Path $outerRoot '.edge-development-authority-run.json'
    $pinnedPath = Get-EdgeAuthorityPinnedPath $script:protocolGitPath
    $marker = [pscustomobject][ordered]@{
        schemaVersion = 2
        runId = $runId
        sourceRepositoryRoot = $script:RepositoryRoot
        authorityRepositoryRoot = $script:RepositoryRoot
        snapshotRoot = $script:RepositoryRoot
        coordinatorRunRoot = $coordinatorRoot
        validatorWorktreePath = $validatorRoot
        replayWorktreePath = $replayRoot
        fixedGitExecutablePath = $script:protocolGitPath
        pinnedPathSha256 = Get-EdgeSha256Text $pinnedPath
    }
    try {
        [void](New-Item -ItemType Directory -Path $outerRoot)
        [void](New-Item -ItemType Directory -Path $emptyPathRoot)
        [IO.File]::WriteAllBytes($markerPath, (ConvertTo-EdgeCanonicalBytes $marker))
        $environment = New-EdgeAuthorityCoordinatorParentEnvironment `
            -OuterMarker $marker -ParentMarkerPath $markerPath `
            -FixedGitExecutablePath $script:protocolGitPath -PinnedPath $pinnedPath
        # Deliberately remove Git/dotnet from the inherited startup PATH. pwsh will
        # still prepend PSHOME; the production coordinator must restore the bound PATH.
        $environment.PATH = $emptyPathRoot
        $result = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot `
                        'Invoke-EdgePluginContractAuthorityCoordinator.ps1')) `
            -WorkingDirectory $script:RepositoryRoot `
            -InputBytes ([Text.UTF8Encoding]::new($false).GetBytes('{}')) `
            -Environment $environment -TimeoutSeconds 20
        if ($result.exitCode -eq 0 -or
            -not $result.stderr.Contains('EDGE-SPLIT-AUTHORITY-REQUEST-001', [StringComparison]::Ordinal) -or
            $result.stderr.Contains('EDGE-SPLIT-AUTHORITY-PARENT-BINDING', [StringComparison]::Ordinal) -or
            $result.stderr.Contains('EDGE-SPLIT-AUTHORITY-GIT-ENV', [StringComparison]::Ordinal) -or
            $result.stdoutBytes.Length -ne 0 -or
            (Test-Path -LiteralPath $coordinatorRoot)) {
            throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 real coordinator did not pass parent binding before its expected request rejection.`n$($result.stderr)"
        }

        $authorityHead = Invoke-ProtocolGit $script:RepositoryRoot @('rev-parse', 'HEAD')
        $authorityTree = Invoke-ProtocolGit $script:RepositoryRoot @('rev-parse', 'HEAD^{tree}')
        $missingLedgerPath = ".artifacts/edge-authority-$runId/missing-ledger.json"
        $missingReceiptPath = ".artifacts/edge-authority-$runId/missing-receipt.json"
        if ((Test-Path -LiteralPath (Join-Path $script:RepositoryRoot $missingLedgerPath)) -or
            (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot $missingReceiptPath))) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 valid-request cleanup fixture paths were not absent.'
        }
        $validRequest = [pscustomobject][ordered]@{
            schemaVersion = 1
            mode = 'development-snapshot'
            runId = $runId
            challengeBase64 = [Convert]::ToBase64String([byte[]]::new(32))
            sourceRepositoryRoot = $script:RepositoryRoot
            authorityRepositoryRoot = $script:RepositoryRoot
            authorityHead = $authorityHead
            authorityTree = $authorityTree
            formalFinalHead = ''
            formalFinalTree = ''
            sourceBaseHead = $authorityHead
            sourceBaseTree = $authorityTree
            sourceDirtyManifestSha256 = ('1' * 64)
            ephemeralSnapshotHead = $authorityHead
            ephemeralSnapshotTree = $authorityTree
            implementationHead = $authorityHead
            implementationTree = $authorityTree
            ledgerPath = $missingLedgerPath
            receiptPath = $missingReceiptPath
            runRoot = $coordinatorRoot
            validatorWorktreePath = $validatorRoot
            replayWorktreePath = $replayRoot
            pluginProject = 'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj'
            configuration = 'Release'
            currentBatch = 'EDGE-SPLIT-000'
            viewIdsAssemblyPath = 'src/Presentation/IIoT.Edge.Presentation.Navigation/bin/Release/net10.0/IIoT.Edge.Presentation.Navigation.dll'
            viewIdsTypeName = 'IIoT.Edge.Presentation.Navigation.PluginSystem.StandardModuleViewIds'
            timeoutSeconds = 60
        }
        $validResult = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot `
                        'Invoke-EdgePluginContractAuthorityCoordinator.ps1')) `
            -WorkingDirectory $script:RepositoryRoot `
            -InputBytes (ConvertTo-EdgeCanonicalBytes $validRequest) `
            -Environment $environment -TimeoutSeconds 20
        $expectedMainError =
            'EDGE-SPLIT-AUTHORITY-COORDINATOR-001 EDGE-SPLIT-AUTHORITY-LEDGER-001 candidate ledger is missing.'
        $validErrorLines = @($validResult.stderr -split '\r?\n' | Where-Object { $_.Length -gt 0 })
        if ($validResult.exitCode -eq 0 -or
            $validResult.stdoutBytes.Length -ne 0 -or
            $validErrorLines.Count -ne 1 -or
            $validErrorLines[0] -cne $expectedMainError -or
            $validResult.stderr.Contains('EDGE-SPLIT-AUTHORITY-CLEANUP-001', [StringComparison]::Ordinal) -or
            (Test-Path -LiteralPath $coordinatorRoot)) {
            throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 real coordinator did not preserve its primary failure while cleaning a valid request.`n$($validResult.stderr)"
        }
        return $result
    }
    finally {
        if (Test-Path -LiteralPath $outerRoot -PathType Container) {
            Remove-Item -LiteralPath $outerRoot -Recurse -Force
        }
    }
}

function Invoke-ProtocolBoundChildEntrypointFixtures {
    $id = [Guid]::NewGuid().ToString('N')
    $emptyPathRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-child-path-$id"
    try {
        [void](New-Item -ItemType Directory -Path $emptyPathRoot)
        $emptyConfigPath = Assert-EdgeAuthorityEmptyGitConfig $script:RepositoryRoot
        $baseEnvironment = New-EdgeAuthorityGitChildEnvironment `
            $emptyConfigPath $script:protocolGitPath
        $baseEnvironment.PATH = $emptyPathRoot

        $nonce = [Guid]::NewGuid().ToString('N')
        $probeEnvironment = [ordered]@{}
        foreach ($name in $baseEnvironment.Keys) {
            $probeEnvironment[[string]$name] = [string]$baseEnvironment[$name]
        }
        $probeEnvironment.EDGE_AUTHORITY_PROTOCOL_FIXTURE = "protocol-test:$nonce"
        $probe = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot `
                        'Invoke-EdgePluginContractAuthorityFixtureChild.ps1'),
                '-FixtureMode', 'pinned-child-environment', '-FixtureNonce', $nonce) `
            -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
            -Environment $probeEnvironment -TimeoutSeconds 20
        if ($probe.exitCode -ne 0) {
            throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bound child probe failed.`n$($probe.stderr)"
        }
        $probeValue = ConvertFrom-EdgeJsonText $probe.stdout
        [byte[]]$probeBytes = $probe.stdoutBytes
        [byte[]]$probeCanonicalBytes = ConvertTo-EdgeCanonicalBytes $probeValue
        if ($probeBytes.Length -ne $probeCanonicalBytes.Length -or
            -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                $probeBytes, $probeCanonicalBytes)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bound child probe output is not canonical JSON.'
        }
        $requiredDotnetVersion = [string]((Get-Content -LiteralPath `
                    (Join-Path $script:RepositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version)
        $expectedDotnetCommand = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0]
        $expectedDotnetPath = Resolve-EdgeFixedExecutable ([string]$expectedDotnetCommand.Source)
        if ([string]$probeValue.pathFirst -cne (Split-Path $script:protocolGitPath -Parent) -or
            [string]$probeValue.gitPath -cne $script:protocolGitPath -or
            [string]$probeValue.dotnetPath -cne $expectedDotnetPath -or
            [string]$probeValue.dotnetVersion -cne $requiredDotnetVersion) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bound child Git/PATH/dotnet identity differs from the production parent preflight.'
        }

        $entryFixtures = @(
            [pscustomobject]@{
                name = 'generator'
                expected = 'EDGE-SPLIT-LEDGER-001 path must stay inside the repository'
                arguments = @('-File', (Join-Path $script:RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1'),
                    '-PluginProject',
                    'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj',
                    '-OutputPath', '../authority-binding-must-not-write.json')
            },
            [pscustomobject]@{
                name = 'authority-validator'
                expected = 'EDGE-SPLIT-AUTHORITY-RESULT-001'
                arguments = @('-File', (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1'),
                    '-RepositoryRoot', $script:RepositoryRoot, '-AuthorityRebuildOnly')
            },
            [pscustomobject]@{
                name = 'fast-receipt'
                expected = 'EDGE-SPLIT-LEDGER-RECEIPT-MISSING'
                arguments = @('-File', (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1'),
                    '-RepositoryRoot', $script:RepositoryRoot, '-LedgerPath', $LedgerPath,
                    '-RequireAuthorityReceipt')
            },
            [pscustomobject]@{
                name = 'behavior'
                expected = 'EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY'
                arguments = @('-File', (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedgerBehavior.ps1'),
                    '-RepositoryRoot', $script:RepositoryRoot, '-BaselinePath', $LedgerPath,
                    '-AuthorityReceiptPath', 'x', '-AuthorityPublicKeySpkiBase64', 'x',
                    '-AuthorityRunId', 'x', '-AuthorityChallengeBase64', 'x',
                    '-AuthoritySourceRepositoryRoot', $script:RepositoryRoot,
                    '-AuthorityBindingsBase64', 'x')
            })
        foreach ($fixture in $entryFixtures) {
            $result = Invoke-ProtocolBoundedPowerShell -Arguments ([string[]]$fixture.arguments) `
                -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
                -Environment $baseEnvironment -TimeoutSeconds 30
            if ($result.exitCode -eq 0 -or
                -not $result.stderr.Contains([string]$fixture.expected, [StringComparison]::Ordinal) -or
                $result.stderr.Contains('EDGE-SPLIT-AUTHORITY-CHILD-BINDING', [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 production $($fixture.name) entry did not pass its exact child binding before the expected cheap rejection.`n$($result.stderr)"
            }
        }

        $standaloneGenerator = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $script:RepositoryRoot `
                        'eng/Generate-EdgePluginContractLedger.ps1'),
                '-PluginProject',
                'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj',
                '-OutputPath', '../standalone-generator-must-not-write.json') `
            -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
            -Environment $null -TimeoutSeconds 30
        if ($standaloneGenerator.exitCode -eq 0 -or
            -not $standaloneGenerator.stderr.Contains(
                'EDGE-SPLIT-LEDGER-001 path must stay inside the repository',
                [StringComparison]::Ordinal) -or
            $standaloneGenerator.stderr.Contains(
                'EDGE-SPLIT-AUTHORITY-CHILD-BINDING', [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 standalone generator semantics were changed by the optional authority child initializer.'
        }
        $missingPhaseFixture = ".artifacts/test-temp/standalone-validator-missing-$id.json"
        $standaloneValidator = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1'),
                '-RepositoryRoot', $script:RepositoryRoot,
                '-PhaseGateFixturePath', $missingPhaseFixture) `
            -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
            -Environment $null -TimeoutSeconds 30
        if ($standaloneValidator.exitCode -eq 0 -or
            -not $standaloneValidator.stderr.Contains(
                'EDGE-SPLIT-LEDGER-001 phase gate fixture does not exist',
                [StringComparison]::Ordinal) -or
            $standaloneValidator.stderr.Contains(
                'EDGE-SPLIT-AUTHORITY-CHILD-BINDING', [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 standalone validator semantics were changed by the optional authority child initializer.'
        }

        $standaloneInjectedEnvironment = [ordered]@{ GIT_NAMESPACE = 'forbidden-standalone-namespace' }
        foreach ($standaloneFixture in @(
                [pscustomobject]@{
                    name = 'generator'
                    arguments = @('-File', (Join-Path $script:RepositoryRoot `
                                'eng/Generate-EdgePluginContractLedger.ps1'),
                        '-PluginProject',
                        'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj',
                        '-OutputPath', '../standalone-injected-must-not-write.json')
                },
                [pscustomobject]@{
                    name = 'validator'
                    arguments = @('-File', (Join-Path $PSScriptRoot `
                                'Test-EdgePluginContractLedger.ps1'),
                        '-RepositoryRoot', $script:RepositoryRoot,
                        '-PhaseGateFixturePath', $missingPhaseFixture)
                })) {
            $standaloneInjected = Invoke-ProtocolBoundedPowerShell `
                -Arguments ([string[]]$standaloneFixture.arguments) `
                -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
                -Environment $standaloneInjectedEnvironment -TimeoutSeconds 30
            if ($standaloneInjected.exitCode -eq 0 -or
                -not $standaloneInjected.stderr.Contains(
                    'EDGE-SPLIT-AUTHORITY-GIT-ENV', [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 standalone $($standaloneFixture.name) accepted an external Git namespace before its clean-ingress fallback."
            }
        }

        $injectedEnvironment = [ordered]@{}
        foreach ($name in $probeEnvironment.Keys) {
            $injectedEnvironment[[string]$name] = [string]$probeEnvironment[$name]
        }
        $injectedEnvironment.GIT_NAMESPACE = 'forbidden-authority-namespace'
        $injected = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot `
                        'Invoke-EdgePluginContractAuthorityFixtureChild.ps1'),
                '-FixtureMode', 'pinned-child-environment', '-FixtureNonce', $nonce) `
            -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
            -Environment $injectedEnvironment -TimeoutSeconds 20
        if ($injected.exitCode -eq 0 -or
            -not $injected.stderr.Contains('EDGE-SPLIT-AUTHORITY-CHILD-BINDING', [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bound child accepted an extra authority-critical Git environment item.'
        }
        $reservedInjectedEnvironment = [ordered]@{}
        foreach ($name in $probeEnvironment.Keys) {
            $reservedInjectedEnvironment[[string]$name] = [string]$probeEnvironment[$name]
        }
        $reservedInjectedEnvironment['eDgE_aUtHoRiTy_ChIlD_eXtRa'] = 'forbidden-binding'
        $reservedInjected = Invoke-ProtocolBoundedPowerShell `
            -Arguments @('-File', (Join-Path $PSScriptRoot `
                        'Invoke-EdgePluginContractAuthorityFixtureChild.ps1'),
                '-FixtureMode', 'pinned-child-environment', '-FixtureNonce', $nonce) `
            -WorkingDirectory $script:RepositoryRoot -InputBytes $null `
            -Environment $reservedInjectedEnvironment -TimeoutSeconds 20
        if ($reservedInjected.exitCode -eq 0 -or
            -not $reservedInjected.stderr.Contains(
                'EDGE-SPLIT-AUTHORITY-CHILD-BINDING', [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 bound child accepted an extra mixed-case reserved binding.'
        }
        return $probeValue
    }
    finally {
        if (Test-Path -LiteralPath $emptyPathRoot -PathType Container) {
            Remove-Item -LiteralPath $emptyPathRoot -Recurse -Force
        }
    }
}

function Invoke-ProtocolFixtureChild {
    param([Parameter(Mandatory)][ValidateSet('success', 'crash', 'timeout')][string]$Mode)
    $nonce = [Guid]::NewGuid().ToString('N')
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:protocolPowerShellPath
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-NoLogo', '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractAuthorityFixtureChild.ps1'),
            '-FixtureMode', $Mode, '-FixtureNonce', $nonce)) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    $startInfo.Environment['EDGE_AUTHORITY_PROTOCOL_FIXTURE'] = "protocol-test:$nonce"
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE fixture did not start.' }
        if ($Mode -ceq 'timeout') {
            if ($process.WaitForExit(400)) { throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE timeout fixture exited before the deadline.' }
            $process.Kill($true)
            [void]$process.WaitForExit(30000)
            return [pscustomobject]@{ timedOut = $true; exitCode = [int]$process.ExitCode }
        }
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE fixture exceeded ten seconds.'
        }
        return [pscustomobject]@{ timedOut = $false; exitCode = [int]$process.ExitCode }
    }
    finally { $process.Dispose() }
}

function Invoke-ProtocolParentExitPipeFixture {
    $nonce = [Guid]::NewGuid().ToString('N')
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:protocolPowerShellPath
    $startInfo.WorkingDirectory = $script:RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-NoLogo', '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractAuthorityFixtureChild.ps1'),
            '-FixtureMode', 'parent-exit-pipe-descendant', '-FixtureNonce', $nonce)) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    $startInfo.Environment['EDGE_AUTHORITY_PROTOCOL_FIXTURE'] = "protocol-test:$nonce"
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        if (-not $process.Start()) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE pipe-holder parent did not start.'
        }
        $started = $true
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync($process.StandardOutput.BaseStream, 1048576)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync($process.StandardError.BaseStream, 1048576)
        if (-not $process.WaitForExit(2000)) {
            try { $process.Kill($true) } catch { }
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE pipe-holder parent did not exit quickly.'
        }
        [void](Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask `
            ([DateTimeOffset]::UtcNow.AddMilliseconds(400)) `
            'EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT')
    }
    finally {
        if ($started -and -not $process.HasExited) {
            try { $process.Kill($true) } catch { }
            [void]$process.WaitForExit(30000)
        }
        $process.Dispose()
    }
}

function Assert-ProtocolExecutableSymlinkPositive {
    $id = [Guid]::NewGuid().ToString('N')
    $root = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-executable-positive-$id"
    $link = Join-Path $root "pwsh-link-$id"
    try {
        [void](New-Item -ItemType Directory -Path $root)
        [void][IO.File]::CreateSymbolicLink($link, $script:protocolPowerShellPath)
        $resolved = Resolve-EdgeFixedExecutable $link
        if (-not (Test-EdgePathIdentity $resolved $script:protocolPowerShellPath)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 executable symlink did not resolve to the fixed final leaf.'
        }
        $item = Get-Item -LiteralPath $resolved -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 resolved executable remains indirect.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force }
        if (Test-Path -LiteralPath $root -PathType Container) { Remove-Item -LiteralPath $root -Force }
    }
}

function Invoke-ProtocolExecutableLoop {
    $id = [Guid]::NewGuid().ToString('N')
    $root = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-executable-loop-$id"
    $first = Join-Path $root "first-$id"
    $second = Join-Path $root "second-$id"
    try {
        [void](New-Item -ItemType Directory -Path $root)
        [void][IO.File]::CreateSymbolicLink($first, $second)
        [void][IO.File]::CreateSymbolicLink($second, $first)
        return Resolve-EdgeFixedExecutable $first
    }
    finally {
        if (Test-Path -LiteralPath $first) { Remove-Item -LiteralPath $first -Force }
        if (Test-Path -LiteralPath $second) { Remove-Item -LiteralPath $second -Force }
        if (Test-Path -LiteralPath $root -PathType Container) { Remove-Item -LiteralPath $root -Force }
    }
}

function Invoke-ProtocolGit {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments
    )
    $output = & $script:protocolGitPath -C $Root @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP protocol fixture git command failed.'
    }
    return $output.Trim()
}

function Invoke-ProtocolTimeoutCleanupFixture {
    $cleanupRunId = [Guid]::NewGuid().ToString('N')
    $outerRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-protocol-outer-$cleanupRunId"
    $authorityRoot = Join-Path $outerRoot "authority-$cleanupRunId"
    $coordinatorRoot = Join-Path $outerRoot "coordinator-$cleanupRunId"
    $validatorRoot = Join-Path $coordinatorRoot "validator-$cleanupRunId"
    $replayRoot = Join-Path $coordinatorRoot "replay-$cleanupRunId"
    $parentMarkerPath = Join-Path $outerRoot '.edge-development-authority-run.json'
    $pinnedPath = Get-EdgeAuthorityPinnedPath $script:protocolGitPath
    $parentMarker = [pscustomobject][ordered]@{
        schemaVersion = 2
        runId = $cleanupRunId
        sourceRepositoryRoot = $script:RepositoryRoot
        authorityRepositoryRoot = $authorityRoot
        snapshotRoot = $authorityRoot
        coordinatorRunRoot = $coordinatorRoot
        validatorWorktreePath = $validatorRoot
        replayWorktreePath = $replayRoot
        fixedGitExecutablePath = $script:protocolGitPath
        pinnedPathSha256 = Get-EdgeSha256Text $pinnedPath
    }
    $coordinatorMarker = $null
    $sharedCleanupCompleted = $false
    try {
        [void](New-Item -ItemType Directory -Path $outerRoot)
        [IO.File]::WriteAllBytes($parentMarkerPath, (ConvertTo-EdgeCanonicalBytes $parentMarker))
        [void](New-Item -ItemType Directory -Path $authorityRoot)
        [void](Invoke-ProtocolGit $authorityRoot @('init', '--quiet'))
        [void](New-Item -ItemType Directory -Path (Join-Path $authorityRoot 'eng'))
        [IO.File]::WriteAllText(
            (Join-Path $authorityRoot 'eng/edge-authority-empty.gitconfig'),
            "# Intentionally contains no Git settings; authority processes pin this file.`n",
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            (Join-Path $authorityRoot 'authority-probe.txt'), 'authority-probe', [Text.UTF8Encoding]::new($false))
        [void](Invoke-ProtocolGit $authorityRoot @(
                'add', '--', 'authority-probe.txt', 'eng/edge-authority-empty.gitconfig'))
        [void](Invoke-ProtocolGit $authorityRoot @(
                '-c', 'user.name=Edge Protocol Fixture',
                '-c', 'user.email=edge-protocol-fixture@example.invalid',
                'commit', '--quiet', '--no-gpg-sign', '-m', 'protocol cleanup fixture'))
        $authorityHead = Invoke-ProtocolGit $authorityRoot @('rev-parse', 'HEAD')
        $coordinatorMarker = [pscustomobject][ordered]@{
            schemaVersion = 1
            runId = $cleanupRunId
            sourceRepositoryRoot = $script:RepositoryRoot
            authorityRepositoryRoot = $authorityRoot
            authorityHead = $authorityHead
            validatorWorktreePath = $validatorRoot
            replayWorktreePath = $replayRoot
        }
        [void](New-Item -ItemType Directory -Path $coordinatorRoot)
        [IO.File]::WriteAllBytes(
            (Join-Path $coordinatorRoot '.edge-authority-run.json'),
            (ConvertTo-EdgeCanonicalBytes $coordinatorMarker))
        [void](Invoke-ProtocolGit $authorityRoot @('worktree', 'add', '--quiet', '--detach', $validatorRoot, $authorityHead))
        [void](Invoke-ProtocolGit $authorityRoot @('worktree', 'add', '--quiet', '--detach', $replayRoot, $authorityHead))

        $result = Invoke-ProtocolFixtureChild timeout
        if (-not $result.timedOut) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP timeout fixture was not killed by its direct parent.'
        }
        Remove-EdgeDevelopmentCoordinatorRunState `
            -GitExecutablePath $script:protocolGitPath `
            -AuthorityRepositoryRoot $authorityRoot `
            -RunRoot $coordinatorRoot `
            -ValidatorWorktreePath $validatorRoot `
            -ReplayWorktreePath $replayRoot `
            -RunId $cleanupRunId `
            -ParentMarkerPath $parentMarkerPath `
            -ParentMarkerExpected $parentMarker `
            -CoordinatorMarkerExpected $coordinatorMarker
        $listed = Invoke-ProtocolGit $authorityRoot @('worktree', 'list', '--porcelain')
        $listedRoots = @($listed -split "`r?`n" |
            Where-Object { $_.StartsWith('worktree ', [StringComparison]::Ordinal) } |
            ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) })
        $registeredSurvivors = @($listedRoots | Where-Object {
                (Test-EdgePathIdentity $_ $validatorRoot) -or (Test-EdgePathIdentity $_ $replayRoot)
            })
        if ($registeredSurvivors.Count -ne 0 -or (Test-Path -LiteralPath $coordinatorRoot)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP killed-child worktree state survived exact parent cleanup.'
        }
        Remove-EdgeDevelopmentOuterRunRoot `
            -RunRoot $outerRoot `
            -SnapshotRoot $authorityRoot `
            -CoordinatorRunRoot $coordinatorRoot `
            -RunId $cleanupRunId `
            -MarkerPath $parentMarkerPath `
            -MarkerExpected $parentMarker
        if ((Test-Path -LiteralPath $outerRoot) -or (Test-Path -LiteralPath $coordinatorRoot)) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP killed-child temporary roots survived exact parent cleanup.'
        }
        $sharedCleanupCompleted = $true
        return $result
    }
    finally {
        if (-not $sharedCleanupCompleted) {
            if ($null -ne $coordinatorMarker -and
                (Test-Path -LiteralPath $parentMarkerPath -PathType Leaf) -and
                (Test-Path -LiteralPath $authorityRoot -PathType Container)) {
                try {
                    Remove-EdgeDevelopmentCoordinatorRunState `
                        -GitExecutablePath $script:protocolGitPath `
                        -AuthorityRepositoryRoot $authorityRoot `
                        -RunRoot $coordinatorRoot `
                        -ValidatorWorktreePath $validatorRoot `
                        -ReplayWorktreePath $replayRoot `
                        -RunId $cleanupRunId `
                        -ParentMarkerPath $parentMarkerPath `
                        -ParentMarkerExpected $parentMarker `
                        -CoordinatorMarkerExpected $coordinatorMarker
                }
                catch { }
            }
            if ((Test-Path -LiteralPath $parentMarkerPath -PathType Leaf) -and
                -not (Test-Path -LiteralPath $coordinatorRoot)) {
                try {
                    Remove-EdgeDevelopmentOuterRunRoot `
                        -RunRoot $outerRoot `
                        -SnapshotRoot $authorityRoot `
                        -CoordinatorRunRoot $coordinatorRoot `
                        -RunId $cleanupRunId `
                        -MarkerPath $parentMarkerPath `
                        -MarkerExpected $parentMarker
                }
                catch { }
            }
        }
    }
}

function Assert-ProtocolCleanupSubcaseRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    try { & $Action }
    catch {
        if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-DEV-CLEANUP', [StringComparison]::Ordinal)) {
            return
        }
        throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP replacement subcase '$Name' failed for the wrong reason: $($_.Exception.Message)"
    }
    throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-CLEANUP replacement subcase '$Name' was accepted."
}

function Remove-ProtocolExactTemporaryPath {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget) -or
        -not $item.PSIsContainer) {
        Remove-Item -LiteralPath $Path -Force
        return
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Invoke-ProtocolCleanupReplacementNegatives {
    $replacementRunId = [Guid]::NewGuid().ToString('N')
    $outerRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-protocol-replacement-$replacementRunId"
    $authorityRoot = Join-Path $outerRoot "snapshot-$replacementRunId"
    $coordinatorRoot = Join-Path $outerRoot "coordinator-$replacementRunId"
    $validatorRoot = Join-Path $coordinatorRoot "validator-$replacementRunId"
    $replayRoot = Join-Path $coordinatorRoot "replay-$replacementRunId"
    $parentMarkerPath = Join-Path $outerRoot '.edge-development-authority-run.json'
    $coordinatorMarkerPath = Join-Path $coordinatorRoot '.edge-authority-run.json'
    $parentMarkerTarget = Join-Path $outerRoot "parent-marker-target-$replacementRunId.json"
    $coordinatorMarkerTarget = Join-Path $outerRoot "coordinator-marker-target-$replacementRunId.json"
    $coordinatorTarget = Join-Path $outerRoot "coordinator-target-$replacementRunId"
    $validatorTarget = Join-Path $outerRoot "validator-target-$replacementRunId"
    $snapshotTarget = Join-Path $outerRoot "snapshot-target-$replacementRunId"
    $pinnedPath = Get-EdgeAuthorityPinnedPath $script:protocolGitPath
    $parentMarker = [pscustomobject][ordered]@{
        schemaVersion = 2
        runId = $replacementRunId
        sourceRepositoryRoot = $script:RepositoryRoot
        authorityRepositoryRoot = $authorityRoot
        snapshotRoot = $authorityRoot
        coordinatorRunRoot = $coordinatorRoot
        validatorWorktreePath = $validatorRoot
        replayWorktreePath = $replayRoot
        fixedGitExecutablePath = $script:protocolGitPath
        pinnedPathSha256 = Get-EdgeSha256Text $pinnedPath
    }
    $coordinatorMarker = $null
    $completed = $false
    try {
        [void](New-Item -ItemType Directory -Path $outerRoot)
        [void](New-Item -ItemType Directory -Path $authorityRoot)
        [void](Invoke-ProtocolGit $authorityRoot @('init', '--quiet'))
        [void](New-Item -ItemType Directory -Path (Join-Path $authorityRoot 'eng'))
        [IO.File]::WriteAllText(
            (Join-Path $authorityRoot 'eng/edge-authority-empty.gitconfig'),
            "# Intentionally contains no Git settings; authority processes pin this file.`n",
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            (Join-Path $authorityRoot 'authority-probe.txt'), 'authority-probe', [Text.UTF8Encoding]::new($false))
        [void](Invoke-ProtocolGit $authorityRoot @(
                'add', '--', 'authority-probe.txt', 'eng/edge-authority-empty.gitconfig'))
        [void](Invoke-ProtocolGit $authorityRoot @(
                '-c', 'user.name=Edge Protocol Fixture',
                '-c', 'user.email=edge-protocol-fixture@example.invalid',
                'commit', '--quiet', '--no-gpg-sign', '-m', 'protocol replacement fixture'))
        $authorityHead = Invoke-ProtocolGit $authorityRoot @('rev-parse', 'HEAD')
        $coordinatorMarker = [pscustomobject][ordered]@{
            schemaVersion = 1
            runId = $replacementRunId
            sourceRepositoryRoot = $script:RepositoryRoot
            authorityRepositoryRoot = $authorityRoot
            authorityHead = $authorityHead
            validatorWorktreePath = $validatorRoot
            replayWorktreePath = $replayRoot
        }

        # Parent marker replacement must be rejected before any cleanup command.
        [IO.File]::WriteAllBytes($parentMarkerTarget, (ConvertTo-EdgeCanonicalBytes $parentMarker))
        [void][IO.File]::CreateSymbolicLink($parentMarkerPath, $parentMarkerTarget)
        Assert-ProtocolCleanupSubcaseRejected 'parent-marker-symlink' {
            Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $script:protocolGitPath -AuthorityRepositoryRoot $authorityRoot `
                -RunRoot $coordinatorRoot -ValidatorWorktreePath $validatorRoot -ReplayWorktreePath $replayRoot `
                -RunId $replacementRunId -ParentMarkerPath $parentMarkerPath -ParentMarkerExpected $parentMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
        }
        Remove-ProtocolExactTemporaryPath $parentMarkerPath
        Remove-ProtocolExactTemporaryPath $parentMarkerTarget
        [IO.File]::WriteAllBytes($parentMarkerPath, (ConvertTo-EdgeCanonicalBytes $parentMarker))

        # A substituted coordinator run root is never followed or recursively removed.
        [void](New-Item -ItemType Directory -Path $coordinatorTarget)
        [void][IO.Directory]::CreateSymbolicLink($coordinatorRoot, $coordinatorTarget)
        Assert-ProtocolCleanupSubcaseRejected 'coordinator-runroot-symlink' {
            Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $script:protocolGitPath -AuthorityRepositoryRoot $authorityRoot `
                -RunRoot $coordinatorRoot -ValidatorWorktreePath $validatorRoot -ReplayWorktreePath $replayRoot `
                -RunId $replacementRunId -ParentMarkerPath $parentMarkerPath -ParentMarkerExpected $parentMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
        }
        Remove-ProtocolExactTemporaryPath $coordinatorRoot
        Remove-ProtocolExactTemporaryPath $coordinatorTarget

        # A substituted known worktree target is rejected before registration cleanup.
        [void](New-Item -ItemType Directory -Path $coordinatorRoot)
        [void](New-Item -ItemType Directory -Path $validatorTarget)
        [void][IO.Directory]::CreateSymbolicLink($validatorRoot, $validatorTarget)
        Assert-ProtocolCleanupSubcaseRejected 'worktree-symlink' {
            Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $script:protocolGitPath -AuthorityRepositoryRoot $authorityRoot `
                -RunRoot $coordinatorRoot -ValidatorWorktreePath $validatorRoot -ReplayWorktreePath $replayRoot `
                -RunId $replacementRunId -ParentMarkerPath $parentMarkerPath -ParentMarkerExpected $parentMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
        }
        Remove-ProtocolExactTemporaryPath $validatorRoot
        Remove-ProtocolExactTemporaryPath $validatorTarget
        Remove-ProtocolExactTemporaryPath $coordinatorRoot

        # The child marker may be absent in the crash window, but may not be substituted.
        [void](New-Item -ItemType Directory -Path $coordinatorRoot)
        [IO.File]::WriteAllBytes($coordinatorMarkerTarget, (ConvertTo-EdgeCanonicalBytes $coordinatorMarker))
        [void][IO.File]::CreateSymbolicLink($coordinatorMarkerPath, $coordinatorMarkerTarget)
        Assert-ProtocolCleanupSubcaseRejected 'coordinator-marker-symlink' {
            Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $script:protocolGitPath -AuthorityRepositoryRoot $authorityRoot `
                -RunRoot $coordinatorRoot -ValidatorWorktreePath $validatorRoot -ReplayWorktreePath $replayRoot `
                -RunId $replacementRunId -ParentMarkerPath $parentMarkerPath -ParentMarkerExpected $parentMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
        }
        Remove-ProtocolExactTemporaryPath $coordinatorMarkerPath
        Remove-ProtocolExactTemporaryPath $coordinatorMarkerTarget
        Remove-ProtocolExactTemporaryPath $coordinatorRoot

        # A substituted snapshot root is rejected by outer cleanup.
        Remove-ProtocolExactTemporaryPath $authorityRoot
        [void](New-Item -ItemType Directory -Path $snapshotTarget)
        [void][IO.Directory]::CreateSymbolicLink($authorityRoot, $snapshotTarget)
        Assert-ProtocolCleanupSubcaseRejected 'snapshot-symlink' {
            Remove-EdgeDevelopmentOuterRunRoot `
                -RunRoot $outerRoot -SnapshotRoot $authorityRoot -CoordinatorRunRoot $coordinatorRoot `
                -RunId $replacementRunId -MarkerPath $parentMarkerPath -MarkerExpected $parentMarker
        }
        Remove-ProtocolExactTemporaryPath $authorityRoot
        Remove-ProtocolExactTemporaryPath $snapshotTarget
        Remove-EdgeDevelopmentOuterRunRoot `
            -RunRoot $outerRoot -SnapshotRoot $authorityRoot -CoordinatorRunRoot $coordinatorRoot `
            -RunId $replacementRunId -MarkerPath $parentMarkerPath -MarkerExpected $parentMarker
        $completed = $true
    }
    finally {
        if (-not $completed) {
            foreach ($path in @(
                    $validatorRoot, $coordinatorMarkerPath, $coordinatorRoot,
                    $authorityRoot, $parentMarkerPath, $parentMarkerTarget,
                    $coordinatorMarkerTarget, $coordinatorTarget, $validatorTarget,
                    $snapshotTarget, $outerRoot)) {
                try { Remove-ProtocolExactTemporaryPath $path } catch { }
            }
        }
    }
}

$baseReceipt = Write-ProtocolReceipt
$baseReceiptBytes = [IO.File]::ReadAllBytes($receiptFullPath)
$request = [pscustomobject][ordered]@{
    schemaVersion = 1; mode = 'development-snapshot'; runId = $runId; challengeBase64 = $challengeBase64
    sourceRepositoryRoot = $RepositoryRoot; authorityRepositoryRoot = $RepositoryRoot
    authorityHead = $head; authorityTree = $tree; formalFinalHead = ''; formalFinalTree = ''
    sourceBaseHead = [string]$ledger.sourceState.head; sourceBaseTree = [string]$ledger.sourceState.tree
    sourceDirtyManifestSha256 = $dirtyManifestSha256; ephemeralSnapshotHead = $head; ephemeralSnapshotTree = $tree
    implementationHead = [string]$ledger.sourceState.head; implementationTree = [string]$ledger.sourceState.tree
    ledgerPath = $LedgerPath; receiptPath = $receiptRelativePath
    runRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-$runId"
    validatorWorktreePath = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-$runId/validator-$runId"
    replayWorktreePath = Join-Path ([IO.Path]::GetTempPath()) "edge-authority-$runId/replay-$runId"
    pluginProject = [string]$ledger.msbuildCompilation.projectPath
    configuration = [string]$ledger.msbuildCompilation.configuration; currentBatch = [string]$ledger.batchId
    viewIdsAssemblyPath = [string]$ledger.msbuildCompilation.viewIdsAssemblyPath
    viewIdsTypeName = [string]$ledger.msbuildCompilation.viewIdsTypeName; timeoutSeconds = 900
}
$descriptorExpected = [pscustomobject][ordered]@{
    runId = $runId; challengeBase64 = $challengeBase64; coordinatorPid = $PID
    processStartUtc = [Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('O')
    sourceRepositoryRoot = $RepositoryRoot; authorityRepositoryRoot = $RepositoryRoot
    authorityHead = $head; authorityTree = $tree; formalFinalHead = ''; formalFinalTree = ''
    sourceBaseHead = [string]$ledger.sourceState.head; sourceBaseTree = [string]$ledger.sourceState.tree
    sourceDirtyManifestSha256 = $dirtyManifestSha256; ephemeralSnapshotHead = $head; ephemeralSnapshotTree = $tree
    implementationHead = [string]$ledger.sourceState.head; implementationTree = [string]$ledger.sourceState.tree
    receiptPath = $receiptRelativePath
}
$descriptor = [pscustomobject][ordered]@{
    schemaVersion = 1; runId = $runId; challengeBase64 = $challengeBase64; coordinatorPid = $PID
    processStartUtc = [string]$descriptorExpected.processStartUtc
    sourceRepositoryRoot = $RepositoryRoot; authorityRepositoryRoot = $RepositoryRoot
    authorityHead = $head; authorityTree = $tree; formalFinalHead = ''; formalFinalTree = ''
    sourceBaseHead = [string]$ledger.sourceState.head; sourceBaseTree = [string]$ledger.sourceState.tree
    sourceDirtyManifestSha256 = $dirtyManifestSha256; ephemeralSnapshotHead = $head; ephemeralSnapshotTree = $tree
    implementationHead = [string]$ledger.sourceState.head; implementationTree = [string]$ledger.sourceState.tree
    receiptPath = $receiptRelativePath; receiptSha256 = Get-EdgeSha256File $receiptFullPath
    publicKeySpkiBase64 = $publicKeyBase64; publicKeySha256 = $publicKeySha256
    authorityCount = 1; replayCount = 1; authorityElapsedMilliseconds = 1; replayElapsedMilliseconds = 1
    totalElapsedMilliseconds = 2
}

$formalImplementationHead = ('1' * 40)
$formalImplementationTree = ('2' * 40)
$formalFinalHead = ('3' * 40)
$formalFinalTree = ('4' * 40)
$formalRequest = Copy-ProtocolValue $request
$formalRequest.mode = 'formal-clean'
$formalRequest.authorityHead = $formalFinalHead
$formalRequest.authorityTree = $formalFinalTree
$formalRequest.formalFinalHead = $formalFinalHead
$formalRequest.formalFinalTree = $formalFinalTree
$formalRequest.sourceBaseHead = $formalFinalHead
$formalRequest.sourceBaseTree = $formalFinalTree
$formalRequest.sourceDirtyManifestSha256 = ''
$formalRequest.ephemeralSnapshotHead = ''
$formalRequest.ephemeralSnapshotTree = ''
$formalRequest.implementationHead = $formalImplementationHead
$formalRequest.implementationTree = $formalImplementationTree
$formalRequest.ledgerPath = 'eng/baselines/edge-plugin-contract-ledger.json'

$formalPayload = Copy-ProtocolValue $basePayload
$formalPayload.mode = 'formal-clean'
$formalPayload.formal = $true
$formalPayload.formalFinalHead = $head
$formalPayload.formalFinalTree = $tree
$formalPayload.sourceDirtyManifestSha256 = ''
$formalPayload.ephemeralSnapshotHead = ''
$formalPayload.ephemeralSnapshotTree = ''
$formalReceipt = New-EdgeSignedAuthorityReceipt `
    -Payload $formalPayload -PrivateKey $privateKey
$formalReceiptBytes = ConvertTo-EdgeCanonicalBytes $formalReceipt
$formalReceiptSha256 = Get-EdgeSha256Bytes $formalReceiptBytes

$formalDescriptorExpected = Copy-ProtocolValue $descriptorExpected
$formalDescriptorExpected.formalFinalHead = $head
$formalDescriptorExpected.formalFinalTree = $tree
$formalDescriptorExpected.sourceDirtyManifestSha256 = ''
$formalDescriptorExpected.ephemeralSnapshotHead = ''
$formalDescriptorExpected.ephemeralSnapshotTree = ''
$formalDescriptor = Copy-ProtocolValue $descriptor
$formalDescriptor.formalFinalHead = $head
$formalDescriptor.formalFinalTree = $tree
$formalDescriptor.sourceDirtyManifestSha256 = ''
$formalDescriptor.ephemeralSnapshotHead = ''
$formalDescriptor.ephemeralSnapshotTree = ''
$formalDescriptor.receiptSha256 = $formalReceiptSha256

$formalResult = [pscustomobject][ordered]@{
    schemaVersion = 1
    ruleId = 'EDGE-SPLIT-LEDGER-001'
    mode = 'formal-clean'
    formal = $true
    passed = $true
    completedUtc = $issued.ToString('O')
    formalFinalHead = $formalFinalHead
    formalFinalTree = $formalFinalTree
    implementationHead = $formalImplementationHead
    implementationTree = $formalImplementationTree
    ledgerPath = 'eng/baselines/edge-plugin-contract-ledger.json'
    ledgerSha256 = Get-EdgeSha256File $ledgerFullPath
    receiptPath = $receiptRelativePath
    receiptSha256 = $formalReceiptSha256
    publicKeySha256 = $publicKeySha256
    authorityCount = 1
    replayCount = 1
    descriptorPidBoundToDirectChild = $true
    descriptorStartBoundToDirectChild = $true
    fastConsumerRequireAuthorityReceipt = $true
    fastConsumerRequireFormalAuthorityReceipt = $true
    postStateStable = $true
    cleanupComplete = $true
}
$formalSchemaPositiveCount = 0
$formalSignedPositiveCount = 0
$formalTransitionPositiveCount = 0
$formalStaticMutationPassed = 0

try {
    # Positive schema/canonical/time-format locks plus one real signed receipt verification.
    foreach ($schemaFixture in @(
            [pscustomobject]@{ value = $request; schema = $requestSchemaPath; code = 'request' },
            [pscustomobject]@{ value = $descriptor; schema = $descriptorSchemaPath; code = 'descriptor' },
            [pscustomobject]@{ value = $baseReceipt; schema = $receiptSchemaPath; code = 'receipt' })) {
        $bytes = ConvertTo-EdgeCanonicalBytes $schemaFixture.value
        [void](Assert-EdgeStrictJson -RawBytes $bytes -SchemaPath $schemaFixture.schema `
            -ErrorCode "EDGE-SPLIT-AUTHORITY-PROTOCOL-$($schemaFixture.code)" -RequireCanonical)
    }
    if ([string]$baseReceipt.payload.issuedUtc -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$' -or
        [string]$baseReceipt.payload.expiresUtc -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$') {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 receipt timestamps must remain seven-digit UTC Z values.'
    }
    [void](Assert-EdgeAuthorityDescriptor -RawBytes (ConvertTo-EdgeCanonicalBytes $descriptor) `
        -SchemaPath $descriptorSchemaPath -Expected $descriptorExpected -ReceiptFullPath $receiptFullPath)
    [void](Invoke-ProtocolReceiptValidation)

    Assert-ProtocolFormalTransitionFixture -Request $formalRequest
    $formalTransitionPositiveCount++
    [IO.File]::WriteAllBytes($receiptFullPath, $formalReceiptBytes)
    foreach ($schemaFixture in @(
            [pscustomobject]@{
                value = $formalRequest; schema = $requestSchemaPath; code = 'formal-request'
            },
            [pscustomobject]@{
                value = $formalDescriptor; schema = $descriptorSchemaPath; code = 'formal-descriptor'
            },
            [pscustomobject]@{
                value = $formalReceipt; schema = $receiptSchemaPath; code = 'formal-receipt'
            },
            [pscustomobject]@{
                value = $formalResult; schema = $formalResultSchemaPath; code = 'formal-result'
            })) {
        [void](Assert-EdgeStrictJson `
            -RawBytes (ConvertTo-EdgeCanonicalBytes $schemaFixture.value) `
            -SchemaPath $schemaFixture.schema `
            -ErrorCode "EDGE-SPLIT-AUTHORITY-PROTOCOL-$($schemaFixture.code)" `
            -RequireCanonical)
        $formalSchemaPositiveCount++
    }
    [void](Assert-EdgeAuthorityDescriptor `
        -RawBytes (ConvertTo-EdgeCanonicalBytes $formalDescriptor) `
        -SchemaPath $descriptorSchemaPath -Expected $formalDescriptorExpected `
        -ReceiptFullPath $receiptFullPath)
    $formalReceiptArguments = Get-ProtocolReceiptArguments @{
        ExpectedFormalFinalHead = $head
        ExpectedFormalFinalTree = $tree
        ExpectedSourceDirtyManifestSha256 = ''
        ExpectedEphemeralSnapshotHead = ''
        ExpectedEphemeralSnapshotTree = ''
    }
    [void](Assert-EdgeAuthorityReceipt @formalReceiptArguments -RequireFormal)
    $formalSignedPositiveCount++
    Assert-ProtocolRejected 'formal-request-dirty-source' 'schema validation failed' {
        $copy = Copy-ProtocolValue $formalRequest
        $copy.sourceDirtyManifestSha256 = ('a' * 64)
        [void](Assert-EdgeStrictJson -RawBytes (ConvertTo-EdgeCanonicalBytes $copy) `
            -SchemaPath $requestSchemaPath -ErrorCode 'FORMAL-REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'formal-result-cleanup-false' 'schema validation failed' {
        $copy = Copy-ProtocolValue $formalResult
        $copy.cleanupComplete = $false
        [void](Assert-EdgeStrictJson -RawBytes (ConvertTo-EdgeCanonicalBytes $copy) `
            -SchemaPath $formalResultSchemaPath -ErrorCode 'FORMAL-RESULT' -RequireCanonical)
    }
    Assert-ProtocolRejected 'formal-result-extra-property' 'schema validation failed' {
        $copy = Copy-ProtocolValue $formalResult
        $copy | Add-Member -NotePropertyName commandOverride -NotePropertyValue 'forbidden'
        [void](Assert-EdgeStrictJson -RawBytes (ConvertTo-EdgeCanonicalBytes $copy) `
            -SchemaPath $formalResultSchemaPath -ErrorCode 'FORMAL-RESULT' -RequireCanonical)
    }
    [IO.File]::WriteAllBytes($receiptFullPath, $baseReceiptBytes)

    [void](Invoke-ProtocolCoordinatorParentBindingFixture)
    $boundChildProbe = Invoke-ProtocolBoundChildEntrypointFixtures
    if ([string]::IsNullOrWhiteSpace([string]$boundChildProbe.dotnetPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 production child preflight did not bind a final dotnet path.'
    }
    Assert-ProtocolExecutableSymlinkPositive

    $requestBytes = ConvertTo-EdgeCanonicalBytes $request
    Assert-ProtocolRejected 'request-bom' 'BOM is forbidden' {
        [void](Assert-EdgeStrictJson -RawBytes ([byte[]]@(0xEF, 0xBB, 0xBF) + $requestBytes) `
            -SchemaPath $requestSchemaPath -ErrorCode 'REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'request-crlf' 'CR/CRLF bytes are forbidden' {
        [void](Assert-EdgeStrictJson -RawBytes ([Text.UTF8Encoding]::new($false).GetBytes(
                    ([Text.UTF8Encoding]::new($false).GetString($requestBytes) + "`r`n"))) `
            -SchemaPath $requestSchemaPath -ErrorCode 'REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'request-trailing-lf' 'canonical UTF-8 representation' {
        [void](Assert-EdgeStrictJson -RawBytes ([byte[]]@($requestBytes + 0x0A)) `
            -SchemaPath $requestSchemaPath -ErrorCode 'REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'request-invalid-utf8' 'not strict UTF-8' {
        [void](Assert-EdgeStrictJson -RawBytes ([byte[]]@(0x7B, 0xFF, 0x7D)) `
            -SchemaPath $requestSchemaPath -ErrorCode 'REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'request-extra-property' 'schema validation failed' {
        $copy = Copy-ProtocolValue $request
        $copy | Add-Member -NotePropertyName commandOverride -NotePropertyValue 'forbidden'
        [void](Assert-EdgeStrictJson -RawBytes (ConvertTo-EdgeCanonicalBytes $copy) `
            -SchemaPath $requestSchemaPath -ErrorCode 'REQUEST' -RequireCanonical)
    }
    Assert-ProtocolRejected 'executable-symlink-loop' 'EDGE-SPLIT-AUTHORITY-EXECUTABLE-001' {
        [void](Invoke-ProtocolExecutableLoop)
    }
    Assert-ProtocolRejected 'cleanup-reparse-replacements' 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP' {
        Invoke-ProtocolCleanupReplacementNegatives
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP all runroot/marker/worktree/snapshot replacement subcases were rejected.'
    }
    Assert-ProtocolRejected 'descriptor-non-z-time' 'schema validation failed' {
        $copy = Copy-ProtocolValue $descriptor; $copy.processStartUtc = '2026-07-18T00:00:00.0000000+00:00'
        [void](Assert-EdgeStrictJson -RawBytes (ConvertTo-EdgeCanonicalBytes $copy) `
            -SchemaPath $descriptorSchemaPath -ErrorCode 'DESCRIPTOR' -RequireCanonical)
    }
    Assert-ProtocolRejected 'receipt-noncanonical-order' 'canonical UTF-8 representation' {
        $raw = [pscustomobject][ordered]@{
            signatureBase64 = [string]$baseReceipt.signatureBase64; payload = $baseReceipt.payload
            signatureAlgorithm = [string]$baseReceipt.signatureAlgorithm; schemaVersion = 1
        }
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($raw | ConvertTo-Json -Depth 100 -Compress))
        [void](Assert-EdgeStrictJson -RawBytes $bytes -SchemaPath $receiptSchemaPath -ErrorCode 'RECEIPT' -RequireCanonical)
    }
    [IO.File]::WriteAllBytes($receiptFullPath, $baseReceiptBytes)
    Assert-ProtocolRejected 'missing-public-anchor' 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY' {
        $args = Get-ProtocolReceiptArguments @{ PublicKeySpkiBase64 = '' }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'missing-run-anchor' 'EDGE-SPLIT-LEDGER-RECEIPT-RUN' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedRunId = '' }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'missing-challenge-anchor' 'EDGE-SPLIT-LEDGER-RECEIPT-CHALLENGE' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedChallengeBase64 = '' }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'public-anchor-invalid-base64' 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY' {
        $args = Get-ProtocolReceiptArguments @{ PublicKeySpkiBase64 = '***' }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'public-anchor-trailing-spki' 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY' {
        $args = Get-ProtocolReceiptArguments @{ PublicKeySpkiBase64 = [Convert]::ToBase64String([byte[]]@($publicBytes + 0x00)) }
        [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'public-anchor-not-p256' 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY' {
        $p384 = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP384'))
        try {
            $p384Bytes = $p384.ExportSubjectPublicKeyInfo()
            [void](Write-ProtocolReceipt { param($payload) $payload.publicKeySha256 = Get-EdgeSha256Bytes $p384Bytes })
            $args = Get-ProtocolReceiptArguments @{ PublicKeySpkiBase64 = [Convert]::ToBase64String($p384Bytes) }
            [void](Assert-EdgeAuthorityReceipt @args)
        }
        finally { $p384.Dispose() }
    }
    Assert-ProtocolRejected 'public-digest-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY' {
        [void](Write-ProtocolReceipt { param($payload) $payload.publicKeySha256 = ('0' * 64) })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'signature-bit-flip' 'EDGE-SPLIT-LEDGER-RECEIPT-SIGNATURE' {
        [void](Write-ProtocolReceipt -MutateReceipt {
                param($receipt); $bytes = [Convert]::FromBase64String([string]$receipt.signatureBase64)
                $bytes[0] = $bytes[0] -bxor 1; $receipt.signatureBase64 = [Convert]::ToBase64String($bytes)
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    [IO.File]::WriteAllBytes($receiptFullPath, $baseReceiptBytes)
    Assert-ProtocolRejected 'run-binding-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-RUN' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedRunId = ('0' * 32) }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'challenge-binding-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-CHALLENGE' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedChallengeBase64 = [Convert]::ToBase64String([byte[]]::new(32)) }
        [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'source-root-binding-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-ROOT' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedSourceRepositoryRoot = (Join-Path ([IO.Path]::GetTempPath()) 'wrong-source') }
        [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'parent-authority-head-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedAuthorityHead = ('0' * 40) }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'parent-source-base-tree-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedSourceBaseTree = ('0' * 40) }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'descriptor-correct-receipt-manifest-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING' {
        [void](Write-ProtocolReceipt { param($payload) $payload.sourceDirtyManifestSha256 = ('b' * 64) })
        $descriptorCopy = Copy-ProtocolValue $descriptor
        $descriptorCopy.receiptSha256 = Get-EdgeSha256File $receiptFullPath
        [void](Assert-EdgeAuthorityDescriptor -RawBytes (ConvertTo-EdgeCanonicalBytes $descriptorCopy) `
            -SchemaPath $descriptorSchemaPath -Expected $descriptorExpected -ReceiptFullPath $receiptFullPath)
        [void](Invoke-ProtocolReceiptValidation)
    }
    [IO.File]::WriteAllBytes($receiptFullPath, $baseReceiptBytes)
    Assert-ProtocolRejected 'parent-ephemeral-head-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedEphemeralSnapshotHead = ('0' * 40) }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'parent-implementation-tree-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING' {
        $args = Get-ProtocolReceiptArguments @{ ExpectedImplementationTree = ('0' * 40) }; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'development-receipt-not-formal' 'EDGE-SPLIT-LEDGER-RECEIPT-FORMAL' {
        $args = Get-ProtocolReceiptArguments; $args.RequireFormal = $true; [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'invalid-calendar-time' 'EDGE-SPLIT-LEDGER-RECEIPT-TIME' {
        [void](Write-ProtocolReceipt { param($payload) $payload.issuedUtc = '2026-99-18T00:00:00.0000000Z' })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'future-issued-time' 'EDGE-SPLIT-LEDGER-RECEIPT-TIME' {
        [void](Write-ProtocolReceipt {
                param($payload); $future = [DateTime]::UtcNow.AddMinutes(10)
                $payload.issuedUtc = $future.ToString('O'); $payload.expiresUtc = $future.AddMinutes(10).ToString('O')
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'expired-time' 'EDGE-SPLIT-LEDGER-RECEIPT-TIME' {
        [void](Write-ProtocolReceipt {
                param($payload); $past = [DateTime]::UtcNow.AddMinutes(-20)
                $payload.issuedUtc = $past.ToString('O'); $payload.expiresUtc = $past.AddMinutes(10).ToString('O')
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'excessive-lifetime' 'EDGE-SPLIT-LEDGER-RECEIPT-TIME' {
        [void](Write-ProtocolReceipt {
                param($payload); $now = [DateTime]::UtcNow
                $payload.issuedUtc = $now.ToString('O'); $payload.expiresUtc = $now.AddMinutes(31).ToString('O')
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'current-head-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-HEAD' {
        [void](Write-ProtocolReceipt {
                param($payload); $payload.authorityHead = ('0' * 40); $payload.ephemeralSnapshotHead = ('0' * 40)
            })
        $args = Get-ProtocolReceiptArguments @{
            ExpectedAuthorityHead = ('0' * 40); ExpectedEphemeralSnapshotHead = ('0' * 40)
        }
        [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'current-tree-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-TREE' {
        [void](Write-ProtocolReceipt {
                param($payload); $payload.authorityTree = ('0' * 40); $payload.ephemeralSnapshotTree = ('0' * 40)
            })
        $args = Get-ProtocolReceiptArguments @{
            ExpectedAuthorityTree = ('0' * 40); ExpectedEphemeralSnapshotTree = ('0' * 40)
        }
        [void](Assert-EdgeAuthorityReceipt @args)
    }
    Assert-ProtocolRejected 'ledger-path-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-LEDGER-PATH' {
        [void](Write-ProtocolReceipt { param($payload) $payload.ledgerPath = '.artifacts/wrong-ledger.json' })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'authority-code-mismatch' 'EDGE-SPLIT-LEDGER-RECEIPT-CODE' {
        [void](Write-ProtocolReceipt { param($payload) $payload.authorityCodeSha256 = ('0' * 64) })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'fact-order-mismatch' 'EDGE-SPLIT-AUTHORITY-FACT-SET' {
        [void](Write-ProtocolReceipt -Unchecked -MutatePayload {
                param($payload); $first = $payload.factGroups[0]; $payload.factGroups[0] = $payload.factGroups[1]
                $payload.factGroups[1] = $first
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'fact-duplicate-name' 'EDGE-SPLIT-AUTHORITY-FACT-SET' {
        [void](Write-ProtocolReceipt -Unchecked -MutatePayload {
                param($payload); $payload.factGroups[1].name = [string]$payload.factGroups[0].name
                $payload.factGroups[1].sha256 = ('f' * 64)
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'fact-plugin-source-sha' 'EDGE-SPLIT-LEDGER-FAST-PLUGIN-SOURCES' {
        [void](Write-ProtocolReceipt -MutatePayload {
                param($payload); ($payload.factGroups | Where-Object name -eq 'pluginSources').sha256 = ('0' * 64)
            })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'payload-binding-mismatch' 'EDGE-SPLIT-LEDGER-FAST-INTEGRITY' {
        [void](Write-ProtocolReceipt { param($payload) $payload.ledgerPayloadSha256 = ('0' * 64) })
        [void](Invoke-ProtocolReceiptValidation)
    }
    Assert-ProtocolRejected 'ledger-byte-binding-mismatch' 'EDGE-SPLIT-LEDGER-FAST-LEDGER-BYTES' {
        [void](Write-ProtocolReceipt { param($payload) $payload.ledgerSha256 = ('0' * 64) })
        [void](Invoke-ProtocolReceiptValidation)
    }

    $fixtureSuccess = Invoke-ProtocolFixtureChild success
    if ($fixtureSuccess.exitCode -ne 0 -or $fixtureSuccess.timedOut) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-FIXTURE explicit quick fixture positive failed.'
    }
    Assert-ProtocolRejected 'quick-fixture-crash' 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CRASH' {
        $result = Invoke-ProtocolFixtureChild crash
        if ($result.exitCode -eq 0 -or $result.timedOut) { return }
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-CRASH direct child crash was rejected after exact exit observation.'
    }
    Assert-ProtocolRejected 'quick-fixture-timeout' 'EDGE-SPLIT-AUTHORITY-PROTOCOL-TIMEOUT' {
        $pipeHoldRejected = $false
        try { Invoke-ProtocolParentExitPipeFixture }
        catch {
            if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT', [StringComparison]::Ordinal)) {
                $pipeHoldRejected = $true
            }
            else { throw }
        }
        if (-not $pipeHoldRejected) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 exited-parent inherited-pipe fixture was accepted.'
        }
        $result = Invoke-ProtocolTimeoutCleanupFixture
        if (-not $result.timedOut) { return }
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-TIMEOUT direct child timeout was rejected, its tree killed, and its exact worktree registrations removed.'
    }
    Assert-ProtocolRejected 'git-environment-mixed-case-injection' 'EDGE-SPLIT-AUTHORITY-GIT-ENV' {
        $prefixName = 'Git_Config_Key_0'
        $exactName = 'gIt_NaMeSpAcE'
        $reservedName = 'eDgE_aUtHoRiTy_CoOrDiNaToR_eXtRa'
        $priorPrefix = [Environment]::GetEnvironmentVariable($prefixName, 'Process')
        $priorExact = [Environment]::GetEnvironmentVariable($exactName, 'Process')
        $priorReserved = [Environment]::GetEnvironmentVariable($reservedName, 'Process')
        try {
            $prefixRejected = $false
            [Environment]::SetEnvironmentVariable($prefixName, 'core.hooksPath', 'Process')
            try { Assert-EdgeAuthorityGitEnvironment }
            catch {
                if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-GIT-ENV', [StringComparison]::Ordinal)) {
                    $prefixRejected = $true
                }
                else { throw }
            }
            if (-not $prefixRejected) {
                throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 mixed-case dynamic Git config key was accepted.'
            }
            [Environment]::SetEnvironmentVariable($prefixName, $priorPrefix, 'Process')
            $reservedRejected = $false
            [Environment]::SetEnvironmentVariable($reservedName, 'forbidden-binding', 'Process')
            try { Assert-EdgeAuthorityGitEnvironment }
            catch {
                if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-GIT-ENV', [StringComparison]::Ordinal)) {
                    $reservedRejected = $true
                }
                else { throw }
            }
            if (-not $reservedRejected) {
                throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 mixed-case reserved authority binding was accepted at external ingress.'
            }
            [Environment]::SetEnvironmentVariable($reservedName, $priorReserved, 'Process')
            [Environment]::SetEnvironmentVariable($exactName, 'authority-namespace', 'Process')
            Assert-EdgeAuthorityGitEnvironment
        }
        finally {
            [Environment]::SetEnvironmentVariable($prefixName, $priorPrefix, 'Process')
            [Environment]::SetEnvironmentVariable($exactName, $priorExact, 'Process')
            [Environment]::SetEnvironmentVariable($reservedName, $priorReserved, 'Process')
        }
    }

    $coordinatorSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractAuthorityCoordinator.ps1') -Raw
    $developmentSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractDevelopmentValidation.ps1') -Raw
    $formalSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractFormalValidation.ps1') -Raw
    $protocolModuleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1') -Raw
    $generatorSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1') -Raw
    $requiredWrapperSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-EdgeRequiredTests.ps1') -Raw
    $validatorSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1') -Raw
    $behaviorSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedgerBehavior.ps1') -Raw
    $requiredXunitSource = Get-Content -LiteralPath (
        Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs') -Raw
    $mutationRunnerSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Test-EdgePluginContractStaticGuard.ps1') -Raw
    $deterministicTargetsSource = Get-Content -LiteralPath (
        Join-Path $RepositoryRoot 'eng/EdgePluginContractDeterministicBuild.targets') -Raw
    Assert-EdgePluginContractStaticGuard `
        -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
        -FormalSource $formalSource `
        -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
        -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
        -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
        -DeterministicTargetsSource $deterministicTargetsSource `
        -MutationRunnerSource $mutationRunnerSource

    $formalStaticMutationRows = @(
        [pscustomobject][ordered]@{
            name = 'formal-parent-parameter-override'
            target = 'formal'
            needle = 'param()'
            replacement = 'param([string]$LedgerPath)'
            expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation entry must expose no override parameters.'
        },
        [pscustomobject][ordered]@{
            name = 'formal-parent-generator-invocation'
            target = 'formal'
            needle = '$initialPreconditions = Assert-FormalValidationPreconditions'
            replacement = "& (Join-Path `$RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1')`n`$initialPreconditions = Assert-FormalValidationPreconditions"
            expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation exposes a generator/fixture/skip/command override.'
        },
        [pscustomobject][ordered]@{
            name = 'formal-clean-second-precondition-bypass'
            target = 'formal'
            needle = '$confirmedPreconditions = Assert-FormalValidationPreconditions'
            replacement = '$confirmedPreconditions = $initialPreconditions # bypassed clean recheck'
            expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $confirmedPreconditions = Assert-FormalValidationPreconditions'
        },
        [pscustomobject][ordered]@{
            name = 'formal-i-to-e-parent-count-bypass'
            target = 'formal'
            needle = "'rev-list', '--count'"
            replacement = "'rev-list', '--max-count=1'"
            expectedMessage = "EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: 'rev-list', '--count'"
        },
        [pscustomobject][ordered]@{
            name = 'formal-cleanup-recursive-delete'
            target = 'protocol'
            needle = 'Remove-Item -LiteralPath $MarkerPath -Force'
            replacement = 'Remove-Item -LiteralPath $MarkerPath -Recurse -Force'
            expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol formal cleanup fail-closed allowlist changed.'
        })
    $formalStaticMutationNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($row in $formalStaticMutationRows) {
        [void]$formalStaticMutationNames.Add([string]$row.name)
    }
    if ($formalStaticMutationRows.Count -ne 5 -or
        $formalStaticMutationNames.Count -ne 5) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 formal static mutation inventory must remain exactly 5 unique names.'
    }
    foreach ($row in $formalStaticMutationRows) {
        $mutatedFormalSource = $formalSource
        $mutatedProtocolSource = $protocolModuleSource
        if ([string]$row.target -ceq 'formal') {
            $mutatedFormalSource = Get-ProtocolExactStaticMutation `
                -Source $formalSource -Needle ([string]$row.needle) `
                -Replacement ([string]$row.replacement) -Name ([string]$row.name)
        }
        else {
            $mutatedProtocolSource = Get-ProtocolExactStaticMutation `
                -Source $protocolModuleSource -Needle ([string]$row.needle) `
                -Replacement ([string]$row.replacement) -Name ([string]$row.name)
        }
        Assert-ProtocolStaticMutationRejected `
            -Name ([string]$row.name) `
            -ExpectedMessage ([string]$row.expectedMessage) `
            -Action {
                Assert-EdgePluginContractStaticGuard `
                    -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                    -FormalSource $mutatedFormalSource `
                    -ProtocolModuleSource $mutatedProtocolSource -GeneratorSource $generatorSource `
                    -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                    -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                    -DeterministicTargetsSource $deterministicTargetsSource `
                    -MutationRunnerSource $mutationRunnerSource
            }
        $formalStaticMutationPassed++
    }

    Assert-ProtocolRejected 'static-fixture-selectors' 'EDGE-SPLIT-AUTHORITY-STATIC-001' {
        $coordinatorRejected = $false
        try {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource ($coordinatorSource + "`nInvoke-EdgePluginContractAuthorityFixtureChild.ps1") `
                -DevelopmentSource $developmentSource -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource `
                -GeneratorSource $generatorSource -RequiredWrapperSource $requiredWrapperSource `
                -ValidatorSource $validatorSource -BehaviorSource $behaviorSource `
                -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        catch {
            if ($_.Exception.Message.Contains('EDGE-SPLIT-AUTHORITY-STATIC-001', [StringComparison]::Ordinal)) {
                $coordinatorRejected = $true
            }
            else { throw }
        }
        if (-not $coordinatorRejected) {
            throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 coordinator fixture selector was accepted.'
        }
        Assert-EdgePluginContractStaticGuard `
            -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
            -FormalSource $formalSource `
            -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
            -RequiredWrapperSource ($requiredWrapperSource + "`nEDGE_AUTHORITY_PROTOCOL_FIXTURE") `
            -ValidatorSource $validatorSource -BehaviorSource $behaviorSource `
            -RequiredXunitSource $requiredXunitSource `
            -DeterministicTargetsSource $deterministicTargetsSource `
            -MutationRunnerSource $mutationRunnerSource
    }
    Assert-ProtocolRejected 'static-pwsh-path-lookup' 'EDGE-SPLIT-AUTHORITY-STATIC-001' {
        $mutated = Get-ProtocolExactStaticMutation `
            -Source $coordinatorSource `
            -Needle '$startInfo.FileName = $script:coordinatorPowerShellPath' `
            -Replacement '$startInfo.FileName = ''pwsh''' `
            -Name 'pwsh-path-lookup'
        Assert-EdgePluginContractStaticGuard -CoordinatorSource $mutated `
            -DevelopmentSource $developmentSource -FormalSource $formalSource `
            -ProtocolModuleSource $protocolModuleSource `
            -GeneratorSource $generatorSource -RequiredWrapperSource $requiredWrapperSource `
            -ValidatorSource $validatorSource -BehaviorSource $behaviorSource `
            -RequiredXunitSource $requiredXunitSource `
            -DeterministicTargetsSource $deterministicTargetsSource `
            -MutationRunnerSource $mutationRunnerSource
    }
    Assert-ProtocolRejected 'static-git-path-lookup' 'EDGE-SPLIT-AUTHORITY-STATIC-001' {
        $devCaptureMutation = Get-ProtocolExactStaticMutation `
            -Source $developmentSource `
            -Needle '$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline' `
            -Replacement '$capture = $null # removed bounded capture wait' `
            -Name 'dev-capture-wait'
        Assert-ProtocolStaticMutationRejected 'dev-capture-wait' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $devCaptureMutation `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $coordinatorGitCaptureMutation = Get-ProtocolExactStaticMutation `
            -Source $coordinatorSource `
            -Needle '$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline' `
            -Replacement '$capture = $null # removed coordinator git capture wait' `
            -Name 'coordinator-git-capture-wait'
        Assert-ProtocolStaticMutationRejected 'coordinator-git-capture-wait' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorGitCaptureMutation -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $coordinatorChildCaptureMutation = Get-ProtocolExactStaticMutation `
            -Source $coordinatorSource `
            -Needle '$capture = Wait-EdgeBoundedCaptureTasks $Child.stdoutTask $Child.stderrTask $CaptureDeadlineUtc' `
            -Replacement '$capture = $null # removed authority child capture wait' `
            -Name 'coordinator-authority-capture-wait'
        Assert-ProtocolStaticMutationRejected 'coordinator-authority-capture-wait' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorChildCaptureMutation -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $cleanupCaptureMutation = Get-ProtocolExactStaticMutation `
            -Source $protocolModuleSource `
            -Needle '$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline' `
            -Replacement '$capture = $null # removed cleanup capture wait' `
            -Name 'cleanup-capture-wait'
        Assert-ProtocolStaticMutationRejected 'cleanup-capture-wait' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $cleanupCaptureMutation -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $devParentBindingMutation = Get-ProtocolExactStaticMutation `
            -Source $developmentSource `
            -Needle '-Environment $coordinatorEnvironment' `
            -Replacement '-Environment $null # removed canonical coordinator parent binding' `
            -Name 'dev-coordinator-parent-binding'
        Assert-ProtocolStaticMutationRejected 'dev-coordinator-parent-binding' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $devParentBindingMutation `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $coordinatorRequestBindingMutation = Get-ProtocolExactStaticMutation `
            -Source $coordinatorSource `
            -Needle 'Assert-EdgeAuthorityCoordinatorParentRequest -Binding $coordinatorParentBinding -Request $request' `
            -Replacement '# removed parent/request cross-binding' `
            -Name 'coordinator-parent-request-binding'
        Assert-ProtocolStaticMutationRejected 'coordinator-parent-request-binding' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorRequestBindingMutation -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $generatorBindingMutation = Get-ProtocolExactStaticMutation `
            -Source $generatorSource `
            -Needle '$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)' `
            -Replacement '$authorityChildEnvironmentBound = $false # removed generator child binding initializer' `
            -Name 'generator-child-binding'
        Assert-ProtocolStaticMutationRejected 'generator-child-binding' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorBindingMutation `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $generatorFallbackMutation = Get-ProtocolExactStaticMutation `
            -Source $generatorSource `
            -Needle "if (-not `$authorityChildEnvironmentBound) {`n    Assert-EdgeAuthorityGitEnvironment`n}" `
            -Replacement '# removed generator external clean-ingress fallback' `
            -Name 'generator-external-fallback'
        Assert-ProtocolStaticMutationRejected 'generator-external-fallback' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorFallbackMutation `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $validatorBindingMutation = Get-ProtocolExactStaticMutation `
            -Source $validatorSource `
            -Needle '$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)' `
            -Replacement '$authorityChildEnvironmentBound = $false # removed validator child binding initializer' `
            -Name 'validator-child-binding'
        Assert-ProtocolStaticMutationRejected 'validator-child-binding' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorBindingMutation `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $validatorFallbackMutation = Get-ProtocolExactStaticMutation `
            -Source $validatorSource `
            -Needle "if (-not `$authorityChildEnvironmentBound) {`n    Assert-EdgeAuthorityGitEnvironment`n}" `
            -Replacement '# removed validator external clean-ingress fallback' `
            -Name 'validator-external-fallback'
        Assert-ProtocolStaticMutationRejected 'validator-external-fallback' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorFallbackMutation `
                -BehaviorSource $behaviorSource -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $behaviorBindingMutation = Get-ProtocolExactStaticMutation `
            -Source $behaviorSource `
            -Needle 'if (-not [bool](Initialize-EdgeAuthorityGitChildEnvironment))' `
            -Replacement 'if ($false) # removed behavior child binding initializer' `
            -Name 'behavior-child-binding'
        Assert-ProtocolStaticMutationRejected 'behavior-child-binding' {
            Assert-EdgePluginContractStaticGuard `
                -CoordinatorSource $coordinatorSource -DevelopmentSource $developmentSource `
                -FormalSource $formalSource `
                -ProtocolModuleSource $protocolModuleSource -GeneratorSource $generatorSource `
                -RequiredWrapperSource $requiredWrapperSource -ValidatorSource $validatorSource `
                -BehaviorSource $behaviorBindingMutation -RequiredXunitSource $requiredXunitSource `
                -DeterministicTargetsSource $deterministicTargetsSource `
                -MutationRunnerSource $mutationRunnerSource
        }
        $mutated = Get-ProtocolExactStaticMutation `
            -Source $coordinatorSource `
            -Needle '$startInfo.FileName = $script:coordinatorGitPath' `
            -Replacement '$startInfo.FileName = ''git''' `
            -Name 'git-path-lookup'
        Assert-EdgePluginContractStaticGuard -CoordinatorSource $mutated `
            -DevelopmentSource $developmentSource -FormalSource $formalSource `
            -ProtocolModuleSource $protocolModuleSource `
            -GeneratorSource $generatorSource -RequiredWrapperSource $requiredWrapperSource `
            -ValidatorSource $validatorSource -BehaviorSource $behaviorSource `
            -RequiredXunitSource $requiredXunitSource `
            -DeterministicTargetsSource $deterministicTargetsSource `
            -MutationRunnerSource $mutationRunnerSource
    }

    if ($formalSchemaPositiveCount -ne 4 -or
        $formalSignedPositiveCount -ne 1 -or
        $formalTransitionPositiveCount -ne 1 -or
        $formalStaticMutationPassed -ne 5) {
        throw 'EDGE-SPLIT-AUTHORITY-PROTOCOL-001 formal synthetic coverage inventory drifted.'
    }
    if ($negativeCount -ne 48) {
        throw "EDGE-SPLIT-AUTHORITY-PROTOCOL-001 protocol negative inventory drifted: $negativeCount/48."
    }
    Write-Host 'Edge plugin contract authority protocol fixtures passed: schema-positive=7, signed-positive=2, formal-transition=1/1, formal-static=5/5, negatives=48/48, authorityLaunches=0, replayLaunches=0.'
}
finally {
    $privateKey.Dispose()
    if (Test-Path -LiteralPath $receiptFullPath -PathType Leaf) { Remove-Item -LiteralPath $receiptFullPath -Force }
}
