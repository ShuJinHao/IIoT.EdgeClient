[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else { $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }

$generatorPath = Join-Path $RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1'
$validatorPath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgePluginContractLedger.ps1'
$protocolModulePath = Join-Path $RepositoryRoot 'scripts/tests/EdgePluginContractLedger.Protocol.psm1'
Import-Module $protocolModulePath -Force

function Get-FunctionDefinitionTexts {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $ScriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 primitive fixture cannot parse $ScriptPath."
    }
    foreach ($name in $Names) {
        $matches = @($ast.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                    [string]$node.Name -ceq $name
                }, $true))
        if ($matches.Count -ne 1) {
            throw "EDGE-SPLIT-LEDGER-001 primitive fixture requires exactly one function '$name' in $ScriptPath."
        }
        [string]$matches[0].Extent.Text
    }
}

function Assert-ExactText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Expected,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Fixture
    )
    if ($Actual -cne $Expected) {
        throw "EDGE-SPLIT-LEDGER-001 primitive fixture '$Fixture' differs: expected='$Expected' actual='$Actual'."
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)][string]$FixtureName,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )
    $rejected = $false
    try { & $Action }
    catch {
        if (($_ | Out-String).Contains('EDGE-SPLIT-LEDGER-001', [StringComparison]::Ordinal)) {
            $rejected = $true
        }
        else { throw }
    }
    if (-not $rejected) {
        throw "EDGE-SPLIT-LEDGER-001 primitive fixture '$FixtureName' did not reject an ambient absolute path."
    }
}

$generatorFunctions = @(
    'ConvertTo-AuthorityTokenText',
    'ConvertTo-RestoreProjectionValue',
    'Get-ProcessEnvironmentStateSnapshot',
    'Restore-ProcessEnvironmentStateSnapshot'
)
$validatorFunctions = @(
    'Sort-Ordinal',
    'ConvertTo-IndependentTokenText',
    'ConvertTo-IndependentRestoreValue',
    'Get-IndependentEnvironmentSnapshot',
    'Restore-IndependentEnvironmentSnapshot',
    'Assert-IndependentAuthorityInventoriesEqual'
)
foreach ($definition in @(Get-FunctionDefinitionTexts -ScriptPath $generatorPath -Names $generatorFunctions)) {
    Invoke-Expression $definition
}
foreach ($definition in @(Get-FunctionDefinitionTexts -ScriptPath $validatorPath -Names $validatorFunctions)) {
    Invoke-Expression $definition
}

$fixtureCount = 0
[byte[]]$emptyBytes = [byte[]]::new(0)
Assert-ExactText 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' `
    (Get-EdgeSha256Bytes $emptyBytes) 'protocol-empty-byte-array-sha256'; $fixtureCount++

$generatorMappings = [object[]]@(
    [pscustomobject]@{ root = '/approved/repository'; token = '$REPOSITORY' }
)
$validatorMappings = [object[]]@(
    [pscustomobject]@{ root = '/approved/repository'; token = '$REPOSITORY' }
)

Assert-ExactText 'http://schemas.microsoft.com/developer/msbuild/2003' `
    (ConvertTo-AuthorityTokenText -Text 'http://schemas.microsoft.com/developer/msbuild/2003' -RootMappings $generatorMappings) `
    'generator-xml-namespace-url'; $fixtureCount++
Assert-ExactText 'https://api.nuget.org/v3/index.json' `
    (ConvertTo-AuthorityTokenText -Text 'https://api.nuget.org/v3/index.json' -RootMappings $generatorMappings) `
    'generator-https-url'; $fixtureCount++
Assert-ExactText '$REPOSITORY/src/A.cs' `
    (ConvertTo-AuthorityTokenText -Text '/approved/repository/src/A.cs' -RootMappings $generatorMappings) `
    'generator-approved-root-token'; $fixtureCount++
