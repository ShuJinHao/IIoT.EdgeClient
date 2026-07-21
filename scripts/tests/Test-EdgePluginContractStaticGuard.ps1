[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else { $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }

$modulePath = Join-Path $PSScriptRoot 'EdgePluginContractStaticGuard.psm1'
Import-Module $modulePath -Force

$sources = [ordered]@{
    CoordinatorSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Invoke-EdgePluginContractAuthorityCoordinator.ps1') -Raw
    DevelopmentSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Invoke-EdgePluginContractDevelopmentValidation.ps1') -Raw
    FormalSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Invoke-EdgePluginContractFormalValidation.ps1') -Raw
    ProtocolModuleSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1') -Raw
    GeneratorSource = Get-Content -LiteralPath (
        Join-Path $RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1') -Raw
    RequiredWrapperSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Invoke-EdgeRequiredTests.ps1') -Raw
    ValidatorSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1') -Raw
    BehaviorSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Test-EdgePluginContractLedgerBehavior.ps1') -Raw
    RequiredXunitSource = Get-Content -LiteralPath (
        Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs') -Raw
    DeterministicTargetsSource = Get-Content -LiteralPath (
        Join-Path $RepositoryRoot 'eng/EdgePluginContractDeterministicBuild.targets') -Raw
    MutationRunnerSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot 'Test-EdgePluginContractStaticGuard.ps1') -Raw
}

function Invoke-CanonicalStaticGuard {
    param([Parameter(Mandatory)][Collections.IDictionary]$SourceSet, [switch]$PassThru)
    return Assert-EdgePluginContractStaticGuard `
        -CoordinatorSource ([string]$SourceSet.CoordinatorSource) `
        -DevelopmentSource ([string]$SourceSet.DevelopmentSource) `
        -FormalSource ([string]$SourceSet.FormalSource) `
        -ProtocolModuleSource ([string]$SourceSet.ProtocolModuleSource) `
        -GeneratorSource ([string]$SourceSet.GeneratorSource) `
        -RequiredWrapperSource ([string]$SourceSet.RequiredWrapperSource) `
        -ValidatorSource ([string]$SourceSet.ValidatorSource) `
        -BehaviorSource ([string]$SourceSet.BehaviorSource) `
        -RequiredXunitSource ([string]$SourceSet.RequiredXunitSource) `
        -DeterministicTargetsSource ([string]$SourceSet.DeterministicTargetsSource) `
        -MutationRunnerSource ([string]$SourceSet.MutationRunnerSource) `
        -PassThru:$PassThru
}

function Replace-StaticGuardExact {
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
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' needle count is $count, expected one."
    }
    $mutated = $Source.Replace($Needle, $Replacement)
    if ([string]::Equals($mutated, $Source, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' mutation was a no-op."
    }
    return $mutated
}

function Replace-StaticGuardOccurrence {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Replacement,
        [Parameter(Mandatory)][int]$Occurrence,
        [Parameter(Mandatory)][int]$ExpectedCount,
        [Parameter(Mandatory)][string]$Name
    )
    $matches = [Text.RegularExpressions.Regex]::Matches(
        $Source, [Text.RegularExpressions.Regex]::Escape($Needle),
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($matches.Count -ne $ExpectedCount -or
        $Occurrence -lt 0 -or $Occurrence -ge $matches.Count) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' occurrence inventory is $($matches.Count)/$Occurrence, expected $ExpectedCount."
    }
    $match = $matches[$Occurrence]
    $mutated = $Source.Remove($match.Index, $match.Length).Insert($match.Index, $Replacement)
    if ([string]::Equals($mutated, $Source, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' occurrence mutation was a no-op."
    }
    return $mutated
}

function Add-StaticGuardStatementAfter {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Anchor,
        [Parameter(Mandatory)][string]$Statement,
        [Parameter(Mandatory)][string]$Name
    )
    return Replace-StaticGuardExact $Source $Anchor (
        $Anchor + "`n" + $Statement + " # mutation:$Name") $Name
}

$developmentMutationNames = @(
    'cmdletbinding-drift', 'repository-param-type', 'plugin-default',
    'configuration-default', 'configuration-validateset', 'current-batch-default',
    'viewids-default', 'timeout-default', 'timeout-range', 'timeout-executable-default',
    'strictmode-downgrade', 'erroraction-silence', 'repository-fallback-parent',
    'protocol-module-path', 'import-module-global', 'powershell-path-lookup',
    'git-command-lookup', 'git-path-forge', 'maximum-output-drift',
    'empty-config-rhs', 'child-environment-rhs', 'pinned-path-rhs',
    'prepin-start-process', 'prepin-static-process-start', 'prepin-file-write',
    'prepin-native-command', 'prepin-new-object-process', 'prepin-invoke-expression',
    'prepin-set-alias', 'prepin-env-provider-write', 'prepin-direct-child',
    'outer-before-pin-file-write', 'shadow-remove-item', 'shadow-get-item',
    'shadow-invokedev-case', 'nested-shadow-get-item', 'second-process-startinfo',
    'second-process-owner', 'process-startinfo-rebind', 'static-process-start-owner',
    'start-process-owner', 'new-object-owner', 'add-type-owner',
    'environment-clear', 'environmentvariables-remove',
    'environment-collection-replace', 'environment-extra-index-write',
    'overlay-case-downgrade', 'overlay-source-replace', 'overlay-value-replace',
    'overlay-guard-disable', 'process-scoped-alias', 'process-ref-escape',
    'temporary-root-postwrite', 'current-directory-postwrite',
    'physical-root-postwrite', 'physical-root-return-replace',
    'current-directory-set-disable', 'current-directory-restore-disable',
    'helper-resolve-path', 'helper-readlink', 'helper-process-start',
    'physical-temp-rhs-replace', 'physical-temp-postwrite',
    'physical-temp-scoped-alias', 'physical-temp-ref-escape',
    'original-snapshot-index-write', 'original-snapshot-add',
    'original-snapshot-alias', 'presence-overwrite', 'original-value-overwrite',
    'tmpdir-lowercase-static-set', 'pin-target-user', 'pin-computed-name',
    'pinned-snapshot-index-write', 'pinned-snapshot-clear',
    'pinned-snapshot-alias', 'restoration-snapshot-index-write',
    'restoration-snapshot-remove', 'restoration-snapshot-alias',
    'restore-presence-check-disable', 'restore-value-index-replace',
    'remove-coordinator-cleanup', 'remove-outer-cleanup',
    'tmp-first-failure-overwrite', 'cwd-first-failure-overwrite',
    'absent-restore-alias'
)

$focusedMutationRows = @(
    [pscustomobject]@{ name = 'dev-remove-coordinator-cleanup'; target = 'development' },
    [pscustomobject]@{ name = 'dev-remove-outer-cleanup'; target = 'development' },
    [pscustomobject]@{ name = 'module-top-start-process'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-top-static-process-start'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-top-file-write'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-strictmode-downgrade'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-erroraction-silence'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-function-shadow-get-item'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-function-shadow-remove-item'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-second-process-start-info'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-process-startinfo-rebind'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-static-process-start-owner'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-addtype-process-injection'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-addtype-static-constructor'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-addtype-file-write-injection'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-cleanup-reachable-from-ingress'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-cleanup-reachable-from-resolver'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-prepin-helper-operator-drift'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-export-alias-surface'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-top-pure-expression'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-dynamic-command'; target = 'protocolModule' },
    [pscustomobject]@{ name = 'module-dynamic-member'; target = 'protocolModule' }
)

$closureMutationNames = @(
    'cleanup-condition-disabled',
    'outer-cleanup-unreachable',
    'post-pin-early-success-exit'
)

function New-DevelopmentStaticMutation {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Name)
    switch ($Name) {
        'cmdletbinding-drift' {
            return Replace-StaticGuardExact $Source '[CmdletBinding()]' '[CmdletBinding(PositionalBinding = $false)]' $Name
        }
        'repository-param-type' {
            return Replace-StaticGuardExact $Source '[string]$RepositoryRoot,' '[object]$RepositoryRoot,' $Name
        }
        'plugin-default' {
            return Replace-StaticGuardExact $Source "[string]`$PluginProject = 'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj'," "[string]`$PluginProject = 'src/Mutated.csproj'," $Name
        }
        'configuration-default' {
            return Replace-StaticGuardExact $Source "[ValidateSet('Debug', 'Release')][string]`$Configuration = 'Release'," "[ValidateSet('Debug', 'Release')][string]`$Configuration = 'Debug'," $Name
        }
        'configuration-validateset' {
            return Replace-StaticGuardExact $Source "[ValidateSet('Debug', 'Release')]" "[ValidateSet('Release')]" $Name
        }
        'current-batch-default' {
            return Replace-StaticGuardExact $Source "[string]`$CurrentBatch = 'EDGE-SPLIT-000'," "[string]`$CurrentBatch = 'EDGE-SPLIT-010'," $Name
        }
        'viewids-default' {
            return Replace-StaticGuardExact $Source "[string]`$ViewIdsTypeName = 'IIoT.Edge.Presentation.Navigation.PluginSystem.StandardModuleViewIds'," "[string]`$ViewIdsTypeName = 'Mutated.ViewIds'," $Name
        }
        'timeout-default' {
            return Replace-StaticGuardExact $Source '[ValidateRange(60, 900)][int]$AuthorityTimeoutSeconds = 900' '[ValidateRange(60, 900)][int]$AuthorityTimeoutSeconds = 899' $Name
        }
        'timeout-range' {
            return Replace-StaticGuardExact $Source '[ValidateRange(60, 900)]' '[ValidateRange(1, 1800)]' $Name
        }
        'timeout-executable-default' {
            return Replace-StaticGuardExact $Source '[ValidateRange(1, 1800)][int]$TimeoutSeconds,' '[ValidateRange(1, 3600)][int]$TimeoutSeconds,' $Name
        }
        'strictmode-downgrade' {
            return Replace-StaticGuardExact $Source 'Set-StrictMode -Version Latest' 'Set-StrictMode -Version 3' $Name
        }
        'erroraction-silence' {
            return Replace-StaticGuardExact $Source '$ErrorActionPreference = ''Stop''' '$ErrorActionPreference = ''Continue''' $Name
        }
        'repository-fallback-parent' {
            return Replace-StaticGuardExact $Source "Join-Path `$PSScriptRoot '../..'" "Join-Path `$PSScriptRoot '../../..'" $Name
        }
        'protocol-module-path' {
            return Replace-StaticGuardExact $Source "'EdgePluginContractLedger.Protocol.psm1'" "'Mutated.Protocol.psm1'" $Name
        }
        'import-module-global' {
            return Replace-StaticGuardExact $Source 'Import-Module $protocolModulePath -Force' 'Import-Module $protocolModulePath -Force -Global' $Name
        }
        'powershell-path-lookup' {
            return Replace-StaticGuardExact $Source '$powerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)' '$powerShellPath = ''pwsh''' $Name
        }
        'git-command-lookup' {
            return Replace-StaticGuardExact $Source 'Get-Command git -CommandType Application -ErrorAction Stop' 'Get-Command git -CommandType All -ErrorAction Stop' $Name
        }
        'git-path-forge' {
            return Replace-StaticGuardExact $Source '$gitPath = Resolve-EdgeFixedExecutable ([string]$gitCommand.Source)' '$gitPath = ''git''' $Name
        }
        'maximum-output-drift' {
            return Replace-StaticGuardExact $Source '$devMaximumCapturedBytes = 16777216' '$devMaximumCapturedBytes = 1' $Name
        }
        'empty-config-rhs' {
            return Replace-StaticGuardExact $Source '$devEmptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $RepositoryRoot' '$devEmptyGitConfigPath = $null' $Name
        }
        'child-environment-rhs' {
            return Replace-StaticGuardExact $Source '$devGitChildEnvironment = New-EdgeAuthorityGitChildEnvironment $devEmptyGitConfigPath $gitPath' '$devGitChildEnvironment = @{}' $Name
        }
        'pinned-path-rhs' {
            return Replace-StaticGuardExact $Source '$devPinnedPath = Get-EdgeAuthorityPinnedPath $gitPath' '$devPinnedPath = [Environment]::GetEnvironmentVariable(''PATH'')' $Name
        }
        'prepin-start-process' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' 'Start-Process -FilePath $powerShellPath' $Name
        }
        'prepin-static-process-start' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$null = [Diagnostics.Process]::Start($powerShellPath)' $Name
        }
        'prepin-file-write' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '[IO.File]::WriteAllBytes((Join-Path $physicalTempRoot ''prepin.bin''), [byte[]]@(1))' $Name
        }
        'prepin-native-command' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '& $gitPath --version' $Name
        }
        'prepin-new-object-process' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$null = New-Object Diagnostics.Process' $Name
        }
        'prepin-invoke-expression' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' 'Invoke-Expression ''Get-Date''' $Name
        }
        'prepin-set-alias' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' 'Set-Alias -Name edgePrePinMutation -Value Get-Item' $Name
        }
        'prepin-env-provider-write' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$Env:TMPDIR = $physicalTempRoot' $Name
        }
        'prepin-direct-child' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$null = Invoke-DevProcess -FileName $powerShellPath -Arguments @(''-NoLogo'') -WorkingDirectory $RepositoryRoot -TimeoutSeconds 1 -InputBytes $null -Environment $null' $Name
        }
        'outer-before-pin-file-write' {
            return Replace-StaticGuardExact $Source `
                "try {`n    [Environment]::SetEnvironmentVariable(" `
                "try {`n    [IO.File]::WriteAllText((Join-Path `$physicalTempRoot 'outer-prepin.txt'), 'x')`n    [Environment]::SetEnvironmentVariable(" $Name
        }
        'shadow-remove-item' {
            return Add-StaticGuardStatementAfter $Source '$devPinnedPath = Get-EdgeAuthorityPinnedPath $gitPath' 'function Remove-Item { throw ''mutated remove owner'' }' $Name
        }
        'shadow-get-item' {
            return Add-StaticGuardStatementAfter $Source '$devPinnedPath = Get-EdgeAuthorityPinnedPath $gitPath' 'function Get-Item { throw ''mutated get owner'' }' $Name
        }
        'shadow-invokedev-case' {
            return Add-StaticGuardStatementAfter $Source '$devPinnedPath = Get-EdgeAuthorityPinnedPath $gitPath' 'function invoke-devprocess { throw ''mutated process owner'' }' $Name
        }
        'nested-shadow-get-item' {
            return Add-StaticGuardStatementAfter $Source 'function Resolve-DevPhysicalTempRoot {' '    function Get-Item { throw ''mutated nested get owner'' }' $Name
        }
        'second-process-startinfo' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$secondStartInfo = [Diagnostics.ProcessStartInfo]::new()' $Name
        }
        'second-process-owner' {
            return Add-StaticGuardStatementAfter $Source '$process = [Diagnostics.Process]::new()' '$secondProcess = [Diagnostics.Process]::new()' $Name
        }
        'process-startinfo-rebind' {
            return Add-StaticGuardStatementAfter $Source '$process.StartInfo = $startInfo' '$process.StartInfo = [Diagnostics.ProcessStartInfo]::new()' $Name
        }
        'static-process-start-owner' {
            return Add-StaticGuardStatementAfter $Source '$process.StartInfo = $startInfo' '$null = [Diagnostics.Process]::Start($FileName)' $Name
        }
        'start-process-owner' {
            return Add-StaticGuardStatementAfter $Source '$process.StartInfo = $startInfo' 'Start-Process -FilePath $FileName' $Name
        }
        'new-object-owner' {
            return Add-StaticGuardStatementAfter $Source '$process = [Diagnostics.Process]::new()' '$secondProcess = New-Object Diagnostics.Process' $Name
        }
        'add-type-owner' {
            return Add-StaticGuardStatementAfter $Source '$process = [Diagnostics.Process]::new()' 'Add-Type -TypeDefinition ''public sealed class EdgeDevelopmentMutation {}''' $Name
        }
        'environment-clear' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$startInfo.Environment.Clear()' $Name
        }
        'environmentvariables-remove' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$null = $startInfo.EnvironmentVariables.Remove(''PATH'')' $Name
        }
        'environment-collection-replace' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$startInfo.Environment = [Collections.Generic.Dictionary[string,string]]::new()' $Name
        }
        'environment-extra-index-write' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$startInfo.Environment[''EDGE_DEVELOPMENT_MUTATION''] = ''1''' $Name
        }
        'overlay-case-downgrade' {
            return Replace-StaticGuardExact $Source `
                "[string]::Equals([string]`$name, 'TMPDIR', [StringComparison]::OrdinalIgnoreCase)" `
                "[string]::Equals([string]`$name, 'TMPDIR', [StringComparison]::Ordinal)" $Name
        }
        'overlay-source-replace' {
            return Replace-StaticGuardExact $Source 'foreach ($name in $Environment.Keys)' 'foreach ($name in $startInfo.Environment.Keys)' $Name
        }
        'overlay-value-replace' {
            return Replace-StaticGuardExact $Source '$startInfo.Environment[[string]$name] = [string]$Environment[$name]' '$startInfo.Environment[[string]$name] = [string]$name' $Name
        }
        'overlay-guard-disable' {
            return Replace-StaticGuardExact $Source `
                "if ([string]::Equals([string]`$name, 'TMPDIR', [StringComparison]::OrdinalIgnoreCase)) {" `
                'if ($false) {' $Name
        }
        'process-scoped-alias' {
            return Add-StaticGuardStatementAfter $Source '$process = [Diagnostics.Process]::new()' '$script:process = $process' $Name
        }
        'process-ref-escape' {
            return Add-StaticGuardStatementAfter $Source '$process = [Diagnostics.Process]::new()' '$processReference = [ref]$process' $Name
        }
        'temporary-root-postwrite' {
            return Add-StaticGuardStatementAfter $Source '$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())' '$temporaryRoot = [IO.Path]::GetTempPath()' $Name
        }
        'current-directory-postwrite' {
            return Add-StaticGuardStatementAfter $Source '$originalCurrentDirectory = [Environment]::CurrentDirectory' '$originalCurrentDirectory = [IO.Path]::GetTempPath()' $Name
        }
        'physical-root-postwrite' {
            return Add-StaticGuardStatementAfter $Source '$physicalRoot = [IO.Path]::GetFullPath([Environment]::CurrentDirectory)' '$physicalRoot = $temporaryRoot' $Name
        }
        'physical-root-return-replace' {
            return Replace-StaticGuardExact $Source 'return $physicalRoot' 'return $temporaryRoot' $Name
        }
        'current-directory-set-disable' {
            return Replace-StaticGuardExact $Source '[Environment]::CurrentDirectory = $temporaryRoot' '$null = $temporaryRoot' $Name
        }
        'current-directory-restore-disable' {
            return Replace-StaticGuardExact $Source '[Environment]::CurrentDirectory = $originalCurrentDirectory' '$null = $originalCurrentDirectory' $Name
        }
        'helper-resolve-path' {
            return Add-StaticGuardStatementAfter $Source '$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())' '$null = Resolve-Path -LiteralPath $temporaryRoot' $Name
        }
        'helper-readlink' {
            return Add-StaticGuardStatementAfter $Source '$temporaryItem = Get-Item -LiteralPath $temporaryRoot -Force' '$null = $temporaryItem.ResolveLinkTarget($true)' $Name
        }
        'helper-process-start' {
            return Add-StaticGuardStatementAfter $Source '$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())' '$null = [Diagnostics.Process]::Start($powerShellPath)' $Name
        }
        'physical-temp-rhs-replace' {
            return Replace-StaticGuardExact $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$physicalTempRoot = [IO.Path]::GetTempPath()' $Name
        }
        'physical-temp-postwrite' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$physicalTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())' $Name
        }
        'physical-temp-scoped-alias' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$script:physicalTempRoot = $physicalTempRoot' $Name
        }
        'physical-temp-ref-escape' {
            return Add-StaticGuardStatementAfter $Source '$physicalTempRoot = Resolve-DevPhysicalTempRoot' '$physicalTempRootReference = [ref]$physicalTempRoot' $Name
        }
        'original-snapshot-index-write' {
            return Add-StaticGuardStatementAfter $Source '$tmpDirectoryEnvironment = [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)' '$tmpDirectoryEnvironment[''TMPDIR''] = $physicalTempRoot' $Name
        }
        'original-snapshot-add' {
            return Add-StaticGuardStatementAfter $Source '$tmpDirectoryEnvironment = [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)' '$tmpDirectoryEnvironment.Add(''EDGE_DEVELOPMENT_MUTATION'', ''1'')' $Name
        }
        'original-snapshot-alias' {
            return Add-StaticGuardStatementAfter $Source '$tmpDirectoryEnvironment = [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)' '$originalSnapshotAlias = $tmpDirectoryEnvironment' $Name
        }
        'presence-overwrite' {
            return Add-StaticGuardStatementAfter $Source '$tmpDirectoryWasPresent = $tmpDirectoryEnvironment.Contains(''TMPDIR'')' '$tmpDirectoryWasPresent = $false' $Name
        }
        'original-value-overwrite' {
            $anchor = "`$tmpDirectoryOriginalValue = if (`$tmpDirectoryWasPresent) {`n    [string]`$tmpDirectoryEnvironment['TMPDIR']`n}`nelse { `$null }"
            return Add-StaticGuardStatementAfter $Source $anchor '$tmpDirectoryOriginalValue = ''''' $Name
        }
        'tmpdir-lowercase-static-set' {
            return Replace-StaticGuardExact $Source `
                "'TMPDIR', `$physicalTempRoot, [EnvironmentVariableTarget]::Process)" `
                "'tmpdir', `$physicalTempRoot, [EnvironmentVariableTarget]::Process)" $Name
        }
        'pin-target-user' {
            return Replace-StaticGuardExact $Source `
                "'TMPDIR', `$physicalTempRoot, [EnvironmentVariableTarget]::Process)" `
                "'TMPDIR', `$physicalTempRoot, [EnvironmentVariableTarget]::User)" $Name
        }
        'pin-computed-name' {
            return Replace-StaticGuardExact $Source `
                "'TMPDIR', `$physicalTempRoot, [EnvironmentVariableTarget]::Process)" `
                "('TMP' + 'DIR'), `$physicalTempRoot, [EnvironmentVariableTarget]::Process)" $Name
        }
        'pinned-snapshot-index-write' {
            $anchor = "`$tmpDirectoryPinnedEnvironment =`n        [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$tmpDirectoryPinnedEnvironment[''TMPDIR''] = $physicalTempRoot' $Name
        }
        'pinned-snapshot-clear' {
            $anchor = "`$tmpDirectoryPinnedEnvironment =`n        [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$tmpDirectoryPinnedEnvironment.Clear()' $Name
        }
        'pinned-snapshot-alias' {
            $anchor = "`$tmpDirectoryPinnedEnvironment =`n        [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$pinnedSnapshotAlias = $tmpDirectoryPinnedEnvironment' $Name
        }
        'restoration-snapshot-index-write' {
            $anchor = "`$tmpDirectoryRestorationEnvironment =`n            [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$tmpDirectoryRestorationEnvironment[''TMPDIR''] = $tmpDirectoryOriginalValue' $Name
        }
        'restoration-snapshot-remove' {
            $anchor = "`$tmpDirectoryRestorationEnvironment =`n            [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$tmpDirectoryRestorationEnvironment.Remove(''TMPDIR'')' $Name
        }
        'restoration-snapshot-alias' {
            $anchor = "`$tmpDirectoryRestorationEnvironment =`n            [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)"
            return Add-StaticGuardStatementAfter $Source $anchor '$restorationSnapshotAlias = $tmpDirectoryRestorationEnvironment' $Name
        }
        'restore-presence-check-disable' {
            return Replace-StaticGuardExact $Source `
                "if (`$tmpDirectoryRestorationEnvironment.Contains('TMPDIR') -ne `$tmpDirectoryWasPresent) {" `
                'if ($false) {' $Name
        }
        'restore-value-index-replace' {
            return Replace-StaticGuardExact $Source `
                "[string]`$tmpDirectoryRestorationEnvironment['TMPDIR'] -cne `$tmpDirectoryOriginalValue" `
                "[string]`$tmpDirectoryRestorationEnvironment['PATH'] -cne `$tmpDirectoryOriginalValue" $Name
        }
        'cleanup-condition-disabled' {
            return Replace-StaticGuardExact $Source '$null -ne $coordinatorMarker' '$false' $Name
        }
        'outer-cleanup-unreachable' {
            $mutated = Replace-StaticGuardExact $Source `
                "    try {`n        Remove-EdgeDevelopmentOuterRunRoot" `
                "    try {`n        if (`$false) {`n            Remove-EdgeDevelopmentOuterRunRoot" $Name
            return Replace-StaticGuardExact $mutated `
                "            -MarkerExpected `$outerMarker`n    }`n    catch {" `
                "            -MarkerExpected `$outerMarker`n        }`n    }`n    catch {" $Name
        }
        'post-pin-early-success-exit' {
            $anchor = "        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP process TMPDIR pin was not exact.'`n    }"
            return Add-StaticGuardStatementAfter $Source $anchor `
                'Write-Output ''{"schemaVersion":1,"passed":true}''; exit 0' $Name
        }
        'remove-coordinator-cleanup' {
            $call = @'
Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $gitPath `
                -AuthorityRepositoryRoot $snapshotRoot `
                -RunRoot $coordinatorRunRoot `
                -ValidatorWorktreePath $validatorWorktreePath `
                -ReplayWorktreePath $replayWorktreePath `
                -RunId $runId `
                -ParentMarkerPath $outerMarkerPath `
                -ParentMarkerExpected $outerMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
'@
            return Replace-StaticGuardExact $Source $call '$null = $coordinatorMarker' $Name
        }
        'dev-remove-coordinator-cleanup' {
            $call = @'
Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $gitPath `
                -AuthorityRepositoryRoot $snapshotRoot `
                -RunRoot $coordinatorRunRoot `
                -ValidatorWorktreePath $validatorWorktreePath `
                -ReplayWorktreePath $replayWorktreePath `
                -RunId $runId `
                -ParentMarkerPath $outerMarkerPath `
                -ParentMarkerExpected $outerMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
'@
            $deadCall = "if (`$false -and `$null -ne `$failure) {`n                " +
                ($call -replace "`n", "`n                ") + "`n            }"
            return Replace-StaticGuardExact $Source $call $deadCall $Name
        }
        'remove-outer-cleanup' {
            $call = @'
Remove-EdgeDevelopmentOuterRunRoot `
            -RunRoot $outerRunRoot `
            -SnapshotRoot $snapshotRoot `
            -CoordinatorRunRoot $coordinatorRunRoot `
            -RunId $runId `
            -MarkerPath $outerMarkerPath `
            -MarkerExpected $outerMarker
'@
            return Replace-StaticGuardExact $Source $call '$null = $outerMarker' $Name
        }
        'dev-remove-outer-cleanup' {
            $call = @'
Remove-EdgeDevelopmentOuterRunRoot `
            -RunRoot $outerRunRoot `
            -SnapshotRoot $snapshotRoot `
            -CoordinatorRunRoot $coordinatorRunRoot `
            -RunId $runId `
            -MarkerPath $outerMarkerPath `
            -MarkerExpected $outerMarker
'@
            $deadCall = "if (`$false -and `$null -ne `$failure) {`n            " +
                ($call -replace "`n", "`n            ") + "`n        }"
            return Replace-StaticGuardExact $Source $call $deadCall $Name
        }
        'tmp-first-failure-overwrite' {
            return Replace-StaticGuardExact $Source 'if ($null -eq $tmpDirectoryFailure) { $tmpDirectoryFailure = $_ }' '$tmpDirectoryFailure = $_' $Name
        }
        'cwd-first-failure-overwrite' {
            return Replace-StaticGuardExact $Source 'if ($null -eq $currentDirectoryFailure) { $currentDirectoryFailure = $_ }' '$currentDirectoryFailure = $_' $Name
        }
        'absent-restore-alias' {
            return Replace-StaticGuardExact $Source "Remove-Item -LiteralPath 'Env:TMPDIR' -ErrorAction Stop" "ri -LiteralPath 'Env:TMPDIR' -ErrorAction Stop" $Name
        }
        default { throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 unknown development mutation '$Name'." }
    }
}

