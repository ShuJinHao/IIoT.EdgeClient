[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$LedgerPath = 'eng/baselines/edge-plugin-contract-ledger.json',
    [string]$Phase0InputsPath = 'eng/baselines/edge-split-phase0-inputs.json',
    [switch]$CommitPairGateOnly,
    [string]$PackageFixturePath = '',
    [string]$PackageFixtureManifestPath = '',
    [string[]]$PackageFixtureOwnedAssemblyPath = @(),
    [string[]]$PackageFixtureDeclaredOwnedAssembly = @(),
    [string]$PhaseGateFixturePath = '',
    [switch]$RequireAuthorityReceipt,
    [switch]$RequireFormalAuthorityReceipt,
    [switch]$AuthorityRebuildOnly,
    [string]$AuthorityResultPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pinnedChildProtocolModulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
Import-Module $pinnedChildProtocolModulePath -Force
$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)
if (-not $authorityChildEnvironmentBound) {
    Assert-EdgeAuthorityGitEnvironment
}
if (($RequireAuthorityReceipt -or $RequireFormalAuthorityReceipt -or $AuthorityRebuildOnly -or
        -not [string]::IsNullOrWhiteSpace($AuthorityResultPath)) -and
    -not $authorityChildEnvironmentBound) {
    throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING authority validator/fast mode requires the exact parent-controlled child binding.'
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

function Assert-NoRepositoryReparsePoint {
    param([Parameter(Mandatory = $true)][string]$FullPath)

    $rootItem = Get-Item -LiteralPath $RepositoryRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootItem.LinkTarget)) {
        throw "EDGE-SPLIT-LEDGER-001 repository root must not be a symlink/reparse point: $RepositoryRoot"
    }
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
            throw "EDGE-SPLIT-LEDGER-001 repository paths must not traverse symlink/reparse points: $current"
        }
    }
}

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $path = if ([IO.Path]::IsPathRooted($PathValue)) {
        [IO.Path]::GetFullPath($PathValue)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
    }
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $path)
    if ($relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "EDGE-SPLIT-LEDGER-001 path must stay inside the repository: $PathValue"
    }
    Assert-NoRepositoryReparsePoint -FullPath $path
    return $path
}

function Test-IndependentPathIdentityEqual {
    param(
        [Parameter(Mandatory = $true)][string]$FirstPath,
        [Parameter(Mandatory = $true)][string]$SecondPath
    )

    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    return [string]::Equals(
        [IO.Path]::GetFullPath($FirstPath),
        [IO.Path]::GetFullPath($SecondPath),
        $comparison)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -LiteralPath $PathValue -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-IndependentPathMapSourceToken {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $fullPath = [IO.Path]::GetFullPath($PathValue).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    if ([string]::IsNullOrWhiteSpace($fullPath)) {
        throw 'EDGE-SPLIT-LEDGER-001 independent PathMap source root is empty.'
    }
    return $fullPath.Replace('=', '==').Replace(',', ',,')
}

function ConvertTo-IndependentMsBuildPropertyValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    return $Value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
}

function Assert-IndependentEmbeddedDebugIdentity {
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    $portablePdbPath = [IO.Path]::ChangeExtension($AssemblyPath, '.pdb')
    if (Test-Path -LiteralPath $portablePdbPath -PathType Leaf) {
        throw "EDGE-SPLIT-LEDGER-001 independent deterministic build left a stale portable PDB sibling: $portablePdbPath"
    }
    $stream = [IO.File]::OpenRead($AssemblyPath)
    try {
        $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $debugEntries = @($peReader.ReadDebugDirectory())
            $codeViewEntries = @($debugEntries | Where-Object Type -eq (
                    [Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView))
            $embeddedEntries = @($debugEntries | Where-Object Type -eq (
                    [Reflection.PortableExecutable.DebugDirectoryEntryType]::EmbeddedPortablePdb))
            if ($codeViewEntries.Count -ne 1 -or $embeddedEntries.Count -ne 1) {
                throw 'EDGE-SPLIT-LEDGER-001 independently rebuilt PE must contain exactly one filename-only CodeView entry and one embedded portable PDB.'
            }
            $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntries[0])
            $expectedPdbName = [IO.Path]::GetFileNameWithoutExtension($AssemblyPath) + '.pdb'
            if ([string]$codeView.Path -cne $expectedPdbName -or
                [string]$codeView.Path -match '[/\\]') {
                throw "EDGE-SPLIT-LEDGER-001 independently rebuilt PE CodeView path is physical or non-canonical: $($codeView.Path)"
            }
            $provider = $peReader.ReadEmbeddedPortablePdbDebugDirectoryData($embeddedEntries[0])
            try {
                $pdbReader = $provider.GetMetadataReader()
                foreach ($documentHandle in $pdbReader.Documents) {
                    $document = $pdbReader.GetDocument($documentHandle)
                    $documentName = $pdbReader.GetString($document.Name)
                    if ($documentName.Contains('//', [StringComparison]::Ordinal) -or
                        -not ($documentName.StartsWith('/_/', [StringComparison]::Ordinal) -or
                            $documentName.StartsWith('/__edge_contract_generated__/', [StringComparison]::Ordinal))) {
                        throw "EDGE-SPLIT-LEDGER-001 independently rebuilt embedded PDB document escaped canonical virtual roots: $documentName"
                    }
                }
            }
            finally { $provider.Dispose() }
        }
        finally { $peReader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-IndependentDeterministicBuildLog {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$EncodedPathMap,
        [Parameter(Mandatory = $true)][string]$EncodedTargetsPath,
        [Parameter(Mandatory = $true)][string]$EncodedRepositoryRoot
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 independent deterministic build log is missing: $LogPath"
    }
    $projectDirectory = Split-Path ([IO.Path]::GetFullPath($ProjectPath)) -Parent
    $projectRelativeDirectory = [IO.Path]::GetRelativePath($RepositoryRoot, $projectDirectory).Replace('\', '/')
    $expectedMarker = "EDGE-SPLIT-AUTHORITY-DETERMINISTIC-BUILD|project=$projectRelativeDirectory|virtual=/_/$projectRelativeDirectory"
    $logText = Get-Content -LiteralPath $LogPath -Raw
    foreach ($requiredText in @(
            '--property:DebugSymbols=true',
            '--property:DebugType=embedded',
            '--property:Deterministic=true',
            "--property:PathMap=$EncodedPathMap",
            "--property:CustomAfterMicrosoftCSharpTargets=$EncodedTargetsPath",
            '--property:_EdgeContractAuthorityBuild=true',
            "--property:_EdgeContractRepositoryRoot=$EncodedRepositoryRoot",
            $expectedMarker)) {
        if (-not $logText.Contains($requiredText, [StringComparison]::Ordinal)) {
            throw "EDGE-SPLIT-LEDGER-001 independent deterministic build log lacks an exact required binding: $requiredText"
        }
    }
}

function Get-IndependentSha512Base64 {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    $inputStream = [IO.File]::OpenRead($FilePath)
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try {
        return [Convert]::ToBase64String($sha512.ComputeHash($inputStream))
    }
    finally {
        $sha512.Dispose()
        $inputStream.Dispose()
    }
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return '' }
    return [string]$property.Value
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    $output = & $FilePath @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')`n$output"
    }
    return $output.Trim()
}

function Get-IndependentEnvironmentSnapshot {
    param([Parameter(Mandatory = $true)][string[]]$VariableNames)

    $states = [ordered]@{}
    foreach ($variableName in $VariableNames) {
        $currentValue = [Environment]::GetEnvironmentVariable($variableName, 'Process')
        $states[$variableName] = [pscustomobject]@{
            present = $null -ne $currentValue
            text = if ($null -eq $currentValue) { '' } else { [string]$currentValue }
        }
    }
    return $states
}

function Restore-IndependentEnvironmentSnapshot {
    param(
        [Parameter(Mandatory = $true)][string[]]$VariableNames,
        [Parameter(Mandatory = $true)]$States
    )

    foreach ($variableName in $VariableNames) {
        if (-not $States.Contains($variableName)) {
            throw "EDGE-SPLIT-LEDGER-001 independent environment snapshot lacks variable: $variableName."
        }
        $state = $States[$variableName]
        if ([bool]$state.present) {
            [Environment]::SetEnvironmentVariable($variableName, [string]$state.text, 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable($variableName, $null, 'Process')
            if ($null -ne [Environment]::GetEnvironmentVariable($variableName, 'Process')) {
                Remove-Item -Path "Env:$variableName" -Force -ErrorAction SilentlyContinue
            }
            if ($null -ne [Environment]::GetEnvironmentVariable($variableName, 'Process')) {
                throw "EDGE-SPLIT-LEDGER-001 independent undefined environment variable could not be restored: $variableName."
            }
        }
    }
}

function Test-IndependentPathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$CandidatePath
    )

    $root = [IO.Path]::GetFullPath($RootPath).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $candidate = [IO.Path]::GetFullPath($CandidatePath)
    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    if ([string]::Equals($root, $candidate, $comparison)) { return $true }
    return $candidate.StartsWith("$root$([IO.Path]::DirectorySeparatorChar)", $comparison)
}