foreach ($fixture in @(
        [pscustomobject]@{ name = 'generator-rooted-windows'; value = 'C:/ambient/file.props' },
        [pscustomobject]@{ name = 'generator-embedded-windows'; value = 'input=C:/ambient/file.props' },
        [pscustomobject]@{ name = 'generator-rooted-unix'; value = '/Users/example/file.props' },
        [pscustomobject]@{ name = 'generator-unc'; value = '\\server\share\file.props' }
    )) {
    Assert-Rejected $fixture.name {
        [void](ConvertTo-AuthorityTokenText -Text $fixture.value -RootMappings $generatorMappings)
    }
    $fixtureCount++
}

Assert-ExactText 'http://schemas.microsoft.com/developer/msbuild/2003' `
    (ConvertTo-IndependentTokenText -InputText 'http://schemas.microsoft.com/developer/msbuild/2003' -Mappings $validatorMappings) `
    'validator-xml-namespace-url'; $fixtureCount++
Assert-ExactText 'https://api.nuget.org/v3/index.json' `
    (ConvertTo-IndependentTokenText -InputText 'https://api.nuget.org/v3/index.json' -Mappings $validatorMappings) `
    'validator-https-url'; $fixtureCount++
Assert-ExactText '$REPOSITORY/src/A.cs' `
    (ConvertTo-IndependentTokenText -InputText '/approved/repository/src/A.cs' -Mappings $validatorMappings) `
    'validator-approved-root-token'; $fixtureCount++
foreach ($fixture in @(
        [pscustomobject]@{ name = 'validator-rooted-windows'; value = 'C:/ambient/file.props' },
        [pscustomobject]@{ name = 'validator-embedded-windows'; value = 'input=C:/ambient/file.props' },
        [pscustomobject]@{ name = 'validator-rooted-unix'; value = '/Users/example/file.props' },
        [pscustomobject]@{ name = 'validator-unc'; value = '\\server\share\file.props' }
    )) {
    Assert-Rejected $fixture.name {
        [void](ConvertTo-IndependentTokenText -InputText $fixture.value -Mappings $validatorMappings)
    }
    $fixtureCount++
}

$environmentFixtureNames = [string[]]@(
    "EDGE_LEDGER_ENV_UNDEFINED_$([Guid]::NewGuid().ToString('N'))",
    "EDGE_LEDGER_ENV_EMPTY_$([Guid]::NewGuid().ToString('N'))",
    "EDGE_LEDGER_ENV_VALUE_$([Guid]::NewGuid().ToString('N'))"
)
try {
    Remove-Item -LiteralPath "Env:$($environmentFixtureNames[0])" -Force -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable($environmentFixtureNames[1], '', 'Process')
    [Environment]::SetEnvironmentVariable($environmentFixtureNames[2], 'before', 'Process')
    $generatorSnapshot = Get-ProcessEnvironmentStateSnapshot -Names $environmentFixtureNames
    foreach ($name in $environmentFixtureNames) {
        [Environment]::SetEnvironmentVariable($name, 'mutated', 'Process')
    }
    Restore-ProcessEnvironmentStateSnapshot -Names $environmentFixtureNames -Snapshot $generatorSnapshot
    if ($null -ne [Environment]::GetEnvironmentVariable($environmentFixtureNames[0], 'Process') -or
        [Environment]::GetEnvironmentVariable($environmentFixtureNames[1], 'Process') -cne '' -or
        [Environment]::GetEnvironmentVariable($environmentFixtureNames[2], 'Process') -cne 'before') {
        throw 'EDGE-SPLIT-LEDGER-001 generator environment restore did not preserve undefined/empty/value states.'
    }
    $fixtureCount++

    $validatorSnapshot = Get-IndependentEnvironmentSnapshot -VariableNames $environmentFixtureNames
    foreach ($name in $environmentFixtureNames) {
        [Environment]::SetEnvironmentVariable($name, 'mutated-again', 'Process')
    }
    Restore-IndependentEnvironmentSnapshot -VariableNames $environmentFixtureNames -States $validatorSnapshot
    if ($null -ne [Environment]::GetEnvironmentVariable($environmentFixtureNames[0], 'Process') -or
        [Environment]::GetEnvironmentVariable($environmentFixtureNames[1], 'Process') -cne '' -or
        [Environment]::GetEnvironmentVariable($environmentFixtureNames[2], 'Process') -cne 'before') {
        throw 'EDGE-SPLIT-LEDGER-001 validator environment restore did not preserve undefined/empty/value states.'
    }
    $fixtureCount++
}
finally {
    foreach ($name in $environmentFixtureNames) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        Remove-Item -LiteralPath "Env:$name" -Force -ErrorAction SilentlyContinue
    }
}