function New-ProtocolModuleStaticMutation {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Name)
    switch ($Name) {
        'module-strictmode-downgrade' {
            return Replace-StaticGuardExact $Source 'Set-StrictMode -Version Latest' 'Set-StrictMode -Version 3' $Name
        }
        'module-erroraction-silence' {
            return Replace-StaticGuardExact $Source '$ErrorActionPreference = ''Stop''' '$ErrorActionPreference = ''Continue''' $Name
        }
        'module-function-shadow-get-item' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' 'function Get-Item { throw ''mutated'' }' $Name
        }
        'module-function-shadow-remove-item' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' 'function Remove-Item { throw ''mutated'' }' $Name
        }
        'module-top-static-process-start' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' '$null = [Diagnostics.Process]::Start(''true'')' $Name
        }
        'module-top-file-write' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' '[IO.File]::WriteAllText(''/tmp/edge-static-mutation'', ''x'')' $Name
        }
        'module-top-pure-expression' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' '1 + 1' $Name
        }
        'module-export-alias-surface' {
            return Replace-StaticGuardExact $Source `
                'Export-ModuleMember -Function @(' `
                'Export-ModuleMember -Function @( # mutation:module-export-alias-surface' $Name |
                ForEach-Object {
                    Replace-StaticGuardExact $_ "    'Wait-EdgeBoundedCaptureTasks'`n)" `
                        "    'Wait-EdgeBoundedCaptureTasks'`n) -Alias *" $Name
                }
        }
        'module-top-start-process' {
            return Add-StaticGuardStatementAfter $Source '$ErrorActionPreference = ''Stop''' 'Start-Process -FilePath ''true''' $Name
        }
        'module-second-process-start-info' {
            return Add-StaticGuardStatementAfter $Source '$startInfo = [Diagnostics.ProcessStartInfo]::new()' '$moduleSecondStartInfo = [Diagnostics.ProcessStartInfo]::new()' $Name
        }
        'module-process-startinfo-rebind' {
            return Add-StaticGuardStatementAfter $Source '$process.StartInfo = $startInfo' '$process.StartInfo = [Diagnostics.ProcessStartInfo]::new()' $Name
        }
        'module-static-process-start-owner' {
            return Add-StaticGuardStatementAfter $Source '$process.StartInfo = $startInfo' '$null = [Diagnostics.Process]::Start($GitExecutablePath)' $Name
        }
        'module-cleanup-reachable-from-ingress' {
            return Add-StaticGuardStatementAfter $Source 'function Assert-EdgeAuthorityGitEnvironment {' '    $null = Invoke-EdgeCleanupGit ''git'' ''.'' ''.'' @(''status'')' $Name
        }
        'module-cleanup-reachable-from-resolver' {
            return Add-StaticGuardStatementAfter $Source 'function Resolve-EdgeFixedExecutable {' '    $null = Invoke-EdgeCleanupGit ''git'' ''.'' ''.'' @(''status'')' $Name
        }
        'module-prepin-helper-operator-drift' {
            return Replace-StaticGuardExact $Source '$resolvedPath = Resolve-EdgeFixedExecutable $fullPath' '$resolvedPath = & Resolve-EdgeFixedExecutable $fullPath' $Name
        }
        'module-dynamic-command' {
            return Replace-StaticGuardExact $Source `
                "`$head = Invoke-EdgeCleanupGit `$gitPath `$RepositoryRoot `$emptyGitConfigPath @('rev-parse', 'HEAD')" `
                "`$head = & `$script:dynamicCleanupCommand `$gitPath `$RepositoryRoot `$emptyGitConfigPath @('rev-parse', 'HEAD')" $Name
        }
        'module-dynamic-member' {
            return Replace-StaticGuardExact $Source '$process.Start()' '$process.$(''Start'')()' $Name
        }
        'module-addtype-process-injection' {
            $anchor = "public static class EdgeAuthorityBoundedStreamCapture`n{"
            $replacement = "public static class EdgeAuthorityBoundedStreamCapture`n{`n    public static void StartInjectedProcess()`n    {`n        _ = System.Diagnostics.Process.Start(`"true`");`n    }"
            return Replace-StaticGuardExact $Source $anchor $replacement $Name
        }
        'module-addtype-static-constructor' {
            $anchor = "public static class EdgeAuthorityBoundedStreamCapture`n{"
            $replacement = "public static class EdgeAuthorityBoundedStreamCapture`n{`n    static EdgeAuthorityBoundedStreamCapture()`n    {`n        Environment.SetEnvironmentVariable(`"EDGE_AUTHORITY_STATIC_MUTATION`", `"1`");`n    }"
            return Replace-StaticGuardExact $Source $anchor $replacement $Name
        }
        'module-addtype-file-write-injection' {
            $anchor = "public static class EdgeAuthorityBoundedStreamCapture`n{"
            $replacement = "public static class EdgeAuthorityBoundedStreamCapture`n{`n    public static void WriteInjectedFile()`n    {`n        System.IO.File.WriteAllText(System.IO.Path.GetTempFileName(), `"edge`");`n    }"
            return Replace-StaticGuardExact $Source $anchor $replacement $Name
        }
        default { throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 unknown protocol mutation '$Name'." }
    }
}