function Assert-IndependentAuthorityPath {
    param(
        [Parameter(Mandatory = $true)][string]$DeclaredRoot,
        [Parameter(Mandatory = $true)][string]$CandidatePath
    )

    $root = [IO.Path]::GetFullPath($DeclaredRoot)
    $candidate = [IO.Path]::GetFullPath($CandidatePath)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "EDGE-SPLIT-LEDGER-001 independent authority root is not a real directory: $root."
    }
    $rootNode = Get-Item -LiteralPath $root -Force
    if (($rootNode.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootNode.LinkTarget)) {
        throw "EDGE-SPLIT-LEDGER-001 independent authority root is a symlink/reparse point: $root."
    }
    if (-not (Test-IndependentPathWithinRoot -RootPath $root -CandidatePath $candidate)) {
        throw "EDGE-SPLIT-LEDGER-001 independent authority path escapes its approved root: $candidate."
    }
    $relative = [IO.Path]::GetRelativePath($root, $candidate)
    $cursor = $root
    foreach ($piece in $relative.Split(
            [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $cursor = Join-Path $cursor $piece
        if (-not (Test-Path -LiteralPath $cursor)) { break }
        $node = Get-Item -LiteralPath $cursor -Force
        if (($node.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$node.LinkTarget)) {
            throw "EDGE-SPLIT-LEDGER-001 independent authority path traverses a symlink/reparse point: $cursor."
        }
    }
}

function Get-IndependentJsonMember {
    param(
        [Parameter(Mandatory = $true)]$Container,
        [Parameter(Mandatory = $true)][string]$MemberName,
        [switch]$Optional
    )

    $ordinalMembers = @($Container.PSObject.Properties | Where-Object { [string]$_.Name -ceq $MemberName })
    if ($ordinalMembers.Count -eq 1) { return $ordinalMembers[0].Value }
    if ($Optional -and $ordinalMembers.Count -eq 0) { return $null }
    throw "EDGE-SPLIT-LEDGER-001 independent restore JSON member is not ordinal-exact: $MemberName count=$($ordinalMembers.Count)."
}

function Get-IndependentNuGetReferenceProvenance {
    param(
        [Parameter(Mandatory = $true)]$ReferenceItem,
        [Parameter(Mandatory = $true)][string]$AssemblyPath,
        [Parameter(Mandatory = $true)]$RestoreAssets,
        [Parameter(Mandatory = $true)][string]$RestoreAssetsPath,
        [Parameter(Mandatory = $true)][string]$PackagesRoot
    )

    $id = Get-OptionalProperty $ReferenceItem 'NuGetPackageId'
    $version = Get-OptionalProperty $ReferenceItem 'NuGetPackageVersion'
    $packagePath = (Get-OptionalProperty $ReferenceItem 'PathInPackage').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($version) -or
        [string]::IsNullOrWhiteSpace($packagePath) -or [IO.Path]::IsPathRooted($packagePath) -or
        $packagePath.StartsWith('../', [StringComparison]::Ordinal) -or
        $packagePath.Contains('/../', [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-LEDGER-001 independent NuGet reference lacks safe exact restore metadata.'
    }
    $expectedAssembly = [IO.Path]::GetFullPath((Join-Path $PackagesRoot "$($id.ToLowerInvariant())/$version/$packagePath"))
    if (-not (Test-IndependentPathIdentityEqual $expectedAssembly $AssemblyPath)) {
        throw "EDGE-SPLIT-LEDGER-001 independent NuGet metadata does not resolve exact assembly bytes: $id/$version/$packagePath."
    }
    Assert-IndependentAuthorityPath -DeclaredRoot $PackagesRoot -CandidatePath $expectedAssembly
    $folderMap = Get-IndependentJsonMember $RestoreAssets 'packageFolders'
    $matchingFolders = @($folderMap.PSObject.Properties | Where-Object {
            Test-IndependentPathIdentityEqual ([string]$_.Name) $PackagesRoot
        })
    if ($matchingFolders.Count -ne 1) {
        throw 'EDGE-SPLIT-LEDGER-001 independent restore graph does not bind exact global packages folder.'
    }
    $assetKey = "$id/$version"
    $libraries = Get-IndependentJsonMember $RestoreAssets 'libraries'
    $library = Get-IndependentJsonMember $libraries $assetKey
    if ([string](Get-IndependentJsonMember $library 'type') -cne 'package' -or
        [string](Get-IndependentJsonMember $library 'path') -cne "$($id.ToLowerInvariant())/$version" -or
        @((Get-IndependentJsonMember $library 'files') | Where-Object { [string]$_ -ceq $packagePath }).Count -ne 1) {
        throw "EDGE-SPLIT-LEDGER-001 independent restore library does not bind exact package asset: $assetKey|$packagePath."
    }
    $targets = Get-IndependentJsonMember $RestoreAssets 'targets'
    $packageTargetCount = 0
    $assetSelectionCount = 0
    foreach ($target in $targets.PSObject.Properties) {
        $targetPackage = Get-IndependentJsonMember $target.Value $assetKey -Optional
        if ($null -eq $targetPackage) { continue }
        $packageTargetCount++
        foreach ($groupName in @('compile', 'runtime', 'runtimeTargets')) {
            $group = Get-IndependentJsonMember $targetPackage $groupName -Optional
            if ($null -ne $group -and
                @($group.PSObject.Properties | Where-Object { [string]$_.Name -ceq $packagePath }).Count -eq 1) {
                $assetSelectionCount++
            }
        }
    }
    if ($packageTargetCount -lt 1 -or $assetSelectionCount -lt 1) {
        throw "EDGE-SPLIT-LEDGER-001 independent restore target does not select exact NuGet reference: $assetKey|$packagePath."
    }
    return [pscustomobject][ordered]@{
        kind = 'nuget-package'
        packageId = $id
        packageVersion = $version
        pathInPackage = $packagePath
        assetsPath = $RestoreAssetsPath
    }
}

function Get-IndependentFrameworkReferenceProvenance {
    param(
        [Parameter(Mandatory = $true)]$ReferenceItem,
        [Parameter(Mandatory = $true)][string]$AssemblyPath,
        [Parameter(Mandatory = $true)][string]$TargetingPackRoot
    )

    $framework = Get-OptionalProperty $ReferenceItem 'FrameworkReferenceName'
    $frameworkVersion = Get-OptionalProperty $ReferenceItem 'FrameworkReferenceVersion'
    $packId = Get-OptionalProperty $ReferenceItem 'NuGetPackageId'
    $packVersion = Get-OptionalProperty $ReferenceItem 'NuGetPackageVersion'
    if ([string]::IsNullOrWhiteSpace($framework) -or [string]::IsNullOrWhiteSpace($frameworkVersion) -or
        $packId -cne "${framework}.Ref" -or $packVersion -cne $frameworkVersion) {
        throw 'EDGE-SPLIT-LEDGER-001 independent framework metadata does not identify exact targeting pack.'
    }
    Assert-IndependentAuthorityPath -DeclaredRoot $TargetingPackRoot -CandidatePath $AssemblyPath
    $relativePackPath = [IO.Path]::GetRelativePath($TargetingPackRoot, $AssemblyPath).Replace('\', '/')
    if (-not $relativePackPath.StartsWith("$packId/$frameworkVersion/ref/", [StringComparison]::Ordinal) -or
        -not $relativePackPath.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
        throw "EDGE-SPLIT-LEDGER-001 independent framework bytes escape exact targeting pack: $relativePackPath."
    }
    return [pscustomobject][ordered]@{
        kind = 'framework-reference'
        frameworkName = $framework
        frameworkVersion = $frameworkVersion
        targetingPackId = $packId
        targetingPackPath = $relativePackPath
    }
}

function Get-IndependentRootMappings {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PackagesRoot,
        [Parameter(Mandatory = $true)][string]$SdkRoot
    )

    $items = [object[]]@(
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($RepoRoot).Replace('\', '/').TrimEnd('/'); token = '$REPOSITORY' },
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($PackagesRoot).Replace('\', '/').TrimEnd('/'); token = '$NUGET_PACKAGES' },
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($SdkRoot).Replace('\', '/').TrimEnd('/'); token = '$DOTNET_ROOT' }
    )
    [Array]::Sort($items, [Collections.Generic.Comparer[object]]::Create([Comparison[object]]{
                param($first, $second)
                $byLength = ([string]$second.root).Length.CompareTo(([string]$first.root).Length)
                if ($byLength -ne 0) { return $byLength }
                return [StringComparer]::Ordinal.Compare([string]$first.root, [string]$second.root)
            }))
    return $items
}

function Get-IndependentNuGetSources {
    param([Parameter(Mandatory = $true)][string]$NuGetConfigPath)

    [xml]$document = Get-Content -LiteralPath $NuGetConfigPath -Raw
    if ($null -eq $document.configuration -or $null -eq $document.configuration.packageSources -or
        @($document.SelectNodes('//packageSourceCredentials')).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 independent NuGet.Config inspection requires packageSources without credentials.'
    }
    $clearOperations = 0
    $values = [Collections.Generic.List[string]]::new()
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($child in $document.configuration.packageSources.ChildNodes) {
        if ($child.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        if ([string]$child.LocalName -ceq 'clear') {
            $clearOperations++
            continue
        }
        if ([string]$child.LocalName -cne 'add') {
            throw "EDGE-SPLIT-LEDGER-001 independent NuGet.Config inspection found unsupported source operation: $($child.LocalName)."
        }
        $name = [string]$child.GetAttribute('key')
        $value = [string]$child.GetAttribute('value')
        $uri = $null
        if ([string]::IsNullOrWhiteSpace($name) -or -not $names.Add($name) -or
            -not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or
            -not (Test-OrdinalEqualsAny $uri.Scheme @('http', 'https')) -or
            -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
            -not [string]::IsNullOrWhiteSpace($uri.Query) -or
            -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
            throw 'EDGE-SPLIT-LEDGER-001 independent NuGet.Config source is ambiguous or credential-bearing.'
        }
        $values.Add($value)
    }
    if ($clearOperations -ne 1 -or $values.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 independent NuGet.Config must clear ambient sources once and declare sources.'
    }
    return @($values.ToArray() | Sort-Ordinal -Unique)
}

function ConvertTo-IndependentTokenText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$InputText,
        [Parameter(Mandatory = $true)][object[]]$Mappings
    )

    $result = $InputText.Replace("`r`n", "`n").Replace('\', '/')
    $comparisonOptions = if ([OperatingSystem]::IsWindows()) {
        [Text.RegularExpressions.RegexOptions]::IgnoreCase
    }
    else { [Text.RegularExpressions.RegexOptions]::None }
    foreach ($mapping in $Mappings) {
        $boundaryPattern = "(?<![A-Za-z0-9_.-])$([Text.RegularExpressions.Regex]::Escape([string]$mapping.root))(?=`$|/)"
        $tokenValue = [string]$mapping.token
        $result = [Text.RegularExpressions.Regex]::Replace(
            $result,
            $boundaryPattern,
            [Text.RegularExpressions.MatchEvaluator]{ param($match) return $tokenValue },
            $comparisonOptions)
    }
    if ($result -match '^(?:[A-Za-z]:/|/)' -or
        $result -match '(?<![A-Za-z0-9])(?:[A-Za-z]:/|/(?:Users|home|root|var|tmp|opt|usr)/)') {
        throw "EDGE-SPLIT-LEDGER-001 independent semantic projection retained an absolute path: $result."
    }
    return $result
}

function ConvertTo-IndependentRestoreValue {
    param(
        [AllowNull()]$InputValue,
        [Parameter(Mandatory = $true)][object[]]$Mappings,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Pointer
    )

    if ($null -eq $InputValue) { return $null }
    if ($InputValue -is [string]) {
        return ConvertTo-IndependentTokenText -InputText ([string]$InputValue) -Mappings $Mappings
    }
    if ($InputValue -is [bool] -or $InputValue -is [byte] -or $InputValue -is [int16] -or
        $InputValue -is [int32] -or $InputValue -is [int64] -or $InputValue -is [decimal] -or
        $InputValue -is [double] -or $InputValue -is [single]) {
        return $InputValue
    }
    if ($InputValue -is [Collections.IEnumerable] -and
        $InputValue -isnot [Management.Automation.PSCustomObject]) {
        $values = @($InputValue | ForEach-Object {
                ConvertTo-IndependentRestoreValue -InputValue $_ -Mappings $Mappings -Pointer "$Pointer/*"
            })
        $setField = $Pointer -ceq '/project/restore/configFilePaths' -or
            $Pointer -match '^/libraries/[^/]+/files$'
        if ($setField) {
            if (@($values | Where-Object { $_ -isnot [string] }).Count -ne 0) {
                throw "EDGE-SPLIT-LEDGER-001 independent declared restore set contains non-string values: $Pointer."
            }
            return @($values | Sort-Ordinal -Unique)
        }
        return $values
    }
    $projectedMembers = [Collections.Generic.List[object]]::new()
    $projectedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($member in $InputValue.PSObject.Properties) {
        $projectedName = ConvertTo-IndependentTokenText `
            -InputText ([string]$member.Name) -Mappings $Mappings
        if (-not $projectedNames.Add($projectedName)) {
            throw "EDGE-SPLIT-LEDGER-001 independent restore JSON keys collide after root tokenization: $Pointer."
        }
        $projectedMembers.Add([pscustomobject]@{
                key = $projectedName
                input = $member.Value
            })
    }
    $sortedProjectedMembers = $projectedMembers.ToArray()
    [Array]::Sort($sortedProjectedMembers, [Collections.Generic.Comparer[object]]::Create([Comparison[object]]{
                param($first, $second)
                return [StringComparer]::Ordinal.Compare([string]$first.key, [string]$second.key)
            }))
    $orderedMembers = [ordered]@{}
    foreach ($projectedMember in $sortedProjectedMembers) {
        $pointerName = ([string]$projectedMember.key).Replace('~', '~0').Replace('/', '~1')
        $orderedMembers[[string]$projectedMember.key] = ConvertTo-IndependentRestoreValue `
            -InputValue $projectedMember.input -Mappings $Mappings -Pointer "$Pointer/$pointerName"
    }
    return [pscustomobject]$orderedMembers
}

function ConvertTo-IndependentXmlInfoset {
    param(
        [Parameter(Mandatory = $true)][Xml.XmlNode]$Element,
        [Parameter(Mandatory = $true)][object[]]$Mappings
    )

    if ($Element.NodeType -ne [Xml.XmlNodeType]::Element) {
        throw "EDGE-SPLIT-LEDGER-001 independent XML projection requires an element: $($Element.NodeType)."
    }
    $attributeFacts = @($Element.Attributes | ForEach-Object {
            [pscustomobject][ordered]@{
                namespace = [string]$_.NamespaceURI
                name = [string]$_.LocalName
                value = ConvertTo-IndependentTokenText -InputText ([string]$_.Value) -Mappings $Mappings
            }
        } | Sort-Ordinal namespace, name)
    $childFacts = [Collections.Generic.List[object]]::new()
    foreach ($childNode in $Element.ChildNodes) {
        if ($childNode.NodeType -eq [Xml.XmlNodeType]::Element) {
            $childFacts.Add((ConvertTo-IndependentXmlInfoset -Element $childNode -Mappings $Mappings))
        }
        elseif ($childNode.NodeType -eq [Xml.XmlNodeType]::Text -or
            $childNode.NodeType -eq [Xml.XmlNodeType]::CDATA) {
            if (-not [string]::IsNullOrWhiteSpace([string]$childNode.Value)) {
                $childFacts.Add([pscustomobject][ordered]@{
                        kind = 'text'
                        value = ConvertTo-IndependentTokenText -InputText ([string]$childNode.Value) -Mappings $Mappings
                    })
            }
        }
        elseif ($childNode.NodeType -ne [Xml.XmlNodeType]::Whitespace -and
            $childNode.NodeType -ne [Xml.XmlNodeType]::SignificantWhitespace) {
            throw "EDGE-SPLIT-LEDGER-001 independent generated NuGet XML contains an unsupported node: $($childNode.NodeType)."
        }
    }
    return [pscustomobject][ordered]@{
        kind = 'element'
        namespace = [string]$Element.NamespaceURI
        name = [string]$Element.LocalName
        attributes = $attributeFacts
        children = $childFacts.ToArray()
    }
}

function Get-IndependentRestoreContentFact {
    param(
        [Parameter(Mandatory = $true)][string]$GeneratedFile,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PackagesRoot,
        [Parameter(Mandatory = $true)][string]$SdkRoot
    )

    $mappings = @(Get-IndependentRootMappings -RepoRoot $RepoRoot -PackagesRoot $PackagesRoot -SdkRoot $SdkRoot)
    $leafName = [IO.Path]::GetFileName($GeneratedFile)
    if ($leafName -ceq 'project.assets.json') {
        $document = Get-Content -LiteralPath $GeneratedFile -Raw | ConvertFrom-Json -Depth 100
        $projectNode = Get-IndependentJsonMember $document 'project'
        $restoreNode = Get-IndependentJsonMember $projectNode 'restore'
        $configFiles = @((Get-IndependentJsonMember $restoreNode 'configFilePaths'))
        $onlyConfig = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'NuGet.Config'))
        if ($configFiles.Count -ne 1 -or
            -not (Test-IndependentPathIdentityEqual ([string]$configFiles[0]) $onlyConfig)) {
            throw 'EDGE-SPLIT-LEDGER-001 independent restore graph was not isolated to repository NuGet.Config.'
        }
        if (-not (Test-IndependentPathIdentityEqual `
            ([string](Get-IndependentJsonMember $restoreNode 'packagesPath')) $PackagesRoot)) {
            throw 'EDGE-SPLIT-LEDGER-001 independent restore graph packagesPath differs from global packages authority.'
        }
        $sourceMap = Get-IndependentJsonMember $restoreNode 'sources'
        $declaredSources = @(Get-IndependentNuGetSources (Join-Path $RepoRoot 'NuGet.Config'))
        $restoredSources = @($sourceMap.PSObject.Properties | ForEach-Object { [string]$_.Name } | Sort-Ordinal -Unique)
        if (($declaredSources -join "`n") -cne ($restoredSources -join "`n")) {
            throw 'EDGE-SPLIT-LEDGER-001 independent restore graph source set differs from tracked NuGet.Config.'
        }
        foreach ($source in $sourceMap.PSObject.Properties) {
            $parsedSource = $null
            if ([Uri]::TryCreate([string]$source.Name, [UriKind]::Absolute, [ref]$parsedSource) -and
                -not [string]::IsNullOrWhiteSpace($parsedSource.UserInfo)) {
                throw 'EDGE-SPLIT-LEDGER-001 independent restore graph contains credential-bearing source authority.'
            }
        }
        $semanticDocument = [pscustomobject][ordered]@{
            policy = 'edge-restore-semantic-v1'
            documentKind = 'project-assets'
            content = ConvertTo-IndependentRestoreValue -InputValue $document -Mappings $mappings -Pointer ''
        }
    }
    elseif ($leafName.EndsWith('.csproj.nuget.g.props', [StringComparison]::Ordinal) -or
        $leafName.EndsWith('.csproj.nuget.g.targets', [StringComparison]::Ordinal)) {
        [xml]$xmlDocument = Get-Content -LiteralPath $GeneratedFile -Raw
        if ($null -eq $xmlDocument.DocumentElement) {
            throw "EDGE-SPLIT-LEDGER-001 independent generated NuGet XML lacks a document element: $GeneratedFile."
        }
        $semanticDocument = [pscustomobject][ordered]@{
            policy = 'edge-restore-semantic-v1'
            documentKind = if ($leafName.EndsWith('.props', [StringComparison]::Ordinal)) {
                'nuget-generated-props'
            }
            else { 'nuget-generated-targets' }
            content = ConvertTo-IndependentXmlInfoset -Element $xmlDocument.DocumentElement -Mappings $mappings
        }
    }
    else {
        throw "EDGE-SPLIT-LEDGER-001 independent projection does not support generated restore input: $GeneratedFile."
    }
    $semanticJson = ($semanticDocument | ConvertTo-Json -Depth 100 -Compress) + "`n"
    $semanticBytes = [Text.UTF8Encoding]::new($false).GetBytes($semanticJson)
    return [pscustomobject][ordered]@{
        representation = 'restore-semantic-v1'
        size = [long]$semanticBytes.Length
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($semanticBytes)).ToLowerInvariant()
    }
}

function Get-IndependentCompilerConfigContentFact {
    param(
        [Parameter(Mandatory = $true)][string]$CompilerConfigPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PackagesRoot,
        [Parameter(Mandatory = $true)][string]$SdkRoot
    )

    $rootMappings = @(Get-IndependentRootMappings `
        -RepoRoot $RepoRoot -PackagesRoot $PackagesRoot -SdkRoot $SdkRoot)
    $canonicalText = ConvertTo-IndependentTokenText `
        -InputText (Get-Content -LiteralPath $CompilerConfigPath -Raw) -Mappings $rootMappings
    if ($canonicalText.Contains("`r", [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 independent compiler-config projection retained CR: $CompilerConfigPath."
    }
    $canonicalBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        "edge-compiler-config-semantic-v1`n$canonicalText")
    return [pscustomobject][ordered]@{
        representation = 'compiler-config-semantic-v1'
        size = [long]$canonicalBytes.Length
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($canonicalBytes)).ToLowerInvariant()
    }
}

function Get-IndependentExecutedToolchainFacts {
    param(
        [Parameter(Mandatory = $true)][string[]]$BuildLogFiles,
        [Parameter(Mandatory = $true)][string]$ExactSdkDirectory,
        [Parameter(Mandatory = $true)][string[]]$RequiredCompilerAssemblies
    )

    $sdkPath = [IO.Path]::GetFullPath($ExactSdkDirectory)
    $executed = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $consider = {
        param([string]$CandidatePath)
        if ([string]::IsNullOrWhiteSpace($CandidatePath)) { return }
        $absolute = [IO.Path]::GetFullPath($CandidatePath)
        if (-not (Test-IndependentPathWithinRoot -RootPath $sdkPath -CandidatePath $absolute) -or
            -not (Test-Path -LiteralPath $absolute -PathType Leaf)) { return }
        if ($executed.ContainsKey($absolute)) {
            if ([string]$executed[$absolute] -cne $absolute) {
                throw "EDGE-SPLIT-LEDGER-001 independent executed SDK assembly paths collide under Windows semantics: $($executed[$absolute]) | $absolute."
            }
            return
        }
        $executed.Add($absolute, $absolute)
    }
    foreach ($requiredCompiler in $RequiredCompilerAssemblies) { & $consider $requiredCompiler }
    foreach ($logFile in $BuildLogFiles) {
        if (-not (Test-Path -LiteralPath $logFile -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 independent diagnostic build log is missing: $logFile."
        }
        $diagnosticText = Get-Content -LiteralPath $logFile -Raw
        foreach ($pathMatch in [Text.RegularExpressions.Regex]::Matches(
                $diagnosticText,
                '(?<path>(?:[A-Za-z]:[\\/]|/)[^\s"''*;<>]+\.dll)',
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            & $consider ([string]$pathMatch.Groups['path'].Value)
        }
    }
    if ($executed.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 independent diagnostic build found no executed SDK assemblies.'
    }
    $records = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $record = {
        param([string]$FilePath, [string]$FactRole)
        $absolute = [IO.Path]::GetFullPath($FilePath)
        if (-not $records.ContainsKey($absolute)) {
            $records.Add($absolute, [pscustomobject]@{
                    path = $absolute
                    roles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                })
        }
        [void]$records[$absolute].roles.Add($FactRole)
    }
    foreach ($executedAssembly in @($executed.Values | Sort-Ordinal)) {
        & $record $executedAssembly 'executed-toolchain-assembly'
        foreach ($sibling in @(Get-ChildItem -LiteralPath (Split-Path $executedAssembly -Parent) -File | Where-Object {
                    Test-OrdinalEqualsAny $_.Extension @('.dll', '.json')
                } | Sort-Ordinal FullName)) {
            & $record $sibling.FullName 'executed-toolchain-closure'
        }
    }
    return @($records.Values | ForEach-Object {
            [pscustomobject][ordered]@{
                path = [string]$_.path
                roles = @($_.roles | Sort-Ordinal)
            }
        } | Sort-Ordinal path)
}

function Assert-IndependentNuGetDiscoveryIsolation {
    param(
        [Parameter(Mandatory = $true)][string[]]$DeclaredEmptyDirectories,
        [Parameter(Mandatory = $true)][string[]]$DiagnosticRestoreLogs
    )

    foreach ($declaredEmptyDirectory in $DeclaredEmptyDirectories) {
        if (-not (Test-Path -LiteralPath $declaredEmptyDirectory -PathType Container)) {
            throw "EDGE-SPLIT-LEDGER-001 independent NuGet discovery directory does not exist: $declaredEmptyDirectory."
        }
        Assert-IndependentAuthorityPath -DeclaredRoot $declaredEmptyDirectory -CandidatePath $declaredEmptyDirectory
        if ($null -ne (Get-ChildItem -LiteralPath $declaredEmptyDirectory -Force | Select-Object -First 1)) {
            throw "EDGE-SPLIT-LEDGER-001 independent NuGet discovery directory is not empty: $declaredEmptyDirectory."
        }
    }
    foreach ($restoreLog in $DiagnosticRestoreLogs) {
        if (-not (Test-Path -LiteralPath $restoreLog -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 independent NuGet restore diagnostic log is missing: $restoreLog."
        }
        $restoreDiagnosticText = Get-Content -LiteralPath $restoreLog -Raw
        if ($restoreDiagnosticText -match '(?i)(?:^|[/\\])\.nuget[/\\]plugins(?:[/\\]|$)' -or
            $restoreDiagnosticText -match '(?i)CredentialProvider\.Microsoft(?:[/\\]|\.|\s)' -or
            $restoreDiagnosticText -match '(?im)(?:loading|loaded|launching|executing|invoking|using|discovered|found)[^\r\n]{0,160}(?:credential\s*provider|credentialprovider|NuGet\s+plugin)') {
            throw "EDGE-SPLIT-LEDGER-001 independent restore diagnostics disclose external NuGet plugin/credential-provider discovery: $restoreLog."
        }
    }
}

function Get-IndependentMsBuildAuthorityInventory {
    param(
        [Parameter(Mandatory = $true)][string[]]$ProjectSeeds,
        [Parameter(Mandatory = $true)][string]$BuildConfiguration,
        [Parameter(Mandatory = $true)][string]$ScratchRoot,
        [Parameter(Mandatory = $true)][string]$DotnetInstallationRoot,
        [Parameter(Mandatory = $true)][string]$GlobalPackagesFolder,
        [Parameter(Mandatory = $true)][string[]]$DeterministicBuildArguments,
        [Parameter(Mandatory = $true)][object[]]$ExecutedToolchainFacts
    )

    if ($DeterministicBuildArguments.Count -ne 7) {
        throw 'EDGE-SPLIT-LEDGER-001 independent authority collection requires the complete deterministic build vector.'
    }
    $toolchainRoot = [IO.Path]::GetFullPath($DotnetInstallationRoot)
    $packageRoot = [IO.Path]::GetFullPath($GlobalPackagesFolder)
    Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $RepositoryRoot
    Assert-IndependentAuthorityPath -DeclaredRoot $toolchainRoot -CandidatePath $toolchainRoot
    Assert-IndependentAuthorityPath -DeclaredRoot $packageRoot -CandidatePath $packageRoot
    $records = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $recordRoles = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
    $caseKeys = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pendingProjects = [Collections.Generic.Queue[string]]::new()
    $visitedProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $projectCasePaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $getIndependentCompiledOutputPaths = {
        param([object]$EvaluatedProperties, [object]$EvaluatedItems, [string]$ProjectPathValue)

        $projectDirectory = Split-Path $ProjectPathValue -Parent
        $paths = [Collections.Generic.List[string]]::new()
        $seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $addPath = {
            param([string]$PathValue)
            if ([string]::IsNullOrWhiteSpace($PathValue)) { return }
            $absolutePath = if ([IO.Path]::IsPathRooted($PathValue)) {
                [IO.Path]::GetFullPath($PathValue)
            }
            else { [IO.Path]::GetFullPath((Join-Path $projectDirectory $PathValue)) }
            if (-not (Test-IndependentPathWithinRoot -RootPath $RepositoryRoot -CandidatePath $absolutePath)) {
                throw "EDGE-SPLIT-LEDGER-001 independent evaluated compiled output escapes the repository: $ProjectPathValue|$absolutePath."
            }
            if ($seenPaths.Add($absolutePath)) { $paths.Add($absolutePath) }
        }

        & $addPath (Get-OptionalProperty $EvaluatedProperties 'TargetPath')
        & $addPath (Get-OptionalProperty $EvaluatedProperties 'TargetRefPath')
        $intermediateAssembly = Get-OptionalProperty $EvaluatedProperties 'IntermediateAssembly'
        & $addPath $intermediateAssembly
        if ([string]::IsNullOrWhiteSpace($intermediateAssembly)) {
            $intermediateOutputPath = Get-OptionalProperty $EvaluatedProperties 'IntermediateOutputPath'
            $targetFileName = Get-OptionalProperty $EvaluatedProperties 'TargetFileName'
            if (-not [string]::IsNullOrWhiteSpace($intermediateOutputPath) -and
                -not [string]::IsNullOrWhiteSpace($targetFileName)) {
                & $addPath (Join-Path $intermediateOutputPath $targetFileName)
            }
        }
        foreach ($referenceAssemblyItem in @(
                Get-OptionalProperty $EvaluatedItems 'IntermediateRefAssembly')) {
            & $addPath (Get-OptionalProperty $referenceAssemblyItem 'FullPath')
        }
        if ($paths.Count -eq 0) {
            throw "EDGE-SPLIT-LEDGER-001 independent authority collection could not resolve compiled outputs: $ProjectPathValue."
        }
        $compiledAndDebugPaths = [Collections.Generic.List[string]]::new()
        foreach ($path in $paths) {
            $compiledAndDebugPaths.Add($path)
            $pdbPath = [IO.Path]::ChangeExtension($path, '.pdb')
            if ($seenPaths.Add($pdbPath)) { $compiledAndDebugPaths.Add($pdbPath) }
        }
        return [string[]]$compiledAndDebugPaths.ToArray()
    }
    $getIndependentCompiledOutputState = {
        param([string]$OutputPath)
        if (Test-Path -LiteralPath $OutputPath) {
            if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 independent evaluated compiled output is not a regular file: $OutputPath."
            }
            Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $OutputPath
            return [pscustomobject][ordered]@{
                exists = $true
                size = [long](Get-Item -LiteralPath $OutputPath -Force).Length
                sha256 = Get-Sha256 $OutputPath
            }
        }
        return [pscustomobject][ordered]@{ exists = $false; size = [long]-1; sha256 = '' }
    }

    $queueProject = {
        param([string]$CandidateProject)
        $absoluteProject = [IO.Path]::GetFullPath($CandidateProject)
        if ($projectCasePaths.ContainsKey($absoluteProject)) {
            if ([string]$projectCasePaths[$absoluteProject] -cne $absoluteProject) {
                throw "EDGE-SPLIT-LEDGER-001 independent ProjectReference inventory has a Windows case collision: $($projectCasePaths[$absoluteProject]) | $absoluteProject."
            }
            return
        }
        $projectCasePaths.Add($absoluteProject, $absoluteProject)
        $pendingProjects.Enqueue($absoluteProject)
    }

    $recordInput = {
        param([string]$CandidateInput, [string]$InputRole)
        if ([string]::IsNullOrWhiteSpace($CandidateInput)) {
            throw "EDGE-SPLIT-LEDGER-001 independent required MSBuild authority path is empty: role=$InputRole."
        }
        if (-not (Test-Path -LiteralPath $CandidateInput -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 independent required MSBuild authority input is missing or not a regular file: role=$InputRole path=$CandidateInput."
        }
        $absoluteInput = [IO.Path]::GetFullPath($CandidateInput)
        $inputOrigin = ''
        $ledgerPath = ''
        if (Test-IndependentPathWithinRoot -RootPath $RepositoryRoot -CandidatePath $absoluteInput) {
            Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $absoluteInput
            $ledgerPath = [IO.Path]::GetRelativePath($RepositoryRoot, $absoluteInput).Replace('\', '/')
            $inputOrigin = if ($ledgerPath -match '(^|/)(?:obj|bin)(?:/|$)') {
                'generated-repository'
            }
            else { 'tracked-repository' }
        }
        elseif (Test-IndependentPathWithinRoot -RootPath $packageRoot -CandidatePath $absoluteInput) {
            Assert-IndependentAuthorityPath -DeclaredRoot $packageRoot -CandidatePath $absoluteInput
            $ledgerPath = 'nuget-cache/' + [IO.Path]::GetRelativePath($packageRoot, $absoluteInput).Replace('\', '/')
            $inputOrigin = 'nuget-cache'
        }
        elseif (Test-IndependentPathWithinRoot -RootPath $toolchainRoot -CandidatePath $absoluteInput) {
            Assert-IndependentAuthorityPath -DeclaredRoot $toolchainRoot -CandidatePath $absoluteInput
            $ledgerPath = 'dotnet-toolchain/' + [IO.Path]::GetRelativePath($toolchainRoot, $absoluteInput).Replace('\', '/')
            $inputOrigin = 'dotnet-toolchain'
        }
        else {
            throw "EDGE-SPLIT-LEDGER-001 independent MSBuild input is outside repository/package/toolchain authority: $absoluteInput."
        }

        $recordKey = "$inputOrigin|$ledgerPath"
        if ($caseKeys.ContainsKey($recordKey)) {
            if ([string]$caseKeys[$recordKey] -cne $recordKey) {
                throw "EDGE-SPLIT-LEDGER-001 independent authority inputs collide under Windows case semantics: $($caseKeys[$recordKey]) | $recordKey."
            }
        }
        else { $caseKeys.Add($recordKey, $recordKey) }

        $contentFact = if ($inputOrigin -ceq 'generated-repository' -and
            ($ledgerPath.EndsWith('/project.assets.json', [StringComparison]::Ordinal) -or
             $ledgerPath.EndsWith('.csproj.nuget.g.props', [StringComparison]::Ordinal) -or
             $ledgerPath.EndsWith('.csproj.nuget.g.targets', [StringComparison]::Ordinal))) {
            Get-IndependentRestoreContentFact -GeneratedFile $absoluteInput -RepoRoot $RepositoryRoot `
                -PackagesRoot $packageRoot -SdkRoot $toolchainRoot
        }
        elseif ($inputOrigin -ceq 'generated-repository' -and
            $ledgerPath.EndsWith('.GeneratedMSBuildEditorConfig.editorconfig', [StringComparison]::Ordinal)) {
            Get-IndependentCompilerConfigContentFact -CompilerConfigPath $absoluteInput `
                -RepoRoot $RepositoryRoot -PackagesRoot $packageRoot -SdkRoot $toolchainRoot
        }
        else {
            [pscustomobject][ordered]@{
                representation = 'raw-sha256'
                size = [long](Get-Item -LiteralPath $absoluteInput -Force).Length
                sha256 = Get-Sha256 $absoluteInput
            }
        }
        $inputRepresentation = [string]$contentFact.representation
        $inputSize = [long]$contentFact.size
        $inputHash = [string]$contentFact.sha256
        if ($records.ContainsKey($recordKey)) {
            $previous = $records[$recordKey]
            if ([string]$previous.representation -cne $inputRepresentation -or
                [long]$previous.size -ne $inputSize -or [string]$previous.sha256 -cne $inputHash) {
                throw "EDGE-SPLIT-LEDGER-001 independent MSBuild authority bytes changed during evaluation: $ledgerPath."
            }
        }
        else {
            $records.Add($recordKey, [pscustomobject][ordered]@{
                    path = $ledgerPath
                    origin = $inputOrigin
                    representation = $inputRepresentation
                    roles = @()
                    size = $inputSize
                    sha256 = $inputHash
                })
            $recordRoles.Add($recordKey, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
        }
        [void]$recordRoles[$recordKey].Add($InputRole)
    }

    foreach ($seedProject in @($ProjectSeeds | Sort-Ordinal -Unique)) { & $queueProject $seedProject }
    foreach ($configurationFile in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.Config')) {
        $configurationPath = Join-Path $RepositoryRoot $configurationFile
        if (Test-Path -LiteralPath $configurationPath -PathType Leaf) {
            & $recordInput $configurationPath 'root-configuration'
        }
    }
    & $recordInput (Join-Path $RepositoryRoot 'eng/EdgePluginContractDeterministicBuild.targets') `
        'deterministic-build-targets'
    foreach ($toolchainFact in $ExecutedToolchainFacts) {
        foreach ($toolchainRole in @($toolchainFact.roles)) {
            & $recordInput ([string]$toolchainFact.path) ([string]$toolchainRole)
        }
    }

    while ($pendingProjects.Count -gt 0) {
        $currentProject = $pendingProjects.Dequeue()
        if (-not $visitedProjects.Add($currentProject)) { continue }
        Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $currentProject
        & $recordInput $currentProject 'evaluated-project'
        $outputPathArguments = [string[]](@(
            'msbuild', $currentProject, '-nologo', '-noAutoResponse',
            "-p:Configuration=$BuildConfiguration"
        ) + $DeterministicBuildArguments + @(
            '-getProperty:TargetPath,TargetRefPath,IntermediateAssembly,IntermediateOutputPath,TargetFileName',
            '-getItem:IntermediateRefAssembly'
        ))
        $outputPathEvaluation = (Invoke-CapturedCommand dotnet $outputPathArguments) | ConvertFrom-Json -Depth 100
        $compiledOutputStatesBefore = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($compiledOutputPath in @(& $getIndependentCompiledOutputPaths `
                    $outputPathEvaluation.Properties $outputPathEvaluation.Items $currentProject)) {
            $compiledOutputStatesBefore.Add($compiledOutputPath, (& $getIndependentCompiledOutputState $compiledOutputPath))
        }

        $evaluationArguments = [string[]](@(
            'msbuild', $currentProject, '-nologo', '-noAutoResponse', '-t:ResolveReferences',
            "-p:Configuration=$BuildConfiguration"
        ) + $DeterministicBuildArguments + @(
            '-getProperty:ProjectAssetsFile,NuGetPackageRoot,NetCoreTargetingPackRoot',
            '-getItem:Compile,ProjectReference,AvaloniaResource,Content,None,Page,EmbeddedResource,AdditionalFiles,Analyzer'
        ))
        $evaluationText = Invoke-CapturedCommand dotnet $evaluationArguments
        $projectEvaluation = $evaluationText | ConvertFrom-Json -Depth 100
        foreach ($kind in @('Compile', 'AvaloniaResource', 'Content', 'None', 'Page', 'EmbeddedResource', 'AdditionalFiles', 'Analyzer')) {
            foreach ($evaluatedItem in @($projectEvaluation.Items.$kind)) {
                $itemPath = Get-OptionalProperty $evaluatedItem 'FullPath'
                & $recordInput $itemPath "item-$($kind.ToLowerInvariant())"
            }
        }
        foreach ($projectItem in @($projectEvaluation.Items.ProjectReference)) {
            $referencedProject = Get-OptionalProperty $projectItem 'FullPath'
            if ([string]::IsNullOrWhiteSpace($referencedProject)) {
                throw 'EDGE-SPLIT-LEDGER-001 independent evaluated ProjectReference lacks a required FullPath.'
            }
            Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $referencedProject
            & $queueProject $referencedProject
        }

        $configEvaluationArguments = [string[]](@(
            'msbuild', $currentProject, '-nologo', '-noAutoResponse', '-t:CoreCompile',
            "-p:Configuration=$BuildConfiguration", '-p:SkipCompilerExecution=true',
            '-p:BuildProjectReferences=false', '-p:UseSharedCompilation=false',
            '-p:TargetsTriggeredByCompilation='
        ) + $DeterministicBuildArguments + @(
            '-getProperty:GeneratedMSBuildEditorConfigFile',
            '-getItem:EditorConfigFiles,GlobalAnalyzerConfigFiles,AnalyzerConfigFiles'))
        $configEvaluationText = Invoke-CapturedCommand dotnet $configEvaluationArguments
        $configEvaluation = $configEvaluationText | ConvertFrom-Json -Depth 100
        foreach ($compiledOutputPath in @($compiledOutputStatesBefore.Keys | Sort-Ordinal)) {
            $beforeState = $compiledOutputStatesBefore[$compiledOutputPath]
            $afterState = & $getIndependentCompiledOutputState $compiledOutputPath
            if ([bool]$beforeState.exists -ne [bool]$afterState.exists -or
                [long]$beforeState.size -ne [long]$afterState.size -or
                [string]$beforeState.sha256 -cne [string]$afterState.sha256) {
                throw "EDGE-SPLIT-LEDGER-001 independent MSBuild authority collection mutated compiled output bytes: $currentProject|$compiledOutputPath."
            }
        }
        $compilerConfigSet = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($compilerConfig in @($configEvaluation.Items.EditorConfigFiles)) {
            $compilerConfigPath = Get-OptionalProperty $compilerConfig 'FullPath'
            if ([string]::IsNullOrWhiteSpace($compilerConfigPath) -or
                -not (Test-Path -LiteralPath $compilerConfigPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 independent compiler EditorConfigFiles item is missing: $currentProject|$compilerConfigPath."
            }
            $compilerConfigPath = [IO.Path]::GetFullPath($compilerConfigPath)
            if (-not (Test-OrdinalEqualsAny ([IO.Path]::GetExtension($compilerConfigPath)) @('.editorconfig', '.globalconfig'))) {
                throw "EDGE-SPLIT-LEDGER-001 independent compiler analyzer-config extension is unknown: $compilerConfigPath."
            }
            if ($compilerConfigSet.ContainsKey($compilerConfigPath)) {
                if ([string]$compilerConfigSet[$compilerConfigPath] -cne $compilerConfigPath) {
                    throw "EDGE-SPLIT-LEDGER-001 independent analyzer-config paths collide under Windows semantics: $($compilerConfigSet[$compilerConfigPath]) | $compilerConfigPath."
                }
            }
            else { $compilerConfigSet.Add($compilerConfigPath, $compilerConfigPath) }
            & $recordInput $compilerConfigPath 'item-editorconfigfiles'
        }
        foreach ($explicitConfig in @($configEvaluation.Items.AnalyzerConfigFiles)) {
            $explicitConfigPath = Get-OptionalProperty $explicitConfig 'FullPath'
            if ([string]::IsNullOrWhiteSpace($explicitConfigPath) -or
                -not (Test-Path -LiteralPath $explicitConfigPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 independent evaluated AnalyzerConfigFiles item is missing: $currentProject|$explicitConfigPath."
            }
            & $recordInput $explicitConfigPath 'item-analyzerconfigfiles'
        }
        $sdkManagedCoreTargets = Join-Path $validatorSdkDirectory 'Roslyn/Microsoft.Managed.Core.targets'
        foreach ($globalConfig in @($configEvaluation.Items.GlobalAnalyzerConfigFiles)) {
            $globalConfigPath = Get-OptionalProperty $globalConfig 'FullPath'
            $globalConfigOwner = Get-OptionalProperty $globalConfig 'DefiningProjectFullPath'
            if ([string]::IsNullOrWhiteSpace($globalConfigPath)) {
                throw "EDGE-SPLIT-LEDGER-001 independent GlobalAnalyzerConfigFiles item lacks FullPath: $currentProject."
            }
            if (-not (Test-Path -LiteralPath $globalConfigPath -PathType Leaf)) {
                if ([IO.Path]::GetFileName($globalConfigPath) -cne '.globalconfig' -or
                    -not (Test-IndependentPathIdentityEqual $globalConfigOwner $sdkManagedCoreTargets)) {
                    throw "EDGE-SPLIT-LEDGER-001 independent missing global analyzer config is not an SDK discovery candidate: $globalConfigPath|$globalConfigOwner."
                }
                continue
            }
            $globalConfigPath = [IO.Path]::GetFullPath($globalConfigPath)
            if (-not $compilerConfigSet.ContainsKey($globalConfigPath)) {
                throw "EDGE-SPLIT-LEDGER-001 independent existing global config is absent from EditorConfigFiles: $globalConfigPath."
            }
            & $recordInput $globalConfigPath 'item-globalanalyzerconfigfiles'
        }
        $generatedConfigValue = [string]$configEvaluation.Properties.GeneratedMSBuildEditorConfigFile
        if (-not [string]::IsNullOrWhiteSpace($generatedConfigValue)) {
            $generatedConfigPath = if ([IO.Path]::IsPathRooted($generatedConfigValue)) {
                [IO.Path]::GetFullPath($generatedConfigValue)
            }
            else { [IO.Path]::GetFullPath((Join-Path (Split-Path $currentProject -Parent) $generatedConfigValue)) }
            if (Test-Path -LiteralPath $generatedConfigPath -PathType Leaf) {
                if (-not $compilerConfigSet.ContainsKey($generatedConfigPath)) {
                    throw "EDGE-SPLIT-LEDGER-001 independent generated editorconfig was not passed to compiler: $generatedConfigPath."
                }
                & $recordInput $generatedConfigPath 'generated-compiler-analyzer-config'
            }
        }
        $restoreGraph = [string]$projectEvaluation.Properties.ProjectAssetsFile
        & $recordInput $restoreGraph 'restore-assets'
        $restoreDocument = Get-Content -LiteralPath $restoreGraph -Raw | ConvertFrom-Json -Depth 100
        $libraries = Get-IndependentJsonMember $restoreDocument 'libraries'
        $approvedSources = @(Get-IndependentNuGetSources (Join-Path $RepositoryRoot 'NuGet.Config'))
        foreach ($libraryMember in @($libraries.PSObject.Properties | Sort-Ordinal Name)) {
            $libraryRecord = $libraryMember.Value
            if ([string](Get-IndependentJsonMember $libraryRecord 'type') -cne 'package') { continue }
            $identity = [string]$libraryMember.Name
            $slash = $identity.LastIndexOf('/')
            if ($slash -le 0 -or $slash -ge $identity.Length - 1) {
                throw "EDGE-SPLIT-LEDGER-001 independent restore package identity is malformed: $identity."
            }
            $id = $identity.Substring(0, $slash)
            $version = $identity.Substring($slash + 1)
            $relativePackageDirectory = [string](Get-IndependentJsonMember $libraryRecord 'path')
            if ($relativePackageDirectory -cne "$($id.ToLowerInvariant())/$version") {
                throw "EDGE-SPLIT-LEDGER-001 independent restore library path is noncanonical: $identity."
            }
            $packageDirectory = [IO.Path]::GetFullPath((Join-Path $packageRoot $relativePackageDirectory))
            Assert-IndependentAuthorityPath -DeclaredRoot $packageRoot -CandidatePath $packageDirectory
            $archive = Join-Path $packageDirectory "$($id.ToLowerInvariant()).$version.nupkg"
            $sha512File = "$archive.sha512"
            $metadataFile = Join-Path $packageDirectory '.nupkg.metadata'
            foreach ($packageFile in @($archive, $sha512File, $metadataFile)) {
                if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
                    throw "EDGE-SPLIT-LEDGER-001 independent isolated restore lacks whole-package authority: $identity."
                }
            }
            if ((Get-Content -LiteralPath $sha512File -Raw).Trim() -cne
                (Get-IndependentSha512Base64 $archive)) {
                throw "EDGE-SPLIT-LEDGER-001 independent package archive SHA512 sidecar is stale: $identity."
            }
            $metadata = Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json -Depth 20
            $libraryContentHash = [string](Get-IndependentJsonMember $libraryRecord 'sha512')
            $metadataContentHash = [string](Get-IndependentJsonMember $metadata 'contentHash')
            $metadataSource = [string](Get-IndependentJsonMember $metadata 'source')
            if ($metadataContentHash -cne $libraryContentHash -or
                @($approvedSources | Where-Object { [string]$_ -ceq $metadataSource }).Count -ne 1) {
                throw "EDGE-SPLIT-LEDGER-001 independent package metadata differs from restore contentHash/source: $identity."
            }
            & $recordInput $archive 'restore-package-archive'
            & $recordInput $sha512File 'restore-package-sha512'
            & $recordInput $metadataFile 'restore-package-metadata'
        }

        $flattenedProject = Join-Path $ScratchRoot "independent-authority-$([Guid]::NewGuid().ToString('N')).xml"
        $preprocessArguments = [string[]](@(
                'msbuild', $currentProject, '-nologo', '-noAutoResponse',
                "-p:Configuration=$BuildConfiguration"
            ) + $DeterministicBuildArguments + @("-preprocess:$flattenedProject"))
        [void](Invoke-CapturedCommand dotnet $preprocessArguments)
        $flattenedText = Get-Content -LiteralPath $flattenedProject -Raw
        foreach ($importMatch in [Text.RegularExpressions.Regex]::Matches(
                $flattenedText,
                '(?m)^(?<path>(?:[A-Za-z]:[\\/]|/)[^\r\n]+)\r?\n={20,}\r?$')) {
            & $recordInput ([string]$importMatch.Groups['path'].Value) 'evaluated-import'
        }
    }

    $inventory = [Collections.Generic.List[object]]::new()
    foreach ($recordKey in @($records.Keys | Sort-Ordinal)) {
        $record = $records[$recordKey]
        $inventory.Add([pscustomobject][ordered]@{
                path = [string]$record.path
                origin = [string]$record.origin
                representation = [string]$record.representation
                roles = @($recordRoles[$recordKey] | Sort-Ordinal)
                size = [long]$record.size
                sha256 = [string]$record.sha256
            })
    }
    return $inventory.ToArray()
}

function Get-ManagedAssemblyFact {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$RecordedPath,
        [bool]$VerifiedPluginOwned
    )

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($PathValue)
    $tokenBytes = $assemblyName.GetPublicKeyToken()
    $stream = [IO.File]::OpenRead($PathValue)
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (-not $peReader.HasMetadata) { throw "not a managed assembly: $PathValue" }
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        if (-not $reader.IsAssembly) { throw "not an assembly: $PathValue" }
        $mvid = $reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString('D')
    }
    finally {
        $peReader.Dispose()
        $stream.Dispose()
    }
    return [pscustomobject][ordered]@{
        path = $RecordedPath
        assemblyName = [string]$assemblyName.Name
        assemblyVersion = [string]$assemblyName.Version
        culture = if ([string]::IsNullOrWhiteSpace($assemblyName.CultureName)) { 'neutral' } else { [string]$assemblyName.CultureName }
        publicKeyToken = if ($null -eq $tokenBytes -or $tokenBytes.Length -eq 0) { 'none' } else { [Convert]::ToHexString($tokenBytes).ToLowerInvariant() }
        mvid = $mvid
        size = (Get-Item -LiteralPath $PathValue).Length
        sha256 = Get-Sha256 $PathValue
        verifiedPluginOwned = $VerifiedPluginOwned
    }
}

function Get-PeAssemblyReferences {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$RecordedPath
    )

    $stream = [IO.File]::OpenRead($PathValue)
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (-not $peReader.HasMetadata) { throw "not a managed assembly: $PathValue" }
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        $definition = $reader.GetAssemblyDefinition()
        $sourceAssembly = $reader.GetString($definition.Name)
        $items = [Collections.Generic.List[object]]::new()
        foreach ($handle in $reader.AssemblyReferences) {
            $reference = $reader.GetAssemblyReference($handle)
            $culture = if ($reference.Culture.IsNil) { '' } else { $reader.GetString($reference.Culture) }
            [byte[]]$publicKeyOrToken = [byte[]]::new(0)
            if (-not $reference.PublicKeyOrToken.IsNil) {
                $publicKeyOrToken = $reader.GetBlobBytes($reference.PublicKeyOrToken)
            }
            $items.Add([pscustomobject][ordered]@{
                    sourcePath = $RecordedPath
                    sourceAssembly = $sourceAssembly
                    referencedAssembly = $reader.GetString($reference.Name)
                    referencedVersion = [string]$reference.Version
                    referencedCulture = if ([string]::IsNullOrWhiteSpace($culture)) { 'neutral' } else { $culture }
                    referencedPublicKeyToken = if ($publicKeyOrToken.Count -eq 0) {
                        'none'
                    }
                    else { [Convert]::ToHexString($publicKeyOrToken).ToLowerInvariant() }
                })
        }
        return @($items | Sort-Ordinal sourceAssembly, referencedAssembly, referencedVersion, referencedCulture, referencedPublicKeyToken, sourcePath)
    }
    finally {
        $peReader.Dispose()
        $stream.Dispose()
    }
}

function Get-AssemblyIdentityKey {
    param([Parameter(Mandatory = $true)]$AssemblyFact)
    return "$([string]$AssemblyFact.assemblyName), Version=$([string]$AssemblyFact.assemblyVersion), Culture=$([string]$AssemblyFact.culture), PublicKeyToken=$([string]$AssemblyFact.publicKeyToken)"
}

function Sort-Ordinal {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]$InputObject,
        [Parameter(Position = 0)][string[]]$Property = @(),
        [switch]$Unique,
        [switch]$IgnoreCase
    )

    begin { $items = [Collections.Generic.List[object]]::new() }
    process { if ($null -ne $InputObject) { $items.Add($InputObject) } }
    end {
        $stringComparer = if ($IgnoreCase) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
        $properties = [string[]]$Property
        $comparison = [Comparison[object]]{
            param($left, $right)
            if ($properties.Count -eq 0) { return $stringComparer.Compare([string]$left, [string]$right) }
            foreach ($propertyName in $properties) {
                $result = $stringComparer.Compare([string]$left.$propertyName, [string]$right.$propertyName)
                if ($result -ne 0) { return $result }
            }
            return 0
        }
        $array = [object[]]$items.ToArray()
        [Array]::Sort($array, [Collections.Generic.Comparer[object]]::Create($comparison))
        if (-not $Unique) { return $array }
        $result = [Collections.Generic.List[object]]::new()
        $previous = $null
        $hasPrevious = $false
        foreach ($item in $array) {
            if (-not $hasPrevious -or $comparison.Invoke($previous, $item) -ne 0) {
                $result.Add($item)
                $previous = $item
                $hasPrevious = $true
            }
        }
        return $result.ToArray()
    }
}

function Group-Ordinal {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]$InputObject,
        [Parameter(Mandatory = $true, Position = 0)][string[]]$Property
    )

    begin {
        $groups = [Collections.Generic.Dictionary[string, Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
        $displayNames = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    }
    process {
        if ($null -eq $InputObject) { return }
        $keyBuilder = [Text.StringBuilder]::new()
        $displayValues = [Collections.Generic.List[string]]::new()
        foreach ($propertyName in $Property) {
            $value = [string]$InputObject.$propertyName
            [void]$keyBuilder.Append($value.Length).Append(':').Append($value).Append(';')
            $displayValues.Add($value)
        }
        $key = $keyBuilder.ToString()
        if (-not $groups.ContainsKey($key)) {
            $groups.Add($key, [Collections.Generic.List[object]]::new())
            $displayNames.Add($key, ($displayValues -join ', '))
        }
        $groups[$key].Add($InputObject)
    }
    end {
        foreach ($key in @($groups.Keys | Sort-Ordinal)) {
            $groupItems = $groups[$key].ToArray()
            [pscustomobject][ordered]@{ Name = $displayNames[$key]; Count = $groupItems.Count; Group = $groupItems }
        }
    }
}

function Test-OrdinalEqualsAny {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        if ([string]::Equals($Value, $candidate, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
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
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "git show failed for $Commit`:${RepositoryPath}: $standardError"
        }
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant()
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

function Assert-TrackedAuthorityRegularBlob {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$WorktreePath
    )

    $repositoryPath = [IO.Path]::GetRelativePath($RepositoryRoot, [IO.Path]::GetFullPath($WorktreePath)).Replace('\', '/')
    if ($repositoryPath -eq '..' -or $repositoryPath.StartsWith('../', [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority escapes the repository: $WorktreePath."
    }
    $treeEntry = (& git -C $RepositoryRoot ls-tree $Commit -- $repositoryPath 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $treeEntry -notmatch '^100644 blob [0-9a-f]{40}\t' -or
        -not $treeEntry.EndsWith("`t$repositoryPath", [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority must be an exact 100644 regular blob at implementation HEAD: $repositoryPath."
    }
    if ((Get-GitBlobSha256 -Commit $Commit -RepositoryPath $repositoryPath) -cne (Get-Sha256 $WorktreePath)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority differs between implementation HEAD and worktree: $repositoryPath."
    }
}

function Copy-GitBlobToFile {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$RepositoryPath,
        [Parameter(Mandatory = $true)][string]$Destination
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
    $stream = [IO.File]::Create($Destination)
    try {
        if (-not $process.Start()) { throw 'could not start git show' }
        $process.StandardOutput.BaseStream.CopyTo($stream)
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "git show failed for $Commit`:${RepositoryPath}: $standardError"
        }
    }
    finally {
        $stream.Dispose()
        $process.Dispose()
    }
}

function Get-GitStatusPaths {
    param([Parameter(Mandatory = $true)][string]$GitRoot)

    return @(& git -C $GitRoot -c core.quotePath=false status --porcelain=v1 --untracked-files=all 2>&1 |
        ForEach-Object {
            $line = [string]$_
            if ($line.Length -lt 4) { return }
            $path = $line.Substring(3)
            if ($path.Contains(' -> ', [StringComparison]::Ordinal)) {
                $path = $path.Substring($path.IndexOf(' -> ', [StringComparison]::Ordinal) + 4)
            }
            $path.Trim('"').Replace('\', '/')
        } | Sort-Ordinal -Unique)
}

function Assert-FinalCanonicalCommitPair {
    param(
        [Parameter(Mandatory = $true)][string]$GitRoot,
        [Parameter(Mandatory = $true)][string]$ImplementationHead,
        [Parameter(Mandatory = $true)][string]$CanonicalRelativePath
    )

    & git -C $GitRoot merge-base --is-ancestor $ImplementationHead HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 recorded implementation HEAD is not an ancestor of the final candidate.'
    }
    $commitDistanceText = (& git -C $GitRoot rev-list --count "$ImplementationHead..HEAD" 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commitDistanceText -notmatch '^\d+$' -or [int]$commitDistanceText -ne 1) {
        throw 'EDGE-SPLIT-LEDGER-001 final canonical validation requires exactly one ledger-only evidence commit after the recorded implementation HEAD.'
    }
    $parents = @((& git -C $GitRoot show -s --format='%P' HEAD 2>&1 | Out-String).Trim().Split(
            ' ', [StringSplitOptions]::RemoveEmptyEntries))
    if ($LASTEXITCODE -ne 0 -or $parents.Count -ne 1 -or [string]$parents[0] -cne $ImplementationHead) {
        throw 'EDGE-SPLIT-LEDGER-001 the final evidence commit must have the recorded implementation HEAD as its only parent.'
    }
    $committedPaths = @(& git -C $GitRoot -c core.quotePath=false diff-tree --no-commit-id --name-only -r HEAD 2>&1 |
        ForEach-Object { ([string]$_).Replace('\', '/') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($committedPaths.Count -ne 1 -or [string]$committedPaths[0] -cne $CanonicalRelativePath) {
        throw 'EDGE-SPLIT-LEDGER-001 the final evidence commit must change exactly the canonical ledger and nothing else.'
    }
    $treeEntry = (& git -C $GitRoot ls-tree HEAD -- $CanonicalRelativePath 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $treeEntry -notmatch '^100644 blob [0-9a-f]{40}\t') {
        throw 'EDGE-SPLIT-LEDGER-001 the final canonical ledger must be a non-executable regular Git blob (mode 100644).'
    }
    $worktreeCanonical = [IO.Path]::GetFullPath((Join-Path $GitRoot $CanonicalRelativePath))
    Assert-NoRepositoryReparsePoint -FullPath $worktreeCanonical
    if ((Get-GitBlobSha256 -Commit 'HEAD' -RepositoryPath $CanonicalRelativePath) -cne (Get-Sha256 $worktreeCanonical)) {
        throw 'EDGE-SPLIT-LEDGER-001 final canonical ledger bytes differ between the evidence commit and worktree.'
    }
    if (@(Get-GitStatusPaths -GitRoot $GitRoot).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 final canonical validation requires a completely clean worktree.'
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if ([string]$Actual -cne [string]$Expected) {
        throw "EDGE-SPLIT-LEDGER-001 $Message expected='$Expected' actual='$Actual'."
    }
}

function Assert-RepositoryRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$Owner
    )
    if ([string]::IsNullOrWhiteSpace($PathValue) -or
        [IO.Path]::IsPathRooted($PathValue) -or
        $PathValue.Contains('\', [StringComparison]::Ordinal) -or
        $PathValue -eq '..' -or
        $PathValue.StartsWith('../', [StringComparison]::Ordinal) -or
        $PathValue.Contains('/../', [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 $Owner must use a normalized repository-relative path: '$PathValue'."
    }
}

function Assert-SafeArtifactUri {
    param([Parameter(Mandatory = $true)][string]$UriValue)

    $uri = $null
    if (-not [Uri]::TryCreate($UriValue, [UriKind]::Absolute, [ref]$uri) -or
        -not (Test-OrdinalEqualsAny $uri.Scheme @('http', 'https')) -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment) -or
        (Test-InvariantPattern $UriValue '(token|secret|password|apikey|api_key)=') ) {
        throw "EDGE-SPLIT-LEDGER-001 artifact URI is not a credential-free immutable HTTP(S) location: $UriValue"
    }
}

function Test-InvariantPattern {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][string]$Pattern
    )
    return [Text.RegularExpressions.Regex]::IsMatch(
        $Value,
        $Pattern,
        [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Assert-AuthorityInputSafe {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [string]$JsonPath = '$'
    )
    if ($Value -is [Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if (Test-InvariantPattern ([string]$property.Name) '(^|_)(secret|password|passwd|credential|api.?key|access.?token|refresh.?token)($|_)') {
                throw "EDGE-SPLIT-LEDGER-001 authority input contains a secret-bearing property at $JsonPath.$($property.Name)."
            }
            Assert-AuthorityInputSafe $property.Value "$JsonPath.$($property.Name)"
        }
        return
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($item in $Value) { Assert-AuthorityInputSafe $item "$JsonPath[$index]"; $index++ }
        return
    }
    if ($Value -is [string]) {
        $stringValue = [string]$Value
        if (Test-InvariantPattern $stringValue '(bearer\s+[A-Za-z0-9._~-]+|(?:secret|password|passwd|credential|api.?key|access.?token|refresh.?token)\s*[:=])') {
            throw "EDGE-SPLIT-LEDGER-001 authority input contains credential-like text at $JsonPath."
        }
        $uri = $null
        if ([Uri]::TryCreate($stringValue, [UriKind]::Absolute, [ref]$uri) -and
            (Test-OrdinalEqualsAny $uri.Scheme @('http', 'https'))) {
            Assert-SafeArtifactUri $stringValue
        }
    }
}

function Get-InternalProjectOwnerFamily {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectName,
        [Parameter(Mandatory = $true)][string]$ProjectPath
    )
    $normalizedPath = $ProjectPath.Replace('\', '/')
    if (-not $normalizedPath.EndsWith("$ProjectName/$ProjectName.csproj", [StringComparison]::Ordinal)) { return 'Unknown' }
    if ($ProjectName -ceq 'IIoT.Edge.Application' -and $normalizedPath -ceq 'src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj') { return 'Application' }
    if ($ProjectName -ceq 'IIoT.Edge.Domain' -and $normalizedPath -ceq 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj') { return 'Domain' }
    if ($ProjectName -ceq 'IIoT.Edge.SharedKernel' -and $normalizedPath -ceq 'src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj') { return 'SharedKernel' }
    if ($ProjectName -ceq 'IIoT.Edge.UI.Shared' -and $normalizedPath -ceq 'src/Shared/IIoT.Edge.UI.Shared/IIoT.Edge.UI.Shared.csproj') { return 'UiShared' }
    if ((Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Module.Sdk', 'IIoT.Edge.Module.Contracts')) -and $normalizedPath.StartsWith('src/Modules/', [StringComparison]::Ordinal)) { return 'SdkContract' }
    if ((Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Architecture.Analyzers', 'IIoT.Edge.Module.Analyzers')) -and
        ($normalizedPath.StartsWith('src/Analyzers/', [StringComparison]::Ordinal) -or $normalizedPath.StartsWith('src/Modules/', [StringComparison]::Ordinal))) { return 'Analyzer' }
    if ($ProjectName.StartsWith('IIoT.Edge.Infrastructure.', [StringComparison]::Ordinal) -and $normalizedPath.StartsWith('src/Infrastructure/', [StringComparison]::Ordinal)) { return 'Infrastructure' }
    if ($ProjectName.StartsWith('IIoT.Edge.Presentation.', [StringComparison]::Ordinal) -and $normalizedPath.StartsWith('src/Presentation/', [StringComparison]::Ordinal)) { return 'Presentation' }
    if (($ProjectName.StartsWith('IIoT.Edge.Host.', [StringComparison]::Ordinal) -or
         (Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Shell', 'IIoT.Edge.Launcher', 'IIoT.Edge.Installer'))) -and
        $normalizedPath.StartsWith('src/Edge/', [StringComparison]::Ordinal)) { return 'Host' }
    if ($ProjectName -ceq 'IIoT.Edge.RuntimeLayoutSync' -and $normalizedPath.StartsWith('src/Tools/', [StringComparison]::Ordinal)) { return 'Host' }
    return 'Unknown'
}

function Get-ExpectedOwnerFamily {
    param(
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$OwnerAuthorityByAssembly
    )
    if ($OwnerAuthorityByAssembly.ContainsKey($AssemblyName)) { return [string]$OwnerAuthorityByAssembly[$AssemblyName] }
    return 'Unknown'
}

function Test-ExpectedSourceForbiddenFamily {
    param([Parameter(Mandatory = $true)][string]$OwnerFamily)
    return Test-OrdinalEqualsAny $OwnerFamily @('Application', 'Domain', 'SharedKernel', 'Infrastructure', 'Presentation', 'Host')
}

function Test-ExpectedPackageForbiddenFamily {
    param([Parameter(Mandatory = $true)][string]$OwnerFamily)
    return (Test-ExpectedSourceForbiddenFamily $OwnerFamily) -or
        (Test-OrdinalEqualsAny $OwnerFamily @('SdkContract', 'UiShared', 'Analyzer', 'Unknown'))
}

function Get-IndependentPackageStaticInputs {
    param(
        [Parameter(Mandatory = $true)][string]$PluginRoot,
        [Parameter(Mandatory = $true)][string]$TargetAssemblyPath,
        [Parameter(Mandatory = $true)][string]$ManifestSourcePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$PluginOwnedAssemblyPaths
    )

    $targetDirectory = Split-Path $TargetAssemblyPath -Parent
    $facts = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $record = {
        param([string]$PackagePath, [string]$SourcePath, [string]$Category, [bool]$Required)
        if (-not $seen.Add($PackagePath)) {
            throw "EDGE-SPLIT-LEDGER-001 independent current build has Windows-colliding package static inputs: $PackagePath."
        }
        $sourceRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, [IO.Path]::GetFullPath($SourcePath)).Replace('\', '/')
        if ($sourceRelativePath -eq '..' -or $sourceRelativePath.StartsWith('../', [StringComparison]::Ordinal)) {
            throw "EDGE-SPLIT-LEDGER-001 independent package static input escapes the repository: $SourcePath."
        }
        $facts.Add([pscustomobject][ordered]@{
                packagePath = $PackagePath
                sourcePath = $sourceRelativePath
                size = (Get-Item -LiteralPath $SourcePath).Length
                sha256 = Get-Sha256 $SourcePath
                category = $Category
                required = $Required
            })
    }

    $manifestOutputPath = Join-Path $targetDirectory 'plugin.json'
    if (-not (Test-Path -LiteralPath $manifestOutputPath -PathType Leaf) -or
        (Get-Item -LiteralPath $manifestOutputPath).Length -ne (Get-Item -LiteralPath $ManifestSourcePath).Length -or
        (Get-Sha256 $manifestOutputPath) -cne (Get-Sha256 $ManifestSourcePath)) {
        throw 'EDGE-SPLIT-LEDGER-001 independently rebuilt plugin.json is absent or differs from source bytes.'
    }
    & $record 'plugin.json' $ManifestSourcePath 'plugin-manifest' $true

    $resourceExtensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @('.axaml', '.json', '.png', '.jpg', '.jpeg', '.svg', '.webp', '.ico', '.ttf', '.otf', '.resx', '.resources')) {
        [void]$resourceExtensions.Add($extension)
    }
    foreach ($topLevelDirectory in @('Config', 'Resources')) {
        $sourceRoot = Join-Path $PluginRoot $topLevelDirectory
        $outputRoot = Join-Path $targetDirectory $topLevelDirectory
        $expectedPackagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { continue }
        foreach ($sourceFile in @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Ordinal FullName)) {
            $packagePath = [IO.Path]::GetRelativePath($PluginRoot, $sourceFile.FullName).Replace('\', '/')
            $safe = -not (Test-InvariantPattern $packagePath '(?:^|/)(?:tests?|testing|testkit|visualtestdata)(?:/|$)') -and
                -not (Test-InvariantPattern $packagePath '(secret|password|token|credential|connectionstring|edge\.db|queue|logs?|recipes?|excel)')
            $category = if ($topLevelDirectory -ceq 'Config' -and
                $packagePath.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -and $safe) {
                'plugin-config'
            }
            elseif ($topLevelDirectory -ceq 'Resources' -and
                $resourceExtensions.Contains([IO.Path]::GetExtension($packagePath)) -and $safe) {
                'plugin-resource'
            }
            else { '' }
            if ([string]::IsNullOrWhiteSpace($category)) { continue }
            if (-not $expectedPackagePaths.Add($packagePath)) {
                throw "EDGE-SPLIT-LEDGER-001 independent package source allowlist contains Windows-colliding paths: $packagePath."
            }
            $outputPath = [IO.Path]::GetFullPath((Join-Path $targetDirectory $packagePath))
            if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or
                $sourceFile.Length -ne (Get-Item -LiteralPath $outputPath).Length -or
                (Get-Sha256 $sourceFile.FullName) -cne (Get-Sha256 $outputPath)) {
                throw "EDGE-SPLIT-LEDGER-001 independent package source is absent or changed in build output: $packagePath."
            }
            & $record $packagePath $sourceFile.FullName $category $true
        }
        if (Test-Path -LiteralPath $outputRoot -PathType Container) {
            foreach ($outputFile in @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Ordinal FullName)) {
                $packagePath = [IO.Path]::GetRelativePath($targetDirectory, $outputFile.FullName).Replace('\', '/')
                $isAllowedOutput = ($topLevelDirectory -ceq 'Config' -and
                        $packagePath.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) -or
                    ($topLevelDirectory -ceq 'Resources' -and
                        $resourceExtensions.Contains([IO.Path]::GetExtension($packagePath)))
                if ($isAllowedOutput -and -not $expectedPackagePaths.Contains($packagePath)) {
                    throw "EDGE-SPLIT-LEDGER-001 independent build output has no exact source allowlist owner: $packagePath."
                }
            }
        }
    }
    foreach ($assemblyPath in @($PluginOwnedAssemblyPaths | Sort-Ordinal -Unique)) {
        $pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
        if (Test-Path -LiteralPath $pdbPath -PathType Leaf) {
            & $record ([IO.Path]::GetFileName($pdbPath)) $pdbPath 'plugin-symbols' $false
        }
    }
    return @($facts.ToArray() | Sort-Ordinal packagePath)
}

function Get-BatchRank {
    param([Parameter(Mandatory = $true)][string]$BatchId)
    return [int]$BatchId.Substring($BatchId.Length - 3)
}

function Get-CarryKey {
    param([Parameter(Mandatory = $true)]$Item)
    return "$([string]$Item.sourcePath)|$([string]$Item.ownerAssembly)|$([string]$Item.symbol)"
}

function Assert-IndependentPhaseLayerGate {
    param(
        [Parameter(Mandatory = $true)][string]$BatchId,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$ProjectForbidden,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$PeForbidden,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$RoslynForbidden,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Carry020Baseline,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Carry030Baseline
    )

    $rank = Get-BatchRank $BatchId
    if ($rank -lt 10) { return }
    $allowedReferenceFamilies = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedRoslynCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    if ($rank -lt 20) {
        foreach ($family in @('Application', 'Domain', 'Presentation')) { [void]$allowedReferenceFamilies.Add($family) }
        foreach ($item in @($Carry020Baseline + $Carry030Baseline)) {
            $key = Get-CarryKey $item
            if (-not $expectedRoslynCounts.TryAdd($key, [int]$item.count)) {
                throw "EDGE-SPLIT-LEDGER-001 duplicate Phase 1 frozen carry identity: $key."
            }
        }
    }
    elseif ($rank -lt 30) {
        [void]$allowedReferenceFamilies.Add('Presentation')
        foreach ($item in $Carry030Baseline) {
            $key = Get-CarryKey $item
            if (-not $expectedRoslynCounts.TryAdd($key, [int]$item.count)) {
                throw "EDGE-SPLIT-LEDGER-001 duplicate Phase 2 frozen carry identity: $key."
            }
        }
    }
    $unexpectedProject = @($ProjectForbidden | Where-Object {
            -not $allowedReferenceFamilies.Contains([string]$_.ownerFamily)
        })
    $unexpectedPe = @($PeForbidden | Where-Object {
            -not $allowedReferenceFamilies.Contains([string]$_.ownerFamily)
        })
    $actualRoslynCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($usage in $RoslynForbidden) {
        $key = Get-CarryKey $usage
        if ($actualRoslynCounts.ContainsKey($key)) { $actualRoslynCounts[$key]++ }
        else { $actualRoslynCounts.Add($key, 1) }
    }
    $roslynMismatch = $actualRoslynCounts.Count -ne $expectedRoslynCounts.Count
    if (-not $roslynMismatch) {
        foreach ($key in $expectedRoslynCounts.Keys) {
            if (-not $actualRoslynCounts.ContainsKey($key) -or
                $actualRoslynCounts[$key] -ne $expectedRoslynCounts[$key]) {
                $roslynMismatch = $true
                break
            }
        }
    }
    if ($unexpectedProject.Count -ne 0 -or $unexpectedPe.Count -ne 0 -or $roslynMismatch) {
        throw "EDGE-SPLIT-LEDGER-001 exact phase layer gate failed for ${BatchId}: project=$($unexpectedProject.Count) pe=$($unexpectedPe.Count) roslynExact=$(-not $roslynMismatch)."
    }
}

function Get-IndependentDisposition {
    param(
        [Parameter(Mandatory = $true)]$Usage,
        [Parameter(Mandatory = $true)][int]$BatchRank,
        [Parameter(Mandatory = $true)][Collections.Generic.HashSet[string]]$Carry020Keys,
        [Parameter(Mandatory = $true)][Collections.Generic.HashSet[string]]$Carry030Keys,
        [Parameter(Mandatory = $true)][string]$OwnerFamily
    )
    $ownerAssembly = [string]$Usage.ownerAssembly
    $carryKey = Get-CarryKey $Usage
    if ($BatchRank -gt 0 -and $Carry020Keys.Contains($carryKey)) {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'bounded-carry-set'; disposition = 'replace-with-domain-neutral-dto-or-port'
            removalBatch = 'EDGE-SPLIT-020'; replacementContract = 'IIoT.Edge.Module.Contracts hardware/dev-sample DTO and contributor port'
            protectionTest = 'Homogenization hardware/dev-sample canonical snapshot and transactional behavior tests'; forbiddenForSourceLayer = $true
        }
    }
    if ($BatchRank -gt 0 -and $Carry030Keys.Contains($carryKey)) {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'bounded-carry-set'; disposition = 'replace-with-stable-ui-contract'
            removalBatch = 'EDGE-SPLIT-030'; replacementContract = 'IIoT.Edge.UI.Shared stable View/ViewModel/resource/navigation contract'
            protectionTest = 'EDGE-SPLIT-030 real View runtime gate'; forbiddenForSourceLayer = $true
        }
    }
    if ($BatchRank -eq 0 -and
        (($ownerAssembly -ceq 'IIoT.Edge.Application' -and
            (([string]$Usage.containingNamespace).StartsWith('IIoT.Edge.Application.Modules.Samples', [StringComparison]::Ordinal) -or
             ([string]$Usage.symbol).Contains('DevelopmentSample', [StringComparison]::Ordinal))) -or
         ($ownerAssembly -ceq 'IIoT.Edge.Domain' -and
            ([string]$Usage.containingNamespace).StartsWith('IIoT.Edge.Domain.Hardware', [StringComparison]::Ordinal)))) {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'bounded-carry-set'; disposition = 'replace-with-domain-neutral-dto-or-port'
            removalBatch = 'EDGE-SPLIT-020'; replacementContract = 'IIoT.Edge.Module.Contracts hardware/dev-sample DTO and contributor port'
            protectionTest = 'Homogenization hardware/dev-sample canonical snapshot and transactional behavior tests'; forbiddenForSourceLayer = $true
        }
    }
    if ($BatchRank -eq 0 -and $OwnerFamily -ceq 'Presentation') {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'bounded-carry-set'; disposition = 'replace-with-stable-ui-contract'
            removalBatch = 'EDGE-SPLIT-030'; replacementContract = 'IIoT.Edge.UI.Shared stable View/ViewModel/resource/navigation contract'
            protectionTest = 'EDGE-SPLIT-030 real View runtime gate'; forbiddenForSourceLayer = $true
        }
    }
    $phase1Facts = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $phase1Facts.Add('Application', [pscustomobject][ordered]@{
            classification = 'phase-1-contract-extraction'; disposition = 'replace-with-contract-or-host-port'
            replacementContract = 'IIoT.Edge.Module.Contracts narrow host port or domain-neutral DTO'
            protectionTest = 'Edge project graph + Homogenization Workflow/Conformance required runners'
        })
    $phase1Facts.Add('Domain', [pscustomobject][ordered]@{
            classification = 'phase-1-contract-extraction'; disposition = 'remove-domain-aggregate-reference'
            replacementContract = 'No Domain aggregate exposure; use a purpose-specific contract DTO/port'
            protectionTest = 'Edge project graph + Domain/Application required runners'
        })
    $phase1Facts.Add('SharedKernel', [pscustomobject][ordered]@{
            classification = 'phase-1-contract-extraction'; disposition = 'replace-with-approved-contract-primitive'
            replacementContract = 'IIoT.Edge.Module.Contracts domain-neutral primitive or stable enum'
            protectionTest = 'Edge public API analyzer + Homogenization required runners'
        })
    if ($phase1Facts.ContainsKey($OwnerFamily)) {
        $fact = $phase1Facts[$OwnerFamily]
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = [string]$fact.classification; disposition = [string]$fact.disposition
            removalBatch = 'EDGE-SPLIT-010'; replacementContract = [string]$fact.replacementContract
            protectionTest = [string]$fact.protectionTest; forbiddenForSourceLayer = $true
        }
    }
    if ($OwnerFamily -ceq 'SdkContract') {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'approved-sdk-contract-consumer'; disposition = 'retain-formal-sdk-contract-usage'
            removalBatch = $null; replacementContract = 'IIoT.Edge.Module.Contracts or IIoT.Edge.Module.Sdk formal package surface'
            protectionTest = 'Edge public API analyzer + SDK contract tests'; forbiddenForSourceLayer = $false
        }
    }
    if ($OwnerFamily -ceq 'UiShared') {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'approved-sdk-ui-contract'; disposition = 'retain-stable-ui-contract'
            removalBatch = $null; replacementContract = 'IIoT.Edge.UI.Shared approved SDK UI surface'
            protectionTest = 'SDK public API gate + EDGE-SPLIT-030 real View runtime gate'; forbiddenForSourceLayer = $false
        }
    }
    if ($OwnerFamily -ceq 'PluginOwned') {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'plugin-owned-contract'; disposition = 'retain-plugin-owned-dependency'
            removalBatch = $null; replacementContract = 'Plugin-owned assembly declared by the plugin build/package graph'
            protectionTest = 'Plugin package ownership and PE closure gate'; forbiddenForSourceLayer = $false
        }
    }
    if ($OwnerFamily -ceq 'PlatformOrThirdParty') {
        return [pscustomobject][ordered]@{
            ownerFamily = $OwnerFamily; classification = 'approved-platform-or-third-party'; disposition = 'retain-approved-external-contract'
            removalBatch = $null; replacementContract = "Approved external contract owned by $ownerAssembly"
            protectionTest = 'Release compilation + package dependency ownership gate'; forbiddenForSourceLayer = $false
        }
    }
    return [pscustomobject][ordered]@{
        ownerFamily = 'Unknown'; classification = 'unclassified'; disposition = 'unclassified'; removalBatch = $null
        replacementContract = 'No approved owner mapping'; protectionTest = 'EDGE-SPLIT-LEDGER-001 classification completeness gate'; forbiddenForSourceLayer = $true
    }
}

function ConvertTo-CountMap {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items)

    $map = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Items) {
        $key = [string]$item.ownerFamily
        if (-not $map.ContainsKey($key)) { $map[$key] = 0 }
        $map[$key] += 1
    }
    return $map
}

function Assert-CountMap {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Recorded,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expected = ConvertTo-CountMap $Items
    $actual = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Recorded) {
        $family = [string]$entry.ownerFamily
        if ($actual.ContainsKey($family) -or [int]$entry.count -le 0) {
            throw "EDGE-SPLIT-LEDGER-001 $Owner contains a duplicate or invalid owner-family count."
        }
        $actual[$family] = [int]$entry.count
    }
    if ($expected.Count -ne $actual.Count) {
        throw "EDGE-SPLIT-LEDGER-001 $Owner owner-family count set is stale."
    }
    foreach ($family in $expected.Keys) {
        if (-not $actual.ContainsKey($family) -or $actual[$family] -ne $expected[$family]) {
            throw "EDGE-SPLIT-LEDGER-001 $Owner owner-family count is stale for '$family'."
        }
    }
}

function Assert-PropertyCountMap {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Recorded,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expected = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Items) {
        $key = [string]$item.$PropertyName
        if (-not $expected.ContainsKey($key)) { $expected[$key] = 0 }
        $expected[$key] += 1
    }
    $actual = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Recorded) {
        $key = [string]$entry.$PropertyName
        if ([string]::IsNullOrWhiteSpace($key) -or $actual.ContainsKey($key) -or [int]$entry.count -le 0) {
            throw "EDGE-SPLIT-LEDGER-001 $Owner contains a duplicate or invalid '$PropertyName' count."
        }
        $actual[$key] = [int]$entry.count
    }
    if ($expected.Count -ne $actual.Count) { throw "EDGE-SPLIT-LEDGER-001 $Owner count set is stale." }
    foreach ($key in $expected.Keys) {
        if (-not $actual.ContainsKey($key) -or $actual[$key] -ne $expected[$key]) {
            throw "EDGE-SPLIT-LEDGER-001 $Owner count is stale for '$key'."
        }
    }
}

function Assert-CarryItemsEqual {
    param(
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][object[]]$Actual,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expectedMap = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Expected) {
        $key = Get-CarryKey $item
        if ($expectedMap.ContainsKey($key)) { throw "EDGE-SPLIT-LEDGER-001 $Owner baseline contains duplicate carry keys." }
        $expectedMap[$key] = [int]$item.count
    }
    $actualMap = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Actual) {
        $key = Get-CarryKey $item
        if ($actualMap.ContainsKey($key)) { throw "EDGE-SPLIT-LEDGER-001 $Owner current set contains duplicate carry keys." }
        $actualMap[$key] = [int]$item.count
    }
    if ($expectedMap.Count -ne $actualMap.Count) { throw "EDGE-SPLIT-LEDGER-001 $Owner item set is not exact." }
    foreach ($key in $expectedMap.Keys) {
        if (-not $actualMap.ContainsKey($key) -or $actualMap[$key] -ne $expectedMap[$key]) {
            throw "EDGE-SPLIT-LEDGER-001 $Owner item set/count drifted at '$key'."
        }
    }
}

function Assert-JsonEqual {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (($Actual | ConvertTo-Json -Depth 100 -Compress) -cne
        ($Expected | ConvertTo-Json -Depth 100 -Compress)) {
        throw "EDGE-SPLIT-LEDGER-001 $Message"
    }
}

function Assert-IndependentAuthorityInventoriesEqual {
    param(
        [Parameter(Mandatory = $true)][object[]]$Recorded,
        [Parameter(Mandatory = $true)][object[]]$Recomputed
    )

    $newMap = {
        param([object[]]$Facts, [string]$Owner)
        $map = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($fact in $Facts) {
            $origin = [string]$fact.origin
            $path = [string]$fact.path
            if ($origin -notmatch '^[a-z-]+$' -or [string]::IsNullOrWhiteSpace($path) -or
                [IO.Path]::IsPathRooted($path) -or $path.Contains('\', [StringComparison]::Ordinal) -or
                $path.Contains(':', [StringComparison]::Ordinal) -or
                $path -match '(^|/)\.\.(?:/|$)') {
                throw "EDGE-SPLIT-LEDGER-001 $Owner authority comparison contains a non-tokenized diagnostic key."
            }
            $key = "$origin|$path"
            if (-not $map.TryAdd($key, $fact)) {
                throw "EDGE-SPLIT-LEDGER-001 $Owner authority comparison contains a duplicate key: $key."
            }
        }
        return $map
    }

    $recordedMap = & $newMap $Recorded 'recorded'
    $recomputedMap = & $newMap $Recomputed 'recomputed'
    $recordedOnly = @($recordedMap.Keys | Where-Object { -not $recomputedMap.ContainsKey($_) } | Sort-Ordinal)
    $recomputedOnly = @($recomputedMap.Keys | Where-Object { -not $recordedMap.ContainsKey($_) } | Sort-Ordinal)
    if ($Recorded.Count -ne $Recomputed.Count -or $recordedOnly.Count -ne 0 -or $recomputedOnly.Count -ne 0) {
        $firstRecordedOnly = if ($recordedOnly.Count -eq 0) { '<none>' } else { [string]$recordedOnly[0] }
        $firstRecomputedOnly = if ($recomputedOnly.Count -eq 0) { '<none>' } else { [string]$recomputedOnly[0] }
        throw "EDGE-SPLIT-LEDGER-001 authority inventory key mismatch: recordedCount=$($Recorded.Count) recomputedCount=$($Recomputed.Count) firstRecordedOnly=$firstRecordedOnly firstRecomputedOnly=$firstRecomputedOnly."
    }
    foreach ($key in @($recordedMap.Keys | Sort-Ordinal)) {
        $recordedFact = $recordedMap[$key]
        $recomputedFact = $recomputedMap[$key]
        $mismatchField = if ([string]$recordedFact.representation -cne [string]$recomputedFact.representation) {
            'representation'
        }
        elseif ((@($recordedFact.roles) -join "`n") -cne (@($recomputedFact.roles) -join "`n")) {
            'roles'
        }
        elseif ([long]$recordedFact.size -ne [long]$recomputedFact.size) {
            'size'
        }
        elseif ([string]$recordedFact.sha256 -cne [string]$recomputedFact.sha256) {
            'sha256'
        }
        else { '' }
        if (-not [string]::IsNullOrEmpty($mismatchField)) {
            throw "EDGE-SPLIT-LEDGER-001 authority inventory fact mismatch: key=$key field=$mismatchField."
        }
    }
}

function Assert-SortedUniqueStrings {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Values,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expected = @($Values | Sort-Ordinal -Unique)
    if (($Values -join "`n") -cne ($expected -join "`n")) {
        throw "EDGE-SPLIT-LEDGER-001 $Owner must be deterministically sorted and unique."
    }
}

function Invoke-RawPackageAudit {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePackagePath,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$VerifiedAssemblyFacts,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$DeclaredPackageNames,
        [Parameter(Mandatory = $true)][Collections.Generic.HashSet[string]]$VerifiedPluginOwnedNames,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$StaticInputs,
        [switch]$RejectForbidden
    )

    $limits = [pscustomobject][ordered]@{
        maxEntryCount = 256
        maxCompressedPackageBytes = 134217728
        maxEntryUncompressedBytes = 67108864
        maxTotalUncompressedBytes = 268435456
    }
    if ((Get-Item -LiteralPath $CandidatePackagePath).Length -gt [long]$limits.maxCompressedPackageBytes) {
        throw 'EDGE-SPLIT-LEDGER-001 candidate package exceeds the compressed-file byte limit.'
    }

    $verifiedFactsByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($fact in $VerifiedAssemblyFacts) {
        if (-not $verifiedFactsByName.TryAdd([string]$fact.assemblyName, $fact)) {
            throw "EDGE-SPLIT-LEDGER-001 verified package inputs contain a duplicate assembly identity: $($fact.assemblyName)."
        }
    }
    $declaredPackageNameSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $packageOwnerAuthority = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($verifiedName in $VerifiedPluginOwnedNames) { $packageOwnerAuthority.Add($verifiedName, 'PluginOwned') }
    foreach ($name in $DeclaredPackageNames) {
        if (-not $verifiedFactsByName.ContainsKey([string]$name) -or
            -not $declaredPackageNameSet.Add([string]$name)) {
            throw "EDGE-SPLIT-LEDGER-001 package ownership declaration lacks unique verified bytes: $name."
        }
    }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 30
    $entryAssemblyName = [IO.Path]::GetFileNameWithoutExtension([string]$manifest.entryAssembly)
    if (-not $declaredPackageNameSet.Contains($entryAssemblyName) -or
        -not $VerifiedPluginOwnedNames.Contains($entryAssemblyName)) {
        throw 'EDGE-SPLIT-LEDGER-001 package ownership declarations must include the manifest entry assembly backed by verified bytes.'
    }

    $entries = [Collections.Generic.List[object]]::new()
    $assemblies = [Collections.Generic.List[object]]::new()
    $entryPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $assemblyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $assemblyIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $staticInputByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($staticInput in $StaticInputs) {
        if (-not $staticInputByPath.TryAdd([string]$staticInput.packagePath, $staticInput)) {
            throw "EDGE-SPLIT-LEDGER-001 independent package static input paths are not ordinal-unique: $($staticInput.packagePath)."
        }
    }
    $packageTempRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-ledger-package-$([Guid]::NewGuid().ToString('N'))"
    [void](New-Item -ItemType Directory -Path $packageTempRoot -Force)
    $archive = [IO.Compression.ZipFile]::OpenRead($CandidatePackagePath)
    try {
        if ($archive.Entries.Count -gt [int]$limits.maxEntryCount) {
            throw 'EDGE-SPLIT-LEDGER-001 candidate package exceeds the ZIP entry-count limit.'
        }
        [long]$totalUncompressedBytes = 0
        foreach ($entry in @($archive.Entries | Sort-Ordinal FullName)) {
            if ([long]$entry.Length -gt [long]$limits.maxEntryUncompressedBytes) {
                throw "EDGE-SPLIT-LEDGER-001 candidate package entry exceeds the uncompressed byte limit: $($entry.FullName)."
            }
            $totalUncompressedBytes += [long]$entry.Length
            if ($totalUncompressedBytes -gt [long]$limits.maxTotalUncompressedBytes) {
                throw 'EDGE-SPLIT-LEDGER-001 candidate package exceeds the total uncompressed byte limit.'
            }
            $externalAttributes = [uint32]$entry.ExternalAttributes
            if (((($externalAttributes -shr 16) -band 0xF000) -eq 0xA000) -or
                ($externalAttributes -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "EDGE-SPLIT-LEDGER-001 candidate package contains a symlink/reparse entry: $($entry.FullName)."
            }
            if ([string]::IsNullOrWhiteSpace($entry.Name)) { continue }
            $entryPath = $entry.FullName.Replace('\', '/')
            if ($entryPath.StartsWith('/', [StringComparison]::Ordinal) -or
                (Test-InvariantPattern $entryPath '^[A-Za-z]:/') -or
                $entryPath.Contains('../', [StringComparison]::Ordinal) -or
                $entryPath.Contains('/..', [StringComparison]::Ordinal) -or
                -not $entryPaths.Add($entryPath)) {
                throw "EDGE-SPLIT-LEDGER-001 candidate package contains unsafe or Windows-colliding paths: $entryPath."
            }
            $entryStream = $entry.Open()
            try {
                $hash = [Security.Cryptography.SHA256]::Create()
                try { $entrySha256 = [Convert]::ToHexString($hash.ComputeHash($entryStream)).ToLowerInvariant() }
                finally { $hash.Dispose() }
            }
            finally { $entryStream.Dispose() }

            $category = 'unclassified'
            $owner = 'unknown'
            $allowed = $false
            if ($staticInputByPath.ContainsKey($entryPath)) {
                $staticInput = $staticInputByPath[$entryPath]
                $category = [string]$staticInput.category
                $owner = 'plugin'
                $allowed = $entrySha256 -ceq [string]$staticInput.sha256 -and
                    [long]$entry.Length -eq [long]$staticInput.size
            }
            elseif ($entryPath.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                $tempAssemblyPath = Join-Path $packageTempRoot "$([Guid]::NewGuid().ToString('N')).dll"
                $input = $entry.Open()
                $output = [IO.File]::Create($tempAssemblyPath)
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
                try {
                    $assemblyFact = Get-ManagedAssemblyFact -PathValue $tempAssemblyPath -RecordedPath $entryPath -VerifiedPluginOwned $false
                }
                catch {
                    throw "EDGE-SPLIT-LEDGER-001 candidate packaged DLL has no valid managed identity: $entryPath."
                }
                $identityKey = Get-AssemblyIdentityKey $assemblyFact
                if (-not $assemblyNames.Add([string]$assemblyFact.assemblyName) -or
                    -not $assemblyIdentities.Add($identityKey)) {
                    throw "EDGE-SPLIT-LEDGER-001 candidate package contains duplicate assembly name/identity: $identityKey."
                }
                $verifiedFact = if ($verifiedFactsByName.ContainsKey([string]$assemblyFact.assemblyName)) {
                    $verifiedFactsByName[[string]$assemblyFact.assemblyName]
                }
                else { $null }
                $byteMatch = $null -ne $verifiedFact -and
                    (Get-AssemblyIdentityKey $assemblyFact) -ceq (Get-AssemblyIdentityKey $verifiedFact) -and
                    [string]$assemblyFact.mvid -ceq [string]$verifiedFact.mvid -and
                    [long]$assemblyFact.size -eq [long]$verifiedFact.size -and
                    [string]$assemblyFact.sha256 -ceq [string]$verifiedFact.sha256
                $declared = $byteMatch -and $declaredPackageNameSet.Contains([string]$assemblyFact.assemblyName)
                $ownerFamily = Get-ExpectedOwnerFamily ([string]$assemblyFact.assemblyName) $packageOwnerAuthority
                if (-not $declared -and $ownerFamily -ceq 'PluginOwned') { $ownerFamily = 'Unknown' }
                $forbiddenAssembly = -not $declared -or (Test-ExpectedPackageForbiddenFamily $ownerFamily)
                $assemblies.Add([pscustomobject][ordered]@{
                        path = $entryPath
                        assemblyName = [string]$assemblyFact.assemblyName
                        assemblyVersion = [string]$assemblyFact.assemblyVersion
                        culture = [string]$assemblyFact.culture
                        publicKeyToken = [string]$assemblyFact.publicKeyToken
                        mvid = [string]$assemblyFact.mvid
                        size = [long]$assemblyFact.size
                        sha256 = [string]$assemblyFact.sha256
                        ownerFamily = $ownerFamily
                        declaredPluginOwned = $declared
                        byteMatchVerifiedInput = $byteMatch
                        forbiddenForPackageLayer = $forbiddenAssembly
                    })
                $category = if ($declared) { 'plugin-owned-assembly' } else { 'forbidden-assembly' }
                $owner = if ($declared) { 'plugin' } else { $ownerFamily }
                $allowed = -not $forbiddenAssembly
            }
            $entries.Add([pscustomobject][ordered]@{
                    path = $entryPath
                    size = [long]$entry.Length
                    sha256 = $entrySha256
                    category = $category
                    owner = $owner
                    allowed = $allowed
                })
        }
    }
    finally {
        $archive.Dispose()
        if (Test-Path -LiteralPath $packageTempRoot -PathType Container) {
            Remove-Item -LiteralPath $packageTempRoot -Recurse -Force
        }
    }
    foreach ($requiredStaticInput in @($StaticInputs | Where-Object required)) {
        if (-not $entryPaths.Contains([string]$requiredStaticInput.packagePath)) {
            throw "EDGE-SPLIT-LEDGER-001 candidate package omits independently required source/build bytes: $($requiredStaticInput.packagePath)."
        }
    }
    if ($RejectForbidden -and
        (@($entries | Where-Object { -not [bool]$_.allowed }).Count -ne 0 -or
         @($entries | Where-Object category -eq 'unclassified').Count -ne 0 -or
         @($assemblies | Where-Object forbiddenForPackageLayer).Count -ne 0)) {
        throw 'EDGE-SPLIT-LEDGER-001 candidate package contains forbidden or unclassified raw content.'
    }
    return [pscustomobject][ordered]@{
        limits = $limits
        entries = $entries.ToArray()
        assemblies = $assemblies.ToArray()
    }
}

if (-not [string]::IsNullOrWhiteSpace($PhaseGateFixturePath)) {
    $resolvedPhaseFixturePath = Resolve-RepositoryPath $PhaseGateFixturePath
    if (-not (Test-Path -LiteralPath $resolvedPhaseFixturePath -PathType Leaf)) {
        throw 'EDGE-SPLIT-LEDGER-001 phase gate fixture does not exist.'
    }
    $phaseFixture = Get-Content -LiteralPath $resolvedPhaseFixturePath -Raw | ConvertFrom-Json -Depth 30
    Assert-IndependentPhaseLayerGate `
        -BatchId ([string]$phaseFixture.batchId) `
        -ProjectForbidden @($phaseFixture.projectForbidden) `
        -PeForbidden @($phaseFixture.peForbidden) `
        -RoslynForbidden @($phaseFixture.roslynForbidden) `
        -Carry020Baseline @($phaseFixture.carry020Baseline) `
        -Carry030Baseline @($phaseFixture.carry030Baseline)
    Write-Host "Edge exact phase layer fixture passed: batch=$($phaseFixture.batchId)."
    return
}

$hasPackageFixture = -not [string]::IsNullOrWhiteSpace($PackageFixturePath) -or
    -not [string]::IsNullOrWhiteSpace($PackageFixtureManifestPath) -or
    $PackageFixtureOwnedAssemblyPath.Count -ne 0 -or
    $PackageFixtureDeclaredOwnedAssembly.Count -ne 0
if ($hasPackageFixture) {
    if ([string]::IsNullOrWhiteSpace($PackageFixturePath) -or
        [string]::IsNullOrWhiteSpace($PackageFixtureManifestPath) -or
        $PackageFixtureOwnedAssemblyPath.Count -eq 0 -or
        $PackageFixtureDeclaredOwnedAssembly.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 raw package fixture requires package, manifest, verified assembly bytes, and explicit ownership declarations.'
    }
    $fixturePackagePath = Resolve-RepositoryPath $PackageFixturePath
    $fixtureManifestPath = Resolve-RepositoryPath $PackageFixtureManifestPath
    $fixtureOwnedPaths = @($PackageFixtureOwnedAssemblyPath | ForEach-Object { Resolve-RepositoryPath $_ } | Sort-Ordinal -Unique)
    foreach ($path in @($fixturePackagePath, $fixtureManifestPath) + $fixtureOwnedPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 raw package fixture input does not exist: $path"
        }
    }
    $fixtureFacts = [Collections.Generic.List[object]]::new()
    $fixtureVerifiedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $fixtureOwnedPaths) {
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
        $fact = Get-ManagedAssemblyFact -PathValue $path -RecordedPath $relativePath -VerifiedPluginOwned $true
        if (-not ([string]$fact.assemblyName).StartsWith('IIoT.Edge.Module.', [StringComparison]::Ordinal) -or
            (Test-OrdinalEqualsAny ([string]$fact.assemblyName) @('IIoT.Edge.Module.Sdk', 'IIoT.Edge.Module.Contracts', 'IIoT.Edge.Module.Analyzers')) -or
            -not $fixtureVerifiedNames.Add([string]$fact.assemblyName)) {
            throw "EDGE-SPLIT-LEDGER-001 raw package fixture lacks a unique non-reserved plugin-owned identity: $($fact.assemblyName)."
        }
        $fixtureFacts.Add($fact)
    }
    $fixtureManifest = Get-Content -LiteralPath $fixtureManifestPath -Raw | ConvertFrom-Json -Depth 30
    $fixtureEntryName = [IO.Path]::GetFileNameWithoutExtension([string]$fixtureManifest.entryAssembly)
    $fixtureEntryPath = @($fixtureOwnedPaths | Where-Object {
            [Reflection.AssemblyName]::GetAssemblyName($_).Name -ceq $fixtureEntryName
        })
    if ($fixtureEntryPath.Count -ne 1) {
        throw 'EDGE-SPLIT-LEDGER-001 raw package fixture must provide exactly one manifest entry assembly build input.'
    }
    $fixtureStaticInputs = @(Get-IndependentPackageStaticInputs `
        -PluginRoot (Split-Path $fixtureManifestPath -Parent) `
        -TargetAssemblyPath $fixtureEntryPath[0] `
        -ManifestSourcePath $fixtureManifestPath `
        -PluginOwnedAssemblyPaths ([string[]]$fixtureOwnedPaths))
    $fixtureAudit = Invoke-RawPackageAudit `
        -CandidatePackagePath $fixturePackagePath `
        -ManifestPath $fixtureManifestPath `
        -VerifiedAssemblyFacts $fixtureFacts.ToArray() `
        -DeclaredPackageNames $PackageFixtureDeclaredOwnedAssembly `
        -VerifiedPluginOwnedNames $fixtureVerifiedNames `
        -StaticInputs $fixtureStaticInputs `
        -RejectForbidden
    Write-Host "Edge plugin raw package fixture passed: entries=$(@($fixtureAudit.entries).Count), assemblies=$(@($fixtureAudit.assemblies).Count)."
    return
}

$resolvedLedgerPath = Resolve-RepositoryPath $LedgerPath
$schemaPath = Resolve-RepositoryPath 'eng/edge-plugin-contract-ledger.schema.json'
$generatorPath = Resolve-RepositoryPath 'eng/Generate-EdgePluginContractLedger.ps1'
$helperPath = Resolve-RepositoryPath 'eng/EdgePluginContractLedger.Roslyn.cs'
$validatorRoslynHelperPath = Resolve-RepositoryPath 'scripts/tests/EdgePluginContractLedger.ValidatorRoslyn.cs'
if (-not $CommitPairGateOnly) {
    $deterministicBuildTargetsPath = Resolve-RepositoryPath 'eng/EdgePluginContractDeterministicBuild.targets'
    $deterministicBuildTargetsSha256 = '24aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
    if ((Get-Sha256 $deterministicBuildTargetsPath) -cne $deterministicBuildTargetsSha256) {
        throw 'EDGE-SPLIT-LEDGER-001 deterministic authority build targets digest differs from the independent pinned contract.'
    }
}
$phaseCloseEvidenceSchemaPath = Resolve-RepositoryPath 'eng/edge-phase-close-evidence.schema.json'
$phaseCloseEvidenceValidatorPath = Resolve-RepositoryPath 'scripts/tests/Test-EdgePhaseCloseEvidence.ps1'
$inputsPath = Resolve-RepositoryPath $Phase0InputsPath
$inputsSchemaPath = Resolve-RepositoryPath 'eng/edge-split-phase0-inputs.schema.json'
$baselineLedgerPath = Resolve-RepositoryPath 'eng/baselines/edge-plugin-contract-ledger.json'
$testInventoryPath = Resolve-RepositoryPath 'scripts/tests/edge-test-inventory.json'
$requiredCountsPath = Resolve-RepositoryPath 'scripts/tests/required-test-counts.json'
$discoveredInventoryPath = Resolve-RepositoryPath 'scripts/tests/discovered-test-inventory.json'
$authorityProtocolModulePath = Resolve-RepositoryPath 'scripts/tests/EdgePluginContractLedger.Protocol.psm1'
$authorityResultSchemaPath = Resolve-RepositoryPath 'eng/edge-plugin-contract-authority-result.schema.json'
if ($RequireAuthorityReceipt) {
    Import-Module $authorityProtocolModulePath -Force
    $receiptRelativePath = [Environment]::GetEnvironmentVariable(
        'EDGE_PLUGIN_CONTRACT_AUTHORITY_RECEIPT', 'Process')
    $publicKey = [Environment]::GetEnvironmentVariable(
        'EDGE_PLUGIN_CONTRACT_AUTHORITY_PUBLIC_KEY', 'Process')
    $runId = [Environment]::GetEnvironmentVariable(
        'EDGE_PLUGIN_CONTRACT_AUTHORITY_RUN_ID', 'Process')
    $challenge = [Environment]::GetEnvironmentVariable(
        'EDGE_PLUGIN_CONTRACT_AUTHORITY_CHALLENGE', 'Process')
    $sourceRoot = [Environment]::GetEnvironmentVariable(
        'EDGE_PLUGIN_CONTRACT_AUTHORITY_SOURCE_ROOT', 'Process')
    $expectedAuthorityHead = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_AUTHORITY_HEAD', 'Process')
    $expectedAuthorityTree = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_AUTHORITY_TREE', 'Process')
    $expectedFormalFinalHead = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_HEAD', 'Process')
    $expectedFormalFinalTree = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_TREE', 'Process')
    $expectedSourceBaseHead = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_SOURCE_BASE_HEAD', 'Process')
    $expectedSourceBaseTree = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_SOURCE_BASE_TREE', 'Process')
    $expectedDirtyManifest = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_SOURCE_DIRTY_MANIFEST_SHA256', 'Process')
    $expectedEphemeralHead = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_HEAD', 'Process')
    $expectedEphemeralTree = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_TREE', 'Process')
    $expectedImplementationHead = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_HEAD', 'Process')
    $expectedImplementationTree = [Environment]::GetEnvironmentVariable('EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_TREE', 'Process')
    if ([string]::IsNullOrWhiteSpace($receiptRelativePath)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-MISSING parent did not inject a receipt path.'
    }
    if ([string]::IsNullOrWhiteSpace($sourceRoot)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-ROOT parent did not inject a source-root binding.'
    }
    $receiptFullPath = Resolve-RepositoryPath $receiptRelativePath
    [void](Assert-EdgeAuthorityReceipt `
        -RepositoryRoot $RepositoryRoot `
        -LedgerPath ([IO.Path]::GetRelativePath($RepositoryRoot, $resolvedLedgerPath).Replace('\', '/')) `
        -ReceiptPath $receiptFullPath `
        -PublicKeySpkiBase64 $publicKey `
        -ExpectedRunId $runId `
        -ExpectedChallengeBase64 $challenge `
        -ExpectedSourceRepositoryRoot $sourceRoot `
        -ExpectedAuthorityHead $expectedAuthorityHead `
        -ExpectedAuthorityTree $expectedAuthorityTree `
        -ExpectedFormalFinalHead $expectedFormalFinalHead `
        -ExpectedFormalFinalTree $expectedFormalFinalTree `
        -ExpectedSourceBaseHead $expectedSourceBaseHead `
        -ExpectedSourceBaseTree $expectedSourceBaseTree `
        -ExpectedSourceDirtyManifestSha256 $expectedDirtyManifest `
        -ExpectedEphemeralSnapshotHead $expectedEphemeralHead `
        -ExpectedEphemeralSnapshotTree $expectedEphemeralTree `
        -ExpectedImplementationHead $expectedImplementationHead `
        -ExpectedImplementationTree $expectedImplementationTree `
        -RequireFormal:$RequireFormalAuthorityReceipt)
    Write-Host "Edge plugin contract ledger fast receipt passed: run=$runId."
    return
}
if ($RequireFormalAuthorityReceipt) {
    throw 'EDGE-SPLIT-LEDGER-RECEIPT-FORMAL RequireFormalAuthorityReceipt requires RequireAuthorityReceipt.'
}
if ($AuthorityRebuildOnly -ne (-not [string]::IsNullOrWhiteSpace($AuthorityResultPath))) {
    throw 'EDGE-SPLIT-AUTHORITY-RESULT-001 AuthorityRebuildOnly and AuthorityResultPath are required together.'
}
if ($CommitPairGateOnly) {
    if (-not (Test-IndependentPathIdentityEqual $resolvedLedgerPath $baselineLedgerPath) -or
        -not (Test-Path -LiteralPath $resolvedLedgerPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-LEDGER-001 commit-pair fixture must target the canonical ledger path.'
    }
    $gateLedger = Get-Content -LiteralPath $resolvedLedgerPath -Raw | ConvertFrom-Json -Depth 20
    Assert-FinalCanonicalCommitPair `
        -GitRoot $RepositoryRoot `
        -ImplementationHead ([string]$gateLedger.sourceState.head) `
        -CanonicalRelativePath ([IO.Path]::GetRelativePath($RepositoryRoot, $baselineLedgerPath).Replace('\', '/'))
    Write-Host 'Edge plugin contract ledger final commit-pair gate passed.'
    return
}
$validatorGlobalJsonPath = Resolve-RepositoryPath 'global.json'
$validatorGlobalJson = Get-Content -LiteralPath $validatorGlobalJsonPath -Raw | ConvertFrom-Json -Depth 20
$validatorRequiredSdkVersion = [string]$validatorGlobalJson.sdk.version
$validatorResolvedSdkVersion = Invoke-CapturedCommand dotnet @('--version')
if ([string]::IsNullOrWhiteSpace($validatorRequiredSdkVersion) -or
    $validatorResolvedSdkVersion -cne $validatorRequiredSdkVersion) {
    throw "EDGE-SPLIT-LEDGER-001 independent validation requires exact global.json SDK: required=$validatorRequiredSdkVersion resolved=$validatorResolvedSdkVersion."
}
$validatorDotnetCommand = (Get-Command dotnet).Source
$validatorDotnetTarget = (Get-Item -LiteralPath $validatorDotnetCommand).Target
if ([string]::IsNullOrWhiteSpace($validatorDotnetTarget)) { $validatorDotnetTarget = $validatorDotnetCommand }
$validatorDotnetRoot = Split-Path ([IO.Path]::GetFullPath($validatorDotnetTarget)) -Parent
$validatorSdkDirectory = Join-Path $validatorDotnetRoot "sdk/$validatorResolvedSdkVersion"
$validatorCompilerPath = Join-Path $validatorSdkDirectory 'Roslyn/bincore/csc.dll'
if (-not (Test-Path -LiteralPath $validatorCompilerPath -PathType Leaf)) {
    throw "EDGE-SPLIT-LEDGER-001 independent exact SDK compiler is missing: $validatorCompilerPath."
}
foreach ($path in @($resolvedLedgerPath, $schemaPath, $generatorPath, $helperPath, $validatorRoslynHelperPath,
        $phaseCloseEvidenceSchemaPath, $phaseCloseEvidenceValidatorPath, $inputsPath, $inputsSchemaPath,
        $testInventoryPath, $requiredCountsPath, $discoveredInventoryPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 required ledger asset does not exist: $path"
    }
}

$ledgerRaw = Get-Content -LiteralPath $resolvedLedgerPath -Raw
$schemaRaw = Get-Content -LiteralPath $schemaPath -Raw
try {
    if (-not ($ledgerRaw | Test-Json -Schema $schemaRaw -ErrorAction Stop)) {
        throw 'schema validation returned false'
    }
}
catch {
    throw "EDGE-SPLIT-LEDGER-001 JSON schema validation failed: $($_.Exception.Message)"
}

$ledger = $ledgerRaw | ConvertFrom-Json -Depth 100
$schema = $schemaRaw | ConvertFrom-Json -Depth 100
$inputsRaw = Get-Content -LiteralPath $inputsPath -Raw
$inputsSchemaRaw = Get-Content -LiteralPath $inputsSchemaPath -Raw
try {
    if (-not ($inputsRaw | Test-Json -Schema $inputsSchemaRaw -ErrorAction Stop)) { throw 'schema validation returned false' }
}
catch { throw "EDGE-SPLIT-LEDGER-001 authority input JSON schema validation failed: $($_.Exception.Message)" }
$inputs = $inputsRaw | ConvertFrom-Json -Depth 40
Assert-AuthorityInputSafe $inputs
$testInventory = Get-Content -LiteralPath $testInventoryPath -Raw | ConvertFrom-Json -Depth 40
$requiredCounts = Get-Content -LiteralPath $requiredCountsPath -Raw | ConvertFrom-Json -Depth 40
$discoveredInventory = Get-Content -LiteralPath $discoveredInventoryPath -Raw | ConvertFrom-Json -Depth 40
$batchRank = Get-BatchRank ([string]$ledger.batchId)
$canonicalLedgerRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $baselineLedgerPath).Replace('\', '/')
$predecessorBatchByCurrentBatch = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-010', 'EDGE-SPLIT-000')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-020', 'EDGE-SPLIT-010')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-030', 'EDGE-SPLIT-020')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-040', 'EDGE-SPLIT-030')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-050', 'EDGE-SPLIT-040')

Assert-Equal $ledger.schemaVersion 2 'ledger schemaVersion mismatch'
Assert-Equal $ledger.ruleId 'EDGE-SPLIT-LEDGER-001' 'ledger ruleId mismatch'
Assert-Equal $schema.properties.schemaVersion.const 2 'schema does not lock schemaVersion'
Assert-Equal $schema.properties.ruleId.const 'EDGE-SPLIT-LEDGER-001' 'schema does not lock ruleId'
if ([bool]$schema.additionalProperties) {
    throw 'EDGE-SPLIT-LEDGER-001 schema must reject undeclared top-level properties.'
}

Assert-Equal $ledger.integrity.schemaSha256 (Get-Sha256 $schemaPath) 'schema digest mismatch'
Assert-Equal $ledger.integrity.generatorSha256 (Get-Sha256 $generatorPath) 'generator digest mismatch'
Assert-Equal $ledger.integrity.roslynHelperSha256 (Get-Sha256 $helperPath) 'Roslyn helper digest mismatch'
Assert-Equal $ledger.integrity.validatorRoslynHelperSha256 (Get-Sha256 $validatorRoslynHelperPath) 'independent validator Roslyn helper digest mismatch'
Assert-Equal $ledger.integrity.phaseCloseEvidenceSchemaSha256 (Get-Sha256 $phaseCloseEvidenceSchemaPath) 'phase-close evidence schema digest mismatch'
Assert-Equal $ledger.integrity.phaseCloseEvidenceValidatorSha256 (Get-Sha256 $phaseCloseEvidenceValidatorPath) 'phase-close evidence validator digest mismatch'
Assert-Equal $ledger.integrity.phase0InputsSha256 (Get-Sha256 $inputsPath) 'Phase 0 inputs digest mismatch'
Assert-Equal $ledger.integrity.phase0InputsSchemaSha256 (Get-Sha256 $inputsSchemaPath) 'Phase 0 inputs schema digest mismatch'
if ($batchRank -eq 0) {
    Assert-Equal $ledger.integrity.baselineLedgerSha256 '' 'Phase 0 must not self-reference its canonical ledger'
    Assert-Equal $ledger.integrity.baselineLedgerBatchId '' 'Phase 0 must not claim a predecessor batch'
    Assert-Equal $ledger.integrity.baselineLedgerEvidenceCommit '' 'Phase 0 must not claim a predecessor evidence commit'
}
else {
    $expectedPredecessorBatch = [string]$predecessorBatchByCurrentBatch[[string]$ledger.batchId]
    Assert-Equal $ledger.integrity.baselineLedgerBatchId $expectedPredecessorBatch 'immediate predecessor batch mismatch'
    $implementationHead = [string]$ledger.sourceState.head
    $ledgerChangingCommits = @((& git -C $RepositoryRoot rev-list --reverse --ancestry-path `
            "$([string]$ledger.frozenPhase0Source.head)..$implementationHead" -- $canonicalLedgerRelativePath 2>&1) |
        ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $matchingEvidenceCommits = [Collections.Generic.List[string]]::new()
    foreach ($candidateCommit in $ledgerChangingCommits) {
        if ((Get-GitBlobSha256 -Commit $candidateCommit -RepositoryPath $canonicalLedgerRelativePath) -ceq
            [string]$ledger.integrity.baselineLedgerSha256) {
            $matchingEvidenceCommits.Add($candidateCommit)
        }
    }
    if ($matchingEvidenceCommits.Count -ne 1 -or
        [string]$matchingEvidenceCommits[0] -cne [string]$ledger.integrity.baselineLedgerEvidenceCommit) {
        throw 'EDGE-SPLIT-LEDGER-001 predecessor evidence commit is missing, ambiguous, or not bound to its original bytes.'
    }
    $evidenceCommit = [string]$matchingEvidenceCommits[0]
    $evidenceLedgerRaw = (& git -C $RepositoryRoot show "$evidenceCommit`:$canonicalLedgerRelativePath" 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'EDGE-SPLIT-LEDGER-001 predecessor evidence ledger cannot be read.' }
    $evidenceLedger = $evidenceLedgerRaw | ConvertFrom-Json -Depth 100
    $predecessorImplementationHead = [string]$evidenceLedger.sourceState.head
    $postPredecessorLedgerCommits = @(& git -C $RepositoryRoot rev-list --reverse --ancestry-path `
        "$predecessorImplementationHead..$implementationHead" -- $canonicalLedgerRelativePath 2>&1 |
        ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $parents = @((& git -C $RepositoryRoot show -s --format='%P' $evidenceCommit 2>&1 | Out-String).Trim().Split(
            ' ', [StringSplitOptions]::RemoveEmptyEntries))
    $paths = @(& git -C $RepositoryRoot -c core.quotePath=false diff-tree --no-commit-id --name-only -r $evidenceCommit 2>&1 |
        ForEach-Object { ([string]$_).Replace('\', '/') } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $treeEntry = (& git -C $RepositoryRoot ls-tree $evidenceCommit -- $canonicalLedgerRelativePath 2>&1 | Out-String).Trim()
    if ($postPredecessorLedgerCommits.Count -ne 1 -or [string]$postPredecessorLedgerCommits[0] -cne $evidenceCommit -or
        $parents.Count -ne 1 -or [string]$parents[0] -cne $predecessorImplementationHead -or
        $paths.Count -ne 1 -or [string]$paths[0] -cne $canonicalLedgerRelativePath -or
        $treeEntry -notmatch '^100644 blob [0-9a-f]{40}\t') {
        throw 'EDGE-SPLIT-LEDGER-001 predecessor evidence must be a direct 100644 ledger-only child of its recorded implementation HEAD.'
    }
    & git -C $RepositoryRoot merge-base --is-ancestor $evidenceCommit $implementationHead 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'EDGE-SPLIT-LEDGER-001 predecessor evidence commit is not an ancestor of the current implementation.' }
    Assert-Equal (Get-GitBlobSha256 -Commit $implementationHead -RepositoryPath $canonicalLedgerRelativePath) `
        $ledger.integrity.baselineLedgerSha256 'predecessor ledger was rewritten after its evidence commit'
}
$expectedPayloadSha256 = [string]$ledger.integrity.payloadSha256
$ledger.integrity.payloadSha256 = ''
$actualPayloadSha256 = Get-TextSha256 (($ledger | ConvertTo-Json -Depth 100) + "`n")
$ledger.integrity.payloadSha256 = $expectedPayloadSha256
Assert-Equal $actualPayloadSha256 $expectedPayloadSha256 'ledger payload digest mismatch'

Assert-Equal $ledger.sourceState.cleanObserved (@($ledger.sourceState.dirtyPaths).Count -eq 0) 'cleanObserved must be derived from captured dirtyPaths'
Assert-Equal $ledger.sourceState.observationMethod 'git-status-porcelain-v1' 'source-state observation method mismatch'
Assert-Equal $ledger.sourceState.generationProtocol 'clean-implementation-head-plus-ledger-evidence-commit' 'source-state generation protocol mismatch'
if (@($ledger.sourceState.excludedPaths).Count -ne 1) {
    throw 'EDGE-SPLIT-LEDGER-001 source-state observation may exclude only its exact output ledger path.'
}
foreach ($path in @($ledger.sourceState.dirtyPaths + $ledger.sourceState.excludedPaths + $ledger.sourceState.pluginSourceDriftFromHead)) {
    Assert-RepositoryRelativePath ([string]$path) 'source-state path'
}
$recordedTree = (& git rev-parse "$([string]$ledger.sourceState.head)^{tree}" 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'EDGE-SPLIT-LEDGER-001 source-state HEAD is not a locally verifiable commit.' }
Assert-Equal $recordedTree $ledger.sourceState.tree 'source-state commit/tree mismatch'
Assert-Equal $ledger.sourceState.originMain ((& git rev-parse origin/main 2>&1 | Out-String).Trim()) 'source-state origin/main mismatch'
& git merge-base --is-ancestor ([string]$ledger.sourceState.head) HEAD 2>$null
if ($LASTEXITCODE -ne 0) { throw 'EDGE-SPLIT-LEDGER-001 source-state HEAD is not an ancestor of the current candidate.' }

$isCanonicalLedger = Test-IndependentPathIdentityEqual $resolvedLedgerPath $baselineLedgerPath
if ($isCanonicalLedger) {
    if (-not [bool]$ledger.sourceState.cleanObserved -or @($ledger.sourceState.dirtyPaths).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 canonical ledger must be generated from a clean implementation commit.'
    }
    $canonicalRelativePath = $canonicalLedgerRelativePath
    if (@($ledger.sourceState.excludedPaths).Count -ne 1 -or
        [string]$ledger.sourceState.excludedPaths[0] -cne $canonicalRelativePath) {
        throw 'EDGE-SPLIT-LEDGER-001 canonical generation may exclude only the canonical ledger evidence path.'
    }
    if (@($ledger.sourceState.pluginSourceDriftFromHead).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 canonical implementation evidence cannot contain plugin-source drift from its recorded HEAD.'
    }
    Assert-FinalCanonicalCommitPair `
        -GitRoot $RepositoryRoot `
        -ImplementationHead ([string]$ledger.sourceState.head) `
        -CanonicalRelativePath $canonicalRelativePath
}

Assert-Equal $ledger.frozenPhase0Source.head $inputs.baselineGit.head 'frozen Phase 0 head mismatch'
Assert-Equal $ledger.frozenPhase0Source.tree $inputs.baselineGit.tree 'frozen Phase 0 tree mismatch'
Assert-Equal $ledger.frozenPhase0Source.originMainAtFreeze $inputs.baselineGit.originMain 'frozen origin/main mismatch'
Assert-Equal $ledger.frozenPhase0Source.pullRequest $inputs.baselineGit.pullRequest 'frozen pull request mismatch'
if ($batchRank -eq 0 -and @($ledger.frozenPhase0Source.pluginSourceDriftFromFrozenHead).Count -ne 0) {
    throw 'EDGE-SPLIT-LEDGER-001 Phase 0 must analyze the unchanged frozen plugin source.'
}

Assert-JsonEqual $ledger.decisions $inputs.decisions 'repository/package decisions differ from the exact Phase 0-5 authority input.'

if ([int]$ledger.solutionInventory.projectCount -ne @($ledger.solutionInventory.projects).Count) {
    throw 'EDGE-SPLIT-LEDGER-001 solution project count must be derived from the recorded project set.'
}
foreach ($project in @($ledger.solutionInventory.projects)) { Assert-RepositoryRelativePath ([string]$project) 'solution project' }
[xml]$solutionDocument = Get-Content -LiteralPath (Resolve-RepositoryPath ([string]$ledger.solutionInventory.solutionPath)) -Raw
$actualSolutionProjects = @($solutionDocument.SelectNodes('//Project') | ForEach-Object {
        ([string]$_.Path).Replace('\', '/')
    } | Sort-Ordinal)
if ((@($ledger.solutionInventory.projects) -join "`n") -cne ($actualSolutionProjects -join "`n")) {
    throw 'EDGE-SPLIT-LEDGER-001 solution project inventory is not an exact sorted projection of the solution.'
}

Assert-Equal $ledger.testInventory.requiredRunnerCount $testInventory.testProjectCount 'required runner inventory mismatch'
Assert-Equal $ledger.testInventory.discoveredCaseCount $requiredCounts.caseCount 'required case-count inventory mismatch'
Assert-Equal $ledger.testInventory.discoveredInventoryCaseCount $discoveredInventory.caseCount 'discovered case-count inventory mismatch'
Assert-Equal $ledger.testInventory.inventorySha256 (Get-Sha256 $testInventoryPath) 'test inventory digest mismatch'
Assert-Equal $ledger.testInventory.requiredCountsSha256 (Get-Sha256 $requiredCountsPath) 'required counts digest mismatch'
Assert-Equal $ledger.testInventory.discoveredInventorySha256 (Get-Sha256 $discoveredInventoryPath) 'discovered inventory digest mismatch'
Assert-Equal $ledger.testInventory.inventorySchemaVersion $testInventory.schemaVersion 'test inventory schema mismatch'
Assert-JsonEqual $ledger.testInventory.historicalRequiredSuiteBaseline $inputs.testEvidence.historicalBaseline 'historical required-suite baseline differs from the authority input.'
$historicalEvidence = $ledger.testInventory.historicalRequiredSuiteBaseline
if ([int]$historicalEvidence.discovered -ne [int]$historicalEvidence.executed -or
    [int]$historicalEvidence.discovered -ne [int]$historicalEvidence.passed -or
    [int]$historicalEvidence.failed -ne 0 -or [int]$historicalEvidence.skipped -ne 0 -or
    [string]$historicalEvidence.evidenceRole -cne 'historical-required-suite-baseline-only') {
    throw 'EDGE-SPLIT-LEDGER-001 historical required-suite evidence does not reconcile or overclaims current status.'
}
$expectedPhaseCloseProtocol = [pscustomobject][ordered]@{
    carriedByLedger = $false
    requiredHead = 'exact-ledger-evidence-commit-head'
    acceptedSources = @('local-trx-manifest', 'required-ci')
    requiredReconciliation = 'discovered=executed=passed;failed=skipped=0'
    schemaPath = 'eng/edge-phase-close-evidence.schema.json'
    schemaSha256 = Get-Sha256 $phaseCloseEvidenceSchemaPath
    validatorPath = 'scripts/tests/Test-EdgePhaseCloseEvidence.ps1'
    validatorSha256 = Get-Sha256 $phaseCloseEvidenceValidatorPath
}
Assert-JsonEqual $ledger.testInventory.phaseCloseEvidenceProtocol $expectedPhaseCloseProtocol 'phase-close evidence protocol drifted or attempted to carry final evidence inside the ledger.'

$manifestPath = Resolve-RepositoryPath ([string]$ledger.pluginManifest.path)
Assert-Equal $ledger.pluginManifest.sha256 (Get-Sha256 $manifestPath) 'plugin manifest digest mismatch'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 30
Assert-JsonEqual $ledger.pluginManifest.value $manifest 'plugin manifest value is not an exact projection of the implementation manifest.'
if ($batchRank -eq 0) {
    Assert-Equal $ledger.pluginManifest.value.moduleId $inputs.publishedComposition.plugin.moduleId 'Phase 0 plugin module mismatch'
    Assert-Equal $ledger.pluginManifest.value.version $inputs.publishedComposition.plugin.version 'Phase 0 plugin version mismatch'
    Assert-Equal $ledger.pluginManifest.value.hostApiVersion $inputs.publishedComposition.plugin.hostApiVersion 'Phase 0 plugin API mismatch'
}

$hostComposition = $ledger.publishedComposition.host
$pluginComposition = $ledger.publishedComposition.plugin
Assert-Equal $ledger.publishedComposition.evidenceRole 'immutable-historical-composition' 'published composition evidence role mismatch'
Assert-Equal $ledger.publishedComposition.byteVerification.status 'verified' 'historical byte-evidence status mismatch'
Assert-Equal $ledger.publishedComposition.byteVerification.method 'download-size-sha256-and-archive-inventory' 'historical byte-evidence method mismatch'
if (($ledger.publishedComposition.byteVerification | ConvertTo-Json -Depth 10 -Compress) -cne
    ($inputs.publishedComposition.byteVerification | ConvertTo-Json -Depth 10 -Compress)) {
    throw 'EDGE-SPLIT-LEDGER-001 historical byte-evidence provenance differs from the frozen authority input.'
}
foreach ($uri in @($ledger.publishedComposition.catalogApiBaseUrl, $hostComposition.manifest.url, $hostComposition.artifact.url, $pluginComposition.artifact.url)) {
    Assert-SafeArtifactUri ([string]$uri)
}
if (-not [bool]$hostComposition.manifest.verified -or -not [bool]$hostComposition.artifact.verified -or -not [bool]$pluginComposition.artifact.verified) {
    throw 'EDGE-SPLIT-LEDGER-001 immutable old-composition bytes must all be verified.'
}
foreach ($field in @('version', 'hostApiVersion', 'sourceCommit')) {
    Assert-Equal $hostComposition.$field $inputs.publishedComposition.host.$field "old host $field mismatch"
    Assert-Equal $pluginComposition.$field $inputs.publishedComposition.plugin.$field "old plugin $field mismatch"
}
Assert-Equal $hostComposition.artifact.sha256 $inputs.publishedComposition.host.sha256 'old host artifact digest mismatch'
Assert-Equal $hostComposition.artifact.size $inputs.publishedComposition.host.size 'old host artifact size mismatch'
Assert-Equal $pluginComposition.artifact.sha256 $inputs.publishedComposition.plugin.sha256 'old plugin artifact digest mismatch'
Assert-Equal $pluginComposition.artifact.size $inputs.publishedComposition.plugin.size 'old plugin artifact size mismatch'
Assert-Equal $ledger.publishedComposition.catalogApiBaseUrl $inputs.publishedComposition.catalogApiBaseUrl 'old catalog API base URL mismatch'
Assert-Equal $hostComposition.catalogDownloadUrl $inputs.publishedComposition.host.catalogDownloadUrl 'old host catalog path mismatch'
Assert-Equal $hostComposition.manifest.url $inputs.publishedComposition.host.manifestUrl 'old host manifest URL mismatch'
Assert-Equal $hostComposition.manifest.sha256 $inputs.publishedComposition.host.manifestSha256 'old host manifest digest mismatch'
Assert-Equal $hostComposition.manifest.size $inputs.publishedComposition.host.manifestSize 'old host manifest size mismatch'
Assert-Equal $hostComposition.artifact.url $inputs.publishedComposition.host.artifactUrl 'old host artifact URL mismatch'
Assert-Equal $pluginComposition.moduleId $inputs.publishedComposition.plugin.moduleId 'old plugin module mismatch'
Assert-Equal $pluginComposition.artifact.url $inputs.publishedComposition.plugin.artifactUrl 'old plugin artifact URL mismatch'
$oldPackageEntries = @($pluginComposition.packageEntries)
$oldPackagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($oldPackageEntry in $oldPackageEntries) { [void]$oldPackagePaths.Add([string]$oldPackageEntry.path) }
if (@($inputs.publishedComposition.plugin.requiredPackageEntries | Where-Object { -not $oldPackagePaths.Contains([string]$_) }).Count -ne 0) {
    throw 'EDGE-SPLIT-LEDGER-001 immutable old plugin package inventory is incomplete.'
}
$expectedOldPackageEntries = @($inputs.publishedComposition.plugin.packageEntries | Sort-Ordinal path)
if (($oldPackageEntries | Sort-Ordinal path | ConvertTo-Json -Depth 10 -Compress) -cne
    ($expectedOldPackageEntries | ConvertTo-Json -Depth 10 -Compress)) {
    throw 'EDGE-SPLIT-LEDGER-001 immutable old plugin package inventory differs from its frozen byte evidence.'
}
foreach ($entry in $oldPackageEntries) {
    Assert-RepositoryRelativePath ([string]$entry.path) 'old plugin package entry'
    if ([long]$entry.size -lt 0) {
        throw 'EDGE-SPLIT-LEDGER-001 old plugin package entry lacks byte-level evidence.'
    }
}

$usages = @($ledger.externalSymbolUsages)
$compilation = $ledger.msbuildCompilation
Assert-Equal $compilation.dotnetSdkVersion $validatorResolvedSdkVersion 'ledger SDK version differs from exact global.json SDK'
$pluginSources = @($compilation.pluginSources)
$generatedSources = @($compilation.generatedSources)
$compilationInputs = @($compilation.compilationInputs)
$references = @($ledger.referenceAssemblies)
if ([int]$compilation.compilationErrorCount -ne 0 -or
    [int]$compilation.msbuildCompileSourceCount -le 0 -or
    [int]$compilation.metadataReferenceCount -le 0 -or
    $pluginSources.Count -le 0) {
    throw 'EDGE-SPLIT-LEDGER-001 MSBuild/Roslyn compilation facts are invalid.'
}
Assert-Equal $compilation.emittedGeneratorSourceCount $generatedSources.Count 'emitted generator source count mismatch'
Assert-Equal $compilation.compilationInputCount $compilationInputs.Count 'complete compilation input count mismatch'
Assert-Equal $compilation.compilationInputCount ([int]$compilation.msbuildCompileSourceCount + [int]$compilation.emittedGeneratorSourceCount) 'MSBuild plus emitted generated input count mismatch'
Assert-Equal $compilation.metadataReferenceCount $references.Count 'metadata reference count mismatch'
Assert-Equal "$([string]$compilation.assemblyName).dll" $manifest.entryAssembly 'compiled assembly name does not match the plugin manifest'
$pluginProjectPath = Resolve-RepositoryPath ([string]$compilation.projectPath)
if (-not (Test-Path -LiteralPath $pluginProjectPath -PathType Leaf)) {
    throw 'EDGE-SPLIT-LEDGER-001 MSBuild project path does not exist.'
}
$pluginRoot = Split-Path $pluginProjectPath -Parent
$pluginRootRelative = [IO.Path]::GetRelativePath($RepositoryRoot, $pluginRoot).Replace('\', '/')
$validatorTemporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-ledger-validator-$([Guid]::NewGuid().ToString('N'))"
$validatorGeneratedRoot = Join-Path $validatorTemporaryRoot 'generated'
$validatorPackagesRoot = Join-Path $validatorTemporaryRoot 'nuget-packages'
$validatorHttpCacheRoot = Join-Path $validatorTemporaryRoot 'nuget-http-cache'
$validatorPluginCacheRoot = Join-Path $validatorTemporaryRoot 'nuget-plugin-cache'
$validatorCredentialProvidersRoot = Join-Path $validatorTemporaryRoot 'nuget-credential-providers-empty'
$validatorPluginDiscoveryRoot = Join-Path $validatorTemporaryRoot 'nuget-plugin-paths-empty'
$validatorRawPathMap = "$(ConvertTo-IndependentPathMapSourceToken $RepositoryRoot)=/_,$(ConvertTo-IndependentPathMapSourceToken $validatorGeneratedRoot)=/__edge_contract_generated__"
$validatorCanonicalPathMap = ConvertTo-IndependentMsBuildPropertyValue $validatorRawPathMap
$validatorBuildTargetsProperty = ConvertTo-IndependentMsBuildPropertyValue $deterministicBuildTargetsPath
$validatorRepositoryRootProperty = ConvertTo-IndependentMsBuildPropertyValue $RepositoryRoot
$validatorDeterministicBuildArguments = [string[]]@(
    '-p:DebugSymbols=true',
    '-p:DebugType=embedded',
    '-p:Deterministic=true',
    "-p:PathMap=$validatorCanonicalPathMap",
    "-p:CustomAfterMicrosoftCSharpTargets=$validatorBuildTargetsProperty",
    '-p:_EdgeContractAuthorityBuild=true',
    "-p:_EdgeContractRepositoryRoot=$validatorRepositoryRootProperty"
)
[void](New-Item -ItemType Directory -Path $validatorGeneratedRoot -Force)
[void](New-Item -ItemType Directory -Path $validatorPackagesRoot -Force)
[void](New-Item -ItemType Directory -Path $validatorHttpCacheRoot -Force)
[void](New-Item -ItemType Directory -Path $validatorPluginCacheRoot -Force)
[void](New-Item -ItemType Directory -Path $validatorCredentialProvidersRoot -Force)
[void](New-Item -ItemType Directory -Path $validatorPluginDiscoveryRoot -Force)
$validatorRestoreEnvironmentNames = @(
    'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH',
    'NUGET_CREDENTIALPROVIDERS_PATH', 'NUGET_PLUGIN_PATHS',
    'RestoreSources', 'RestoreAdditionalProjectSources', 'RestoreFallbackFolders'
)
$validatorRestoreEnvironmentBefore = Get-IndependentEnvironmentSnapshot `
    -VariableNames ([string[]]$validatorRestoreEnvironmentNames)
try {
$validatorRestoreConfigPath = Resolve-RepositoryPath 'NuGet.Config'
$validatorTrackedSources = @(Get-IndependentNuGetSources $validatorRestoreConfigPath)
[Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $validatorPackagesRoot, 'Process')
[Environment]::SetEnvironmentVariable('NUGET_HTTP_CACHE_PATH', $validatorHttpCacheRoot, 'Process')
[Environment]::SetEnvironmentVariable('NUGET_PLUGINS_CACHE_PATH', $validatorPluginCacheRoot, 'Process')
[Environment]::SetEnvironmentVariable('NUGET_CREDENTIALPROVIDERS_PATH', $validatorCredentialProvidersRoot, 'Process')
[Environment]::SetEnvironmentVariable('NUGET_PLUGIN_PATHS', $validatorPluginDiscoveryRoot, 'Process')
foreach ($environmentName in @('RestoreSources', 'RestoreAdditionalProjectSources', 'RestoreFallbackFolders')) {
    [Environment]::SetEnvironmentVariable($environmentName, $null, 'Process')
}
$validatorAuthoritySeedPaths = [Collections.Generic.List[string]]::new()
$validatorAuthoritySeedPaths.Add($pluginProjectPath)
foreach ($surfaceRecord in @($ledger.dependencyLayers.sdkUiContractClosures.surfaces)) {
    $validatorAuthoritySeedPaths.Add((Resolve-RepositoryPath ([string]$surfaceRecord.projectPath)))
}
$validatorRestoreDiagnosticLogs = [Collections.Generic.List[string]]::new()
$validatorRestoreOrdinal = 0
foreach ($restoreProject in @($validatorAuthoritySeedPaths.ToArray() | Sort-Ordinal -Unique)) {
    $validatorRestoreDiagnosticLog = Join-Path $validatorTemporaryRoot "nuget-restore-$validatorRestoreOrdinal.log"
    $validatorRestoreDiagnosticLogs.Add($validatorRestoreDiagnosticLog)
    [void](Invoke-CapturedCommand dotnet @(
            'restore', $restoreProject, '--force-evaluate', '--disable-parallel',
            '--no-http-cache', '--packages', $validatorPackagesRoot,
            '--configfile', $validatorRestoreConfigPath, "-p:RestoreConfigFile=$validatorRestoreConfigPath",
            "-p:RestoreSources=$($validatorTrackedSources -join ';')",
            '-p:RestoreAdditionalProjectSources=', '-p:RestoreFallbackFolders=',
            "-p:RestorePackagesPath=$validatorPackagesRoot",
            "-flp:logfile=$validatorRestoreDiagnosticLog;verbosity=diagnostic",
            '--nologo', '-noAutoResponse'))
    $validatorRestoreOrdinal++
}
Assert-IndependentNuGetDiscoveryIsolation `
    -DeclaredEmptyDirectories ([string[]]@($validatorCredentialProvidersRoot, $validatorPluginDiscoveryRoot)) `
    -DiagnosticRestoreLogs ([string[]]$validatorRestoreDiagnosticLogs.ToArray())
$validatorToolchainDiagnosticLogs = [Collections.Generic.List[string]]::new()
$validatorSurfaceBuilds = @($ledger.dependencyLayers.sdkUiContractClosures.surfaces | ForEach-Object {
        [pscustomobject][ordered]@{
            assemblyName = [string]$_.assemblyName
            projectPath = Resolve-RepositoryPath ([string]$_.projectPath)
        }
    } | Sort-Ordinal assemblyName)
foreach ($validatorSurfaceBuild in $validatorSurfaceBuilds) {
    $surfaceAssemblyName = [string]$validatorSurfaceBuild.assemblyName
    if ($surfaceAssemblyName -notmatch '^[A-Za-z0-9_.-]+$') {
        throw 'EDGE-SPLIT-LEDGER-001 independent surface build has an unsafe assembly identity.'
    }
    $surfaceDiagnosticLog = Join-Path $validatorTemporaryRoot "toolchain-$surfaceAssemblyName.log"
    $validatorToolchainDiagnosticLogs.Add($surfaceDiagnosticLog)
    $validatorSurfaceBuildArguments = [string[]](@(
            'build', ([string]$validatorSurfaceBuild.projectPath),
            '-c', ([string]$compilation.configuration), '--no-restore', '--no-incremental', '-t:Rebuild',
            '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false'
        ) + $validatorDeterministicBuildArguments + @(
            "-p:SourceRevisionId=$([string]$ledger.sourceState.head)",
            "-p:RepositoryCommit=$([string]$ledger.sourceState.head)",
            '-p:ContinuousIntegrationBuild=true',
            "-flp:logfile=$surfaceDiagnosticLog;verbosity=diagnostic",
            '--disable-build-servers', '--nologo', '-noAutoResponse'
        ))
    [void](Invoke-CapturedCommand dotnet $validatorSurfaceBuildArguments)
    Assert-IndependentDeterministicBuildLog `
        -LogPath $surfaceDiagnosticLog -ProjectPath ([string]$validatorSurfaceBuild.projectPath) `
        -EncodedPathMap $validatorCanonicalPathMap -EncodedTargetsPath $validatorBuildTargetsProperty `
        -EncodedRepositoryRoot $validatorRepositoryRootProperty
}
$validatorDiagnosticLog = Join-Path $validatorTemporaryRoot 'toolchain-plugin.log'
$validatorToolchainDiagnosticLogs.Add($validatorDiagnosticLog)
$validatorBuildArguments = [string[]](@(
    'build', $pluginProjectPath, '-c', ([string]$compilation.configuration), '--no-restore', '--no-incremental', '-t:Rebuild',
    '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '-p:EmitCompilerGeneratedFiles=false'
) + $validatorDeterministicBuildArguments + @(
    "-p:SourceRevisionId=$([string]$ledger.sourceState.head)", "-p:RepositoryCommit=$([string]$ledger.sourceState.head)",
    '-p:ContinuousIntegrationBuild=true',
    "-flp:logfile=$validatorDiagnosticLog;verbosity=diagnostic",
    '--disable-build-servers', '--nologo', '-noAutoResponse'
))
[void](Invoke-CapturedCommand dotnet $validatorBuildArguments)
$validatorPluginOnlyDiagnosticLog = Join-Path $validatorTemporaryRoot 'toolchain-plugin-only.log'
$validatorToolchainDiagnosticLogs.Add($validatorPluginOnlyDiagnosticLog)
Assert-IndependentDeterministicBuildLog `
    -LogPath $validatorDiagnosticLog -ProjectPath $pluginProjectPath `
    -EncodedPathMap $validatorCanonicalPathMap -EncodedTargetsPath $validatorBuildTargetsProperty `
    -EncodedRepositoryRoot $validatorRepositoryRootProperty
$validatorPluginOnlyBuildArguments = [string[]](@(
    'build', $pluginProjectPath, '-c', ([string]$compilation.configuration), '--no-restore', '--no-incremental', '-t:Rebuild',
    '-m:1', '-p:BuildInParallel=false', '-p:BuildProjectReferences=false', '-p:UseSharedCompilation=false',
    '-p:EmitCompilerGeneratedFiles=true', "-p:CompilerGeneratedFilesOutputPath=$validatorGeneratedRoot"
) + $validatorDeterministicBuildArguments + @(
    "-p:SourceRevisionId=$([string]$ledger.sourceState.head)", "-p:RepositoryCommit=$([string]$ledger.sourceState.head)",
    '-p:ContinuousIntegrationBuild=true',
    "-flp:logfile=$validatorPluginOnlyDiagnosticLog;verbosity=diagnostic",
    '--disable-build-servers', '--nologo', '-noAutoResponse'
))
[void](Invoke-CapturedCommand dotnet $validatorPluginOnlyBuildArguments)
Assert-IndependentDeterministicBuildLog `
    -LogPath $validatorPluginOnlyDiagnosticLog -ProjectPath $pluginProjectPath `
    -EncodedPathMap $validatorCanonicalPathMap -EncodedTargetsPath $validatorBuildTargetsProperty `
    -EncodedRepositoryRoot $validatorRepositoryRootProperty
$validatorExecutedToolchainFacts = @(Get-IndependentExecutedToolchainFacts `
    -BuildLogFiles ([string[]]$validatorToolchainDiagnosticLogs.ToArray()) `
    -ExactSdkDirectory $validatorSdkDirectory `
    -RequiredCompilerAssemblies ([string[]]@($validatorCompilerPath)))
$validatorEvaluationArguments = @(
    'msbuild', $pluginProjectPath, '-nologo', '-noAutoResponse', '-t:ResolveReferences',
    "-p:Configuration=$([string]$compilation.configuration)",
    "-p:SourceRevisionId=$([string]$ledger.sourceState.head)", "-p:RepositoryCommit=$([string]$ledger.sourceState.head)",
    '-getProperty:AssemblyName,TargetFramework,TargetPath,DefineConstants,LangVersion,Nullable,ProjectAssetsFile,NuGetPackageRoot,NetCoreTargetingPackRoot',
    '-getItem:Compile,ProjectReference,ReferencePath,AvaloniaResource,Content,None,Page,EmbeddedResource,AdditionalFiles,Analyzer,FrameworkReference,PackageReference'
)
$validatorEvaluation = (Invoke-CapturedCommand dotnet $validatorEvaluationArguments) | ConvertFrom-Json -Depth 100
if ([string]$validatorEvaluation.Properties.AssemblyName -cne [string]$compilation.assemblyName) {
    throw 'EDGE-SPLIT-LEDGER-001 independent MSBuild assembly identity differs from ledger.'
}
[string[]]$validatorCompileSourcePaths = @($validatorEvaluation.Items.Compile | ForEach-Object { [string]$_.FullPath } |
    Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Sort-Ordinal -Unique)
[string[]]$validatorGeneratedSourcePaths = @(Get-ChildItem -LiteralPath $validatorGeneratedRoot -Recurse -File -Filter '*.cs' |
    ForEach-Object FullName | Sort-Ordinal -Unique)
[string[]]$validatorReferencePaths = @($validatorEvaluation.Items.ReferencePath | ForEach-Object { [string]$_.FullPath } |
    Where-Object { $_.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Sort-Ordinal -Unique)
[string[]]$validatorPreprocessorSymbols = @(([string]$validatorEvaluation.Properties.DefineConstants).Split(
        ';', [StringSplitOptions]::RemoveEmptyEntries))

$validatorNugetRoot = [string]$validatorEvaluation.Properties.NuGetPackageRoot
if ([string]::IsNullOrWhiteSpace($validatorNugetRoot) -or
    -not (Test-Path -LiteralPath $validatorNugetRoot -PathType Container)) {
    throw 'EDGE-SPLIT-LEDGER-001 independent MSBuild evaluation did not expose a valid global packages folder.'
}
$independentAuthorityInputs = @(Get-IndependentMsBuildAuthorityInventory `
    -ProjectSeeds ([string[]]$validatorAuthoritySeedPaths.ToArray()) `
    -BuildConfiguration ([string]$compilation.configuration) `
    -ScratchRoot $validatorTemporaryRoot `
    -DotnetInstallationRoot $validatorDotnetRoot `
    -GlobalPackagesFolder $validatorNugetRoot `
    -DeterministicBuildArguments ([string[]]$validatorDeterministicBuildArguments) `
    -ExecutedToolchainFacts $validatorExecutedToolchainFacts)
$recordedAuthorityInputs = @($compilation.authorityInputs)
$expectedAuthorityProjectionPolicy = [pscustomobject][ordered]@{
    version = 1
    restoreConfigPath = 'NuGet.Config'
    restoreIsolation = 'empty-caches-explicit-sources-no-ambient'
    pluginDiscoveryIsolation = 'explicit-empty-discovery-paths-plus-diagnostic-rejection'
    restorePackageAuthority = 'nupkg-archive-sha512-metadata-contenthash-v1'
    restoreGeneratedRepresentation = 'restore-semantic-v1'
    compilerConfigRepresentation = 'compiler-config-semantic-v1'
    rawRepresentation = 'raw-sha256'
    resolvedSdkPolicy = 'global-json-version-exact'
    executedToolchainAuthority = 'diagnostic-executed-assembly-plus-sibling-managed-closure'
    rootReplacement = 'longest-first-boundary-aware'
    xmlProjection = 'infoset-element-order-attribute-ordinal'
    jsonSetFields = @('/libraries/*/files', '/project/restore/configFilePaths')
    unknownJsonArrayPolicy = 'preserve-sequence'
    nugetCacheRepresentation = 'root-relative-path-plus-package-content-sha256'
    dotnetToolchainRepresentation = 'root-relative-path-plus-distribution-content-sha256'
    absoluteRootTokens = @('$DOTNET_ROOT', '$NUGET_PACKAGES', '$REPOSITORY')
}
Assert-JsonEqual $compilation.authorityProjectionPolicy $expectedAuthorityProjectionPolicy `
    'MSBuild authority projection policy drifted.'
Assert-Equal $compilation.authorityInputCount $recordedAuthorityInputs.Count 'MSBuild authority input count mismatch'
Assert-IndependentAuthorityInventoriesEqual `
    -Recorded ([object[]]$recordedAuthorityInputs) `
    -Recomputed ([object[]]$independentAuthorityInputs)

$pluginSourcePaths = @($pluginSources | ForEach-Object { [string]$_.path })
Assert-SortedUniqueStrings $pluginSourcePaths 'MSBuild plugin source inventory'
foreach ($source in $pluginSources) {
    Assert-RepositoryRelativePath ([string]$source.path) 'MSBuild Compile source'
    if (-not ([string]$source.path).StartsWith("$pluginRootRelative/", [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-LEDGER-001 MSBuild plugin source escapes the plugin project root.'
    }
    $sourcePath = Resolve-RepositoryPath ([string]$source.path)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 recorded plugin source does not exist: $($source.path)"
    }
    Assert-Equal $source.sha256 (Get-Sha256 $sourcePath) "plugin source digest mismatch: $($source.path)"
    if ($isCanonicalLedger) {
        Assert-TrackedAuthorityRegularBlob -Commit ([string]$ledger.sourceState.head) -WorktreePath $sourcePath
    }
}
$actualStaticPluginSources = @(Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter '*.cs' |
    Where-Object {
        $relative = [IO.Path]::GetRelativePath($pluginRoot, $_.FullName).Replace('\', '/')
        -not $relative.StartsWith('bin/', [StringComparison]::Ordinal) -and
            -not $relative.StartsWith('obj/', [StringComparison]::Ordinal)
    } | ForEach-Object {
        [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName).Replace('\', '/')
    } | Sort-Ordinal -Unique)
if (($pluginSourcePaths -join "`n") -cne ($actualStaticPluginSources -join "`n")) {
    throw 'EDGE-SPLIT-LEDGER-001 plugin source inventory is not an exact projection of current static Compile sources.'
}
$generatedSourcePaths = @($generatedSources | ForEach-Object { [string]$_.path })
Assert-SortedUniqueStrings $generatedSourcePaths 'generated source inventory'
foreach ($source in $generatedSources) {
    if (-not ([string]$source.path).StartsWith('generated/', [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-LEDGER-001 generated source inventory must use the generated/ virtual root.'
    }
}
$actualPluginSourceInventory = @($validatorCompileSourcePaths | Where-Object {
        $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $_).Replace('\', '/')
        $relative.StartsWith("$pluginRootRelative/", [StringComparison]::Ordinal) -and
            -not $relative.Contains('/obj/', [StringComparison]::Ordinal)
    } | ForEach-Object {
        [pscustomobject][ordered]@{
            path = [IO.Path]::GetRelativePath($RepositoryRoot, $_).Replace('\', '/')
            sha256 = Get-Sha256 $_
        }
    } | Sort-Ordinal path)
Assert-JsonEqual $pluginSources $actualPluginSourceInventory 'plugin source inventory differs from independent raw MSBuild Compile inputs.'
$actualGeneratedSourceInventory = @($validatorGeneratedSourcePaths | ForEach-Object {
        [pscustomobject][ordered]@{
            path = "generated/$([IO.Path]::GetRelativePath($validatorGeneratedRoot, $_).Replace('\', '/'))"
            sha256 = Get-Sha256 $_
        }
    } | Sort-Ordinal path)
Assert-JsonEqual $generatedSources $actualGeneratedSourceInventory 'generated source inventory differs from independent forced-build output.'
$actualCompilationInputs = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($path in $validatorCompileSourcePaths) {
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
    if (-not $actualCompilationInputs.TryAdd($relative, [pscustomobject][ordered]@{ path = $relative; sha256 = Get-Sha256 $path })) {
        throw "EDGE-SPLIT-LEDGER-001 independent MSBuild Compile inputs contain a duplicate path: $relative."
    }
}
foreach ($source in $actualGeneratedSourceInventory) {
    if (-not $actualCompilationInputs.TryAdd([string]$source.path, $source)) {
        throw "EDGE-SPLIT-LEDGER-001 independent generated source path collides with a Compile input: $($source.path)."
    }
}
$actualCompilationInputInventory = @($actualCompilationInputs.Values | Sort-Ordinal path)
Assert-JsonEqual $compilationInputs $actualCompilationInputInventory `
    'complete Roslyn input inventory differs from independent forced build/MSBuild evaluation.'
$compilationInputPaths = @($compilationInputs | ForEach-Object { [string]$_.path })
Assert-SortedUniqueStrings $compilationInputPaths 'complete Roslyn compilation input inventory'
foreach ($source in $compilationInputs) {
    Assert-RepositoryRelativePath ([string]$source.path) 'complete compilation input'
    if (-not ([string]$source.path).StartsWith('generated/', [StringComparison]::Ordinal)) {
        $sourcePath = Resolve-RepositoryPath ([string]$source.path)
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 compilation input does not exist: $($source.path)"
        }
        Assert-Equal $source.sha256 (Get-Sha256 $sourcePath) "compilation input digest mismatch: $($source.path)"
    }
}
$peLayer = $ledger.dependencyLayers.peAssemblyReferences
$peInputFacts = @($peLayer.inputs)
if ($peInputFacts.Count -lt 1) { throw 'EDGE-SPLIT-LEDGER-001 PE layer must record at least the built entry assembly bytes.' }
$verifiedPluginOwnedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$actualPeInputFacts = [Collections.Generic.List[object]]::new()
foreach ($inputFact in $peInputFacts) {
    Assert-RepositoryRelativePath ([string]$inputFact.path) 'PE assembly input'
    $inputPath = Resolve-RepositoryPath ([string]$inputFact.path)
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 PE input bytes do not exist: $($inputFact.path)"
    }
    Assert-IndependentEmbeddedDebugIdentity -AssemblyPath $inputPath
    $actualFact = Get-ManagedAssemblyFact -PathValue $inputPath -RecordedPath ([string]$inputFact.path) -VerifiedPluginOwned $true
    Assert-JsonEqual $inputFact $actualFact "PE input identity/size/SHA/MVID differs from raw bytes: $($inputFact.path)."
    if (-not ([string]$actualFact.assemblyName).StartsWith('IIoT.Edge.Module.', [StringComparison]::Ordinal) -or
        (Test-OrdinalEqualsAny ([string]$actualFact.assemblyName) @('IIoT.Edge.Module.Sdk', 'IIoT.Edge.Module.Contracts', 'IIoT.Edge.Module.Analyzers')) -or
        -not $verifiedPluginOwnedNames.Add([string]$actualFact.assemblyName)) {
        throw "EDGE-SPLIT-LEDGER-001 PE input does not establish a unique non-reserved plugin-owned identity: $($actualFact.assemblyName)."
    }
    $actualPeInputFacts.Add($actualFact)
}
$entryAssemblyName = [IO.Path]::GetFileNameWithoutExtension([string]$manifest.entryAssembly)
if (-not $verifiedPluginOwnedNames.Contains($entryAssemblyName) -or
    [string]$compilation.assemblyName -cne $entryAssemblyName) {
    throw 'EDGE-SPLIT-LEDGER-001 manifest/MSBuild entry identity is not backed by verified entry assembly bytes.'
}
$actualPeInputPaths = @($peInputFacts | ForEach-Object { Resolve-RepositoryPath ([string]$_.path) })
$entryAssemblyPath = @($actualPeInputPaths | Where-Object {
        [Reflection.AssemblyName]::GetAssemblyName($_).Name -ceq $entryAssemblyName
    })
if ($entryAssemblyPath.Count -ne 1) {
    throw 'EDGE-SPLIT-LEDGER-001 independently rebuilt PE inputs must contain exactly one entry assembly path.'
}
$independentPackageStaticInputs = @(Get-IndependentPackageStaticInputs `
    -PluginRoot $pluginRoot `
    -TargetAssemblyPath $entryAssemblyPath[0] `
    -ManifestSourcePath $manifestPath `
    -PluginOwnedAssemblyPaths ([string[]]$actualPeInputPaths))

$pathComparer = if ([OperatingSystem]::IsWindows()) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$projectAuthorityByAssembly = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$projectAuthorityByOutputPath = [Collections.Generic.Dictionary[string, object]]::new($pathComparer)
$projectAuthorityByProjectPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$projectCaseGuard = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$actualProjectItems = @($validatorEvaluation.Items.ProjectReference | ForEach-Object {
        $fullPath = [string]$_.FullPath
        if ([string]::IsNullOrWhiteSpace($fullPath) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw 'EDGE-SPLIT-LEDGER-001 independent ProjectReference lacks an existing regular project file.'
        }
        $fullPath = [IO.Path]::GetFullPath($fullPath)
        Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $fullPath
        if ($projectCaseGuard.ContainsKey($fullPath)) {
            if ([string]$projectCaseGuard[$fullPath] -cne $fullPath) {
                throw "EDGE-SPLIT-LEDGER-001 independent ProjectReference paths collide under Windows case semantics: $($projectCaseGuard[$fullPath]) | $fullPath."
            }
            return
        }
        $projectCaseGuard.Add($fullPath, $fullPath)
        $projectEvaluation = (Invoke-CapturedCommand dotnet @(
                'msbuild', $fullPath, '-nologo', '-noAutoResponse',
                "-p:Configuration=$([string]$compilation.configuration)",
                "-p:SourceRevisionId=$([string]$ledger.sourceState.head)",
                "-p:RepositoryCommit=$([string]$ledger.sourceState.head)",
                '-getProperty:AssemblyName,TargetPath')) | ConvertFrom-Json -Depth 30
        $projectName = [string]$projectEvaluation.Properties.AssemblyName
        $outputValue = [string]$projectEvaluation.Properties.TargetPath
        if ([string]::IsNullOrWhiteSpace($projectName) -or [string]::IsNullOrWhiteSpace($outputValue)) {
            throw "EDGE-SPLIT-LEDGER-001 independent ProjectReference lacks evaluated assembly/output identity: $fullPath."
        }
        $outputPath = [IO.Path]::GetFullPath($outputValue)
        if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 independent ProjectReference output bytes do not exist: $outputPath."
        }
        Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $outputPath
        $projectRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $fullPath).Replace('\', '/')
        $outputRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $outputPath).Replace('\', '/')
        $ownerFamily = Get-InternalProjectOwnerFamily -ProjectName $projectName -ProjectPath $projectRelativePath
        if ($verifiedPluginOwnedNames.Contains($projectName) -and $projectName -cne $entryAssemblyName) {
            if (-not $projectRelativePath.StartsWith("src/Modules/$entryAssemblyName.", [StringComparison]::Ordinal)) {
                throw 'EDGE-SPLIT-LEDGER-001 additional plugin-owned project is outside the entry plugin build closure.'
            }
            $ownerFamily = 'PluginOwned'
        }
        if ($ownerFamily -ceq 'Unknown') {
            throw "EDGE-SPLIT-LEDGER-001 raw ProjectReference has no path-bound owner authority: $projectRelativePath."
        }
        $authority = [pscustomobject][ordered]@{
            projectPath = $projectRelativePath
            projectFullPath = $fullPath
            outputPath = $outputRelativePath
            outputFullPath = $outputPath
            assemblyName = $projectName
            ownerFamily = $ownerFamily
        }
        if ($projectAuthorityByAssembly.ContainsKey($projectName) -and
            [string]$projectAuthorityByAssembly[$projectName].projectPath -cne $projectRelativePath) {
            throw "EDGE-SPLIT-LEDGER-001 raw ProjectReference assembly identity is ambiguous: $projectName."
        }
        if (-not $projectAuthorityByAssembly.ContainsKey($projectName)) { $projectAuthorityByAssembly.Add($projectName, $authority) }
        if ($projectAuthorityByOutputPath.ContainsKey($outputPath)) {
            throw "EDGE-SPLIT-LEDGER-001 independent ProjectReference outputs are ambiguous: $outputPath."
        }
        $projectAuthorityByOutputPath.Add($outputPath, $authority)
        $projectAuthorityByProjectPath.Add($projectRelativePath, $authority)
        $referenceOutputAssemblyText = Get-OptionalProperty $_ 'ReferenceOutputAssembly'
        $definingProject = Get-OptionalProperty $_ 'DefiningProjectFullPath'
        [pscustomobject][ordered]@{
            projectPath = $projectRelativePath
            projectName = $projectName
            ownerFamily = $ownerFamily
            direct = -not [string]::IsNullOrWhiteSpace($definingProject) -and
                (Test-IndependentPathIdentityEqual $definingProject $pluginProjectPath)
            referenceOutputAssembly = $referenceOutputAssemblyText -cne 'false'
            outputItemType = Get-OptionalProperty $_ 'OutputItemType'
            forbiddenForSourceLayer = (Test-ExpectedSourceForbiddenFamily $ownerFamily) -and $referenceOutputAssemblyText -cne 'false'
        }
    } | Sort-Ordinal projectPath, direct -Unique)
$referencePathSet = [Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($path in $validatorReferencePaths) { [void]$referencePathSet.Add([IO.Path]::GetFullPath($path)) }
foreach ($inputFact in $actualPeInputFacts) {
    if ([string]$inputFact.assemblyName -ceq $entryAssemblyName) { continue }
    $inputPath = Resolve-RepositoryPath ([string]$inputFact.path)
    if (-not $referencePathSet.Contains([IO.Path]::GetFullPath($inputPath)) -or
        -not $projectAuthorityByAssembly.ContainsKey([string]$inputFact.assemblyName) -or
        -not (Test-IndependentPathIdentityEqual `
            ([string]$projectAuthorityByAssembly[[string]$inputFact.assemblyName].outputFullPath) $inputPath) -or
        [string]$projectAuthorityByAssembly[[string]$inputFact.assemblyName].ownerFamily -cne 'PluginOwned') {
        throw 'EDGE-SPLIT-LEDGER-001 additional plugin-owned bytes are not an exact current ReferencePath/ProjectReference output.'
    }
}

$validatorAssetsPath = [string]$validatorEvaluation.Properties.ProjectAssetsFile
if ([string]::IsNullOrWhiteSpace($validatorAssetsPath) -or
    -not (Test-Path -LiteralPath $validatorAssetsPath -PathType Leaf)) {
    throw 'EDGE-SPLIT-LEDGER-001 independent plugin project.assets.json is missing.'
}
Assert-IndependentAuthorityPath -DeclaredRoot $RepositoryRoot -CandidatePath $validatorAssetsPath
$validatorAssetsRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $validatorAssetsPath).Replace('\', '/')
$validatorAssets = Get-Content -LiteralPath $validatorAssetsPath -Raw | ConvertFrom-Json -Depth 100
$validatorTargetingPackRoot = [string]$validatorEvaluation.Properties.NetCoreTargetingPackRoot
if ([string]::IsNullOrWhiteSpace($validatorTargetingPackRoot) -or
    -not (Test-Path -LiteralPath $validatorTargetingPackRoot -PathType Container)) {
    throw 'EDGE-SPLIT-LEDGER-001 independent targeting-pack root is unavailable.'
}
Assert-IndependentAuthorityPath -DeclaredRoot $validatorTargetingPackRoot -CandidatePath $validatorTargetingPackRoot

$referenceItemByPath = [Collections.Generic.Dictionary[string, object]]::new($pathComparer)
$referenceCaseGuard = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in @($validatorEvaluation.Items.ReferencePath)) {
    $fullPath = [IO.Path]::GetFullPath([string]$item.FullPath)
    if ($referenceCaseGuard.ContainsKey($fullPath)) {
        if ([string]$referenceCaseGuard[$fullPath] -cne $fullPath) {
            throw "EDGE-SPLIT-LEDGER-001 independent ReferencePath inputs collide under Windows case semantics: $($referenceCaseGuard[$fullPath]) | $fullPath."
        }
        throw "EDGE-SPLIT-LEDGER-001 independent ReferencePath has duplicate raw authority: $fullPath."
    }
    $referenceCaseGuard.Add($fullPath, $fullPath)
    $referenceItemByPath.Add($fullPath, $item)
}
$referenceOwnerByAssembly = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$referenceFactByIdentity = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$actualReferenceFacts = [Collections.Generic.List[object]]::new()
foreach ($path in $validatorReferencePaths) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $referenceItemByPath.ContainsKey($fullPath)) {
        throw 'EDGE-SPLIT-LEDGER-001 raw ReferencePath bytes lack their MSBuild authority item.'
    }
    $item = $referenceItemByPath[$fullPath]
    $rawFact = Get-ManagedAssemblyFact -PathValue $fullPath -RecordedPath '' -VerifiedPluginOwned $false
    $nugetId = Get-OptionalProperty $item 'NuGetPackageId'
    $nugetVersion = Get-OptionalProperty $item 'NuGetPackageVersion'
    $pathInPackage = Get-OptionalProperty $item 'PathInPackage'
    $frameworkName = Get-OptionalProperty $item 'FrameworkReferenceName'
    $frameworkVersion = Get-OptionalProperty $item 'FrameworkReferenceVersion'
    $sourceProject = Get-OptionalProperty $item 'MSBuildSourceProjectFile'
    $sourceTarget = Get-OptionalProperty $item 'ReferenceSourceTarget'
    $origin = ''
    $provenance = $null
    $ownerFamily = 'Unknown'
    if ($projectAuthorityByOutputPath.ContainsKey($fullPath)) {
        $projectAuthority = $projectAuthorityByOutputPath[$fullPath]
        if ([string]$rawFact.assemblyName -cne [string]$projectAuthority.assemblyName -or
            $sourceTarget -cne 'ProjectReference' -or [string]::IsNullOrWhiteSpace($sourceProject) -or
            -not (Test-IndependentPathIdentityEqual $sourceProject ([string]$projectAuthority.projectFullPath))) {
            throw "EDGE-SPLIT-LEDGER-001 independent repository reference is not exact ProjectReference output: $fullPath."
        }
        $origin = "repository:$([string]$projectAuthority.outputPath)"
        $provenance = [pscustomobject][ordered]@{
            kind = 'project-reference'
            projectPath = [string]$projectAuthority.projectPath
            outputPath = [string]$projectAuthority.outputPath
        }
        $ownerFamily = [string]$projectAuthority.ownerFamily
    }
    elseif (Test-IndependentPathWithinRoot -RootPath $RepositoryRoot -CandidatePath $fullPath) {
        throw "EDGE-SPLIT-LEDGER-001 independent repository reference lacks exact ProjectReference owner: $fullPath."
    }
    elseif (-not [string]::IsNullOrWhiteSpace($frameworkName)) {
        $provenance = Get-IndependentFrameworkReferenceProvenance -ReferenceItem $item `
            -AssemblyPath $fullPath -TargetingPackRoot $validatorTargetingPackRoot
        $origin = "framework:$frameworkName/$frameworkVersion"
        $ownerFamily = 'PlatformOrThirdParty'
    }
    elseif (-not [string]::IsNullOrWhiteSpace($nugetId) -and
        -not [string]::IsNullOrWhiteSpace($pathInPackage)) {
        $provenance = Get-IndependentNuGetReferenceProvenance -ReferenceItem $item `
            -AssemblyPath $fullPath -RestoreAssets $validatorAssets `
            -RestoreAssetsPath $validatorAssetsRelativePath -PackagesRoot $validatorNugetRoot
        $origin = "nuget:$nugetId/$nugetVersion"
        $ownerFamily = 'PlatformOrThirdParty'
    }
    if ($null -eq $provenance -or $ownerFamily -ceq 'Unknown') {
        throw "EDGE-SPLIT-LEDGER-001 independent reference lacks exact project/restore/framework authority: $($rawFact.assemblyName)|$fullPath."
    }
    $fact = [pscustomobject][ordered]@{
        assemblyName = [string]$rawFact.assemblyName
        assemblyVersion = [string]$rawFact.assemblyVersion
        culture = [string]$rawFact.culture
        publicKeyToken = [string]$rawFact.publicKeyToken
        mvid = [string]$rawFact.mvid
        size = [long]$rawFact.size
        origin = $origin
        provenance = $provenance
        sha256 = [string]$rawFact.sha256
        ownerFamily = $ownerFamily
    }
    $identityKey = Get-AssemblyIdentityKey $fact
    if (-not $referenceFactByIdentity.TryAdd($identityKey, $fact) -or
        $referenceOwnerByAssembly.ContainsKey([string]$fact.assemblyName)) {
        throw "EDGE-SPLIT-LEDGER-001 raw resolved reference identity/simple name is ambiguous: $identityKey."
    }
    $referenceOwnerByAssembly.Add([string]$fact.assemblyName, $ownerFamily)
    $actualReferenceFacts.Add($fact)
}
foreach ($fact in $actualPeInputFacts) {
    if (-not $referenceOwnerByAssembly.TryAdd([string]$fact.assemblyName, 'PluginOwned')) {
        throw 'EDGE-SPLIT-LEDGER-001 plugin-owned identity collides with an external reference.'
    }
}
$actualReferenceFactsSorted = @($actualReferenceFacts.ToArray() |
    Sort-Ordinal assemblyName, assemblyVersion, culture, publicKeyToken, origin)
Assert-JsonEqual $references $actualReferenceFactsSorted 'reference assembly full identities/bytes/origins differ from independent raw MSBuild evaluation.'

$actualPageInventory = [Collections.Generic.List[object]]::new()
$actualResourceInventory = [Collections.Generic.List[object]]::new()
foreach ($xamlFile in @(Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter '*.axaml' |
        Where-Object {
            $relative = [IO.Path]::GetRelativePath($pluginRoot, $_.FullName).Replace('\', '/')
            -not $relative.StartsWith('bin/', [StringComparison]::Ordinal) -and
                -not $relative.StartsWith('obj/', [StringComparison]::Ordinal)
        } | Sort-Ordinal FullName)) {
    [xml]$xaml = Get-Content -LiteralPath $xamlFile.FullName -Raw
    $namespaceManager = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
    $namespaceManager.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $xamlFile.FullName).Replace('\', '/')
    $xamlSize = [long]$xamlFile.Length
    $xamlSha256 = Get-Sha256 $xamlFile.FullName
    foreach ($node in @($xaml.SelectNodes('//*[@x:Key]', $namespaceManager))) {
        $actualResourceInventory.Add([pscustomobject][ordered]@{
            sourcePath = $relativePath
            sourceSize = $xamlSize
            sourceSha256 = $xamlSha256
            key = [string]$node.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
            valueType = [string]$node.LocalName
        })
    }
    $className = [string]$xaml.DocumentElement.GetAttribute('Class', 'http://schemas.microsoft.com/winfx/2006/xaml')
    if (-not [string]::IsNullOrWhiteSpace($className)) {
        $codeBehindPath = "$($xamlFile.FullName).cs"
        if (-not (Test-Path -LiteralPath $codeBehindPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 XAML page lacks its required code-behind: $relativePath"
        }
        $actualPageInventory.Add([pscustomobject][ordered]@{
            sourcePath = $relativePath
            sourceSize = $xamlSize
            sourceSha256 = $xamlSha256
            className = $className
            codeBehindPath = [IO.Path]::GetRelativePath($RepositoryRoot, $codeBehindPath).Replace('\', '/')
            codeBehindSize = [long](Get-Item -LiteralPath $codeBehindPath).Length
            codeBehindSha256 = Get-Sha256 $codeBehindPath
        })
    }
}
Assert-JsonEqual @($ledger.pageInventory) @($actualPageInventory) 'page inventory is not an exact projection of current XAML sources.'
Assert-JsonEqual @($ledger.resourceInventory) @($actualResourceInventory | Sort-Ordinal sourcePath, key, valueType) 'resource inventory is not an exact projection of current XAML sources.'

$views = @($ledger.viewInventory)
$viewProperties = @($views | ForEach-Object { [string]$_.propertyName })
Assert-SortedUniqueStrings $viewProperties 'view inventory'
$viewIdsAssemblyPath = Resolve-RepositoryPath ([string]$compilation.viewIdsAssemblyPath)
if (-not (Test-Path -LiteralPath $viewIdsAssemblyPath -PathType Leaf)) {
    throw 'EDGE-SPLIT-LEDGER-001 deterministic rebuild closure did not produce the recorded view-ID contract assembly.'
}
$viewIdsAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($viewIdsAssemblyPath)
$viewIdsReference = @($references | Where-Object {
        [string]$_.assemblyName -ceq [string]$viewIdsAssemblyName.Name -and
            [string]$_.assemblyVersion -ceq [string]$viewIdsAssemblyName.Version
    })
if ($viewIdsReference.Count -ne 1) {
    throw 'EDGE-SPLIT-LEDGER-001 view-ID contract assembly must have one exact repository reference identity.'
}
Assert-Equal $viewIdsReference[0].sha256 (Get-Sha256 $viewIdsAssemblyPath) 'view-ID contract assembly digest mismatch'
$viewIdsAssembly = [Reflection.Assembly]::LoadFrom($viewIdsAssemblyPath)
$viewIdsType = $viewIdsAssembly.GetType([string]$compilation.viewIdsTypeName, $true, $false)
$createViewIds = $viewIdsType.GetMethod('Create', [Reflection.BindingFlags]'Public,Static')
if ($null -eq $createViewIds) {
    throw 'EDGE-SPLIT-LEDGER-001 view-ID contract type lacks its public static Create method.'
}
$viewIds = $createViewIds.Invoke($null, @([string]$manifest.moduleId))
$registrationSource = [string]($usages | Where-Object {
        [string]$_.symbol -like "*$([string]$compilation.viewIdsTypeName)*"
    } | Select-Object -First 1 -ExpandProperty sourcePath)
$expectedViews = @($viewIdsType.GetProperties([Reflection.BindingFlags]'Public,Instance') |
    Sort-Ordinal Name |
    ForEach-Object {
        [pscustomobject][ordered]@{
            viewId = [string]$_.GetValue($viewIds)
            propertyName = [string]$_.Name
            registrationSource = $registrationSource
            viewOwner = if ([string]$_.Name -eq 'DataView') { 'plugin-custom-page' } else { 'host-standard-page' }
        }
    })
Assert-JsonEqual $views $expectedViews 'view inventory is not an exact dynamic projection of the recorded view-ID contract assembly and current registration source.'
foreach ($view in $views) {
    if ([string]::IsNullOrWhiteSpace([string]$view.registrationSource) -or
        -not ([Collections.Generic.HashSet[string]]::new([string[]]$pluginSourcePaths, [StringComparer]::Ordinal)).Contains([string]$view.registrationSource)) {
        throw "EDGE-SPLIT-LEDGER-001 view registration is not bound to a recorded plugin source: $($view.viewId)"
    }
}

$usages = @($ledger.externalSymbolUsages)
if ($usages.Count -eq 0 -or $usages.Count -ne [int]$ledger.summary.externalSymbolUsageCount) {
    throw 'EDGE-SPLIT-LEDGER-001 external symbol usage count is empty or stale.'
}
$compilationInputPathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $compilationInputPaths) { [void]$compilationInputPathSet.Add($path) }
$carry020BaselineKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$carry030BaselineKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($item in @($ledger.carrySets.'EDGE-SPLIT-020'.baselineItems)) { [void]$carry020BaselineKeys.Add((Get-CarryKey $item)) }
foreach ($item in @($ledger.carrySets.'EDGE-SPLIT-030'.baselineItems)) { [void]$carry030BaselineKeys.Add((Get-CarryKey $item)) }

$sdkDirectory = $validatorSdkDirectory
$csharpCompilerPath = $validatorCompilerPath
if (-not (Test-Path -LiteralPath $csharpCompilerPath -PathType Leaf)) {
    throw 'EDGE-SPLIT-LEDGER-001 independent validator cannot locate the SDK Roslyn compiler.'
}
$validatorRoslynAssemblyPath = Join-Path $validatorTemporaryRoot 'EdgePluginContractLedger.ValidatorRoslyn.dll'
$validatorCompilerArguments = [Collections.Generic.List[string]]::new()
foreach ($argument in @($csharpCompilerPath, '/nologo', '/target:library', '/langversion:preview', '/nullable:enable',
        '/deterministic', "/out:$validatorRoslynAssemblyPath")) {
    $validatorCompilerArguments.Add($argument)
}
$roslynRuntimeReferences = @(
    [Microsoft.CodeAnalysis.Compilation].Assembly.Location,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilation].Assembly.Location
)
foreach ($referencePath in @($validatorReferencePaths + $roslynRuntimeReferences | Sort-Ordinal -Unique)) {
    $validatorCompilerArguments.Add("/reference:$referencePath")
}
$validatorCompilerArguments.Add($validatorRoslynHelperPath)
[void](Invoke-CapturedCommand dotnet ([string[]]$validatorCompilerArguments))
[void][Reflection.Assembly]::LoadFrom($validatorRoslynAssemblyPath)
$validatorAnalysis = [IIoT.Edge.ContractLedger.Validation.EdgePluginContractLedgerValidatorRoslyn]::Analyze(
    [string]$compilation.assemblyName,
    $RepositoryRoot,
    $validatorGeneratedRoot,
    $validatorCompileSourcePaths,
    $validatorGeneratedSourcePaths,
    $validatorReferencePaths,
    $validatorPreprocessorSymbols)
if ($validatorAnalysis.CompilationErrors.Count -ne 0) {
    throw "EDGE-SPLIT-LEDGER-001 independent validator Roslyn compilation failed:`n$(@($validatorAnalysis.CompilationErrors | Select-Object -First 30) -join "`n")"
}
$independentRawUsages = @($validatorAnalysis.SymbolUsages | ForEach-Object {
        $identityKey = "$([string]$_.OwnerAssembly), Version=$([string]$_.OwnerAssemblyVersion), Culture=$([string]$_.OwnerAssemblyCulture), PublicKeyToken=$([string]$_.OwnerAssemblyPublicKeyToken)"
        if (-not $referenceFactByIdentity.ContainsKey($identityKey)) {
            throw "EDGE-SPLIT-LEDGER-001 independent Roslyn symbol owner lacks exact raw ReferencePath identity: $identityKey."
        }
        [pscustomobject][ordered]@{
            sourcePath = [string]$_.SourcePath
            line = [int]$_.Line
            column = [int]$_.Column
            symbol = [string]$_.Symbol
            symbolKind = [string]$_.SymbolKind
            ownerAssembly = [string]$_.OwnerAssembly
            ownerAssemblyVersion = [string]$_.OwnerAssemblyVersion
            ownerAssemblyCulture = [string]$_.OwnerAssemblyCulture
            ownerAssemblyPublicKeyToken = [string]$_.OwnerAssemblyPublicKeyToken
            containingNamespace = [string]$_.ContainingNamespace
            usageKind = [string]$_.UsageKind
        }
    })
$recordedRawUsages = @($usages | ForEach-Object {
        [pscustomobject][ordered]@{
            sourcePath = [string]$_.sourcePath
            line = [int]$_.line
            column = [int]$_.column
            symbol = [string]$_.symbol
            symbolKind = [string]$_.symbolKind
            ownerAssembly = [string]$_.ownerAssembly
            ownerAssemblyVersion = [string]$_.ownerAssemblyVersion
            ownerAssemblyCulture = [string]$_.ownerAssemblyCulture
            ownerAssemblyPublicKeyToken = [string]$_.ownerAssemblyPublicKeyToken
            containingNamespace = [string]$_.containingNamespace
            usageKind = [string]$_.usageKind
        }
    })
$recordedRawJson = @($recordedRawUsages | ForEach-Object { $_ | ConvertTo-Json -Compress })
$independentRawJson = @($independentRawUsages | ForEach-Object { $_ | ConvertTo-Json -Compress })
if (($recordedRawJson -join "`n") -cne ($independentRawJson -join "`n")) {
    $firstDifference = 0
    $sharedCount = [Math]::Min($recordedRawJson.Count, $independentRawJson.Count)
    while ($firstDifference -lt $sharedCount -and
        $recordedRawJson[$firstDifference] -ceq $independentRawJson[$firstDifference]) { $firstDifference++ }
    $recordedDifference = if ($firstDifference -lt $recordedRawJson.Count) { $recordedRawJson[$firstDifference] } else { '<end>' }
    $independentDifference = if ($firstDifference -lt $independentRawJson.Count) { $independentRawJson[$firstDifference] } else { '<end>' }
    throw "EDGE-SPLIT-LEDGER-001 external symbol usage set differs from the independent raw Roslyn semantic scan: recorded=$($recordedRawJson.Count) independent=$($independentRawJson.Count) firstIndex=$firstDifference`nrecorded=$recordedDifference`nindependent=$independentDifference"
}

$actualUsageProjection = [Collections.Generic.List[object]]::new()
for ($usageIndex = 0; $usageIndex -lt $usages.Count; $usageIndex++) {
    $usage = $usages[$usageIndex]
    $independentRawUsage = $independentRawUsages[$usageIndex]
    Assert-RepositoryRelativePath ([string]$usage.sourcePath) 'external symbol usage'
    if (-not $compilationInputPathSet.Contains([string]$usage.sourcePath)) {
        throw "EDGE-SPLIT-LEDGER-001 external symbol usage is not bound to a complete Compile input: $($usage.sourcePath)."
    }
    foreach ($field in @('symbol', 'symbolKind', 'ownerAssembly', 'usageKind', 'ownerFamily', 'classification', 'disposition', 'replacementContract', 'protectionTest')) {
        if ([string]::IsNullOrWhiteSpace([string]$usage.$field)) { throw "EDGE-SPLIT-LEDGER-001 external symbol usage is missing '$field'." }
    }
    $expectedOwnerFamily = Get-ExpectedOwnerFamily ([string]$independentRawUsage.ownerAssembly) $referenceOwnerByAssembly
    $expectedForbidden = Test-ExpectedSourceForbiddenFamily $expectedOwnerFamily
    if ([string]$usage.ownerFamily -cne $expectedOwnerFamily -or
        [bool]$usage.forbiddenForSourceLayer -ne $expectedForbidden -or
        $expectedOwnerFamily -ceq 'Unknown' -or [string]$usage.classification -ceq 'unclassified' -or [string]$usage.disposition -ceq 'unclassified') {
        throw 'EDGE-SPLIT-LEDGER-001 external symbol usage remains unclassified.'
    }
    $expectedDisposition = Get-IndependentDisposition -Usage $independentRawUsage -BatchRank $batchRank `
        -Carry020Keys $carry020BaselineKeys -Carry030Keys $carry030BaselineKeys -OwnerFamily $expectedOwnerFamily
    $recordedDisposition = [pscustomobject][ordered]@{
        ownerFamily = [string]$usage.ownerFamily
        classification = [string]$usage.classification
        disposition = [string]$usage.disposition
        removalBatch = if ($null -eq $usage.removalBatch) { $null } else { [string]$usage.removalBatch }
        replacementContract = [string]$usage.replacementContract
        protectionTest = [string]$usage.protectionTest
        forbiddenForSourceLayer = [bool]$usage.forbiddenForSourceLayer
    }
    Assert-JsonEqual $recordedDisposition $expectedDisposition `
        "external symbol disposition was not independently derived: $($usage.ownerAssembly)|$($usage.symbol)."
    $expectedRemovalBatch = $expectedDisposition.removalBatch
    $recordedRemovalBatch = if ($null -eq $usage.removalBatch) { $null } else { [string]$usage.removalBatch }
    if ($recordedRemovalBatch -cne $expectedRemovalBatch) {
        throw "EDGE-SPLIT-LEDGER-001 external symbol removal semantics were not independently derived: $($usage.ownerAssembly)|$($usage.symbol)."
    }
    if ((Test-OrdinalEqualsAny $expectedOwnerFamily @('SdkContract', 'UiShared', 'Analyzer', 'PluginOwned', 'PlatformOrThirdParty')) -and
        ($null -ne $recordedRemovalBatch -or [bool]$usage.forbiddenForSourceLayer)) {
        throw 'EDGE-SPLIT-LEDGER-001 legal SDK/UI/analyzer/plugin/platform consumer usage cannot be treated as a removal residual.'
    }
    $actualUsageProjection.Add([pscustomobject][ordered]@{
            sourcePath = [string]$independentRawUsage.sourcePath
            line = [int]$independentRawUsage.line
            column = [int]$independentRawUsage.column
            symbol = [string]$independentRawUsage.symbol
            symbolKind = [string]$independentRawUsage.symbolKind
            ownerAssembly = [string]$independentRawUsage.ownerAssembly
            ownerAssemblyVersion = [string]$independentRawUsage.ownerAssemblyVersion
            ownerAssemblyCulture = [string]$independentRawUsage.ownerAssemblyCulture
            ownerAssemblyPublicKeyToken = [string]$independentRawUsage.ownerAssemblyPublicKeyToken
            containingNamespace = [string]$independentRawUsage.containingNamespace
            usageKind = [string]$independentRawUsage.usageKind
            ownerFamily = [string]$expectedDisposition.ownerFamily
            classification = [string]$expectedDisposition.classification
            disposition = [string]$expectedDisposition.disposition
            removalBatch = $expectedDisposition.removalBatch
            replacementContract = [string]$expectedDisposition.replacementContract
            protectionTest = [string]$expectedDisposition.protectionTest
            forbiddenForSourceLayer = [bool]$expectedDisposition.forbiddenForSourceLayer
        })
}

$projectLayer = $ledger.dependencyLayers.evaluatedProjectReferences
$projectItems = @($projectLayer.items)
Assert-JsonEqual $projectItems $actualProjectItems 'evaluated ProjectReference items/families/flags differ from independent MSBuild evaluation.'
$projectForbidden = @($projectItems | Where-Object forbiddenForSourceLayer)
$projectUnknown = @($projectItems | Where-Object ownerFamily -eq 'Unknown')
Assert-Equal $projectLayer.inputProject $ledger.msbuildCompilation.projectPath 'evaluated ProjectReference input project mismatch'
Assert-RepositoryRelativePath ([string]$projectLayer.inputProject) 'evaluated ProjectReference input project'
Assert-Equal $projectLayer.totalCount $projectItems.Count 'evaluated ProjectReference total mismatch'
Assert-Equal $projectLayer.forbiddenCount $projectForbidden.Count 'evaluated ProjectReference forbidden count mismatch'
Assert-Equal $projectLayer.unknownAssemblyCount $projectUnknown.Count 'evaluated ProjectReference unknown count mismatch'
Assert-CountMap $projectForbidden @($projectLayer.forbiddenCountByOwnerFamily) 'evaluated ProjectReference layer'
foreach ($item in $projectItems) {
    Assert-RepositoryRelativePath ([string]$item.projectPath) 'evaluated ProjectReference'
    $expectedFamily = if ($projectAuthorityByAssembly.ContainsKey([string]$item.projectName)) {
        [string]$projectAuthorityByAssembly[[string]$item.projectName].ownerFamily
    }
    else { 'Unknown' }
    if ([string]$item.ownerFamily -cne $expectedFamily -or $expectedFamily -ceq 'Unknown' -or
        [bool]$item.forbiddenForSourceLayer -ne ((Test-ExpectedSourceForbiddenFamily $expectedFamily) -and [bool]$item.referenceOutputAssembly)) {
        throw 'EDGE-SPLIT-LEDGER-001 evaluated ProjectReference owner/forbidden classification is not independently valid.'
    }
}

$roslynLayer = $ledger.dependencyLayers.roslynForbiddenSymbols
$roslynForbidden = @($usages | Where-Object forbiddenForSourceLayer)
$expectedRoslynInputs = @($ledger.msbuildCompilation.compilationInputs | ForEach-Object path | Sort-Ordinal -Unique)
if ((@($roslynLayer.inputs) -join "`n") -cne ($expectedRoslynInputs -join "`n")) {
    throw 'EDGE-SPLIT-LEDGER-001 Roslyn input source inventory is stale.'
}
Assert-Equal $roslynLayer.totalExternalUsageCount $usages.Count 'Roslyn external usage total mismatch'
Assert-Equal $roslynLayer.forbiddenUsageCount $roslynForbidden.Count 'Roslyn forbidden usage count mismatch'
Assert-CountMap $roslynForbidden @($roslynLayer.forbiddenCountByOwnerFamily) 'Roslyn forbidden-symbol layer'

$peItems = @($peLayer.items)
$actualPeItems = @($peInputFacts | ForEach-Object {
        $inputPath = Resolve-RepositoryPath ([string]$_.path)
        Get-PeAssemblyReferences -PathValue $inputPath -RecordedPath ([string]$_.path)
    } | ForEach-Object {
        $identityKey = "$([string]$_.referencedAssembly), Version=$([string]$_.referencedVersion), Culture=$([string]$_.referencedCulture), PublicKeyToken=$([string]$_.referencedPublicKeyToken)"
        $ownerFamily = if ($referenceFactByIdentity.ContainsKey($identityKey)) {
            [string]$referenceFactByIdentity[$identityKey].ownerFamily
        }
        else { 'Unknown' }
        [pscustomobject][ordered]@{
            sourcePath = [string]$_.sourcePath
            sourceAssembly = [string]$_.sourceAssembly
            referencedAssembly = [string]$_.referencedAssembly
            referencedVersion = [string]$_.referencedVersion
            referencedCulture = [string]$_.referencedCulture
            referencedPublicKeyToken = [string]$_.referencedPublicKeyToken
            ownerFamily = $ownerFamily
            forbiddenForSourceLayer = Test-ExpectedSourceForbiddenFamily $ownerFamily
        }
    } | Sort-Ordinal sourceAssembly, referencedAssembly, referencedVersion, referencedCulture, referencedPublicKeyToken, sourcePath)
Assert-JsonEqual $peItems $actualPeItems 'PE AssemblyRef items/families/flags differ from independent raw metadata evaluation.'
$peForbidden = @($peItems | Where-Object forbiddenForSourceLayer)
$peUnknown = @($peItems | Where-Object ownerFamily -eq 'Unknown')
Assert-Equal $peLayer.totalCount $peItems.Count 'PE AssemblyRef total mismatch'
Assert-Equal $peLayer.forbiddenCount $peForbidden.Count 'PE AssemblyRef forbidden count mismatch'
Assert-Equal $peLayer.unknownAssemblyCount $peUnknown.Count 'PE AssemblyRef unknown count mismatch'
Assert-CountMap $peForbidden @($peLayer.forbiddenCountByOwnerFamily) 'PE AssemblyRef layer'
foreach ($item in $peItems) {
    Assert-RepositoryRelativePath ([string]$item.sourcePath) 'PE AssemblyRef source'
    $identityKey = "$([string]$item.referencedAssembly), Version=$([string]$item.referencedVersion), Culture=$([string]$item.referencedCulture), PublicKeyToken=$([string]$item.referencedPublicKeyToken)"
    $expectedFamily = if ($referenceFactByIdentity.ContainsKey($identityKey)) {
        [string]$referenceFactByIdentity[$identityKey].ownerFamily
    }
    else { 'Unknown' }
    if ([string]$item.ownerFamily -cne $expectedFamily -or $expectedFamily -ceq 'Unknown' -or
        [bool]$item.forbiddenForSourceLayer -ne (Test-ExpectedSourceForbiddenFamily $expectedFamily)) {
        throw 'EDGE-SPLIT-LEDGER-001 PE AssemblyRef owner/forbidden classification is not independently valid.'
    }
}

$contractLayer = $ledger.dependencyLayers.sdkUiContractClosures
$contractSurfaces = @($contractLayer.surfaces)
$contractForbiddenCount = 0
$actualContractSurfaces = [Collections.Generic.List[object]]::new()
foreach ($surface in $contractSurfaces) {
    $surfaceProjectPath = Resolve-RepositoryPath ([string]$surface.projectPath)
    $surfaceInputPath = Resolve-RepositoryPath ([string]$surface.assemblyInput.path)
    Assert-IndependentEmbeddedDebugIdentity -AssemblyPath $surfaceInputPath
    $actualSurfaceFact = Get-ManagedAssemblyFact -PathValue $surfaceInputPath `
        -RecordedPath ([string]$surface.assemblyInput.path) -VerifiedPluginOwned $false
    Assert-JsonEqual $surface.assemblyInput $actualSurfaceFact "formal surface byte identity is stale: $($surface.assemblyName)."
    if ([string]$actualSurfaceFact.assemblyName -cne [string]$surface.assemblyName) {
        throw 'EDGE-SPLIT-LEDGER-001 formal surface assembly/project identity mismatch.'
    }
    $surfaceEvaluationOutput = & dotnet msbuild $surfaceProjectPath -nologo -noAutoResponse -t:ResolveReferences `
        "-p:Configuration=$([string]$compilation.configuration)" '-getProperty:AssemblyName,TargetPath' `
        '-getItem:ProjectReference' 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "EDGE-SPLIT-LEDGER-001 formal surface MSBuild evaluation failed.`n$surfaceEvaluationOutput" }
    $surfaceEvaluation = $surfaceEvaluationOutput | ConvertFrom-Json -Depth 100
    $actualSurfaceProjectReferences = @($surfaceEvaluation.Items.ProjectReference | ForEach-Object {
            $fullPath = [string]$_.FullPath
            if ([string]::IsNullOrWhiteSpace($fullPath) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return }
            $projectName = [IO.Path]::GetFileNameWithoutExtension($fullPath)
            $projectRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $fullPath).Replace('\', '/')
            $ownerFamily = Get-InternalProjectOwnerFamily -ProjectName $projectName -ProjectPath $projectRelativePath
            $referenceOutputAssemblyText = Get-OptionalProperty $_ 'ReferenceOutputAssembly'
            [pscustomobject][ordered]@{
                projectPath = $projectRelativePath
                projectName = $projectName
                ownerFamily = $ownerFamily
                referenceOutputAssembly = $referenceOutputAssemblyText -cne 'false'
                forbiddenForContractSurface = ((Test-ExpectedSourceForbiddenFamily $ownerFamily) -or $ownerFamily -ceq 'PluginOwned') -and
                    $referenceOutputAssemblyText -cne 'false'
            }
        } | Sort-Ordinal projectPath -Unique)
    Assert-JsonEqual @($surface.projectReferences) $actualSurfaceProjectReferences "formal surface ProjectReference closure is stale: $($surface.assemblyName)."
    $actualSurfaceAssemblyReferences = @(Get-PeAssemblyReferences -PathValue $surfaceInputPath `
            -RecordedPath ([string]$surface.assemblyInput.path) | ForEach-Object {
            $identityKey = "$([string]$_.referencedAssembly), Version=$([string]$_.referencedVersion), Culture=$([string]$_.referencedCulture), PublicKeyToken=$([string]$_.referencedPublicKeyToken)"
            $ownerFamily = if ($referenceFactByIdentity.ContainsKey($identityKey)) {
                [string]$referenceFactByIdentity[$identityKey].ownerFamily
            }
            else { 'Unknown' }
            [pscustomobject][ordered]@{
                referencedAssembly = [string]$_.referencedAssembly
                referencedVersion = [string]$_.referencedVersion
                referencedCulture = [string]$_.referencedCulture
                referencedPublicKeyToken = [string]$_.referencedPublicKeyToken
                ownerFamily = $ownerFamily
                forbiddenForContractSurface = (Test-ExpectedSourceForbiddenFamily $ownerFamily) -or $ownerFamily -ceq 'PluginOwned'
            }
        } | Sort-Ordinal referencedAssembly, referencedVersion, referencedCulture, referencedPublicKeyToken)
    Assert-JsonEqual @($surface.assemblyReferences) $actualSurfaceAssemblyReferences "formal surface PE closure is stale: $($surface.assemblyName)."
    $expectedForbiddenProjects = @($actualSurfaceProjectReferences | Where-Object forbiddenForContractSurface).Count
    $expectedForbiddenAssemblies = @($actualSurfaceAssemblyReferences | Where-Object forbiddenForContractSurface).Count
    Assert-Equal $surface.forbiddenProjectReferenceCount $expectedForbiddenProjects 'formal surface forbidden ProjectReference count mismatch'
    Assert-Equal $surface.forbiddenAssemblyReferenceCount $expectedForbiddenAssemblies 'formal surface forbidden AssemblyRef count mismatch'
    $contractForbiddenCount += $expectedForbiddenProjects + $expectedForbiddenAssemblies
    $actualContractSurfaces.Add([pscustomobject][ordered]@{
            projectPath = [IO.Path]::GetRelativePath($RepositoryRoot, $surfaceProjectPath).Replace('\', '/')
            assemblyName = [string]$actualSurfaceFact.assemblyName
            assemblyInput = $actualSurfaceFact
            projectReferences = @($actualSurfaceProjectReferences)
            assemblyReferences = @($actualSurfaceAssemblyReferences)
            forbiddenProjectReferenceCount = $expectedForbiddenProjects
            forbiddenAssemblyReferenceCount = $expectedForbiddenAssemblies
            unknownOwnerCount = @($actualSurfaceProjectReferences + $actualSurfaceAssemblyReferences |
                Where-Object ownerFamily -eq 'Unknown').Count
        })
}
Assert-Equal $contractLayer.surfaceCount $contractSurfaces.Count 'formal surface count mismatch'
Assert-Equal $contractLayer.forbiddenReferenceCount $contractForbiddenCount 'formal surface aggregate forbidden count mismatch'
Assert-Equal $contractLayer.unknownOwnerCount 0 'formal surface unknown-owner count mismatch'
if ($batchRank -ge 10 -and ($contractSurfaces.Count -ne @($inputs.decisions.sdkPackages).Count -or $contractForbiddenCount -ne 0)) {
    throw 'EDGE-SPLIT-LEDGER-001 Phase 1+ requires all formal SDK/UI projects and zero reverse host-implementation closure.'
}
$actualContractLayer = [pscustomobject][ordered]@{
    status = 'evaluated'
    surfaces = @($actualContractSurfaces.ToArray() | Sort-Ordinal assemblyName)
    surfaceCount = $actualContractSurfaces.Count
    forbiddenReferenceCount = $contractForbiddenCount
    unknownOwnerCount = @($actualContractSurfaces.ToArray() | Where-Object unknownOwnerCount -ne 0).Count
}

$packageLayer = $ledger.dependencyLayers.packagedAssemblies
$packageAssemblies = @($packageLayer.assemblies)
$expectedPackageLimits = [pscustomobject][ordered]@{
    maxEntryCount = 256
    maxCompressedPackageBytes = 134217728
    maxEntryUncompressedBytes = 67108864
    maxTotalUncompressedBytes = 268435456
}
Assert-JsonEqual $packageLayer.limits $expectedPackageLimits 'candidate package ZIP resource limits drifted.'
$recordedPackageStaticInputs = @($packageLayer.staticInputs)
Assert-JsonEqual $recordedPackageStaticInputs $independentPackageStaticInputs `
    'package Config/Resources/PDB/manifest inputs differ from independent current source/build bytes.'
$staticPackagePaths = @($recordedPackageStaticInputs | ForEach-Object { [string]$_.packagePath })
Assert-SortedUniqueStrings $staticPackagePaths 'package static input paths'
if (@($staticPackagePaths | Sort-Ordinal -Unique -IgnoreCase).Count -ne $staticPackagePaths.Count -or
    @($recordedPackageStaticInputs | Where-Object { [string]$_.packagePath -ceq 'plugin.json' -and [bool]$_.required }).Count -ne 1) {
    throw 'EDGE-SPLIT-LEDGER-001 package static inputs must be Windows-distinct and require exactly one plugin.json.'
}
foreach ($staticInput in $recordedPackageStaticInputs) {
    Assert-RepositoryRelativePath ([string]$staticInput.packagePath) 'package static target'
    Assert-RepositoryRelativePath ([string]$staticInput.sourcePath) 'package static source'
    $staticSourcePath = Resolve-RepositoryPath ([string]$staticInput.sourcePath)
    if (-not (Test-Path -LiteralPath $staticSourcePath -PathType Leaf) -or
        [long]$staticInput.size -ne (Get-Item -LiteralPath $staticSourcePath).Length -or
        [string]$staticInput.sha256 -cne (Get-Sha256 $staticSourcePath)) {
        throw "EDGE-SPLIT-LEDGER-001 package static input is stale: $($staticInput.packagePath)."
    }
    if ($isCanonicalLedger -and [string]$staticInput.category -cne 'plugin-symbols') {
        Assert-TrackedAuthorityRegularBlob -Commit ([string]$ledger.sourceState.head) -WorktreePath $staticSourcePath
    }
}
$declaredPackageNames = @($packageLayer.declaredPluginOwnedAssemblies | ForEach-Object { [string]$_ })
Assert-SortedUniqueStrings $declaredPackageNames 'declared plugin-owned package assemblies'
$declaredPackageNameSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$verifiedFactsByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($fact in $actualPeInputFacts) { $verifiedFactsByName.Add([string]$fact.assemblyName, $fact) }
foreach ($name in $declaredPackageNames) {
    if (-not $verifiedFactsByName.ContainsKey($name) -or -not $declaredPackageNameSet.Add($name)) {
        throw "EDGE-SPLIT-LEDGER-001 package ownership declaration lacks unique verified bytes: $name."
    }
}
if (-not $declaredPackageNameSet.Contains($entryAssemblyName)) {
    throw 'EDGE-SPLIT-LEDGER-001 package ownership declarations must include the manifest entry assembly.'
}
$packageForbidden = @($packageAssemblies | Where-Object forbiddenForPackageLayer)
$packageUnknown = @($packageAssemblies | Where-Object ownerFamily -eq 'Unknown')
$packageForbiddenFiles = @($packageLayer.entries | Where-Object { -not [bool]$_.allowed })
$packageUnclassifiedFiles = @($packageLayer.entries | Where-Object category -eq 'unclassified')
Assert-Equal $packageLayer.totalEntryCount @($packageLayer.entries).Count 'packaged entry count mismatch'
Assert-Equal $packageLayer.totalAssemblyCount $packageAssemblies.Count 'packaged assembly count mismatch'
Assert-Equal $packageLayer.forbiddenCount $packageForbidden.Count 'packaged forbidden assembly count mismatch'
Assert-Equal $packageLayer.unknownAssemblyCount $packageUnknown.Count 'packaged unknown assembly count mismatch'
Assert-Equal $packageLayer.forbiddenFileCount $packageForbiddenFiles.Count 'packaged forbidden file count mismatch'
Assert-Equal $packageLayer.unclassifiedFileCount $packageUnclassifiedFiles.Count 'packaged unclassified file count mismatch'
Assert-CountMap $packageForbidden @($packageLayer.forbiddenCountByOwnerFamily) 'package assembly layer'
foreach ($entry in @($packageLayer.entries)) {
    Assert-RepositoryRelativePath ([string]$entry.path) 'candidate package entry'
    if ([long]$entry.size -lt 0 -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'EDGE-SPLIT-LEDGER-001 candidate package entry lacks byte evidence.'
    }
}
$candidatePackagePaths = @($packageLayer.entries | ForEach-Object path)
if (@($candidatePackagePaths | Sort-Ordinal -Unique -IgnoreCase).Count -ne $candidatePackagePaths.Count) {
    throw 'EDGE-SPLIT-LEDGER-001 candidate package contains duplicate entry paths.'
}
foreach ($assembly in $packageAssemblies) {
    Assert-RepositoryRelativePath ([string]$assembly.path) 'candidate packaged assembly'
    if ([string]$assembly.ownerFamily -ceq 'Unknown' -or
        -not ([Collections.Generic.HashSet[string]]::new([string[]]$candidatePackagePaths, [StringComparer]::Ordinal)).Contains([string]$assembly.path)) {
        throw 'EDGE-SPLIT-LEDGER-001 candidate packaged assembly is unclassified or absent from package entries.'
    }
}
$rawPackageAudit = $null
if ($batchRank -lt 40) {
    Assert-Equal $packageLayer.status 'not-applicable-before-EDGE-SPLIT-040' 'pre-Phase-4 package layer must be explicitly not applicable'
    if (-not [string]::IsNullOrWhiteSpace([string]$packageLayer.packagePath) -or
        -not [string]::IsNullOrWhiteSpace([string]$packageLayer.packageSha256) -or
        @($packageLayer.entries).Count -ne 0 -or $packageAssemblies.Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 a not-applicable package layer cannot masquerade as evaluated zero.'
    }
}
else {
    Assert-Equal $packageLayer.status 'evaluated' 'Phase 4/5 package layer must be evaluated'
    Assert-RepositoryRelativePath ([string]$packageLayer.packagePath) 'candidate plugin package'
    if ([string]$packageLayer.packageSha256 -notmatch '^[0-9a-f]{64}$' -or @($packageLayer.entries).Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 evaluated package layer lacks artifact bytes or entries.'
    }
    $candidatePackagePath = Resolve-RepositoryPath ([string]$packageLayer.packagePath)
    if (-not (Test-Path -LiteralPath $candidatePackagePath -PathType Leaf)) {
        throw 'EDGE-SPLIT-LEDGER-001 evaluated candidate package does not exist.'
    }
    Assert-Equal $packageLayer.packageSha256 (Get-Sha256 $candidatePackagePath) 'candidate package digest mismatch'
    $rawPackageAudit = Invoke-RawPackageAudit `
        -CandidatePackagePath $candidatePackagePath `
        -ManifestPath $manifestPath `
        -VerifiedAssemblyFacts @($actualPeInputFacts) `
        -DeclaredPackageNames $declaredPackageNames `
        -VerifiedPluginOwnedNames $verifiedPluginOwnedNames `
        -StaticInputs $independentPackageStaticInputs `
        -RejectForbidden
    Assert-JsonEqual $packageLayer.limits $rawPackageAudit.limits 'raw ZIP auditor limits differ from the ledger protocol.'
    Assert-JsonEqual @($packageLayer.entries) @($rawPackageAudit.entries) 'candidate package entry allowlist is not an exact raw-zip projection.'
    Assert-JsonEqual $packageAssemblies @($rawPackageAudit.assemblies) 'candidate package assembly identities/bytes/owners are not an exact raw-zip projection.'
}

if ($batchRank -lt 40) {
    $actualPackageLayer = [pscustomobject][ordered]@{
        status = 'not-applicable-before-EDGE-SPLIT-040'
        declaredPluginOwnedAssemblies = @($declaredPackageNames)
        limits = $expectedPackageLimits
        staticInputs = @($independentPackageStaticInputs)
        packagePath = ''
        packageSha256 = ''
        entries = @()
        totalEntryCount = 0
        assemblies = @()
        totalAssemblyCount = 0
        forbiddenCount = 0
        forbiddenCountByOwnerFamily = @()
        unknownAssemblyCount = 0
        forbiddenFileCount = 0
        unclassifiedFileCount = 0
    }
}
else {
    $actualPackageEntries = @($rawPackageAudit.entries)
    $actualPackageAssemblies = @($rawPackageAudit.assemblies)
    $actualPackageForbiddenAssemblies = @($actualPackageAssemblies | Where-Object forbiddenForPackageLayer)
    $actualPackageForbiddenCounts = @($actualPackageForbiddenAssemblies | Group-Ordinal ownerFamily |
        ForEach-Object { [pscustomobject][ordered]@{ ownerFamily = [string]$_.Name; count = $_.Count } } |
        Sort-Ordinal ownerFamily)
    $actualPackageLayer = [pscustomobject][ordered]@{
        status = 'evaluated'
        declaredPluginOwnedAssemblies = @($declaredPackageNames)
        limits = $rawPackageAudit.limits
        staticInputs = @($independentPackageStaticInputs)
        packagePath = [IO.Path]::GetRelativePath($RepositoryRoot, $candidatePackagePath).Replace('\', '/')
        packageSha256 = Get-Sha256 $candidatePackagePath
        entries = $actualPackageEntries
        totalEntryCount = $actualPackageEntries.Count
        assemblies = $actualPackageAssemblies
        totalAssemblyCount = $actualPackageAssemblies.Count
        forbiddenCount = $actualPackageForbiddenAssemblies.Count
        forbiddenCountByOwnerFamily = $actualPackageForbiddenCounts
        unknownAssemblyCount = @($actualPackageAssemblies | Where-Object ownerFamily -eq 'Unknown').Count
        forbiddenFileCount = @($actualPackageEntries | Where-Object { -not [bool]$_.allowed }).Count
        unclassifiedFileCount = @($actualPackageEntries | Where-Object category -eq 'unclassified').Count
    }
}

$inputDigestLines = [Collections.Generic.List[string]]::new()
foreach ($source in $compilationInputs) {
    $inputDigestLines.Add("source|$($source.path)|$($source.sha256)")
}
foreach ($reference in $references) {
    $provenanceJson = $reference.provenance | ConvertTo-Json -Depth 10 -Compress
    $inputDigestLines.Add("reference|$($reference.assemblyName)|$($reference.assemblyVersion)|$($reference.culture)|$($reference.publicKeyToken)|$($reference.mvid)|$($reference.size)|$($reference.origin)|$provenanceJson|$($reference.sha256)|$($reference.ownerFamily)")
}
foreach ($reference in $projectItems) {
    $inputDigestLines.Add("project-reference|$($reference.projectPath)|$($reference.ownerFamily)|$($reference.direct)|$($reference.referenceOutputAssembly)|$($reference.outputItemType)")
}
foreach ($reference in $peItems) {
    $inputDigestLines.Add("pe-reference|$($reference.sourceAssembly)|$($reference.referencedAssembly)|$($reference.referencedVersion)|$($reference.referencedCulture)|$($reference.referencedPublicKeyToken)|$($reference.ownerFamily)")
}
foreach ($inputFact in $peInputFacts) {
    $inputDigestLines.Add("pe-input|$($inputFact.path)|$($inputFact.assemblyName)|$($inputFact.assemblyVersion)|$($inputFact.culture)|$($inputFact.publicKeyToken)|$($inputFact.mvid)|$($inputFact.size)|$($inputFact.sha256)")
}
foreach ($surface in $contractSurfaces) {
    $inputDigestLines.Add("contract-surface|$($surface.projectPath)|$($surface.assemblyInput.sha256)|$($surface.assemblyInput.mvid)")
    foreach ($reference in $surface.projectReferences) {
        $inputDigestLines.Add("contract-project-reference|$($surface.assemblyName)|$($reference.projectPath)|$($reference.ownerFamily)|$($reference.referenceOutputAssembly)")
    }
    foreach ($reference in $surface.assemblyReferences) {
        $inputDigestLines.Add("contract-pe-reference|$($surface.assemblyName)|$($reference.referencedAssembly)|$($reference.referencedVersion)|$($reference.referencedCulture)|$($reference.referencedPublicKeyToken)|$($reference.ownerFamily)")
    }
}
if ([string]$packageLayer.status -eq 'evaluated') {
    $inputDigestLines.Add("candidate-package|$($packageLayer.packagePath)|$($packageLayer.packageSha256)")
}
foreach ($staticInput in $recordedPackageStaticInputs) {
    $inputDigestLines.Add("package-static|$($staticInput.packagePath)|$($staticInput.sourcePath)|$($staticInput.size)|$($staticInput.sha256)|$($staticInput.category)|$($staticInput.required)")
}
$inputDigestLines.Add("msbuild-authority-policy|$(($expectedAuthorityProjectionPolicy | ConvertTo-Json -Depth 20 -Compress))")
foreach ($authorityInput in $recordedAuthorityInputs) {
    $inputDigestLines.Add("msbuild-authority|$($authorityInput.origin)|$($authorityInput.representation)|$($authorityInput.path)|$(@($authorityInput.roles) -join ',')|$($authorityInput.size)|$($authorityInput.sha256)")
    if ($isCanonicalLedger -and [string]$authorityInput.origin -ceq 'tracked-repository') {
        Assert-TrackedAuthorityRegularBlob -Commit ([string]$ledger.sourceState.head) `
            -WorktreePath (Resolve-RepositoryPath ([string]$authorityInput.path))
    }
}
$peInputPaths = @($peInputFacts | ForEach-Object { [string]$_.path })
if ($peInputPaths.Count -lt 1) {
    throw 'EDGE-SPLIT-LEDGER-001 PE layer must record the built plugin assembly input.'
}
if (@($peInputPaths | Sort-Ordinal -Unique).Count -ne $peInputPaths.Count) {
    throw 'EDGE-SPLIT-LEDGER-001 PE input assembly paths must be unique.'
}
foreach ($ownedPathValue in @($peInputFacts | Where-Object assemblyName -cne $entryAssemblyName | ForEach-Object path)) {
    $ownedPath = Resolve-RepositoryPath $ownedPathValue
    if (-not (Test-Path -LiteralPath $ownedPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 additional plugin-owned assembly input does not exist: $ownedPathValue"
    }
    $inputDigestLines.Add("plugin-owned-assembly|$ownedPathValue|$(Get-Sha256 $ownedPath)")
}
$fixedInputPaths = @(
    $pluginProjectPath,
    $manifestPath,
    $inputsPath,
    $inputsSchemaPath,
    $schemaPath,
    $helperPath,
    $validatorRoslynHelperPath,
    $deterministicBuildTargetsPath,
    $phaseCloseEvidenceSchemaPath,
    $phaseCloseEvidenceValidatorPath,
    $generatorPath,
    $testInventoryPath,
    $requiredCountsPath,
    $discoveredInventoryPath,
    (Resolve-RepositoryPath 'scripts/tests/Test-EdgePluginContractLedger.ps1'),
    (Resolve-RepositoryPath 'scripts/tests/Test-EdgePluginContractLedgerBehavior.ps1'),
    (Resolve-RepositoryPath 'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs'),
    (Resolve-RepositoryPath 'src/Tests/IIoT.Edge.Architecture.Tests/IIoT.Edge.Architecture.Tests.csproj'),
    (Resolve-RepositoryPath 'IIoT.EdgeClient.slnx')
)
foreach ($path in $fixedInputPaths) {
    $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
    $digest = Get-Sha256 $path
    if ($isCanonicalLedger) {
        Assert-TrackedAuthorityRegularBlob -Commit ([string]$ledger.sourceState.head) -WorktreePath $path
    }
    $inputDigestLines.Add("file|$relativePath|$digest")
}
if ($batchRank -gt 0) {
    $inputDigestLines.Add("baseline-ledger|$canonicalLedgerRelativePath|$($ledger.integrity.baselineLedgerSha256)")
}
$recomputedAnalyzedInputsSha256 = Get-TextSha256 ((@($inputDigestLines | Sort-Ordinal) -join "`n") + "`n")
Assert-Equal $ledger.integrity.analyzedInputsSha256 $recomputedAnalyzedInputsSha256 'analyzed input digest mismatch'

if ([int]$projectLayer.unknownAssemblyCount -ne 0 -or [int]$peLayer.unknownAssemblyCount -ne 0 -or
    [int]$packageLayer.unknownAssemblyCount -ne 0 -or [int]$roslynLayer.unclassifiedSymbolCount -ne 0 -or
    [int]$ledger.summary.unknownAssemblyCount -ne 0 -or [int]$ledger.summary.unclassifiedSymbolCount -ne 0) {
    throw 'EDGE-SPLIT-LEDGER-001 unknown assemblies and unclassified symbols must remain zero in every evaluated layer.'
}

foreach ($batchId in @('EDGE-SPLIT-020', 'EDGE-SPLIT-030')) {
    $carry = $ledger.carrySets.$batchId
    $baselineItems = @($carry.baselineItems)
    $currentItems = @($carry.currentItems)
    if ($baselineItems.Count -eq 0 -or [int]$carry.baselineItemCount -ne $baselineItems.Count -or [int]$carry.currentItemCount -ne $currentItems.Count) {
        throw "EDGE-SPLIT-LEDGER-001 $batchId carry-set inventory is empty or stale."
    }
    Assert-Equal $carry.baselineOccurrenceCount (($baselineItems | Measure-Object -Property count -Sum).Sum) "$batchId baseline occurrence count mismatch"
    $currentOccurrenceCount = ($currentItems | Measure-Object -Property count -Sum).Sum
    if ($null -eq $currentOccurrenceCount) { $currentOccurrenceCount = 0 }
    Assert-Equal $carry.currentOccurrenceCount $currentOccurrenceCount "$batchId current occurrence count mismatch"
    foreach ($item in @($baselineItems + $currentItems)) {
        Assert-RepositoryRelativePath ([string]$item.sourcePath) "$batchId carry item"
        if ([string]$item.removalBatch -cne $batchId -or [int]$item.count -le 0 -or [string]$item.ownerFamily -eq 'Unknown') {
            throw "EDGE-SPLIT-LEDGER-001 $batchId contains an invalid carry item."
        }
    }
    $expectedGroups = @($usages | Where-Object removalBatch -eq $batchId |
        Group-Ordinal sourcePath, ownerAssembly, symbol |
        ForEach-Object {
            $first = $_.Group[0]
            [pscustomobject]@{
                sourcePath = [string]$first.sourcePath
                ownerAssembly = [string]$first.ownerAssembly
                symbol = [string]$first.symbol
                count = $_.Count
            }
        })
    Assert-CarryItemsEqual $expectedGroups $currentItems "$batchId semantic current set"
    $removalRank = Get-BatchRank $batchId
    $expectedStatus = if ($batchRank -eq 0) { 'frozen' } elseif ($batchRank -lt $removalRank) { 'retained-exact' } else { 'closed' }
    Assert-Equal $carry.lifecycleStatus $expectedStatus "$batchId lifecycle status mismatch"
    if (Test-OrdinalEqualsAny $expectedStatus @('frozen', 'retained-exact')) {
        Assert-CarryItemsEqual $baselineItems $currentItems "$batchId frozen carry set"
    }
    elseif ($currentItems.Count -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 $batchId must be zero after its removal batch."
    }
}

if ($batchRank -ge 10 -and @($usages | Where-Object removalBatch -eq 'EDGE-SPLIT-010').Count -ne 0) {
    throw 'EDGE-SPLIT-LEDGER-001 Phase 1 residual symbols remain after EDGE-SPLIT-010.'
}
Assert-IndependentPhaseLayerGate `
    -BatchId ([string]$ledger.batchId) `
    -ProjectForbidden $projectForbidden `
    -PeForbidden $peForbidden `
    -RoslynForbidden $roslynForbidden `
    -Carry020Baseline @($ledger.carrySets.'EDGE-SPLIT-020'.baselineItems) `
    -Carry030Baseline @($ledger.carrySets.'EDGE-SPLIT-030'.baselineItems)
if ($batchRank -ge 40 -and ($packageForbidden.Count -ne 0 -or $packageForbiddenFiles.Count -ne 0 -or $packageUnclassifiedFiles.Count -ne 0)) {
    throw 'EDGE-SPLIT-LEDGER-001 the Phase 4+ package allowlist must contain zero forbidden/unclassified entries.'
}

Assert-Equal $ledger.summary.viewCount @($ledger.viewInventory).Count 'View inventory summary mismatch'
Assert-Equal $ledger.summary.pageCount @($ledger.pageInventory).Count 'page inventory summary mismatch'
Assert-Equal $ledger.summary.resourceKeyOccurrenceCount @($ledger.resourceInventory).Count 'resource inventory summary mismatch'
Assert-Equal $ledger.summary.uniqueExternalSymbolCount @($usages | ForEach-Object { "$($_.ownerAssembly)|$($_.symbol)" } | Sort-Ordinal -Unique).Count 'unique external-symbol summary mismatch'
Assert-PropertyCountMap $usages @($ledger.summary.dispositionCounts) 'disposition' 'summary disposition'
Assert-PropertyCountMap $usages @($ledger.summary.ownerAssemblyCounts) 'ownerAssembly' 'summary owner-assembly'
Assert-CountMap $usages @($ledger.summary.ownerFamilyCounts) 'summary owner-family'
Assert-Equal $ledger.summary.carrySet020ItemCount @($ledger.carrySets.'EDGE-SPLIT-020'.currentItems).Count 'summary Phase 2 carry count mismatch'
Assert-Equal $ledger.summary.carrySet030ItemCount @($ledger.carrySets.'EDGE-SPLIT-030'.currentItems).Count 'summary Phase 3 carry count mismatch'
Assert-Equal $ledger.summary.carrySet020OccurrenceCount $ledger.carrySets.'EDGE-SPLIT-020'.currentOccurrenceCount 'summary Phase 2 carry occurrence count mismatch'
Assert-Equal $ledger.summary.carrySet030OccurrenceCount $ledger.carrySets.'EDGE-SPLIT-030'.currentOccurrenceCount 'summary Phase 3 carry occurrence count mismatch'
Assert-Equal $ledger.summary.evaluatedProjectReferenceForbiddenCount $projectForbidden.Count 'summary ProjectReference forbidden count mismatch'
Assert-Equal $ledger.summary.roslynForbiddenSymbolCount $roslynForbidden.Count 'summary Roslyn forbidden count mismatch'
Assert-Equal $ledger.summary.peForbiddenAssemblyReferenceCount $peForbidden.Count 'summary PE AssemblyRef forbidden count mismatch'
Assert-Equal $ledger.summary.packagedForbiddenAssemblyCount $packageForbidden.Count 'summary package forbidden count mismatch'
Assert-Equal $ledger.summary.contractSurfaceForbiddenReferenceCount $contractForbiddenCount 'summary contract-surface forbidden count mismatch'
Assert-Equal $ledger.summary.packagedForbiddenFileCount $packageForbiddenFiles.Count 'summary package forbidden-file count mismatch'
Assert-Equal $ledger.summary.packagedUnclassifiedFileCount $packageUnclassifiedFiles.Count 'summary package unclassified-file count mismatch'
foreach ($entry in @($ledger.pageInventory + $ledger.resourceInventory)) { Assert-RepositoryRelativePath ([string]$entry.sourcePath) 'page/resource inventory' }

if ($AuthorityRebuildOnly) {
    Import-Module $authorityProtocolModulePath -Force
    $actualMsbuildCore = [pscustomobject][ordered]@{
        projectPath = [IO.Path]::GetRelativePath($RepositoryRoot, $pluginProjectPath).Replace('\', '/')
        dotnetSdkVersion = [string]$validatorResolvedSdkVersion
        configuration = [string]$compilation.configuration
        targetFramework = [string]$validatorEvaluation.Properties.TargetFramework
        langVersion = [string]$validatorEvaluation.Properties.LangVersion
        nullable = [string]$validatorEvaluation.Properties.Nullable
        assemblyName = [string]$validatorEvaluation.Properties.AssemblyName
        msbuildCompileSourceCount = @($validatorCompileSourcePaths).Count
        emittedGeneratorSourceCount = @($actualGeneratedSourceInventory).Count
        compilationInputCount = @($actualCompilationInputInventory).Count
        metadataReferenceCount = @($actualReferenceFactsSorted).Count
        compilationErrorCount = $validatorAnalysis.CompilationErrors.Count
        generatedSources = @($actualGeneratedSourceInventory)
        compilationInputs = @($actualCompilationInputInventory)
        authorityProjectionPolicy = $expectedAuthorityProjectionPolicy
        authorityInputCount = @($independentAuthorityInputs).Count
        authorityInputs = @($independentAuthorityInputs)
        viewIdsAssemblyPath = [IO.Path]::GetRelativePath($RepositoryRoot, $viewIdsAssemblyPath).Replace('\', '/')
        viewIdsTypeName = [string]$viewIdsType.FullName
    }
    $actualProjectForbidden = @($actualProjectItems | Where-Object forbiddenForSourceLayer)
    $actualProjectUnknown = @($actualProjectItems | Where-Object ownerFamily -eq 'Unknown')
    $actualProjectCounts = @($actualProjectForbidden | Group-Ordinal ownerFamily | ForEach-Object {
            [pscustomobject][ordered]@{ ownerFamily = [string]$_.Name; count = $_.Count }
        } | Sort-Ordinal ownerFamily)
    $actualProjectLayer = [pscustomobject][ordered]@{
        status = 'evaluated'
        inputProject = [string]$ledger.msbuildCompilation.projectPath
        items = @($actualProjectItems)
        totalCount = @($actualProjectItems).Count
        forbiddenCount = $actualProjectForbidden.Count
        forbiddenCountByOwnerFamily = $actualProjectCounts
        unknownAssemblyCount = $actualProjectUnknown.Count
    }
    $actualRoslynForbidden = @($usages | Where-Object forbiddenForSourceLayer)
    $actualRoslynCounts = @($actualRoslynForbidden | Group-Ordinal ownerFamily | ForEach-Object {
            [pscustomobject][ordered]@{ ownerFamily = [string]$_.Name; count = $_.Count }
        } | Sort-Ordinal ownerFamily)
    $actualRoslynLayer = [pscustomobject][ordered]@{
        status = 'evaluated'
        inputs = @($expectedRoslynInputs)
        totalExternalUsageCount = $independentRawUsages.Count
        forbiddenUsageCount = $actualRoslynForbidden.Count
        forbiddenCountByOwnerFamily = $actualRoslynCounts
        unclassifiedSymbolCount = @($usages | Where-Object classification -eq 'unclassified').Count
    }
    $actualPeForbidden = @($actualPeItems | Where-Object forbiddenForSourceLayer)
    $actualPeUnknown = @($actualPeItems | Where-Object ownerFamily -eq 'Unknown')
    $actualPeCounts = @($actualPeForbidden | Group-Ordinal ownerFamily | ForEach-Object {
            [pscustomobject][ordered]@{ ownerFamily = [string]$_.Name; count = $_.Count }
        } | Sort-Ordinal ownerFamily)
    $actualPeLayer = [pscustomobject][ordered]@{
        status = 'evaluated'
        inputs = @($actualPeInputFacts.ToArray())
        items = @($actualPeItems)
        totalCount = @($actualPeItems).Count
        forbiddenCount = $actualPeForbidden.Count
        forbiddenCountByOwnerFamily = $actualPeCounts
        unknownAssemblyCount = $actualPeUnknown.Count
    }
    $actualSolutionInventory = [pscustomobject][ordered]@{
        solutionPath = [string]$ledger.solutionInventory.solutionPath
        projectCount = $actualSolutionProjects.Count
        projects = @($actualSolutionProjects)
    }
    $actualPluginManifest = [pscustomobject][ordered]@{
        path = [string]$ledger.pluginManifest.path
        sha256 = Get-Sha256 $manifestPath
        value = $manifest
    }
    $authorityOverrides = [ordered]@{
        solutionInventory = $actualSolutionInventory
        pluginManifest = $actualPluginManifest
        pluginSources = @($actualPluginSourceInventory)
        msbuildCore = $actualMsbuildCore
        views = @($expectedViews)
        pages = @($actualPageInventory.ToArray())
        resources = @($actualResourceInventory.ToArray() | Sort-Ordinal sourcePath, key, valueType)
        references = @($actualReferenceFactsSorted)
        usages = @($actualUsageProjection.ToArray())
        projectLayer = $actualProjectLayer
        roslynLayer = $actualRoslynLayer
        peLayer = $actualPeLayer
        contractLayer = $actualContractLayer
        packageLayer = $actualPackageLayer
        analyzedInputs = [string]$recomputedAnalyzedInputsSha256
    }
    $authorityResult = [pscustomobject][ordered]@{
        schemaVersion = 1
        ruleId = 'EDGE-SPLIT-LEDGER-001'
        ledgerSha256 = Get-EdgeSha256File $resolvedLedgerPath
        ledgerPayloadSha256 = [string]$ledger.integrity.payloadSha256
        analyzedInputsSha256 = [string]$recomputedAnalyzedInputsSha256
        authorityCodeSha256 = Get-EdgeAuthorityCodeDigest $RepositoryRoot
        factGroups = @(New-EdgeAuthorityFactGroups -Ledger $ledger -IndependentOverrides $authorityOverrides)
    }
    $authorityResultRaw = ConvertTo-EdgeCanonicalJson $authorityResult
    try {
        if (-not ($authorityResultRaw | Test-Json -Schema (Get-Content -LiteralPath $authorityResultSchemaPath -Raw) -ErrorAction Stop)) {
            throw 'schema validation returned false'
        }
    }
    catch { throw "EDGE-SPLIT-AUTHORITY-RESULT-001 authority result schema rejected the independent projection: $($_.Exception.Message)" }
    $resolvedAuthorityResultPath = Resolve-RepositoryPath $AuthorityResultPath
    [void](New-Item -ItemType Directory -Path (Split-Path $resolvedAuthorityResultPath -Parent) -Force)
    [IO.File]::WriteAllText($resolvedAuthorityResultPath, $authorityResultRaw, [Text.UTF8Encoding]::new($false))
}

if ($isCanonicalLedger -and -not $AuthorityRebuildOnly) {
    $replayToken = [Guid]::NewGuid().ToString('N')
    $replayRelativePath = ".artifacts/test-temp/edge-ledger-canonical-replay-$replayToken.json"
    $replayPath = Resolve-RepositoryPath $replayRelativePath
    $predecessorReplayRelativePath = ".artifacts/test-temp/edge-ledger-predecessor-$replayToken.json"
    $predecessorReplayPath = Resolve-RepositoryPath $predecessorReplayRelativePath
    [void](New-Item -ItemType Directory -Path (Split-Path $replayPath -Parent) -Force)
    try {
        $replayParameters = @{
            PluginProject = [string]$compilation.projectPath
            OutputPath = $replayRelativePath
            Configuration = [string]$compilation.configuration
            CurrentBatch = [string]$ledger.batchId
            ViewIdsAssemblyPath = [string]$compilation.viewIdsAssemblyPath
            ViewIdsTypeName = [string]$compilation.viewIdsTypeName
            ValidationReplayImplementationHead = [string]$ledger.sourceState.head
            ValidationReplayImplementationTree = [string]$ledger.sourceState.tree
        }
        if ($batchRank -gt 0) {
            Copy-GitBlobToFile `
                -Commit ([string]$ledger.sourceState.head) `
                -RepositoryPath $canonicalLedgerRelativePath `
                -Destination $predecessorReplayPath
            $replayParameters.BaselineLedgerPath = $predecessorReplayRelativePath
        }
        if ([string]$packageLayer.status -eq 'evaluated') {
            $replayParameters.PluginPackagePath = [string]$packageLayer.packagePath
        }
        $additionalOwnedPaths = @($peInputFacts | Where-Object assemblyName -cne $entryAssemblyName | ForEach-Object path)
        if ($additionalOwnedPaths.Count -ne 0) {
            $replayParameters.PluginOwnedAssemblyPath = [string[]]$additionalOwnedPaths
        }
        $declaredOwnedNames = @($declaredPackageNames | Where-Object { [string]$_ -cne $entryAssemblyName } | Sort-Ordinal -Unique)
        if ($declaredOwnedNames.Count -ne 0) {
            $replayParameters.PluginOwnedPackageAssembly = [string[]]$declaredOwnedNames
        }
        $replayOutput = & $generatorPath @replayParameters 2>&1 | Out-String
        if (-not (Test-Path -LiteralPath $replayPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 canonical generator replay did not emit a ledger.`n$replayOutput"
        }
        Import-Module $authorityProtocolModulePath -Force
        Assert-EdgeReplayEquivalent `
            -CanonicalLedgerPath $resolvedLedgerPath `
            -ReplayLedgerPath $replayPath `
            -LedgerSchemaPath $schemaPath `
            -CanonicalOutputRelativePath $canonicalLedgerRelativePath `
            -ReplayOutputRelativePath $replayRelativePath
    }
    finally {
        foreach ($temporaryPath in @($replayPath, $predecessorReplayPath)) {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }
}

if ($AuthorityRebuildOnly) {
    Write-Host "Edge plugin contract independent authority rebuild passed: batch=$($ledger.batchId), authorityResult=$AuthorityResultPath."
}
else {
    Write-Host "Edge plugin contract ledger passed: batch=$($ledger.batchId), usages=$($usages.Count), carry020=$(@($ledger.carrySets.'EDGE-SPLIT-020'.currentItems).Count)/$($ledger.carrySets.'EDGE-SPLIT-020'.lifecycleStatus), carry030=$(@($ledger.carrySets.'EDGE-SPLIT-030'.currentItems).Count)/$($ledger.carrySets.'EDGE-SPLIT-030'.lifecycleStatus), project=$($projectForbidden.Count), roslyn=$($roslynForbidden.Count), pe=$($peForbidden.Count), package=$($packageForbidden.Count)/$($packageLayer.status), unknown=0, unclassified=0."
}
}
finally {
    Restore-IndependentEnvironmentSnapshot `
        -VariableNames ([string[]]$validatorRestoreEnvironmentNames) `
        -States $validatorRestoreEnvironmentBefore
    if (Test-Path -LiteralPath $validatorTemporaryRoot -PathType Container) {
        Remove-Item -LiteralPath $validatorTemporaryRoot -Recurse -Force
    }
}