$baseAuthorityFact = [pscustomobject][ordered]@{
    path = 'src/Test.csproj'
    origin = 'tracked-repository'
    representation = 'raw-sha256'
    roles = @('evaluated-project')
    size = 10
    sha256 = ('1' * 64)
}
$extraAuthorityFact = [pscustomobject][ordered]@{
    path = 'dotnet-toolchain/sdk/tool.dll'
    origin = 'dotnet-toolchain'
    representation = 'raw-sha256'
    roles = @('executed-toolchain-assembly')
    size = 20
    sha256 = ('2' * 64)
}
$keyDiagnostic = ''
try {
    Assert-IndependentAuthorityInventoriesEqual `
        -Recorded ([object[]]@($baseAuthorityFact)) `
        -Recomputed ([object[]]@($extraAuthorityFact))
}
catch { $keyDiagnostic = $_ | Out-String }
if (-not $keyDiagnostic.Contains('recordedCount=1 recomputedCount=1', [StringComparison]::Ordinal) -or
    -not $keyDiagnostic.Contains('firstRecordedOnly=tracked-repository|src/Test.csproj', [StringComparison]::Ordinal) -or
    -not $keyDiagnostic.Contains('firstRecomputedOnly=dotnet-toolchain|dotnet-toolchain/sdk/tool.dll', [StringComparison]::Ordinal)) {
    throw 'EDGE-SPLIT-LEDGER-001 synthetic authority key comparer fixture lacks safe count/first-diff diagnostics.'
}
$fixtureCount++

$roleMismatchFact = [pscustomobject][ordered]@{
    path = 'src/Test.csproj'
    origin = 'tracked-repository'
    representation = 'raw-sha256'
    roles = @('root-configuration')
    size = 10
    sha256 = ('1' * 64)
}
$fieldDiagnostic = ''
try {
    Assert-IndependentAuthorityInventoriesEqual `
        -Recorded ([object[]]@($baseAuthorityFact)) `
        -Recomputed ([object[]]@($roleMismatchFact))
}
catch { $fieldDiagnostic = $_ | Out-String }
if (-not $fieldDiagnostic.Contains('key=tracked-repository|src/Test.csproj field=roles', [StringComparison]::Ordinal) -or
    $fieldDiagnostic.Contains($RepositoryRoot, [StringComparison]::Ordinal)) {
    throw 'EDGE-SPLIT-LEDGER-001 synthetic authority field comparer fixture lacks safe tokenized diagnostics.'
}
$fixtureCount++