function New-CanonicalDigestShapeDecoy {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Name
    )
    $decoyText = switch ($Name) {
        'timeout-executable-default' { '[ValidateRange(1, 3600)][int]$TimeoutSeconds,' }
        'strictmode-downgrade' { 'Set-StrictMode -Version 3' }
        'erroraction-silence' { '$ErrorActionPreference = ''Continue''' }
        'repository-fallback-parent' { "Join-Path `$PSScriptRoot '../../..'" }
        'protocol-module-path' { "'Mutated.Protocol.psm1'" }
        'powershell-path-lookup' { '$powerShellPath = ''pwsh''' }
        'git-command-lookup' { 'Get-Command git -CommandType All -ErrorAction Stop' }
        'git-path-forge' { '$gitPath = ''git''' }
        'maximum-output-drift' { '$devMaximumCapturedBytes = 1' }
        'empty-config-rhs' { '$devEmptyGitConfigPath = $null' }
        'child-environment-rhs' { '$devGitChildEnvironment = @{}' }
        'prepin-file-write' {
            '[IO.File]::WriteAllBytes((Join-Path $physicalTempRoot ''prepin.bin''), [byte[]]@(1))'
        }
        'module-addtype-process-injection' {
            '_ = System.Diagnostics.Process.Start("true");'
        }
        'module-addtype-static-constructor' {
            'static EdgeAuthorityBoundedStreamCapture()'
        }
        'module-addtype-file-write-injection' {
            'System.IO.File.WriteAllText(System.IO.Path.GetTempFileName(), "edge");'
        }
        default {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 unknown canonical shape decoy '$Name'."
        }
    }
    return $Source + "`n# canonical-shape-comment-decoy[$Name]: $decoyText" +
        "`n@'`n$decoyText`n'@" +
        "`n# unrelated-digest-drift[$Name]: this comment changes bytes only`n"
}

$canonical = Invoke-CanonicalStaticGuard -SourceSet $sources -PassThru
if ($canonical.schemaVersion -ne 1 -or
    $canonical.owner -cne 'scripts/tests/EdgePluginContractStaticGuard.psm1' -or
    $canonical.scope -cne 'production' -or
    $canonical.passed -ne $true -or
    $canonical.sourceCount -ne 11) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 canonical static guard returned an invalid structured result.'
}