function New-RootKeyDocument {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$Nested,
        [switch]$CollidingTokenKey
    )

    $rootKey = if ($Nested) { "$Root/nested" } else { $Root }
    $json = if ($CollidingTokenKey) {
        "{`"packageFolders`":{`"$rootKey`":{},`"`$NUGET_PACKAGES`":{}}}"
    }
    elseif ($Nested) { "{`"outer`":{`"$rootKey`":{}}}" }
    else { "{`"packageFolders`":{`"$rootKey`":{}},`"packagesPath`":`"$Root`"}" }
    return $json | ConvertFrom-Json -Depth 20
}

$generatorRootA = '/tmp/edge-contract-ledger-aaa/nuget-packages'
$generatorRootB = '/tmp/edge-ledger-validator-longer-bbb/nuget-packages'
$generatorProjectionA = ConvertTo-RestoreProjectionValue `
    -Value (New-RootKeyDocument -Root $generatorRootA) `
    -RootMappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
    -JsonPointer ''
$generatorProjectionB = ConvertTo-RestoreProjectionValue `
    -Value (New-RootKeyDocument -Root $generatorRootB) `
    -RootMappings ([object[]]@([pscustomobject]@{ root = $generatorRootB; token = '$NUGET_PACKAGES' })) `
    -JsonPointer ''
Assert-ExactText ($generatorProjectionA | ConvertTo-Json -Depth 20 -Compress) `
    ($generatorProjectionB | ConvertTo-Json -Depth 20 -Compress) `
    'generator-package-folder-key-root-stability'; $fixtureCount++

$validatorProjectionA = ConvertTo-IndependentRestoreValue `
    -InputValue (New-RootKeyDocument -Root $generatorRootA) `
    -Mappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
    -Pointer ''
$validatorProjectionB = ConvertTo-IndependentRestoreValue `
    -InputValue (New-RootKeyDocument -Root $generatorRootB) `
    -Mappings ([object[]]@([pscustomobject]@{ root = $generatorRootB; token = '$NUGET_PACKAGES' })) `
    -Pointer ''
Assert-ExactText ($validatorProjectionA | ConvertTo-Json -Depth 20 -Compress) `
    ($validatorProjectionB | ConvertTo-Json -Depth 20 -Compress) `
    'validator-package-folder-key-root-stability'; $fixtureCount++
Assert-ExactText ($generatorProjectionA | ConvertTo-Json -Depth 20 -Compress) `
    ($validatorProjectionA | ConvertTo-Json -Depth 20 -Compress) `
    'generator-validator-package-folder-projection-a'
Assert-ExactText ($generatorProjectionB | ConvertTo-Json -Depth 20 -Compress) `
    ($validatorProjectionB | ConvertTo-Json -Depth 20 -Compress) `
    'generator-validator-package-folder-projection-b'; $fixtureCount++

$nestedGenerator = ConvertTo-RestoreProjectionValue `
    -Value (New-RootKeyDocument -Root $generatorRootA -Nested) `
    -RootMappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
    -JsonPointer ''
$nestedValidator = ConvertTo-IndependentRestoreValue `
    -InputValue (New-RootKeyDocument -Root $generatorRootA -Nested) `
    -Mappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
    -Pointer ''
$nestedGeneratorKey = [string](@($nestedGenerator.outer.PSObject.Properties)[0].Name)
$nestedValidatorKey = [string](@($nestedValidator.outer.PSObject.Properties)[0].Name)
if ($nestedGeneratorKey -cne '$NUGET_PACKAGES/nested' -or
    $nestedValidatorKey -cne '$NUGET_PACKAGES/nested') {
    throw 'EDGE-SPLIT-LEDGER-001 nested restore JSON property names were not root-tokenized by both implementations.'
}
$fixtureCount++

$collisionDocument = New-RootKeyDocument -Root $generatorRootA -CollidingTokenKey
$generatorCollision = ''
try {
    [void](ConvertTo-RestoreProjectionValue -Value $collisionDocument `
            -RootMappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
            -JsonPointer '')
}
catch { $generatorCollision = $_ | Out-String }
$validatorCollision = ''
try {
    [void](ConvertTo-IndependentRestoreValue -InputValue $collisionDocument `
            -Mappings ([object[]]@([pscustomobject]@{ root = $generatorRootA; token = '$NUGET_PACKAGES' })) `
            -Pointer '')
}
catch { $validatorCollision = $_ | Out-String }
if (-not $generatorCollision.Contains('collide after root tokenization', [StringComparison]::Ordinal) -or
    -not $validatorCollision.Contains('collide after root tokenization', [StringComparison]::Ordinal)) {
    throw 'EDGE-SPLIT-LEDGER-001 normalized restore JSON key collisions were not rejected by both implementations.'
}
$fixtureCount++

$caseCollisionJson = '{"keys":{"Alpha":1,"alpha":2}}'
$caseCollisionParseErrorId = ''
try {
    [void]($caseCollisionJson | ConvertFrom-Json -Depth 20)
}
catch { $caseCollisionParseErrorId = [string]$_.FullyQualifiedErrorId }
if ($caseCollisionParseErrorId -cne
    'KeysWithDifferentCasingInJsonString,Microsoft.PowerShell.Commands.ConvertFromJsonCommand') {
    throw 'EDGE-SPLIT-LEDGER-001 case-only restore JSON keys were not rejected at the shared ConvertFrom-Json parser boundary.'
}
$fixtureCount++

Write-Host "Edge plugin contract ledger primitive fixtures passed: $fixtureCount/$fixtureCount."