# GAP-A-LEGACY-PROJECTION-BEGIN
$legacyLf = [string][char]10
$legacyGrave = [string][char]96
$legacyProjection = [IO.File]::ReadAllText($PSCommandPath)
$legacyBlockStart = $legacyProjection.IndexOf(
    '# GAP-A-LEGACY-PROJECTION-BEGIN', [StringComparison]::Ordinal)
$legacyBlockEnd = $legacyProjection.LastIndexOf(
    '# GAP-A-LEGACY-PROJECTION-END', [StringComparison]::Ordinal)
$legacyBlockAfter = if ($legacyBlockEnd -ge 0) {
    $legacyProjection.IndexOf($legacyLf, $legacyBlockEnd, [StringComparison]::Ordinal)
}
else { -1 }
if ($legacyBlockStart -lt 0 -or $legacyBlockEnd -le $legacyBlockStart -or
    $legacyBlockAfter -le $legacyBlockEnd) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 legacy runner projection marker contract changed.'
}
$legacyProjection = $legacyProjection.Remove(
    $legacyBlockStart, ($legacyBlockAfter + 1) - $legacyBlockStart)
$legacyDirectPinStart = $legacyProjection.IndexOf(
    '# GAP-A-DIRECT-INVENTORY-PINS-BEGIN', [StringComparison]::Ordinal)
$legacyDirectPinEnd = $legacyProjection.LastIndexOf(
    '# GAP-A-DIRECT-INVENTORY-PINS-END', [StringComparison]::Ordinal)
$legacyDirectPinAfter = if ($legacyDirectPinEnd -ge 0) {
    $legacyProjection.IndexOf(
        $legacyLf, $legacyDirectPinEnd, [StringComparison]::Ordinal)
}
else { -1 }
if ($legacyDirectPinStart -lt 0 -or
    $legacyDirectPinEnd -le $legacyDirectPinStart -or
    $legacyDirectPinAfter -le $legacyDirectPinEnd) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 legacy direct-inventory projection marker contract changed.'
}
$legacyProjection = $legacyProjection.Remove(
    $legacyDirectPinStart,
    ($legacyDirectPinAfter + 1) - $legacyDirectPinStart)
function Remove-LegacyProjectionExact {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Name
    )
    $matches = [Text.RegularExpressions.Regex]::Matches(
        $Source, [Text.RegularExpressions.Regex]::Escape($Needle),
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($matches.Count -ne 1) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' needle count is $($matches.Count), expected one."
    }
    $match = $matches[0]
    $mutated = $Source.Remove($match.Index, $match.Length)
    if ([string]::Equals($mutated, $Source, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$Name' removal was a no-op."
    }
    return $mutated
}
$legacyFormalSourceRows = '    FormalSource = Get-Content -LiteralPath (' + $legacyLf +
    '        Join-Path $PSScriptRoot ''Invoke-EdgePluginContractFormalValidation.ps1'') -Raw' +
    $legacyLf
$legacyProjection = Remove-LegacyProjectionExact `
    -Source $legacyProjection -Needle $legacyFormalSourceRows `
    -Name 'legacy-projection-formal-source-row'
$legacyFormalArgument =
    '        -FormalSource ([string]$SourceSet.FormalSource) ' + $legacyGrave + $legacyLf
$legacyProjection = Remove-LegacyProjectionExact `
    -Source $legacyProjection -Needle $legacyFormalArgument `
    -Name 'legacy-projection-formal-argument'
$legacyProjection = Replace-StaticGuardExact `
    -Source $legacyProjection `
    -Needle '    $canonical.sourceCount -ne 11)' `
    -Replacement '    $canonical.sourceCount -ne 10)' `
    -Name 'legacy-projection-source-count'
$legacyFormalStart = $legacyProjection.IndexOf(
    '$formalMutationRows = @(', [StringComparison]::Ordinal)
$legacyFormalEnd = $legacyProjection.IndexOf(
    '$behaviorRuntimeShapeMutationPassed = 0',
    $legacyFormalStart, [StringComparison]::Ordinal)
if ($legacyFormalStart -lt 0 -or $legacyFormalEnd -le $legacyFormalStart -or
    $legacyProjection.IndexOf(
        '$formalMutationRows = @(', $legacyFormalStart + 1,
        [StringComparison]::Ordinal) -ge 0) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 legacy runner formal-block projection changed.'
}
$legacyProjection = $legacyProjection.Remove(
    $legacyFormalStart, $legacyFormalEnd - $legacyFormalStart)
$legacyCanonicalFields =
    '    canonicalSourceCount = [int]$canonical.sourceCount' + $legacyLf +
    '    canonicalFormalSha256 = [string]$canonical.sourceDigests.formal' + $legacyLf
$legacyProjection = Remove-LegacyProjectionExact `
    -Source $legacyProjection -Needle $legacyCanonicalFields `
    -Name 'legacy-projection-canonical-formal-fields'
$legacyProjectionFields =
    '    legacyProjectionByteLength = $legacyProjectionBytes.Length' + $legacyLf +
    '    legacyProjectionSha256 = $legacyProjectionSha256' + $legacyLf +
    '    legacyPriorMutationTotal = $legacyPriorMutationTotal' + $legacyLf
$legacyProjection = Remove-LegacyProjectionExact `
    -Source $legacyProjection -Needle $legacyProjectionFields `
    -Name 'legacy-projection-proof-fields'
$legacyFormalFields =
    '    formalMutationPassed = $formalMutationPassed' + $legacyLf +
    '    formalMutationTotal = $formalMutationRows.Count' + $legacyLf +
    '    formalResultSchemaNegativePassed = $formalResultSchemaNegativePassed' + $legacyLf +
    '    formalResultSchemaNegativeTotal = 1' + $legacyLf
$legacyProjection = Remove-LegacyProjectionExact `
    -Source $legacyProjection -Needle $legacyFormalFields `
    -Name 'legacy-projection-formal-result-fields'
$legacyProjectionBytes = [Text.UTF8Encoding]::new($false).GetBytes($legacyProjection)
$legacyProjectionSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        $legacyProjectionBytes)).ToLowerInvariant()
$legacyPriorMutationTotal = 87 + 22 + 3
if ($legacyProjectionBytes.Length -ne 90962 -or
    $legacyProjectionSha256 -cne
        '8bd90bd5790186501fe2a1dea1cb54ec8e8735ae9e3ec228f237027ea53a4676' -or
    $legacyPriorMutationTotal -ne 112) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 frozen legacy runner projection changed.'
}
# GAP-A-LEGACY-PROJECTION-END
$formalMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'formal-parameter-override'
        target = 'formal'
        needle = 'param()'
        replacement = 'param([string]$LedgerPath)'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation entry must expose no override parameters.'
    },
    [pscustomobject][ordered]@{
        name = 'formal-ledger-path-override'
        target = 'formal'
        needle = '$canonicalLedgerRelativePath = ''eng/baselines/edge-plugin-contract-ledger.json'''
        replacement = '$canonicalLedgerRelativePath = ''.artifacts/override-ledger.json'''
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $canonicalLedgerRelativePath = ''eng/baselines/edge-plugin-contract-ledger.json'''
    },
    [pscustomobject][ordered]@{
        name = 'formal-generator-invocation'
        target = 'formal'
        needle = '$initialPreconditions = Assert-FormalValidationPreconditions'
        replacement = "& (Join-Path `$RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1')`n`$initialPreconditions = Assert-FormalValidationPreconditions"
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation exposes a generator/fixture/skip/command override.'
    },
    [pscustomobject][ordered]@{
        name = 'formal-index-inventory-bypass'
        target = 'formal'
        needle = "'ls-files', '--stage', '-z'"
        replacement = "'diff', '--cached', '--name-only', '-z'"
        expectedMessage = "EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: 'ls-files', '--stage', '-z'"
    },
    [pscustomobject][ordered]@{
        name = 'formal-second-precondition-bypass'
        target = 'formal'
        needle = '$confirmedPreconditions = Assert-FormalValidationPreconditions'
        replacement = '$confirmedPreconditions = $initialPreconditions # bypassed second precondition'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $confirmedPreconditions = Assert-FormalValidationPreconditions'
    },
    [pscustomobject][ordered]@{
        name = 'formal-fast-switch-downgrade'
        target = 'formal'
        needle = "'-RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt'"
        replacement = "'-RequireAuthorityReceipt'"
        expectedMessage = "EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: -RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt'"
    },
    [pscustomobject][ordered]@{
        name = 'formal-unmarked-recursive-cleanup'
        target = 'formal'
        needle = 'Remove-Item -LiteralPath $expectedOuter -Force'
        replacement = 'Remove-Item -LiteralPath $expectedOuter -Recurse -Force'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation cleanup must remain exact and non-recursive.'
    },
    [pscustomobject][ordered]@{
        name = 'formal-final-receipt-recheck-removed'
        target = 'formal'
        needle = '$finalReceiptSha256 = Assert-FormalReceiptIdentity'
        replacement = '$finalReceiptSha256 = [string]$descriptor.receiptSha256 # bypassed final receipt identity'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $finalReceiptSha256 = Assert-FormalReceiptIdentity'
    },
    [pscustomobject][ordered]@{
        name = 'formal-final-poststate-removed'
        target = 'formal'
        needle = '$finalState = Get-FormalRepositoryState'
        replacement = '$finalState = $initialPreconditions.state # bypassed final post-state read'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $finalState = Get-FormalRepositoryState'
    },
    [pscustomobject][ordered]@{
        name = 'formal-protocol-recursive-cleanup'
        target = 'protocolModule'
        needle = 'Remove-Item -LiteralPath $MarkerPath -Force'
        replacement = 'Remove-Item -LiteralPath $MarkerPath -Recurse -Force'
        expectedMessage = 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol formal cleanup fail-closed allowlist changed.'
    })
$formalMutationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($row in $formalMutationRows) {
    [void]$formalMutationNames.Add([string]$row.name)
}
if ($formalMutationRows.Count -ne 10 -or $formalMutationNames.Count -ne 10) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 formal static mutation inventory must remain exactly 10 unique names.'
}
$formalMutationPassed = 0
foreach ($row in $formalMutationRows) {
    $mutated = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutated[$key] = [string]$sources[$key] }
    $sourceKey = if ([string]$row.target -ceq 'formal') {
        'FormalSource'
    }
    else { 'ProtocolModuleSource' }
    $mutated[$sourceKey] = Replace-StaticGuardExact `
        -Source ([string]$mutated[$sourceKey]) `
        -Needle ([string]$row.needle) `
        -Replacement ([string]$row.replacement) `
        -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        ([string]$mutated[$sourceKey]), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced PowerShell parse errors."
    }
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutated) }
    catch {
        if ([string]$_.Exception.Message -ceq [string]$row.expectedMessage) {
            $formalMutationPassed++
            continue
        }
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' was rejected by an unexpected semantic owner: $($_.Exception.Message)"
    }
    throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' was accepted."
}
if ($formalMutationPassed -ne $formalMutationRows.Count) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 formal static mutation inventory was not fully rejected.'
}

$formalResultSchemaPath = Join-Path $RepositoryRoot `
    'eng/edge-plugin-contract-formal-validation-result.schema.json'
$formalResultSchemaRaw = Get-Content -LiteralPath $formalResultSchemaPath -Raw
$formalResultSchemaFixture = [pscustomobject][ordered]@{
    schemaVersion = 1
    ruleId = 'EDGE-SPLIT-LEDGER-001'
    mode = 'formal-clean'
    formal = $true
    passed = $true
    completedUtc = '2026-07-21T00:00:00.0000000Z'
    formalFinalHead = ('1' * 40)
    formalFinalTree = ('2' * 40)
    implementationHead = ('3' * 40)
    implementationTree = ('4' * 40)
    ledgerPath = 'eng/baselines/edge-plugin-contract-ledger.json'
    ledgerSha256 = ('5' * 64)
    receiptPath = '.artifacts/formal-receipt.json'
    receiptSha256 = ('6' * 64)
    publicKeySha256 = ('7' * 64)
    authorityCount = 1
    replayCount = 1
    descriptorPidBoundToDirectChild = $true
    descriptorStartBoundToDirectChild = $true
    fastConsumerRequireAuthorityReceipt = $true
    fastConsumerRequireFormalAuthorityReceipt = $true
    postStateStable = $true
    cleanupComplete = $true
}
$formalResultSchemaPositive = ConvertTo-Json $formalResultSchemaFixture -Depth 10 -Compress
if (-not ($formalResultSchemaPositive | Test-Json -Schema $formalResultSchemaRaw -ErrorAction Stop)) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 canonical formal result schema fixture was rejected.'
}
$formalResultSchemaFixture.receiptPath = '.artifacts/../x'
$formalResultSchemaNegativePassed = 0
try {
    if (-not ((ConvertTo-Json $formalResultSchemaFixture -Depth 10 -Compress) |
            Test-Json -Schema $formalResultSchemaRaw -ErrorAction Stop)) {
        $formalResultSchemaNegativePassed = 1
    }
}
catch { $formalResultSchemaNegativePassed = 1 }
if ($formalResultSchemaNegativePassed -ne 1) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 formal result schema accepted .artifacts/../x.'
}

$behaviorRuntimeShapeMutationPassed = 0
$behaviorRuntimeShapeMutatedSources = [ordered]@{}
foreach ($key in $sources.Keys) {
    $behaviorRuntimeShapeMutatedSources[$key] = [string]$sources[$key]
}
$behaviorRuntimeShapeMutatedSources.BehaviorSource = Replace-StaticGuardExact `
    -Source ([string]$sources.BehaviorSource) `
    -Needle '[byte[]]$canonicalBindingBytes = @(' `
    -Replacement '$canonicalBindingBytes = @(' `
    -Name 'behavior-canonical-binding-remove-byte-array'
$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseInput(
    ([string]$behaviorRuntimeShapeMutatedSources.BehaviorSource),
    [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior canonical binding byte-array mutation produced parse errors.'
}
try {
    [void](Invoke-CanonicalStaticGuard -SourceSet $behaviorRuntimeShapeMutatedSources)
}
catch {
    if ($_.Exception.Message -ceq
        'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior canonical binding byte-array shape changed.') {
        $behaviorRuntimeShapeMutationPassed = 1
    }
    else { throw }
}
if ($behaviorRuntimeShapeMutationPassed -ne 1) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior canonical binding byte-array mutation was accepted.'
}

$developmentManifestRuntimeShapeMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'development-manifest-remove-byte-array-type'
        replacement = '$manifestBytes = @('
    },
    [pscustomobject][ordered]@{
        name = 'development-manifest-weaken-array-capture'
        replacement = '[byte[]]$manifestBytes = ('
    })
$developmentManifestRuntimeShapeMutationPassed = 0
foreach ($row in $developmentManifestRuntimeShapeMutationRows) {
    $mutatedDevelopmentSource = Replace-StaticGuardExact `
        -Source ([string]$sources.DevelopmentSource) `
        -Needle '[byte[]]$manifestBytes = @(' `
        -Replacement ([string]$row.replacement) -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        $mutatedDevelopmentSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced PowerShell parse errors."
    }
    $targetResult = Assert-EdgePluginContractStaticGuard `
        -MutationTarget development -MutationName ([string]$row.name) `
        -TargetOwner 'Development.ManifestCanonicalBytes' -ExpectedShape 'None' `
        -MutationSource $mutatedDevelopmentSource
    if ($targetResult.passed -ne $true -or
        $targetResult.targetOwner -cne 'Development.ManifestCanonicalBytes') {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' returned an invalid manifest byte-array target result."
    }
    $developmentManifestRuntimeShapeMutationPassed++
}
if ($developmentManifestRuntimeShapeMutationRows.Count -ne 2 -or
    $developmentManifestRuntimeShapeMutationPassed -ne 2) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 development manifest byte-array mutations did not pass 2/2.'
}

$behaviorReplayFixtureProtocolMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'behavior-replay-fixture-remove-protocol-copy'
        needle = '[IO.File]::WriteAllBytes($fixtureProtocolModulePath, [IO.File]::ReadAllBytes($protocolModulePath))'
        replacement = '# replay fixture protocol copy removed'
    },
    [pscustomobject][ordered]@{
        name = 'behavior-replay-fixture-remove-protocol-add'
        needle = "            'scripts/tests/EdgePluginContractLedger.Protocol.psm1',"
        replacement = '            # replay fixture protocol path not staged'
    })
$behaviorReplayFixtureProtocolMutationPassed = 0
foreach ($row in $behaviorReplayFixtureProtocolMutationRows) {
    $mutatedSources = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutatedSources[$key] = [string]$sources[$key] }
    $mutatedSources.BehaviorSource = Replace-StaticGuardExact `
        -Source ([string]$sources.BehaviorSource) `
        -Needle ([string]$row.needle) -Replacement ([string]$row.replacement) `
        -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        ([string]$mutatedSources.BehaviorSource), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced parse errors."
    }
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutatedSources) }
    catch {
        if ($_.Exception.Message -ceq
            'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior replay fixture protocol dependency changed.') {
            $behaviorReplayFixtureProtocolMutationPassed++
        }
        else { throw }
    }
}
if ($behaviorReplayFixtureProtocolMutationRows.Count -ne 2 -or
    $behaviorReplayFixtureProtocolMutationPassed -ne 2) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior replay fixture protocol mutations did not pass 2/2.'
}

$behaviorPackageCountMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'behavior-package-count-restore-na-status'
        needle = '$package.status = ''evaluated'''
        replacement = '$package.status = ''not-applicable-before-EDGE-SPLIT-040'''
    },
    [pscustomobject][ordered]@{
        name = 'behavior-package-count-restore-old-total'
        needle = '$package.totalEntryCount = 2'
        replacement = '$package.totalEntryCount = 1'
    })
$behaviorPackageCountMutationPassed = 0
foreach ($row in $behaviorPackageCountMutationRows) {
    $mutatedSources = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutatedSources[$key] = [string]$sources[$key] }
    $mutatedSources.BehaviorSource = Replace-StaticGuardExact `
        -Source ([string]$sources.BehaviorSource) `
        -Needle ([string]$row.needle) -Replacement ([string]$row.replacement) `
        -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        ([string]$mutatedSources.BehaviorSource), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced parse errors."
    }
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutatedSources) }
    catch {
        if ($_.Exception.Message -ceq
            'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior package count fixture body changed.') {
            $behaviorPackageCountMutationPassed++
        }
        else { throw }
    }
}
if ($behaviorPackageCountMutationRows.Count -ne 2 -or
    $behaviorPackageCountMutationPassed -ne 2) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior package count mutations did not pass 2/2.'
}

$behaviorTailContractMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'behavior-tail-uri-restore-query'
        needle = "`$ledger.publishedComposition.plugin.artifact.url =`n                'https://forbidden:forbidden@example.invalid/artifact.zip'"
        replacement = "`$ledger.publishedComposition.plugin.artifact.url += '?token=forbidden'"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-tail-uri-remove-userinfo'
        needle = "`$ledger.publishedComposition.plugin.artifact.url =`n                'https://forbidden:forbidden@example.invalid/artifact.zip'"
        replacement = "`$ledger.publishedComposition.plugin.artifact.url =`n                'https://example.invalid/artifact.zip'"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-tail-verified-restore-subcode'
        needle = "'unverified-old-host' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'"
        replacement = "'unverified-old-host' = 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-VERIFIED'"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-tail-blank-restore-absolute'
        needle = "`$ledger.externalSymbolUsages[0].sourcePath = ' '"
        replacement = "`$ledger.externalSymbolUsages[0].sourcePath = '/tmp/escape.cs'"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-tail-blank-restore-normal'
        needle = "`$ledger.externalSymbolUsages[0].sourcePath = ' '"
        replacement = "`$ledger.externalSymbolUsages[0].sourcePath = 'src/Modules/IIoT.Edge.Module.Homogenization/Module.cs'"
    })
$behaviorTailContractMutationPassed = 0
foreach ($row in $behaviorTailContractMutationRows) {
    $mutatedSources = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutatedSources[$key] = [string]$sources[$key] }
    $mutatedSources.BehaviorSource = Replace-StaticGuardExact `
        -Source ([string]$sources.BehaviorSource) `
        -Needle ([string]$row.needle) -Replacement ([string]$row.replacement) `
        -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        ([string]$mutatedSources.BehaviorSource), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced parse errors."
    }
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutatedSources) }
    catch {
        if ($_.Exception.Message -ceq
            'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior tail semantic fixture contract changed.') {
            $behaviorTailContractMutationPassed++
        }
        else { throw }
    }
}
if ($behaviorTailContractMutationRows.Count -ne 5 -or
    $behaviorTailContractMutationPassed -ne 5) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior tail contract mutations did not pass 5/5.'
}

$validatorCommitPairOrderingMutationPassed = 0
$validatorCommitPairOrderingMutatedSources = [ordered]@{}
foreach ($key in $sources.Keys) {
    $validatorCommitPairOrderingMutatedSources[$key] = [string]$sources[$key]
}
$validatorCommitPairOrderingMutatedSources.ValidatorSource = Replace-StaticGuardExact `
    -Source ([string]$sources.ValidatorSource) `
    -Needle 'if (-not $CommitPairGateOnly) {' `
    -Replacement 'if ($true) {' `
    -Name 'validator-commit-pair-force-deterministic-target-pin'
$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseInput(
    ([string]$validatorCommitPairOrderingMutatedSources.ValidatorSource),
    [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 validator CommitPairGateOnly ordering mutation produced parse errors.'
}
try {
    [void](Invoke-CanonicalStaticGuard -SourceSet $validatorCommitPairOrderingMutatedSources)
}
catch {
    if ($_.Exception.Message -ceq
        'EDGE-SPLIT-AUTHORITY-STATIC-001 validator CommitPairGateOnly deterministic-target ordering changed.') {
        $validatorCommitPairOrderingMutationPassed = 1
    }
    else { throw }
}
if ($validatorCommitPairOrderingMutationPassed -ne 1) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 validator CommitPairGateOnly ordering mutation was accepted.'
}

$behaviorDiagnosticMutationRows = @(
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-disable-sensitive-rejection'
        needle = '$behaviorOutput.Contains('
        replacement = '$false -and $behaviorOutput.Contains('
    },
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-move-to-success'
        needle = "if (`$behaviorFailed) {`n        `$behaviorReceiptRaw"
        replacement = "if (-not `$behaviorFailed) {`n        `$behaviorReceiptRaw"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-read-stdout-on-failure'
        needle = '[byte[]]$behaviorResult.stderrBytes'
        replacement = '[byte[]]$behaviorResult.stdoutBytes'
    },
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-emit-raw-stderr'
        needle = '$([string]::Join(''; '', $behaviorDiagnosticParts))'
        replacement = '$behaviorOutput'
    },
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-add-file-write'
        needle = '$behaviorDiagnosticParts = [Collections.Generic.List[string]]::new()'
        replacement = "`$behaviorDiagnosticParts = [Collections.Generic.List[string]]::new()`n        [IO.File]::WriteAllText((Join-Path `$snapshot.root 'behavior-stderr.txt'), `$behaviorOutput)"
    },
    [pscustomobject][ordered]@{
        name = 'behavior-diagnostic-add-child'
        needle = '$behaviorDiagnosticParts = [Collections.Generic.List[string]]::new()'
        replacement = "`$behaviorDiagnosticParts = [Collections.Generic.List[string]]::new()`n        `$null = Invoke-DevProcess -FileName `$powerShellPath -Arguments @('-NoLogo') -WorkingDirectory `$snapshot.root -TimeoutSeconds 1 -InputBytes `$null -Environment `$fastEnvironment"
    })
$behaviorDiagnosticMutationPassed = 0
foreach ($row in $behaviorDiagnosticMutationRows) {
    $mutatedDevelopmentSource = Replace-StaticGuardExact `
        -Source ([string]$sources.DevelopmentSource) `
        -Needle ([string]$row.needle) -Replacement ([string]$row.replacement) `
        -Name ([string]$row.name)
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        $mutatedDevelopmentSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced PowerShell parse errors."
    }
    $expectedOwner = switch ([string]$row.name) {
        'behavior-diagnostic-add-child' { 'Development.ProcessCallOwnership' }
        default { 'Development.BehaviorDiagnosticFlow' }
    }
    $targetResult = Assert-EdgePluginContractStaticGuard `
        -MutationTarget development -MutationName ([string]$row.name) `
        -TargetOwner $expectedOwner -ExpectedShape 'None' `
        -MutationSource $mutatedDevelopmentSource
    if ($targetResult.passed -ne $true -or
        $targetResult.targetOwner -cne $expectedOwner) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' returned an invalid behavior diagnostic target result."
    }
    $behaviorDiagnosticMutationPassed++
}
if ($behaviorDiagnosticMutationRows.Count -ne 6 -or
    $behaviorDiagnosticMutationPassed -ne 6) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 behavior diagnostic mutations did not pass 6/6.'
}

$deterministicMutationRows = [Collections.Generic.List[object]]::new()
foreach ($sourceKey in @('GeneratorSource', 'ValidatorSource')) {
    $vectorName = if ($sourceKey -ceq 'GeneratorSource') {
        '$canonicalDeterministicBuildArguments'
    }
    else { '$validatorDeterministicBuildArguments' }
    $prefix = if ($sourceKey -ceq 'GeneratorSource') { 'generator' } else { 'validator' }
    foreach ($buildIndex in 0..2) {
        $deterministicMutationRows.Add([pscustomobject][ordered]@{
                name = "$prefix-build-$($buildIndex + 1)-remove-vector"
                sourceKey = $sourceKey
                needle = ") + $vectorName + @("; replacement = ') + @('
                occurrence = $buildIndex; expectedCount = 3
            })
    }
    $deterministicMutationRows.Add([pscustomobject][ordered]@{
            name = "$prefix-collector-core-remove-vector"
            sourceKey = $sourceKey
            needle = ') + $DeterministicBuildArguments + @('; replacement = ') + @('
            occurrence = 2; expectedCount = 4
        })
    $deterministicMutationRows.Add([pscustomobject][ordered]@{
            name = "$prefix-collector-preprocess-remove-vector"
            sourceKey = $sourceKey
            needle = ') + $DeterministicBuildArguments + @('; replacement = ') + @('
            occurrence = 3; expectedCount = 4
        })
    $deterministicMutationRows.Add([pscustomobject][ordered]@{
            name = "$prefix-collector-enable-triggered-compilation"
            sourceKey = $sourceKey
            needle = "'-p:TargetsTriggeredByCompilation='"
            replacement = "'-p:TargetsTriggeredByCompilation=CompileAvaloniaXaml'"
            occurrence = $null; expectedCount = $null
        })
}
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-remove-msbuild-comma-escape'; sourceKey = 'GeneratorSource'
        needle = "return `$Value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')"
        replacement = "return `$Value.Replace('%', '%25').Replace(';', '%3B')"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-remove-msbuild-comma-escape'; sourceKey = 'ValidatorSource'
        needle = "return `$Value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')"
        replacement = "return `$Value.Replace('%', '%25').Replace(';', '%3B')"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-remove-pathmap-source-escape'; sourceKey = 'GeneratorSource'
        needle = "return `$fullPath.Replace('=', '==').Replace(',', ',,')"
        replacement = 'return $fullPath'; occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-remove-pathmap-source-escape'; sourceKey = 'ValidatorSource'
        needle = "return `$fullPath.Replace('=', '==').Replace(',', ',,')"
        replacement = 'return $fullPath'; occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-embedded-to-portable'; sourceKey = 'GeneratorSource'
        needle = "    '-p:DebugType=embedded',"; replacement = "    '-p:DebugType=portable',"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-embedded-to-portable'; sourceKey = 'ValidatorSource'
        needle = "    '-p:DebugType=embedded',"; replacement = "    '-p:DebugType=portable',"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-physical-pathmap-root'; sourceKey = 'GeneratorSource'
        needle = '=/_,$(ConvertTo-CanonicalPathMapSourceToken $generatedRoot)=/__edge_contract_generated__'
        replacement = '=/physical,$(ConvertTo-CanonicalPathMapSourceToken $generatedRoot)=/__edge_contract_generated__'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-physical-pathmap-root'; sourceKey = 'ValidatorSource'
        needle = '=/_,$(ConvertTo-IndependentPathMapSourceToken $validatorGeneratedRoot)=/__edge_contract_generated__'
        replacement = '=/physical,$(ConvertTo-IndependentPathMapSourceToken $validatorGeneratedRoot)=/__edge_contract_generated__'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'targets-use-physical-project-directory'; sourceKey = 'DeterministicTargetsSource'
        needle = 'ProjectDirectory="$(_EdgeContractVirtualProjectDirectory)"'
        replacement = 'ProjectDirectory="$(MSBuildProjectDirectory)"'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'targets-remove-rooted-path-guard'; sourceKey = 'DeterministicTargetsSource'
        needle = "OR `$([System.IO.Path]::IsPathRooted('`$(_EdgeContractProjectRelativeDirectoryNormalized)'))"
        replacement = "OR 'false' == 'true'"; occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'targets-drop-ref-assembly-binding'; sourceKey = 'DeterministicTargetsSource'
        needle = 'RefAssemblyFile="@(IntermediateRefAssembly)"'; replacement = 'RefAssemblyFile=""'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'protocol-drop-target-authority-code'; sourceKey = 'ProtocolModuleSource'
        needle = "        'eng/EdgePluginContractDeterministicBuild.targets',`n"
        replacement = "        # deterministic target authority code removed`n"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'protocol-drop-static-module-authority-code'; sourceKey = 'ProtocolModuleSource'
        needle = "        'scripts/tests/EdgePluginContractStaticGuard.psm1',`n"
        replacement = "        # static module authority code removed`n"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'protocol-drop-static-runner-authority-code'; sourceKey = 'ProtocolModuleSource'
        needle = "        'scripts/tests/Test-EdgePluginContractStaticGuard.ps1',`n"
        replacement = "        # static runner authority code removed`n"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-drop-target-input-role'; sourceKey = 'GeneratorSource'
        needle = "        'deterministic-build-targets'"; replacement = "        'root-configuration'"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-drop-target-input-role'; sourceKey = 'ValidatorSource'
        needle = "        'deterministic-build-targets'"; replacement = "        'root-configuration'"
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-weaken-raw-pe-equality'; sourceKey = 'ValidatorSource'
        needle = 'Assert-JsonEqual $inputFact $actualFact "PE input identity/size/SHA/MVID differs from raw bytes: $($inputFact.path)."'
        replacement = 'Assert-Equal $inputFact.sha256 $actualFact.sha256 "PE input SHA differs from raw bytes."'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'generator-forge-target-digest-pin'; sourceKey = 'GeneratorSource'
        needle = '24aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
        replacement = '04aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
        occurrence = $null; expectedCount = $null
    })
$deterministicMutationRows.Add([pscustomobject][ordered]@{
        name = 'validator-forge-target-digest-pin'; sourceKey = 'ValidatorSource'
        needle = '24aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
        replacement = '04aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
        occurrence = $null; expectedCount = $null
    })
if ($deterministicMutationRows.Count -ne 31 -or
    @($deterministicMutationRows.name | Select-Object -Unique).Count -ne 31) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 deterministic-build mutation inventory drifted from 31.'
}
$deterministicMutationPassed = 0
$deterministicMutationInventoryRows = [Collections.Generic.List[string]]::new()
foreach ($row in $deterministicMutationRows) {
    $mutated = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutated[$key] = [string]$sources[$key] }
    $sourceKey = [string]$row.sourceKey
    $mutatedSource = if ($null -ne $row.occurrence) {
        Replace-StaticGuardOccurrence `
            -Source ([string]$mutated[$sourceKey]) -Needle ([string]$row.needle) `
            -Replacement ([string]$row.replacement) -Occurrence ([int]$row.occurrence) `
            -ExpectedCount ([int]$row.expectedCount) -Name ([string]$row.name)
    }
    else {
        Replace-StaticGuardExact `
            -Source ([string]$mutated[$sourceKey]) -Needle ([string]$row.needle) `
            -Replacement ([string]$row.replacement) -Name ([string]$row.name)
    }
    $mutated[$sourceKey] = $mutatedSource
    $changedSourceKeys = @($sources.Keys | Where-Object {
            -not [string]::Equals(
                [string]$mutated[$_], [string]$sources[$_], [StringComparison]::Ordinal)
        })
    if ($changedSourceKeys.Count -ne 1 -or $changedSourceKeys[0] -cne $sourceKey) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' changed an undeclared deterministic source."
    }
    if ($sourceKey -ceq 'DeterministicTargetsSource') {
        try { [void][xml]$mutatedSource }
        catch { throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced malformed XML." }
    }
    else {
        $tokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseInput(
            $mutatedSource, [ref]$tokens, [ref]$parseErrors)
        if (@($parseErrors).Count -ne 0) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' produced PowerShell parse errors."
        }
    }
    $rejected = $false
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutated) }
    catch {
        if ($_.Exception.Message.Contains(
                'EDGE-SPLIT-AUTHORITY-STATIC-001', [StringComparison]::Ordinal)) {
            $rejected = $true
        }
        else { throw }
    }
    if (-not $rejected) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($row.name)' deterministic mutation was accepted."
    }
    $mutationDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes($mutatedSource))).ToLowerInvariant()
    $deterministicMutationInventoryRows.Add(
        [string]$row.name + '|' + $sourceKey + '|' + $mutationDigest)
    $deterministicMutationPassed++
}
$deterministicMutationInventorySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes(
            (@($deterministicMutationInventoryRows) -join "`n")))).ToLowerInvariant()

$cases = [Collections.Generic.List[object]]::new()
foreach ($name in $developmentMutationNames) {
    $cases.Add([pscustomobject]@{ name = $name; target = 'development'; batch = 'prior-87' })
}
foreach ($row in $focusedMutationRows) {
    $cases.Add([pscustomobject]@{ name = [string]$row.name; target = [string]$row.target; batch = 'prior-22' })
}
foreach ($name in $closureMutationNames) {
    $cases.Add([pscustomobject]@{ name = $name; target = 'development'; batch = 'closure-3' })
}

$targetContracts = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
function Add-MutationTargetContract {
    param(
        [Parameter(Mandatory)][string[]]$Names,
        [Parameter(Mandatory)][string]$Owner,
        [string]$Shape = 'None'
    )
    foreach ($name in $Names) {
        if (-not $targetContracts.TryAdd($name, [pscustomobject][ordered]@{
                    owner = $Owner
                    shape = $Shape
                })) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 duplicate target contract '$name'."
        }
    }
}

Add-MutationTargetContract @('cmdletbinding-drift') 'Development.CmdletBinding'
Add-MutationTargetContract @(
    'repository-param-type', 'plugin-default', 'configuration-default',
    'configuration-validateset', 'current-batch-default', 'viewids-default',
    'timeout-default', 'timeout-range') 'Development.ParameterContract'
Add-MutationTargetContract @('timeout-executable-default') `
    'Development.CanonicalFunctionDigest' 'Development.InvokeDevProcessTimeoutParameter'
Add-MutationTargetContract @('strictmode-downgrade') `
    'Development.CanonicalSourceDigest' 'Development.StrictModeTopLevel'
Add-MutationTargetContract @('erroraction-silence') `
    'Development.CanonicalSourceDigest' 'Development.ErrorActionTopLevel'
Add-MutationTargetContract @('repository-fallback-parent') `
    'Development.CanonicalSourceDigest' 'Development.RepositoryFallback'
Add-MutationTargetContract @('protocol-module-path') `
    'Development.CanonicalSourceDigest' 'Development.ProtocolModulePath'
Add-MutationTargetContract @('import-module-global') 'Development.ModuleImport'
Add-MutationTargetContract @('powershell-path-lookup') `
    'Development.CanonicalSourceDigest' 'Development.PowerShellPathBinding'
Add-MutationTargetContract @('git-command-lookup') `
    'Development.CanonicalSourceDigest' 'Development.GitCommandBinding'
Add-MutationTargetContract @('git-path-forge') `
    'Development.CanonicalSourceDigest' 'Development.GitPathBinding'
Add-MutationTargetContract @('maximum-output-drift') `
    'Development.CanonicalSourceDigest' 'Development.MaximumOutputBinding'
Add-MutationTargetContract @('empty-config-rhs') `
    'Development.CanonicalSourceDigest' 'Development.EmptyGitConfigBinding'
Add-MutationTargetContract @('child-environment-rhs') `
    'Development.CanonicalSourceDigest' 'Development.GitChildEnvironmentBinding'
Add-MutationTargetContract @('pinned-path-rhs') 'Development.EnvironmentInvocation'
Add-MutationTargetContract @('prepin-start-process') `
    'Development.CommandOwnerInventory' 'Development.PrePinStatement'
Add-MutationTargetContract @('prepin-static-process-start') `
    'Development.ProcessOwnerStart' 'Development.PrePinStatement'
Add-MutationTargetContract @('prepin-file-write') `
    'Development.CanonicalSourceDigest' 'Development.PrePinStatement'
Add-MutationTargetContract @(
    'prepin-native-command', 'prepin-new-object-process',
    'prepin-invoke-expression', 'prepin-set-alias',
    'prepin-env-provider-write') `
    'Development.CommandOwnerInventory' 'Development.PrePinStatement'
Add-MutationTargetContract @('prepin-direct-child') `
    'Development.ProcessCallOwnership' 'Development.PrePinStatement'
Add-MutationTargetContract @('outer-before-pin-file-write') 'Development.PinFirst'
Add-MutationTargetContract @(
    'shadow-remove-item', 'shadow-get-item', 'shadow-invokedev-case',
    'nested-shadow-get-item') `
    'Development.FunctionInventory' 'Development.FunctionShadow'
Add-MutationTargetContract @(
    'second-process-startinfo', 'process-startinfo-rebind') `
    'Development.ProcessStartInfoOwner'
Add-MutationTargetContract @(
    'second-process-owner', 'static-process-start-owner') `
    'Development.ProcessOwnerStart'
Add-MutationTargetContract @(
    'start-process-owner', 'new-object-owner', 'add-type-owner') `
    'Development.CommandOwnerInventory'
Add-MutationTargetContract @(
    'environment-clear', 'environment-collection-replace',
    'environment-extra-index-write', 'overlay-source-replace') `
    'Development.ProcessStartInfoMemberInventory'
Add-MutationTargetContract @('environmentvariables-remove') `
    'Development.ProcessStartInfoForbiddenMember'
Add-MutationTargetContract @(
    'overlay-case-downgrade', 'overlay-guard-disable') `
    'Development.OverlayTmpdirRejection'
Add-MutationTargetContract @('overlay-value-replace') 'Development.OverlayAssignment'
Add-MutationTargetContract @('process-scoped-alias') `
    'Development.ProtectedScopedAlias' 'Development.ProcessScopedVariableAlias'
Add-MutationTargetContract @('process-ref-escape') `
    'Development.ProtectedReference' 'Development.ProcessReferenceEscape'
Add-MutationTargetContract @(
    'temporary-root-postwrite', 'current-directory-postwrite') `
    'Development.PhysicalTempMemberAllowlist'
Add-MutationTargetContract @('physical-root-postwrite') 'Development.PhysicalTempDataflow'
Add-MutationTargetContract @('physical-root-return-replace') 'Development.PhysicalTempReturn'
Add-MutationTargetContract @(
    'current-directory-set-disable', 'current-directory-restore-disable') `
    'Development.PhysicalTempCurrentDirectory'
Add-MutationTargetContract @('helper-resolve-path') 'Development.PhysicalTempCommandAllowlist'
Add-MutationTargetContract @('helper-readlink') 'Development.PhysicalTempDynamicMember'
Add-MutationTargetContract @('helper-process-start') `
    'Development.ProcessOwnerStart' 'Development.PhysicalTempHelperProcess'
Add-MutationTargetContract @(
    'physical-temp-rhs-replace', 'physical-temp-postwrite') `
    'Development.PhysicalTempInvocation'
Add-MutationTargetContract @('physical-temp-scoped-alias') `
    'Development.ProtectedScopedAlias' 'Development.PhysicalTempScopedVariableAlias'
Add-MutationTargetContract @('physical-temp-ref-escape') `
    'Development.ProtectedReference' 'Development.PhysicalTempReferenceEscape'
Add-MutationTargetContract @(
    'original-snapshot-index-write', 'original-snapshot-add',
    'original-snapshot-alias', 'pinned-snapshot-index-write',
    'pinned-snapshot-clear', 'pinned-snapshot-alias',
    'restoration-snapshot-index-write', 'restoration-snapshot-remove',
    'restoration-snapshot-alias', 'restore-presence-check-disable',
    'restore-value-index-replace') 'Development.TmpdirSnapshotReadInventory'
Add-MutationTargetContract @('presence-overwrite') 'Development.TmpdirOriginalPresence'
Add-MutationTargetContract @('original-value-overwrite') 'Development.TmpdirOriginalValue'
Add-MutationTargetContract @(
    'tmpdir-lowercase-static-set', 'pin-target-user', 'pin-computed-name') `
    'Development.TmpdirProcessCalls'
Add-MutationTargetContract @('remove-coordinator-cleanup') `
    'Development.CleanupInvocationOrder' 'Development.CoordinatorCleanupRemoved'
Add-MutationTargetContract @('remove-outer-cleanup') `
    'Development.CleanupInvocationOrder' 'Development.OuterCleanupRemoved'
Add-MutationTargetContract @('tmp-first-failure-overwrite') 'Development.TmpdirFailureCatch'
Add-MutationTargetContract @('cwd-first-failure-overwrite') `
    'Development.CurrentDirectoryFailureCatch'
Add-MutationTargetContract @('absent-restore-alias') `
    'Development.CommandOwnerInventory' 'Development.RestoreCommandAlias'
Add-MutationTargetContract @('dev-remove-coordinator-cleanup') `
    'Development.CleanupDirectOwner' 'Development.CoordinatorCleanupUnreachable'
Add-MutationTargetContract @('dev-remove-outer-cleanup') `
    'Development.CleanupDirectOwner' 'Development.OuterCleanupUnreachable'
Add-MutationTargetContract @(
    'module-top-start-process', 'module-top-static-process-start',
    'module-top-file-write') `
    'Protocol.TopLevelFunctionInventory' 'Protocol.TopLevelStatement'
Add-MutationTargetContract @('module-strictmode-downgrade') 'Protocol.SetStrictMode'
Add-MutationTargetContract @('module-erroraction-silence') 'Protocol.ErrorAction'
Add-MutationTargetContract @(
    'module-function-shadow-get-item', 'module-function-shadow-remove-item') `
    'Protocol.TopLevelFunctionInventory' 'Protocol.FunctionShadow'
Add-MutationTargetContract @(
    'module-second-process-start-info', 'module-process-startinfo-rebind',
    'module-static-process-start-owner') 'Protocol.ProcessConstruction'
Add-MutationTargetContract @('module-addtype-process-injection') `
    'Protocol.AddTypeCanonicalDigest' 'Protocol.AddTypeProcessBody'
Add-MutationTargetContract @('module-addtype-static-constructor') `
    'Protocol.AddTypeCanonicalDigest' 'Protocol.AddTypeStaticConstructor'
Add-MutationTargetContract @('module-addtype-file-write-injection') `
    'Protocol.AddTypeCanonicalDigest' 'Protocol.AddTypeFileWriteBody'
Add-MutationTargetContract @(
    'module-cleanup-reachable-from-ingress',
    'module-cleanup-reachable-from-resolver') 'Protocol.PrePinReachability'
Add-MutationTargetContract @(
    'module-prepin-helper-operator-drift', 'module-dynamic-command') `
    'Protocol.DynamicCommand'
Add-MutationTargetContract @('module-export-alias-surface') 'Protocol.ExportSurface'
Add-MutationTargetContract @('module-top-pure-expression') `
    'Protocol.TopLevelFunctionInventory' 'Protocol.TopLevelStatement'
Add-MutationTargetContract @('module-dynamic-member') 'Protocol.DynamicMember'
Add-MutationTargetContract @(
    'cleanup-condition-disabled', 'outer-cleanup-unreachable') `
    'Development.CleanupDirectOwner'
Add-MutationTargetContract @('post-pin-early-success-exit') `
    'Development.OuterFlowDigest' 'Development.PostPinEarlySuccessExit'

if ($developmentMutationNames.Count -ne 87 -or
    $focusedMutationRows.Count -ne 22 -or
    $closureMutationNames.Count -ne 3 -or
    $cases.Count -ne 112 -or
    @($cases.name | Select-Object -Unique).Count -ne 112 -or
    $targetContracts.Count -ne 112 -or
    @($cases | Where-Object { -not $targetContracts.ContainsKey([string]$_.name) }).Count -ne 0) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 persisted mutation inventory drifted from 87+22+3=112.'
}

$passed = 0
$targetOwnerVerified = 0
$mutationBodyDigestOwners = [Collections.Generic.Dictionary[string, string]]::new(
    [StringComparer]::Ordinal)
$mutationBodyDigestRows = [Collections.Generic.List[string]]::new()
$targetOwnerRows = [Collections.Generic.List[string]]::new()
foreach ($case in $cases) {
    $mutated = [ordered]@{}
    foreach ($key in $sources.Keys) { $mutated[$key] = [string]$sources[$key] }
    if ($case.target -ceq 'development') {
        $mutated.DevelopmentSource = New-DevelopmentStaticMutation `
            -Source ([string]$mutated.DevelopmentSource) -Name ([string]$case.name)
        $mutatedSource = [string]$mutated.DevelopmentSource
    }
    elseif ($case.target -ceq 'protocolModule') {
        $mutated.ProtocolModuleSource = New-ProtocolModuleStaticMutation `
            -Source ([string]$mutated.ProtocolModuleSource) -Name ([string]$case.name)
        $mutatedSource = [string]$mutated.ProtocolModuleSource
    }
    else { throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 unknown target '$($case.target)'." }

    $expectedSourceKey = if ($case.target -ceq 'development') {
        'DevelopmentSource'
    }
    else { 'ProtocolModuleSource' }
    $changedSourceKeys = @($sources.Keys | Where-Object {
            -not [string]::Equals(
                [string]$mutated[$_], [string]$sources[$_],
                [StringComparison]::Ordinal)
        })
    if ($changedSourceKeys.Count -ne 1 -or $changedSourceKeys[0] -cne $expectedSourceKey) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' changed a source outside its declared AST owner."
    }

    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        $mutatedSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' produced parse errors."
    }

    $contract = $targetContracts[[string]$case.name]
    $targetResult = Assert-EdgePluginContractStaticGuard `
        -MutationTarget ([string]$case.target) `
        -MutationName ([string]$case.name) `
        -TargetOwner ([string]$contract.owner) `
        -ExpectedShape ([string]$contract.shape) `
        -MutationSource $mutatedSource
    if ($targetResult.passed -ne $true -or
        $targetResult.mutationName -cne [string]$case.name -or
        $targetResult.target -cne [string]$case.target -or
        $targetResult.targetOwner -cne [string]$contract.owner -or
        $targetResult.shapePredicate -cne [string]$contract.shape) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' returned an invalid dedicated target-owner result."
    }
    $targetOwnerVerified++
    $targetOwnerRows.Add(
        [string]$case.batch + '|' + [string]$case.target + '|' +
        [string]$case.name + '|' + [string]$contract.owner + '|' +
        [string]$contract.shape)

    $mutationBodySource = [Text.RegularExpressions.Regex]::Replace(
        $mutatedSource, '(?m)[ \t]*# mutation:[^\r\n]*', '',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $mutationBodyDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes($mutationBodySource))).ToLowerInvariant()
    $mutationBodyOwner = [string]$case.target + '|' + [string]$case.name
    if (-not $mutationBodyDigestOwners.TryAdd(
            $mutationBodyDigest, $mutationBodyOwner)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$mutationBodyOwner' duplicates mutation body digest owned by '$($mutationBodyDigestOwners[$mutationBodyDigest])'."
    }
    $mutationBodyDigestRows.Add($mutationBodyOwner + '|' + $mutationBodyDigest)

    $rejected = $false
    try { [void](Invoke-CanonicalStaticGuard -SourceSet $mutated) }
    catch {
        if ($_.Exception.Message.Contains(
                'EDGE-SPLIT-AUTHORITY-STATIC-001', [StringComparison]::Ordinal)) {
            $rejected = $true
        }
        else { throw }
    }
    if (-not $rejected) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' was accepted."
    }
    $passed++
}

$canonicalDigestCases = @($cases | Where-Object {
        $owner = [string]$targetContracts[[string]$_.name].owner
        $owner.StartsWith('Development.Canonical', [StringComparison]::Ordinal) -or
        $owner -ceq 'Protocol.AddTypeCanonicalDigest'
    })
$powerShellAstCanonicalShapeVerified = @($canonicalDigestCases | Where-Object {
        [string]$_.target -ceq 'development'
    }).Count
$roslynCSharpCanonicalShapeVerified = @($canonicalDigestCases | Where-Object {
        [string]$_.target -ceq 'protocolModule'
    }).Count
$canonicalShapeDecoyPassed = 0
$canonicalShapeDecoyRows = [Collections.Generic.List[string]]::new()
if ($canonicalDigestCases.Count -ne 15 -or
    $powerShellAstCanonicalShapeVerified -ne 12 -or
    $roslynCSharpCanonicalShapeVerified -ne 3) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 canonical shape-decoy inventory drifted from 15.'
}
foreach ($case in $canonicalDigestCases) {
    $contract = $targetContracts[[string]$case.name]
    $canonicalSource = if ($case.target -ceq 'development') {
        [string]$sources.DevelopmentSource
    }
    else { [string]$sources.ProtocolModuleSource }
    $decoySource = New-CanonicalDigestShapeDecoy `
        -Source $canonicalSource -Name ([string]$case.name)
    if ([string]::Equals(
            $decoySource, $canonicalSource, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' shape decoy did not drift source bytes."
    }
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput(
        $decoySource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' shape decoy has PowerShell parse errors."
    }
    $shapeRejected = $false
    try {
        [void](Assert-EdgePluginContractStaticGuard `
            -MutationTarget ([string]$case.target) `
            -MutationName ([string]$case.name) `
            -TargetOwner ([string]$contract.owner) `
            -ExpectedShape ([string]$contract.shape) `
            -MutationSource $decoySource)
    }
    catch {
        if ($_.Exception.Message.StartsWith(
                'EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001',
                [StringComparison]::Ordinal)) {
            $shapeRejected = $true
        }
        else { throw }
    }
    if (-not $shapeRejected) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-001 '$($case.name)' comment/string decoy reached a digest or was accepted instead of failing its AST shape."
    }
    $decoyDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes($decoySource))).ToLowerInvariant()
    $canonicalShapeDecoyRows.Add(
        [string]$case.target + '|' + [string]$case.name + '|' +
        [string]$contract.owner + '|' + [string]$contract.shape + '|' +
        $decoyDigest)
    $canonicalShapeDecoyPassed++
}
if ($canonicalShapeDecoyPassed -ne 15 -or
    $canonicalShapeDecoyRows.Count -ne 15) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 canonical shape decoys did not pass 15/15 at the mutation-target shape layer.'
}
$prePinDirectPipelineDecoySource = Add-StaticGuardStatementAfter `
    -Source ([string]$sources.DevelopmentSource) `
    -Anchor '$physicalTempRoot = Resolve-DevPhysicalTempRoot' `
    -Statement '$false -and [IO.File]::WriteAllBytes((Join-Path $physicalTempRoot ''prepin.bin''), [byte[]]@(1))' `
    -Name 'prepin-file-write-unreachable-wrapper-decoy'
$prePinDirectPipelineDecoySource +=
    "`n# unrelated-digest-drift[prepin-file-write-unreachable-wrapper-decoy]: bytes only`n"
$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseInput(
    $prePinDirectPipelineDecoySource, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 prepin direct-pipeline decoy has PowerShell parse errors.'
}
$prePinDirectPipelineDecoyPassed = 0
$prePinContract = $targetContracts['prepin-file-write']
try {
    [void](Assert-EdgePluginContractStaticGuard `
        -MutationTarget development `
        -MutationName 'prepin-file-write' `
        -TargetOwner ([string]$prePinContract.owner) `
        -ExpectedShape ([string]$prePinContract.shape) `
        -MutationSource $prePinDirectPipelineDecoySource)
}
catch {
    if ($_.Exception.Message.StartsWith(
            'EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001',
            [StringComparison]::Ordinal)) {
        $prePinDirectPipelineDecoyPassed = 1
    }
    else { throw }
}
if ($prePinDirectPipelineDecoyPassed -ne 1) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 unreachable prepin logical wrapper reached a digest or was accepted instead of failing its direct-pipeline shape.'
}
$prePinDirectPipelineDecoySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes(
            $prePinDirectPipelineDecoySource))).ToLowerInvariant()
if ($mutationBodyDigestOwners.Count -ne 112 -or
    $mutationBodyDigestRows.Count -ne 112 -or
    $targetOwnerVerified -ne 112 -or $targetOwnerRows.Count -ne 112) {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 mutation-body or target-owner inventory drifted from 112 verified mutations.'
}

$inventoryText = (@($cases | ForEach-Object {
            [string]$_.batch + '|' + [string]$_.target + '|' + [string]$_.name
        }) -join "`n")
$inventorySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes($inventoryText))).ToLowerInvariant()
$mutationBodyInventoryText = @($mutationBodyDigestRows) -join "`n"
$mutationBodyInventorySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes($mutationBodyInventoryText))).ToLowerInvariant()
$targetOwnerInventoryText = @($targetOwnerRows) -join "`n"
$targetOwnerInventorySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes(
            $targetOwnerInventoryText))).ToLowerInvariant()
# GAP-A-DIRECT-INVENTORY-PINS-BEGIN
if ($inventorySha256 -cne
        '85ca980331e817ad4ba7e151a5891530c6b0dd7285e1dd3041b01638f3647dfe' -or
    $targetOwnerInventorySha256 -cne
        '6a67b37b7d72103b1ef5e7fbaa476f541b01a48030d49a2c9a5916b0265223de') {
    throw 'EDGE-SPLIT-AUTHORITY-MUTATION-001 legacy mutation name/owner inventory changed.'
}
# GAP-A-DIRECT-INVENTORY-PINS-END
$canonicalShapeDecoyInventoryText = @($canonicalShapeDecoyRows) -join "`n"
$canonicalShapeDecoyInventorySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.UTF8Encoding]::new($false).GetBytes(
            $canonicalShapeDecoyInventoryText))).ToLowerInvariant()
$targetOwnerCountMap = [Collections.Generic.Dictionary[string, int]]::new(
    [StringComparer]::Ordinal)
foreach ($case in $cases) {
    $owner = [string]$targetContracts[[string]$case.name].owner
    $targetOwnerCountMap[$owner] = 1 + [int]$targetOwnerCountMap[$owner]
}
$targetOwnerNames = [string[]]@($targetOwnerCountMap.Keys)
[Array]::Sort($targetOwnerNames, [StringComparer]::Ordinal)
$targetOwnerCounts = [ordered]@{}
foreach ($owner in $targetOwnerNames) {
    $targetOwnerCounts[$owner] = [int]$targetOwnerCountMap[$owner]
}
$canonicalDigestTargetCount = @($cases | Where-Object {
        $owner = [string]$targetContracts[[string]$_.name].owner
        $owner.StartsWith('Development.Canonical', [StringComparison]::Ordinal) -or
        $owner -ceq 'Protocol.AddTypeCanonicalDigest'
    }).Count
$shapePredicateVerified = @($cases | Where-Object {
        [string]$targetContracts[[string]$_.name].shape -cne 'None'
    }).Count
$result = [pscustomobject][ordered]@{
    schemaVersion = 1
    owner = 'scripts/tests/Test-EdgePluginContractStaticGuard.ps1'
    canonicalOwner = [string]$canonical.owner
    canonicalPassed = $true
    canonicalSourceCount = [int]$canonical.sourceCount
    canonicalFormalSha256 = [string]$canonical.sourceDigests.formal
    legacyProjectionByteLength = $legacyProjectionBytes.Length
    legacyProjectionSha256 = $legacyProjectionSha256
    legacyPriorMutationTotal = $legacyPriorMutationTotal
    priorDevelopmentPassed = 87
    priorFocusedPassed = 22
    closurePassed = 3
    formalMutationPassed = $formalMutationPassed
    formalMutationTotal = $formalMutationRows.Count
    formalResultSchemaNegativePassed = $formalResultSchemaNegativePassed
    formalResultSchemaNegativeTotal = 1
    behaviorRuntimeShapeMutationPassed = $behaviorRuntimeShapeMutationPassed
    behaviorRuntimeShapeMutationTotal = 1
    developmentManifestRuntimeShapeMutationPassed = $developmentManifestRuntimeShapeMutationPassed
    developmentManifestRuntimeShapeMutationTotal = $developmentManifestRuntimeShapeMutationRows.Count
    behaviorReplayFixtureProtocolMutationPassed = $behaviorReplayFixtureProtocolMutationPassed
    behaviorReplayFixtureProtocolMutationTotal = $behaviorReplayFixtureProtocolMutationRows.Count
    behaviorPackageCountMutationPassed = $behaviorPackageCountMutationPassed
    behaviorPackageCountMutationTotal = $behaviorPackageCountMutationRows.Count
    behaviorTailContractMutationPassed = $behaviorTailContractMutationPassed
    behaviorTailContractMutationTotal = $behaviorTailContractMutationRows.Count
    validatorCommitPairOrderingMutationPassed = $validatorCommitPairOrderingMutationPassed
    validatorCommitPairOrderingMutationTotal = 1
    behaviorDiagnosticMutationPassed = $behaviorDiagnosticMutationPassed
    behaviorDiagnosticMutationTotal = $behaviorDiagnosticMutationRows.Count
    deterministicMutationPassed = $deterministicMutationPassed
    deterministicMutationTotal = $deterministicMutationRows.Count
    deterministicMutationInventorySha256 = $deterministicMutationInventorySha256
    mutationPassed = $passed
    mutationTotal = $cases.Count
    inventorySha256 = $inventorySha256
    mutationBodyUnique = $mutationBodyDigestOwners.Count
    mutationBodyInventorySha256 = $mutationBodyInventorySha256
    targetOwnerVerified = $targetOwnerVerified
    targetOwnerInventorySha256 = $targetOwnerInventorySha256
    canonicalDigestTargetCount = $canonicalDigestTargetCount
    dedicatedSemanticTargetCount = $targetOwnerVerified - $canonicalDigestTargetCount
    shapePredicateVerified = $shapePredicateVerified
    powerShellAstCanonicalShapeVerified = $powerShellAstCanonicalShapeVerified
    roslynCSharpCanonicalShapeVerified = $roslynCSharpCanonicalShapeVerified
    canonicalShapeDecoyPassed = $canonicalShapeDecoyPassed
    canonicalShapeDecoyTotal = $canonicalDigestCases.Count
    canonicalShapeDecoyInventorySha256 = $canonicalShapeDecoyInventorySha256
    prePinDirectPipelineDecoyPassed = $prePinDirectPipelineDecoyPassed
    prePinDirectPipelineDecoyTotal = 1
    prePinDirectPipelineDecoySha256 = $prePinDirectPipelineDecoySha256
    targetOwnerCounts = [pscustomobject]$targetOwnerCounts
}
$result | ConvertTo-Json -Depth 4 -Compress
