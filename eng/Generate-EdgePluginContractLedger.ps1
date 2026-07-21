[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginProject,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Phase0InputsPath = 'eng/baselines/edge-split-phase0-inputs.json',

    [ValidateSet('EDGE-SPLIT-000', 'EDGE-SPLIT-010', 'EDGE-SPLIT-020', 'EDGE-SPLIT-030', 'EDGE-SPLIT-040', 'EDGE-SPLIT-050')]
    [string]$CurrentBatch = 'EDGE-SPLIT-000',

    [string]$BaselineLedgerPath = 'eng/baselines/edge-plugin-contract-ledger.json',

    [string]$PluginPackagePath = '',

    [string[]]$PluginOwnedAssemblyPath = @(),

    [string[]]$PluginOwnedPackageAssembly = @(),

    [string]$ViewIdsAssemblyPath = '',

    [string]$ViewIdsTypeName = 'IIoT.Edge.Presentation.Navigation.PluginSystem.StandardModuleViewIds',

    [switch]$RefreshHistoricalArtifactEvidence,

    [string]$ValidationReplayImplementationHead = '',

    [string]$ValidationReplayImplementationTree = '',

    [switch]$ValidationReplayGateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pinnedChildProtocolModulePath = Join-Path $PSScriptRoot `
    '../scripts/tests/EdgePluginContractLedger.Protocol.psm1'
Import-Module $pinnedChildProtocolModulePath -Force
$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)
if (-not $authorityChildEnvironmentBound) {
    Assert-EdgeAuthorityGitEnvironment
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-NoRepositoryReparsePoint {
    param([Parameter(Mandatory = $true)][string]$FullPath)

    $rootItem = Get-Item -LiteralPath $repositoryRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootItem.LinkTarget)) {
        throw "EDGE-SPLIT-LEDGER-001 repository root must not be a symlink/reparse point: $repositoryRoot"
    }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $FullPath)
    $current = $repositoryRoot
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
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $PathValue))
    }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $path)
    if ($relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "EDGE-SPLIT-LEDGER-001 path must stay inside the repository: $PathValue"
    }
    Assert-NoRepositoryReparsePoint -FullPath $path
    return $path
}

function ConvertTo-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $fullPath = [IO.Path]::GetFullPath($PathValue)
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $fullPath)
    if ($relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        return ''
    }
    return $relative.Replace('\', '/')
}

function Test-ResolvedPathIdentityEqual {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    return [string]::Equals([IO.Path]::GetFullPath($Left), [IO.Path]::GetFullPath($Right), $comparison)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    return (Get-FileHash -LiteralPath $PathValue -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-CanonicalPathMapSourceToken {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $fullPath = [IO.Path]::GetFullPath($PathValue).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    if ([string]::IsNullOrWhiteSpace($fullPath)) {
        throw 'EDGE-SPLIT-LEDGER-001 canonical PathMap source root is empty.'
    }
    return $fullPath.Replace('=', '==').Replace(',', ',,')
}

function ConvertTo-CanonicalMsBuildPropertyValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    return $Value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
}

function Assert-CanonicalEmbeddedDebugIdentity {
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    $portablePdbPath = [IO.Path]::ChangeExtension($AssemblyPath, '.pdb')
    if (Test-Path -LiteralPath $portablePdbPath -PathType Leaf) {
        throw "EDGE-SPLIT-LEDGER-001 deterministic authority build left a stale portable PDB sibling: $portablePdbPath"
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
                throw 'EDGE-SPLIT-LEDGER-001 authority PE must contain exactly one filename-only CodeView entry and one embedded portable PDB.'
            }
            $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntries[0])
            $expectedPdbName = [IO.Path]::GetFileNameWithoutExtension($AssemblyPath) + '.pdb'
            if ([string]$codeView.Path -cne $expectedPdbName -or
                [string]$codeView.Path -match '[/\\]') {
                throw "EDGE-SPLIT-LEDGER-001 authority PE CodeView path is physical or non-canonical: $($codeView.Path)"
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
                        throw "EDGE-SPLIT-LEDGER-001 authority embedded PDB document escaped canonical virtual roots: $documentName"
                    }
                }
            }
            finally { $provider.Dispose() }
        }
        finally { $peReader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-CanonicalDeterministicBuildLog {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$EncodedPathMap,
        [Parameter(Mandatory = $true)][string]$EncodedTargetsPath,
        [Parameter(Mandatory = $true)][string]$EncodedRepositoryRoot
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 deterministic authority build log is missing: $LogPath"
    }
    $projectDirectory = Split-Path ([IO.Path]::GetFullPath($ProjectPath)) -Parent
    $projectRelativeDirectory = [IO.Path]::GetRelativePath($repositoryRoot, $projectDirectory).Replace('\', '/')
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
            throw "EDGE-SPLIT-LEDGER-001 deterministic authority build log lacks an exact required binding: $requiredText"
        }
    }
}

function Get-Sha512Base64 {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $stream = [IO.File]::OpenRead($PathValue)
    $hash = [Security.Cryptography.SHA512]::Create()
    try {
        return [Convert]::ToBase64String($hash.ComputeHash($stream))
    }
    finally {
        $hash.Dispose()
        $stream.Dispose()
    }
}

function Get-AssemblyIdentityKey {
    param([Parameter(Mandatory = $true)]$AssemblyFact)

    return "$([string]$AssemblyFact.assemblyName), Version=$([string]$AssemblyFact.assemblyVersion), Culture=$([string]$AssemblyFact.culture), PublicKeyToken=$([string]$AssemblyFact.publicKeyToken)"
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-GitBlobSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$RepositoryPath
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $repositoryRoot
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
            throw "EDGE-SPLIT-LEDGER-001 git show failed for $Commit`:${RepositoryPath}: $standardError"
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

    $repositoryPath = ConvertTo-RepositoryPath $WorktreePath
    if ([string]::IsNullOrWhiteSpace($repositoryPath)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority escapes the repository: $WorktreePath."
    }
    $treeEntry = Invoke-CapturedCommand git @('ls-tree', $Commit, '--', $repositoryPath)
    if ($treeEntry -notmatch '^100644 blob [0-9a-f]{40}\t' -or
        -not $treeEntry.EndsWith("`t$repositoryPath", [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority must be an exact 100644 regular blob at implementation HEAD: $repositoryPath."
    }
    if ((Get-GitBlobSha256 -Commit $Commit -RepositoryPath $repositoryPath) -cne (Get-Sha256 $WorktreePath)) {
        throw "EDGE-SPLIT-LEDGER-001 tracked authority differs between implementation HEAD and worktree: $repositoryPath."
    }
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

function Get-ProcessEnvironmentStateSnapshot {
    param([Parameter(Mandatory = $true)][string[]]$Names)

    $snapshot = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        $snapshot.Add($name, [pscustomobject][ordered]@{
                isDefined = $null -ne $value
                value = if ($null -eq $value) { '' } else { [string]$value }
            })
    }
    return $snapshot
}

function Restore-ProcessEnvironmentStateSnapshot {
    param(
        [Parameter(Mandatory = $true)][string[]]$Names,
        [Parameter(Mandatory = $true)]$Snapshot
    )

    foreach ($name in $Names) {
        if (-not $Snapshot.ContainsKey($name)) {
            throw "EDGE-SPLIT-LEDGER-001 environment snapshot does not contain required variable: $name."
        }
        $state = $Snapshot[$name]
        if ([bool]$state.isDefined) {
            [Environment]::SetEnvironmentVariable($name, [string]$state.value, 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
            # .NET on Unix can materialize SetEnvironmentVariable(name, null) as a defined empty
            # value. Remove the provider entry as a portability fallback, then prove absence.
            if ($null -ne [Environment]::GetEnvironmentVariable($name, 'Process')) {
                Remove-Item -LiteralPath "Env:$name" -Force -ErrorAction SilentlyContinue
            }
            if ($null -ne [Environment]::GetEnvironmentVariable($name, 'Process')) {
                throw "EDGE-SPLIT-LEDGER-001 undefined process environment variable could not be restored: $name."
            }
        }
    }
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return ''
    }
    return [string]$property.Value
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
            if ($properties.Count -eq 0) {
                return $stringComparer.Compare([string]$left, [string]$right)
            }
            foreach ($propertyName in $properties) {
                $leftValue = [string]$left.$propertyName
                $rightValue = [string]$right.$propertyName
                $result = $stringComparer.Compare($leftValue, $rightValue)
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
            [pscustomobject][ordered]@{
                Name = $displayNames[$key]
                Count = $groupItems.Count
                Group = $groupItems
            }
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

function Get-BatchRank {
    param([Parameter(Mandatory = $true)][string]$BatchId)

    return [int]$BatchId.Substring($BatchId.Length - 3)
}

function Get-InternalProjectOwnerFamily {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectName,
        [Parameter(Mandatory = $true)][string]$ProjectPath
    )

    $normalizedPath = $ProjectPath.Replace('\', '/')
    $expectedLeaf = "$ProjectName/$ProjectName.csproj"
    if (-not $normalizedPath.EndsWith($expectedLeaf, [StringComparison]::Ordinal)) { return 'Unknown' }
    if ($ProjectName -ceq 'IIoT.Edge.Application' -and
        $normalizedPath -ceq 'src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj') { return 'Application' }
    if ($ProjectName -ceq 'IIoT.Edge.Domain' -and
        $normalizedPath -ceq 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj') { return 'Domain' }
    if ($ProjectName -ceq 'IIoT.Edge.SharedKernel' -and
        $normalizedPath -ceq 'src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj') { return 'SharedKernel' }
    if ($ProjectName -ceq 'IIoT.Edge.UI.Shared' -and
        $normalizedPath -ceq 'src/Shared/IIoT.Edge.UI.Shared/IIoT.Edge.UI.Shared.csproj') { return 'UiShared' }
    if ((Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Module.Sdk', 'IIoT.Edge.Module.Contracts')) -and
        $normalizedPath.StartsWith('src/Modules/', [StringComparison]::Ordinal)) { return 'SdkContract' }
    if ((Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Architecture.Analyzers', 'IIoT.Edge.Module.Analyzers')) -and
        ($normalizedPath.StartsWith('src/Analyzers/', [StringComparison]::Ordinal) -or
         $normalizedPath.StartsWith('src/Modules/', [StringComparison]::Ordinal))) { return 'Analyzer' }
    if ($ProjectName.StartsWith('IIoT.Edge.Infrastructure.', [StringComparison]::Ordinal) -and
        $normalizedPath.StartsWith('src/Infrastructure/', [StringComparison]::Ordinal)) { return 'Infrastructure' }
    if ($ProjectName.StartsWith('IIoT.Edge.Presentation.', [StringComparison]::Ordinal) -and
        $normalizedPath.StartsWith('src/Presentation/', [StringComparison]::Ordinal)) { return 'Presentation' }
    if (($ProjectName.StartsWith('IIoT.Edge.Host.', [StringComparison]::Ordinal) -or
         (Test-OrdinalEqualsAny $ProjectName @('IIoT.Edge.Shell', 'IIoT.Edge.Launcher', 'IIoT.Edge.Installer'))) -and
        $normalizedPath.StartsWith('src/Edge/', [StringComparison]::Ordinal)) { return 'Host' }
    if ($ProjectName -ceq 'IIoT.Edge.RuntimeLayoutSync' -and
        $normalizedPath.StartsWith('src/Tools/', [StringComparison]::Ordinal)) { return 'Host' }
    return 'Unknown'
}

function Get-OwnerFamily {
    param(
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$ReferenceOwnerByAssembly
    )

    if ($ReferenceOwnerByAssembly.ContainsKey($AssemblyName)) {
        return [string]$ReferenceOwnerByAssembly[$AssemblyName]
    }
    return 'Unknown'
}

function Test-PluginOwnedIdentityCandidate {
    param([Parameter(Mandatory = $true)][string]$AssemblyName)

    return $AssemblyName.StartsWith('IIoT.Edge.Module.', [StringComparison]::Ordinal) -and
        -not (Test-OrdinalEqualsAny $AssemblyName @('IIoT.Edge.Module.Sdk', 'IIoT.Edge.Module.Contracts', 'IIoT.Edge.Module.Analyzers'))
}

function Test-SourceForbiddenOwnerFamily {
    param([Parameter(Mandatory = $true)][string]$OwnerFamily)

    return Test-OrdinalEqualsAny $OwnerFamily @('Application', 'Domain', 'SharedKernel', 'Infrastructure', 'Presentation', 'Host')
}

function Test-PackageForbiddenOwnerFamily {
    param([Parameter(Mandatory = $true)][string]$OwnerFamily)

    return (Test-SourceForbiddenOwnerFamily $OwnerFamily) -or
        (Test-OrdinalEqualsAny $OwnerFamily @('SdkContract', 'UiShared', 'Analyzer'))
}

function Get-PackageStaticInputFacts {
    param(
        [Parameter(Mandatory = $true)][string]$PluginRoot,
        [Parameter(Mandatory = $true)][string]$TargetAssemblyPath,
        [Parameter(Mandatory = $true)][string]$ManifestSourcePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$PluginOwnedAssemblyPaths
    )

    $targetDirectory = Split-Path $TargetAssemblyPath -Parent
    $facts = [Collections.Generic.List[object]]::new()
    $packagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $addFact = {
        param(
            [string]$PackagePath,
            [string]$SourcePath,
            [string]$Category,
            [bool]$Required
        )
        if (-not $packagePaths.Add($PackagePath)) {
            throw "EDGE-SPLIT-LEDGER-001 current build has Windows-colliding package static inputs: $PackagePath."
        }
        $repositoryPath = ConvertTo-RepositoryPath $SourcePath
        if ([string]::IsNullOrWhiteSpace($repositoryPath)) {
            throw "EDGE-SPLIT-LEDGER-001 package static input must stay inside the repository: $SourcePath."
        }
        $facts.Add([pscustomobject][ordered]@{
                packagePath = $PackagePath
                sourcePath = $repositoryPath
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
        throw 'EDGE-SPLIT-LEDGER-001 current build output plugin.json is absent or differs from its source bytes.'
    }
    & $addFact 'plugin.json' $ManifestSourcePath 'plugin-manifest' $true

    $allowedResourceExtensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @('.axaml', '.json', '.png', '.jpg', '.jpeg', '.svg', '.webp', '.ico', '.ttf', '.otf', '.resx', '.resources')) {
        [void]$allowedResourceExtensions.Add($extension)
    }
    foreach ($topLevelDirectory in @('Config', 'Resources')) {
        $sourceRoot = Join-Path $PluginRoot $topLevelDirectory
        $outputRoot = Join-Path $targetDirectory $topLevelDirectory
        $expectedPackagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { continue }
        foreach ($sourceFile in @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Ordinal FullName)) {
            $packagePath = [IO.Path]::GetRelativePath($PluginRoot, $sourceFile.FullName).Replace('\', '/')
            $isSafeName = -not (Test-InvariantPattern $packagePath '(?:^|/)(?:tests?|testing|testkit|visualtestdata)(?:/|$)') -and
                -not (Test-InvariantPattern $packagePath '(secret|password|token|credential|connectionstring|edge\.db|queue|logs?|recipes?|excel)')
            $category = if ($topLevelDirectory -ceq 'Config' -and
                $packagePath.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -and $isSafeName) {
                'plugin-config'
            }
            elseif ($topLevelDirectory -ceq 'Resources' -and
                $allowedResourceExtensions.Contains([IO.Path]::GetExtension($packagePath)) -and $isSafeName) {
                'plugin-resource'
            }
            else { '' }
            if ([string]::IsNullOrWhiteSpace($category)) { continue }
            if (-not $expectedPackagePaths.Add($packagePath)) {
                throw "EDGE-SPLIT-LEDGER-001 package source allowlist contains Windows-colliding paths: $packagePath."
            }
            $outputPath = [IO.Path]::GetFullPath((Join-Path $targetDirectory $packagePath))
            if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or
                $sourceFile.Length -ne (Get-Item -LiteralPath $outputPath).Length -or
                (Get-Sha256 $sourceFile.FullName) -cne (Get-Sha256 $outputPath)) {
                throw "EDGE-SPLIT-LEDGER-001 package source is absent or changed in current build output: $packagePath."
            }
            & $addFact $packagePath $sourceFile.FullName $category $true
        }
        if (Test-Path -LiteralPath $outputRoot -PathType Container) {
            foreach ($outputFile in @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Ordinal FullName)) {
                $packagePath = [IO.Path]::GetRelativePath($targetDirectory, $outputFile.FullName).Replace('\', '/')
                $isAllowedOutput = ($topLevelDirectory -ceq 'Config' -and
                        $packagePath.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) -or
                    ($topLevelDirectory -ceq 'Resources' -and
                        $allowedResourceExtensions.Contains([IO.Path]::GetExtension($packagePath)))
                if ($isAllowedOutput -and -not $expectedPackagePaths.Contains($packagePath)) {
                    throw "EDGE-SPLIT-LEDGER-001 current build output has no exact source allowlist owner: $packagePath."
                }
            }
        }
    }

    foreach ($assemblyPath in @($PluginOwnedAssemblyPaths | Sort-Ordinal -Unique)) {
        $pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
        if (-not (Test-Path -LiteralPath $pdbPath -PathType Leaf)) { continue }
        & $addFact ([IO.Path]::GetFileName($pdbPath)) $pdbPath 'plugin-symbols' $false
    }
    return @($facts.ToArray() | Sort-Ordinal packagePath)
}

function Test-PathInsideRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$PathValue
    )
    $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($Root), [IO.Path]::GetFullPath($PathValue))
    return $relative -ne '..' -and
        -not $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -and
        -not [IO.Path]::IsPathRooted($relative)
}

function Assert-NoPathReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$FullPath
    )
    $rootPath = [IO.Path]::GetFullPath($Root)
    $path = [IO.Path]::GetFullPath($FullPath)
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw "EDGE-SPLIT-LEDGER-001 authority root is not a real directory: $rootPath."
    }
    $rootItem = Get-Item -LiteralPath $rootPath -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootItem.LinkTarget)) {
        throw "EDGE-SPLIT-LEDGER-001 authority root must not be a symlink/reparse point: $rootPath."
    }
    if (-not (Test-PathInsideRoot $rootPath $path)) {
        throw "EDGE-SPLIT-LEDGER-001 authority path escapes its declared root: $path."
    }
    $current = $rootPath
    foreach ($segment in [IO.Path]::GetRelativePath($rootPath, $path).Split(
            [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
            throw "EDGE-SPLIT-LEDGER-001 authority path must not traverse symlink/reparse points: $current."
        }
    }
}

function Get-AuthorityRootTokenMappings {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$NuGetRoot,
        [Parameter(Mandatory = $true)][string]$DotnetRoot
    )

    $mappings = [object[]]@(
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($RepositoryRoot).Replace('\', '/').TrimEnd('/'); token = '$REPOSITORY' },
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($NuGetRoot).Replace('\', '/').TrimEnd('/'); token = '$NUGET_PACKAGES' },
        [pscustomobject]@{ root = [IO.Path]::GetFullPath($DotnetRoot).Replace('\', '/').TrimEnd('/'); token = '$DOTNET_ROOT' }
    )
    [Array]::Sort($mappings, [Collections.Generic.Comparer[object]]::Create([Comparison[object]]{
                param($left, $right)
                $lengthResult = ([string]$right.root).Length.CompareTo(([string]$left.root).Length)
                if ($lengthResult -ne 0) { return $lengthResult }
                return [StringComparer]::Ordinal.Compare([string]$left.root, [string]$right.root)
            }))
    return $mappings
}

function Get-TrackedNuGetSourceSet {
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    [xml]$config = Get-Content -LiteralPath $ConfigPath -Raw
    if ($null -eq $config.configuration -or $null -eq $config.configuration.packageSources -or
        @($config.SelectNodes('//packageSourceCredentials')).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 tracked NuGet.Config must have packageSources and no credential section.'
    }
    $clearCount = 0
    $sources = [Collections.Generic.List[string]]::new()
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($node in $config.configuration.packageSources.ChildNodes) {
        if ($node.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        if ([string]$node.LocalName -ceq 'clear') {
            $clearCount++
            continue
        }
        if ([string]$node.LocalName -cne 'add') {
            throw "EDGE-SPLIT-LEDGER-001 tracked NuGet.Config contains unsupported packageSources operation: $($node.LocalName)."
        }
        $key = [string]$node.GetAttribute('key')
        $value = [string]$node.GetAttribute('value')
        if ([string]::IsNullOrWhiteSpace($key) -or -not $keys.Add($key)) {
            throw 'EDGE-SPLIT-LEDGER-001 tracked NuGet.Config package-source keys must be nonempty and Windows-distinct.'
        }
        $uri = $null
        if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or
            -not (Test-OrdinalEqualsAny $uri.Scheme @('http', 'https')) -or
            -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
            -not [string]::IsNullOrWhiteSpace($uri.Query) -or
            -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
            throw "EDGE-SPLIT-LEDGER-001 tracked NuGet.Config package source must be credential-free HTTP(S): $value."
        }
        $sources.Add($value)
    }
    if ($clearCount -ne 1 -or $sources.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 tracked NuGet.Config must clear ambient sources exactly once and declare at least one source.'
    }
    return @($sources.ToArray() | Sort-Ordinal -Unique)
}

function ConvertTo-AuthorityTokenText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory = $true)][object[]]$RootMappings
    )

    $normalized = $Text.Replace("`r`n", "`n").Replace('\', '/')
    $options = if ([OperatingSystem]::IsWindows()) {
        [Text.RegularExpressions.RegexOptions]::IgnoreCase
    }
    else { [Text.RegularExpressions.RegexOptions]::None }
    foreach ($mapping in $RootMappings) {
        $pattern = "(?<![A-Za-z0-9_.-])$([Text.RegularExpressions.Regex]::Escape([string]$mapping.root))(?=`$|/)"
        $replacement = [string]$mapping.token
        $normalized = [Text.RegularExpressions.Regex]::Replace(
            $normalized,
            $pattern,
            [Text.RegularExpressions.MatchEvaluator]{ param($match) return $replacement },
            $options)
    }
    if ($normalized -match '^(?:[A-Za-z]:/|/)' -or
        $normalized -match '(?<![A-Za-z0-9])(?:[A-Za-z]:/|/(?:Users|home|root|var|tmp|opt|usr)/)') {
        throw "EDGE-SPLIT-LEDGER-001 semantic authority retained an unapproved absolute path: $normalized."
    }
    return $normalized
}

function ConvertTo-RestoreProjectionValue {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory = $true)][object[]]$RootMappings,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$JsonPointer
    )

    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) {
        return ConvertTo-AuthorityTokenText -Text ([string]$Value) -RootMappings $RootMappings
    }
    if ($Value -is [bool] -or $Value -is [byte] -or $Value -is [int16] -or
        $Value -is [int32] -or $Value -is [int64] -or $Value -is [decimal] -or
        $Value -is [double] -or $Value -is [single]) {
        return $Value
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [Management.Automation.PSCustomObject]) {
        $projectedItems = @($Value | ForEach-Object {
                ConvertTo-RestoreProjectionValue -Value $_ -RootMappings $RootMappings -JsonPointer "$JsonPointer/*"
            })
        $isDeclaredSet = $JsonPointer -ceq '/project/restore/configFilePaths' -or
            $JsonPointer -match '^/libraries/[^/]+/files$'
        if ($isDeclaredSet) {
            if (@($projectedItems | Where-Object { $_ -isnot [string] }).Count -ne 0) {
                throw "EDGE-SPLIT-LEDGER-001 declared restore set contains a non-string value: $JsonPointer."
            }
            return @($projectedItems | Sort-Ordinal -Unique)
        }
        return $projectedItems
    }
    $normalizedMembers = [Collections.Generic.List[object]]::new()
    $normalizedKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Value.PSObject.Properties) {
        $normalizedName = ConvertTo-AuthorityTokenText `
            -Text ([string]$property.Name) -RootMappings $RootMappings
        if (-not $normalizedKeys.Add($normalizedName)) {
            throw "EDGE-SPLIT-LEDGER-001 restore JSON property keys collide after root tokenization: $JsonPointer."
        }
        $normalizedMembers.Add([pscustomobject]@{
                name = $normalizedName
                value = $property.Value
            })
    }
    $sortedMembers = $normalizedMembers.ToArray()
    [Array]::Sort($sortedMembers, [Collections.Generic.Comparer[object]]::Create([Comparison[object]]{
                param($left, $right)
                return [StringComparer]::Ordinal.Compare([string]$left.name, [string]$right.name)
            }))
    $ordered = [ordered]@{}
    foreach ($member in $sortedMembers) {
        $escapedName = ([string]$member.name).Replace('~', '~0').Replace('/', '~1')
        $ordered[[string]$member.name] = ConvertTo-RestoreProjectionValue -Value $member.value `
            -RootMappings $RootMappings -JsonPointer "$JsonPointer/$escapedName"
    }
    return [pscustomobject]$ordered
}

function ConvertTo-NuGetXmlInfoset {
    param(
        [Parameter(Mandatory = $true)][Xml.XmlNode]$Node,
        [Parameter(Mandatory = $true)][object[]]$RootMappings
    )

    if ($Node.NodeType -ne [Xml.XmlNodeType]::Element) {
        throw "EDGE-SPLIT-LEDGER-001 NuGet XML projection received a non-element root: $($Node.NodeType)."
    }
    $attributes = @($Node.Attributes | ForEach-Object {
            [pscustomobject][ordered]@{
                namespace = [string]$_.NamespaceURI
                name = [string]$_.LocalName
                value = ConvertTo-AuthorityTokenText -Text ([string]$_.Value) -RootMappings $RootMappings
            }
        } | Sort-Ordinal namespace, name)
    $children = [Collections.Generic.List[object]]::new()
    foreach ($child in $Node.ChildNodes) {
        if ($child.NodeType -eq [Xml.XmlNodeType]::Element) {
            $children.Add((ConvertTo-NuGetXmlInfoset -Node $child -RootMappings $RootMappings))
        }
        elseif ($child.NodeType -eq [Xml.XmlNodeType]::Text -or
            $child.NodeType -eq [Xml.XmlNodeType]::CDATA) {
            if (-not [string]::IsNullOrWhiteSpace([string]$child.Value)) {
                $children.Add([pscustomobject][ordered]@{
                        kind = 'text'
                        value = ConvertTo-AuthorityTokenText -Text ([string]$child.Value) -RootMappings $RootMappings
                    })
            }
        }
        elseif ($child.NodeType -ne [Xml.XmlNodeType]::Whitespace -and
            $child.NodeType -ne [Xml.XmlNodeType]::SignificantWhitespace) {
            throw "EDGE-SPLIT-LEDGER-001 generated NuGet XML contains an unsupported infoset node: $($child.NodeType)."
        }
    }
    return [pscustomobject][ordered]@{
        kind = 'element'
        namespace = [string]$Node.NamespaceURI
        name = [string]$Node.LocalName
        attributes = $attributes
        children = $children.ToArray()
    }
}

function Get-RestoreSemanticContentFact {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$NuGetRoot,
        [Parameter(Mandatory = $true)][string]$DotnetRoot
    )

    $rootMappings = @(Get-AuthorityRootTokenMappings -RepositoryRoot $RepositoryRoot -NuGetRoot $NuGetRoot -DotnetRoot $DotnetRoot)
    $fileName = [IO.Path]::GetFileName($PathValue)
    if ($fileName -ceq 'project.assets.json') {
        $assets = Get-Content -LiteralPath $PathValue -Raw | ConvertFrom-Json -Depth 100
        $project = Get-ExactJsonProperty $assets 'project'
        $restore = Get-ExactJsonProperty $project 'restore'
        $configPaths = @((Get-ExactJsonProperty $restore 'configFilePaths'))
        $expectedConfigPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'NuGet.Config'))
        if ($configPaths.Count -ne 1 -or
            -not (Test-ResolvedPathIdentityEqual ([string]$configPaths[0]) $expectedConfigPath)) {
            throw 'EDGE-SPLIT-LEDGER-001 restore assets must be produced solely from the tracked repository NuGet.Config.'
        }
        if (-not (Test-ResolvedPathIdentityEqual `
            ([string](Get-ExactJsonProperty $restore 'packagesPath')) $NuGetRoot)) {
            throw 'EDGE-SPLIT-LEDGER-001 restore assets global packages folder differs from evaluated NuGetPackageRoot.'
        }
        $sources = Get-ExactJsonProperty $restore 'sources'
        $expectedSources = @(Get-TrackedNuGetSourceSet (Join-Path $RepositoryRoot 'NuGet.Config'))
        $actualSources = @($sources.PSObject.Properties | ForEach-Object { [string]$_.Name } | Sort-Ordinal -Unique)
        if (($expectedSources -join "`n") -cne ($actualSources -join "`n")) {
            throw 'EDGE-SPLIT-LEDGER-001 restore assets source set differs from tracked NuGet.Config.'
        }
        foreach ($sourceProperty in $sources.PSObject.Properties) {
            $sourceUri = $null
            if ([Uri]::TryCreate([string]$sourceProperty.Name, [UriKind]::Absolute, [ref]$sourceUri) -and
                -not [string]::IsNullOrWhiteSpace($sourceUri.UserInfo)) {
                throw 'EDGE-SPLIT-LEDGER-001 restore assets must not contain credential-bearing package-source URLs.'
            }
        }
        $projection = [pscustomobject][ordered]@{
            policy = 'edge-restore-semantic-v1'
            documentKind = 'project-assets'
            content = ConvertTo-RestoreProjectionValue -Value $assets -RootMappings $rootMappings -JsonPointer ''
        }
    }
    elseif ($fileName.EndsWith('.csproj.nuget.g.props', [StringComparison]::Ordinal) -or
        $fileName.EndsWith('.csproj.nuget.g.targets', [StringComparison]::Ordinal)) {
        [xml]$xmlDocument = Get-Content -LiteralPath $PathValue -Raw
        if ($null -eq $xmlDocument.DocumentElement) {
            throw "EDGE-SPLIT-LEDGER-001 generated NuGet XML has no document element: $PathValue."
        }
        $projection = [pscustomobject][ordered]@{
            policy = 'edge-restore-semantic-v1'
            documentKind = if ($fileName.EndsWith('.props', [StringComparison]::Ordinal)) { 'nuget-generated-props' } else { 'nuget-generated-targets' }
            content = ConvertTo-NuGetXmlInfoset -Node $xmlDocument.DocumentElement -RootMappings $rootMappings
        }
    }
    else {
        throw "EDGE-SPLIT-LEDGER-001 unsupported restore semantic authority input: $PathValue."
    }
    $projectionJson = ($projection | ConvertTo-Json -Depth 100 -Compress) + "`n"
    $projectionBytes = [Text.UTF8Encoding]::new($false).GetBytes($projectionJson)
    return [pscustomobject][ordered]@{
        representation = 'restore-semantic-v1'
        size = [long]$projectionBytes.Length
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectionBytes)).ToLowerInvariant()
    }
}

function Get-CompilerConfigSemanticContentFact {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$NuGetRoot,
        [Parameter(Mandatory = $true)][string]$DotnetRoot
    )

    $rootMappings = @(Get-AuthorityRootTokenMappings `
        -RepositoryRoot $RepositoryRoot -NuGetRoot $NuGetRoot -DotnetRoot $DotnetRoot)
    $projectedText = ConvertTo-AuthorityTokenText `
        -Text (Get-Content -LiteralPath $PathValue -Raw) -RootMappings $rootMappings
    if ($projectedText.Contains("`r", [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 generated compiler config retained a noncanonical line ending: $PathValue."
    }
    $projectionBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        "edge-compiler-config-semantic-v1`n$projectedText")
    return [pscustomobject][ordered]@{
        representation = 'compiler-config-semantic-v1'
        size = [long]$projectionBytes.Length
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($projectionBytes)).ToLowerInvariant()
    }
}

function Get-ExecutedToolchainClosure {
    param(
        [Parameter(Mandatory = $true)][string[]]$DiagnosticLogPaths,
        [Parameter(Mandatory = $true)][string]$SdkDirectory,
        [Parameter(Mandatory = $true)][string[]]$ExplicitAssemblyPaths
    )

    $sdkRoot = [IO.Path]::GetFullPath($SdkDirectory)
    $directAssemblies = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $addDirect = {
        param([string]$Candidate)
        if ([string]::IsNullOrWhiteSpace($Candidate)) { return }
        $fullPath = [IO.Path]::GetFullPath($Candidate)
        if (-not (Test-PathInsideRoot $sdkRoot $fullPath) -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return }
        if ($directAssemblies.ContainsKey($fullPath)) {
            if ([string]$directAssemblies[$fullPath] -cne $fullPath) {
                throw "EDGE-SPLIT-LEDGER-001 executed toolchain paths collide under Windows case semantics: $($directAssemblies[$fullPath]) | $fullPath."
            }
            return
        }
        $directAssemblies.Add($fullPath, $fullPath)
    }
    foreach ($assemblyPath in $ExplicitAssemblyPaths) { & $addDirect $assemblyPath }
    foreach ($logPath in $DiagnosticLogPaths) {
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 required diagnostic build log does not exist: $logPath."
        }
        $logText = Get-Content -LiteralPath $logPath -Raw
        foreach ($match in [Text.RegularExpressions.Regex]::Matches(
                $logText,
                '(?<path>(?:[A-Za-z]:[\\/]|/)[^\s"''*;<>]+\.dll)',
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            & $addDirect ([string]$match.Groups['path'].Value)
        }
    }
    if ($directAssemblies.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 diagnostic builds did not identify any executed SDK assemblies.'
    }
    $facts = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $addFact = {
        param([string]$PathValue, [string]$Role)
        $fullPath = [IO.Path]::GetFullPath($PathValue)
        if ($facts.ContainsKey($fullPath)) {
            $fact = $facts[$fullPath]
            [void]$fact.roles.Add($Role)
            return
        }
        $roles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        [void]$roles.Add($Role)
        $facts.Add($fullPath, [pscustomobject]@{ path = $fullPath; roles = $roles })
    }
    foreach ($directPath in @($directAssemblies.Values | Sort-Ordinal)) {
        & $addFact $directPath 'executed-toolchain-assembly'
        $directory = Split-Path $directPath -Parent
        foreach ($closureFile in @(Get-ChildItem -LiteralPath $directory -File | Where-Object {
                    Test-OrdinalEqualsAny $_.Extension @('.dll', '.json')
                } | Sort-Ordinal FullName)) {
            & $addFact $closureFile.FullName 'executed-toolchain-closure'
        }
    }
    return @($facts.Values | ForEach-Object {
            [pscustomobject][ordered]@{
                path = [string]$_.path
                roles = @($_.roles | Sort-Ordinal)
            }
        } | Sort-Ordinal path)
}

function Assert-NuGetPluginDiscoveryIsolation {
    param(
        [Parameter(Mandatory = $true)][string[]]$EmptyDiscoveryDirectories,
        [Parameter(Mandatory = $true)][string[]]$RestoreDiagnosticLogPaths
    )

    foreach ($discoveryDirectory in $EmptyDiscoveryDirectories) {
        if (-not (Test-Path -LiteralPath $discoveryDirectory -PathType Container)) {
            throw "EDGE-SPLIT-LEDGER-001 isolated NuGet plugin discovery directory does not exist: $discoveryDirectory."
        }
        Assert-NoPathReparsePoint $discoveryDirectory $discoveryDirectory
        if ($null -ne (Get-ChildItem -LiteralPath $discoveryDirectory -Force | Select-Object -First 1)) {
            throw "EDGE-SPLIT-LEDGER-001 isolated NuGet plugin discovery directory is not empty: $discoveryDirectory."
        }
    }
    foreach ($diagnosticLogPath in $RestoreDiagnosticLogPaths) {
        if (-not (Test-Path -LiteralPath $diagnosticLogPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 required NuGet restore diagnostic log does not exist: $diagnosticLogPath."
        }
        $diagnosticText = Get-Content -LiteralPath $diagnosticLogPath -Raw
        if ($diagnosticText -match '(?i)(?:^|[/\\])\.nuget[/\\]plugins(?:[/\\]|$)' -or
            $diagnosticText -match '(?i)CredentialProvider\.Microsoft(?:[/\\]|\.|\s)' -or
            $diagnosticText -match '(?im)(?:loading|loaded|launching|executing|invoking|using|discovered|found)[^\r\n]{0,160}(?:credential\s*provider|credentialprovider|NuGet\s+plugin)') {
            throw "EDGE-SPLIT-LEDGER-001 NuGet restore diagnostics disclose an external plugin/credential-provider discovery path: $diagnosticLogPath."
        }
    }
}

function Get-MsBuildAuthorityInputs {
    param(
        [Parameter(Mandatory = $true)][string[]]$SeedProjects,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$TemporaryRoot,
        [Parameter(Mandatory = $true)][string]$DotnetRoot,
        [Parameter(Mandatory = $true)][string]$NugetRoot,
        [Parameter(Mandatory = $true)][string[]]$DeterministicBuildArguments,
        [Parameter(Mandatory = $true)][object[]]$ExecutedToolchainFacts
    )

    if ($DeterministicBuildArguments.Count -ne 7) {
        throw 'EDGE-SPLIT-LEDGER-001 MSBuild authority collection requires the complete deterministic build vector.'
    }
    $dotnetRoot = [IO.Path]::GetFullPath($DotnetRoot)
    $nugetRoot = [IO.Path]::GetFullPath($NugetRoot)
    Assert-NoPathReparsePoint $repositoryRoot $repositoryRoot
    Assert-NoPathReparsePoint $dotnetRoot $dotnetRoot
    Assert-NoPathReparsePoint $nugetRoot $nugetRoot
    $factsByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $rolesByPath = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
    $authorityKeyCasing = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $projectQueue = [Collections.Generic.Queue[string]]::new()
    $seenProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $projectPathCasing = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $getCompiledOutputPaths = {
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
            if (-not (Test-PathInsideRoot $repositoryRoot $absolutePath)) {
                throw "EDGE-SPLIT-LEDGER-001 evaluated compiled output escapes the repository: $ProjectPathValue|$absolutePath."
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
            throw "EDGE-SPLIT-LEDGER-001 MSBuild authority collection could not resolve compiled outputs: $ProjectPathValue."
        }
        $compiledAndDebugPaths = [Collections.Generic.List[string]]::new()
        foreach ($path in $paths) {
            $compiledAndDebugPaths.Add($path)
            $pdbPath = [IO.Path]::ChangeExtension($path, '.pdb')
            if ($seenPaths.Add($pdbPath)) { $compiledAndDebugPaths.Add($pdbPath) }
        }
        return [string[]]$compiledAndDebugPaths.ToArray()
    }
    $getCompiledOutputState = {
        param([string]$OutputPath)
        if (Test-Path -LiteralPath $OutputPath) {
            if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 evaluated compiled output is not a regular file: $OutputPath."
            }
            Assert-NoPathReparsePoint $repositoryRoot $OutputPath
            return [pscustomobject][ordered]@{
                exists = $true
                size = [long](Get-Item -LiteralPath $OutputPath -Force).Length
                sha256 = Get-Sha256 $OutputPath
            }
        }
        return [pscustomobject][ordered]@{ exists = $false; size = [long]-1; sha256 = '' }
    }
    $enqueueProject = {
        param([string]$ProjectPathValue)
        $fullProjectPath = [IO.Path]::GetFullPath($ProjectPathValue)
        if ($projectPathCasing.ContainsKey($fullProjectPath)) {
            if ([string]$projectPathCasing[$fullProjectPath] -cne $fullProjectPath) {
                throw "EDGE-SPLIT-LEDGER-001 MSBuild ProjectReference paths collide under Windows case semantics: $($projectPathCasing[$fullProjectPath]) | $fullProjectPath."
            }
            return
        }
        $projectPathCasing.Add($fullProjectPath, $fullProjectPath)
        $projectQueue.Enqueue($fullProjectPath)
    }
    foreach ($seed in @($SeedProjects | Sort-Ordinal -Unique)) { & $enqueueProject $seed }

    $addAuthority = {
        param([string]$PathValue, [string]$Role)
        if ([string]::IsNullOrWhiteSpace($PathValue)) {
            throw "EDGE-SPLIT-LEDGER-001 required MSBuild authority path is empty: role=$Role."
        }
        if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 required MSBuild authority input is missing or not a regular file: role=$Role path=$PathValue."
        }
        $fullPath = [IO.Path]::GetFullPath($PathValue)
        $origin = ''
        $normalizedPath = ''
        if (Test-PathInsideRoot $repositoryRoot $fullPath) {
            $normalizedPath = ConvertTo-RepositoryPath $fullPath
            $origin = if (Test-InvariantPattern $normalizedPath '(^|/)(?:obj|bin)(?:/|$)') { 'generated-repository' } else { 'tracked-repository' }
            Assert-NoPathReparsePoint $repositoryRoot $fullPath
        }
        elseif (Test-PathInsideRoot $nugetRoot $fullPath) {
            $normalizedPath = 'nuget-cache/' + [IO.Path]::GetRelativePath($nugetRoot, $fullPath).Replace('\', '/')
            $origin = 'nuget-cache'
            Assert-NoPathReparsePoint $nugetRoot $fullPath
        }
        elseif (Test-PathInsideRoot $dotnetRoot $fullPath) {
            $normalizedPath = 'dotnet-toolchain/' + [IO.Path]::GetRelativePath($dotnetRoot, $fullPath).Replace('\', '/')
            $origin = 'dotnet-toolchain'
            Assert-NoPathReparsePoint $dotnetRoot $fullPath
        }
        else {
            throw "EDGE-SPLIT-LEDGER-001 MSBuild authority input has no approved repository/NuGet/toolchain root: $fullPath."
        }
        $key = "$origin|$normalizedPath"
        if ($authorityKeyCasing.ContainsKey($key)) {
            if ([string]$authorityKeyCasing[$key] -cne $key) {
                throw "EDGE-SPLIT-LEDGER-001 authority inputs collide under Windows case semantics: $($authorityKeyCasing[$key]) | $key."
            }
        }
        else { $authorityKeyCasing.Add($key, $key) }
        $contentFact = if ($origin -ceq 'generated-repository' -and
            ($normalizedPath.EndsWith('/project.assets.json', [StringComparison]::Ordinal) -or
             $normalizedPath.EndsWith('.csproj.nuget.g.props', [StringComparison]::Ordinal) -or
             $normalizedPath.EndsWith('.csproj.nuget.g.targets', [StringComparison]::Ordinal))) {
            Get-RestoreSemanticContentFact -PathValue $fullPath -RepositoryRoot $repositoryRoot `
                -NuGetRoot $nugetRoot -DotnetRoot $dotnetRoot
        }
        elseif ($origin -ceq 'generated-repository' -and
            $normalizedPath.EndsWith('.GeneratedMSBuildEditorConfig.editorconfig', [StringComparison]::Ordinal)) {
            Get-CompilerConfigSemanticContentFact -PathValue $fullPath -RepositoryRoot $repositoryRoot `
                -NuGetRoot $nugetRoot -DotnetRoot $dotnetRoot
        }
        else {
            [pscustomobject][ordered]@{
                representation = 'raw-sha256'
                size = [long](Get-Item -LiteralPath $fullPath -Force).Length
                sha256 = Get-Sha256 $fullPath
            }
        }
        $representation = [string]$contentFact.representation
        $sha256 = [string]$contentFact.sha256
        $size = [long]$contentFact.size
        if ($factsByPath.ContainsKey($key)) {
            $existing = $factsByPath[$key]
            if ([string]$existing.representation -cne $representation -or
                [string]$existing.sha256 -cne $sha256 -or [long]$existing.size -ne [long]$size) {
                throw "EDGE-SPLIT-LEDGER-001 MSBuild authority bytes changed during evaluation: $normalizedPath."
            }
        }
        else {
            $factsByPath.Add($key, [pscustomobject][ordered]@{
                    path = $normalizedPath
                    origin = $origin
                    representation = $representation
                    roles = @()
                    size = [long]$size
                    sha256 = $sha256
                })
            $rolesByPath.Add($key, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
        }
        [void]$rolesByPath[$key].Add($Role)
    }

    foreach ($rootFileName in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.Config')) {
        $rootConfigurationPath = Join-Path $repositoryRoot $rootFileName
        if (Test-Path -LiteralPath $rootConfigurationPath -PathType Leaf) {
            & $addAuthority $rootConfigurationPath 'root-configuration'
        }
    }
    & $addAuthority (Join-Path $repositoryRoot 'eng/EdgePluginContractDeterministicBuild.targets') `
        'deterministic-build-targets'
    foreach ($toolchainFact in $ExecutedToolchainFacts) {
        foreach ($role in @($toolchainFact.roles)) {
            & $addAuthority ([string]$toolchainFact.path) ([string]$role)
        }
    }
    while ($projectQueue.Count -ne 0) {
        $projectPath = $projectQueue.Dequeue()
        if (-not $seenProjects.Add($projectPath)) { continue }
        Assert-NoPathReparsePoint $repositoryRoot $projectPath
        & $addAuthority $projectPath 'evaluated-project'
        $outputPathArguments = [string[]](@(
            'msbuild', $projectPath, '-nologo', '-noAutoResponse',
            "-p:Configuration=$Configuration"
        ) + $DeterministicBuildArguments + @(
            '-getProperty:TargetPath,TargetRefPath,IntermediateAssembly,IntermediateOutputPath,TargetFileName',
            '-getItem:IntermediateRefAssembly'
        ))
        $outputPathEvaluation = (Invoke-CapturedCommand dotnet $outputPathArguments) | ConvertFrom-Json -Depth 100
        $compiledOutputStatesBefore = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($compiledOutputPath in @(& $getCompiledOutputPaths `
                    $outputPathEvaluation.Properties $outputPathEvaluation.Items $projectPath)) {
            $compiledOutputStatesBefore.Add($compiledOutputPath, (& $getCompiledOutputState $compiledOutputPath))
        }

        $arguments = [string[]](@(
            'msbuild', $projectPath, '-nologo', '-noAutoResponse', '-t:ResolveReferences',
            "-p:Configuration=$Configuration"
        ) + $DeterministicBuildArguments + @(
            '-getProperty:ProjectAssetsFile,NuGetPackageRoot,NetCoreTargetingPackRoot',
            '-getItem:Compile,ProjectReference,AvaloniaResource,Content,None,Page,EmbeddedResource,AdditionalFiles,Analyzer'
        ))
        $projectEvaluation = (Invoke-CapturedCommand dotnet $arguments) | ConvertFrom-Json -Depth 100
        foreach ($itemType in @('Compile', 'AvaloniaResource', 'Content', 'None', 'Page', 'EmbeddedResource', 'AdditionalFiles', 'Analyzer')) {
            foreach ($item in @($projectEvaluation.Items.$itemType)) {
                $fullPath = Get-OptionalProperty $item 'FullPath'
                & $addAuthority $fullPath "item-$($itemType.ToLowerInvariant())"
            }
        }
        foreach ($projectReference in @($projectEvaluation.Items.ProjectReference)) {
            $referenceProjectPath = Get-OptionalProperty $projectReference 'FullPath'
            if ([string]::IsNullOrWhiteSpace($referenceProjectPath)) {
                throw 'EDGE-SPLIT-LEDGER-001 evaluated ProjectReference lacks a required FullPath.'
            }
            Assert-NoPathReparsePoint $repositoryRoot $referenceProjectPath
            & $enqueueProject $referenceProjectPath
        }

        $compilerConfigArguments = [string[]](@(
                'msbuild', $projectPath, '-nologo', '-noAutoResponse', '-t:CoreCompile',
                "-p:Configuration=$Configuration", '-p:SkipCompilerExecution=true',
                '-p:BuildProjectReferences=false', '-p:UseSharedCompilation=false',
                '-p:TargetsTriggeredByCompilation='
            ) + $DeterministicBuildArguments + @(
                '-getProperty:GeneratedMSBuildEditorConfigFile',
                '-getItem:EditorConfigFiles,GlobalAnalyzerConfigFiles,AnalyzerConfigFiles'))
        $compilerConfigEvaluation = (Invoke-CapturedCommand dotnet $compilerConfigArguments) |
            ConvertFrom-Json -Depth 100
        foreach ($compiledOutputPath in @($compiledOutputStatesBefore.Keys | Sort-Ordinal)) {
            $beforeState = $compiledOutputStatesBefore[$compiledOutputPath]
            $afterState = & $getCompiledOutputState $compiledOutputPath
            if ([bool]$beforeState.exists -ne [bool]$afterState.exists -or
                [long]$beforeState.size -ne [long]$afterState.size -or
                [string]$beforeState.sha256 -cne [string]$afterState.sha256) {
                throw "EDGE-SPLIT-LEDGER-001 MSBuild authority collection mutated compiled output bytes: $projectPath|$compiledOutputPath."
            }
        }
        $editorConfigPaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($editorConfigItem in @($compilerConfigEvaluation.Items.EditorConfigFiles)) {
            $editorConfigPath = Get-OptionalProperty $editorConfigItem 'FullPath'
            if ([string]::IsNullOrWhiteSpace($editorConfigPath) -or
                -not (Test-Path -LiteralPath $editorConfigPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 evaluated compiler EditorConfigFiles item is missing: $projectPath|$editorConfigPath."
            }
            $editorConfigPath = [IO.Path]::GetFullPath($editorConfigPath)
            $editorConfigExtension = [IO.Path]::GetExtension($editorConfigPath)
            if (-not (Test-OrdinalEqualsAny $editorConfigExtension @('.editorconfig', '.globalconfig'))) {
                throw "EDGE-SPLIT-LEDGER-001 compiler analyzer-config item has an unsupported extension: $editorConfigPath."
            }
            if ($editorConfigPaths.ContainsKey($editorConfigPath)) {
                if ([string]$editorConfigPaths[$editorConfigPath] -cne $editorConfigPath) {
                    throw "EDGE-SPLIT-LEDGER-001 compiler analyzer-config paths collide under Windows semantics: $($editorConfigPaths[$editorConfigPath]) | $editorConfigPath."
                }
            }
            else { $editorConfigPaths.Add($editorConfigPath, $editorConfigPath) }
            & $addAuthority $editorConfigPath 'item-editorconfigfiles'
        }
        foreach ($analyzerConfigItem in @($compilerConfigEvaluation.Items.AnalyzerConfigFiles)) {
            $analyzerConfigPath = Get-OptionalProperty $analyzerConfigItem 'FullPath'
            if ([string]::IsNullOrWhiteSpace($analyzerConfigPath) -or
                -not (Test-Path -LiteralPath $analyzerConfigPath -PathType Leaf)) {
                throw "EDGE-SPLIT-LEDGER-001 evaluated AnalyzerConfigFiles item is missing: $projectPath|$analyzerConfigPath."
            }
            & $addAuthority $analyzerConfigPath 'item-analyzerconfigfiles'
        }
        $managedCoreTargetsPath = Join-Path $ledgerSdkDirectory 'Roslyn/Microsoft.Managed.Core.targets'
        foreach ($globalConfigItem in @($compilerConfigEvaluation.Items.GlobalAnalyzerConfigFiles)) {
            $globalConfigPath = Get-OptionalProperty $globalConfigItem 'FullPath'
            $definingProject = Get-OptionalProperty $globalConfigItem 'DefiningProjectFullPath'
            if ([string]::IsNullOrWhiteSpace($globalConfigPath)) {
                throw "EDGE-SPLIT-LEDGER-001 evaluated GlobalAnalyzerConfigFiles item lacks FullPath: $projectPath."
            }
            if (-not (Test-Path -LiteralPath $globalConfigPath -PathType Leaf)) {
                if ([IO.Path]::GetFileName($globalConfigPath) -cne '.globalconfig' -or
                    -not (Test-ResolvedPathIdentityEqual $definingProject $managedCoreTargetsPath)) {
                    throw "EDGE-SPLIT-LEDGER-001 unknown missing GlobalAnalyzerConfigFiles item is not an SDK discovery candidate: $globalConfigPath|$definingProject."
                }
                continue
            }
            $globalConfigPath = [IO.Path]::GetFullPath($globalConfigPath)
            if (-not $editorConfigPaths.ContainsKey($globalConfigPath)) {
                throw "EDGE-SPLIT-LEDGER-001 existing global analyzer config is absent from the compiler EditorConfigFiles set: $globalConfigPath."
            }
            & $addAuthority $globalConfigPath 'item-globalanalyzerconfigfiles'
        }
        $generatedEditorConfig = [string]$compilerConfigEvaluation.Properties.GeneratedMSBuildEditorConfigFile
        if (-not [string]::IsNullOrWhiteSpace($generatedEditorConfig)) {
            $generatedEditorConfigPath = if ([IO.Path]::IsPathRooted($generatedEditorConfig)) {
                [IO.Path]::GetFullPath($generatedEditorConfig)
            }
            else { [IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath -Parent) $generatedEditorConfig)) }
            if (Test-Path -LiteralPath $generatedEditorConfigPath -PathType Leaf) {
                if (-not $editorConfigPaths.ContainsKey($generatedEditorConfigPath)) {
                    throw "EDGE-SPLIT-LEDGER-001 generated MSBuild editorconfig exists but was not passed to the compiler: $generatedEditorConfigPath."
                }
                & $addAuthority $generatedEditorConfigPath 'generated-compiler-analyzer-config'
            }
        }
        $assetsPath = [string]$projectEvaluation.Properties.ProjectAssetsFile
        & $addAuthority $assetsPath 'restore-assets'
        $assetsDocument = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -Depth 100
        $restoreLibraries = Get-ExactJsonProperty $assetsDocument 'libraries'
        $allowedPackageSources = @(Get-TrackedNuGetSourceSet (Join-Path $repositoryRoot 'NuGet.Config'))
        foreach ($libraryProperty in @($restoreLibraries.PSObject.Properties | Sort-Ordinal Name)) {
            $library = $libraryProperty.Value
            if ([string](Get-ExactJsonProperty $library 'type') -cne 'package') { continue }
            $assetKey = [string]$libraryProperty.Name
            $separatorIndex = $assetKey.LastIndexOf('/')
            if ($separatorIndex -le 0 -or $separatorIndex -ge $assetKey.Length - 1) {
                throw "EDGE-SPLIT-LEDGER-001 restore package identity is malformed: $assetKey."
            }
            $packageId = $assetKey.Substring(0, $separatorIndex)
            $packageVersion = $assetKey.Substring($separatorIndex + 1)
            $libraryPath = [string](Get-ExactJsonProperty $library 'path')
            if ($libraryPath -cne "$($packageId.ToLowerInvariant())/$packageVersion") {
                throw "EDGE-SPLIT-LEDGER-001 restore package library path is not canonical: $assetKey|$libraryPath."
            }
            $packageDirectory = [IO.Path]::GetFullPath((Join-Path $nugetRoot $libraryPath))
            Assert-NoPathReparsePoint $nugetRoot $packageDirectory
            $packageArchive = Join-Path $packageDirectory "$($packageId.ToLowerInvariant()).$packageVersion.nupkg"
            $packageHashFile = "$packageArchive.sha512"
            $packageMetadataFile = Join-Path $packageDirectory '.nupkg.metadata'
            foreach ($requiredPackageFile in @($packageArchive, $packageHashFile, $packageMetadataFile)) {
                if (-not (Test-Path -LiteralPath $requiredPackageFile -PathType Leaf)) {
                    throw "EDGE-SPLIT-LEDGER-001 isolated restore lacks whole-package authority: $assetKey|$requiredPackageFile."
                }
            }
            $archiveSha512 = Get-Sha512Base64 $packageArchive
            if ((Get-Content -LiteralPath $packageHashFile -Raw).Trim() -cne $archiveSha512) {
                throw "EDGE-SPLIT-LEDGER-001 restored package archive SHA512 sidecar does not match bytes: $assetKey."
            }
            $packageMetadata = Get-Content -LiteralPath $packageMetadataFile -Raw | ConvertFrom-Json -Depth 20
            $expectedContentHash = [string](Get-ExactJsonProperty $library 'sha512')
            $metadataContentHash = [string](Get-ExactJsonProperty $packageMetadata 'contentHash')
            $metadataSource = [string](Get-ExactJsonProperty $packageMetadata 'source')
            if ($metadataContentHash -cne $expectedContentHash -or
                @($allowedPackageSources | Where-Object { [string]$_ -ceq $metadataSource }).Count -ne 1) {
                throw "EDGE-SPLIT-LEDGER-001 restored package metadata does not bind assets contentHash/source: $assetKey."
            }
            & $addAuthority $packageArchive 'restore-package-archive'
            & $addAuthority $packageHashFile 'restore-package-sha512'
            & $addAuthority $packageMetadataFile 'restore-package-metadata'
        }

        $preprocessedPath = Join-Path $TemporaryRoot "authority-$([Guid]::NewGuid().ToString('N')).xml"
        $preprocessArguments = [string[]](@(
                'msbuild', $projectPath, '-nologo', '-noAutoResponse', "-p:Configuration=$Configuration"
            ) + $DeterministicBuildArguments + @("-preprocess:$preprocessedPath"))
        [void](Invoke-CapturedCommand dotnet $preprocessArguments)
        $preprocessedText = Get-Content -LiteralPath $preprocessedPath -Raw
        foreach ($match in [Text.RegularExpressions.Regex]::Matches(
                $preprocessedText,
                '(?m)^(?<path>(?:[A-Za-z]:[\\/]|/)[^\r\n]+)\r?\n={20,}\r?$')) {
            & $addAuthority ([string]$match.Groups['path'].Value) 'evaluated-import'
        }
    }
    $result = [Collections.Generic.List[object]]::new()
    foreach ($key in @($factsByPath.Keys | Sort-Ordinal)) {
        $fact = $factsByPath[$key]
        $result.Add([pscustomobject][ordered]@{
                path = [string]$fact.path
                origin = [string]$fact.origin
                representation = [string]$fact.representation
                roles = @($rolesByPath[$key] | Sort-Ordinal)
                size = [long]$fact.size
                sha256 = [string]$fact.sha256
            })
    }
    return $result.ToArray()
}

function Get-ExactJsonProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$Optional
    )

    $matches = @($Object.PSObject.Properties | Where-Object { [string]$_.Name -ceq $Name })
    if ($matches.Count -eq 1) { return $matches[0].Value }
    if ($Optional -and $matches.Count -eq 0) { return $null }
    throw "EDGE-SPLIT-LEDGER-001 restore authority property must exist exactly once with ordinal casing: $Name count=$($matches.Count)."
}

function Get-NuGetReferenceProvenance {
    param(
        [Parameter(Mandatory = $true)]$ReferenceItem,
        [Parameter(Mandatory = $true)][string]$ReferencePath,
        [Parameter(Mandatory = $true)]$Assets,
        [Parameter(Mandatory = $true)][string]$AssetsRepositoryPath,
        [Parameter(Mandatory = $true)][string]$NuGetRoot
    )

    $packageId = Get-OptionalProperty $ReferenceItem 'NuGetPackageId'
    $packageVersion = Get-OptionalProperty $ReferenceItem 'NuGetPackageVersion'
    $pathInPackage = (Get-OptionalProperty $ReferenceItem 'PathInPackage').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($packageVersion) -or
        [string]::IsNullOrWhiteSpace($pathInPackage) -or [IO.Path]::IsPathRooted($pathInPackage) -or
        $pathInPackage.StartsWith('../', [StringComparison]::Ordinal) -or
        $pathInPackage.Contains('/../', [StringComparison]::Ordinal) -or
        $pathInPackage.Contains('\', [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-LEDGER-001 NuGet ReferencePath lacks a safe exact id/version/PathInPackage authority.'
    }
    $packageRoot = [IO.Path]::GetFullPath($NuGetRoot)
    Assert-NoPathReparsePoint $packageRoot $packageRoot
    $expectedPath = [IO.Path]::GetFullPath((Join-Path $packageRoot "$($packageId.ToLowerInvariant())/$packageVersion/$pathInPackage"))
    if (-not (Test-ResolvedPathIdentityEqual $expectedPath $ReferencePath)) {
        throw "EDGE-SPLIT-LEDGER-001 NuGet metadata does not identify the exact ReferencePath bytes: $packageId/$packageVersion/$pathInPackage."
    }
    Assert-NoPathReparsePoint $packageRoot $expectedPath

    $packageFolders = Get-ExactJsonProperty $Assets 'packageFolders'
    $matchingFolders = @($packageFolders.PSObject.Properties | Where-Object {
            Test-ResolvedPathIdentityEqual ([string]$_.Name) $packageRoot
        })
    if ($matchingFolders.Count -ne 1) {
        throw "EDGE-SPLIT-LEDGER-001 project.assets.json does not bind the exact global packages folder: $packageRoot."
    }

    $assetKey = "$packageId/$packageVersion"
    $libraries = Get-ExactJsonProperty $Assets 'libraries'
    $library = Get-ExactJsonProperty $libraries $assetKey
    if ([string](Get-ExactJsonProperty $library 'type') -cne 'package' -or
        [string](Get-ExactJsonProperty $library 'path') -cne "$($packageId.ToLowerInvariant())/$packageVersion") {
        throw "EDGE-SPLIT-LEDGER-001 NuGet restore library authority is not an exact package path: $assetKey."
    }
    $libraryFiles = @((Get-ExactJsonProperty $library 'files'))
    if (@($libraryFiles | Where-Object { [string]$_ -ceq $pathInPackage }).Count -ne 1) {
        throw "EDGE-SPLIT-LEDGER-001 NuGet restore library does not contain exact PathInPackage: $assetKey|$pathInPackage."
    }

    $targets = Get-ExactJsonProperty $Assets 'targets'
    $assetOccurrences = 0
    $compileOccurrences = 0
    foreach ($targetProperty in $targets.PSObject.Properties) {
        $targetPackage = Get-ExactJsonProperty $targetProperty.Value $assetKey -Optional
        if ($null -eq $targetPackage) { continue }
        $assetOccurrences++
        foreach ($assetGroupName in @('compile', 'runtime', 'runtimeTargets')) {
            $assetGroup = Get-ExactJsonProperty $targetPackage $assetGroupName -Optional
            if ($null -ne $assetGroup -and
                @($assetGroup.PSObject.Properties | Where-Object { [string]$_.Name -ceq $pathInPackage }).Count -eq 1) {
                $compileOccurrences++
            }
        }
    }
    if ($assetOccurrences -lt 1 -or $compileOccurrences -lt 1) {
        throw "EDGE-SPLIT-LEDGER-001 NuGet restore targets do not select exact reference bytes: $assetKey|$pathInPackage."
    }
    return [pscustomobject][ordered]@{
        kind = 'nuget-package'
        packageId = $packageId
        packageVersion = $packageVersion
        pathInPackage = $pathInPackage
        assetsPath = $AssetsRepositoryPath
    }
}

function Get-FrameworkReferenceProvenance {
    param(
        [Parameter(Mandatory = $true)]$ReferenceItem,
        [Parameter(Mandatory = $true)][string]$ReferencePath,
        [Parameter(Mandatory = $true)][string]$TargetingPackRoot
    )

    $frameworkName = Get-OptionalProperty $ReferenceItem 'FrameworkReferenceName'
    $frameworkVersion = Get-OptionalProperty $ReferenceItem 'FrameworkReferenceVersion'
    $packId = Get-OptionalProperty $ReferenceItem 'NuGetPackageId'
    $packVersion = Get-OptionalProperty $ReferenceItem 'NuGetPackageVersion'
    if ([string]::IsNullOrWhiteSpace($frameworkName) -or [string]::IsNullOrWhiteSpace($frameworkVersion) -or
        $packId -cne "${frameworkName}.Ref" -or $packVersion -cne $frameworkVersion) {
        throw 'EDGE-SPLIT-LEDGER-001 framework ReferencePath metadata does not identify one exact targeting pack.'
    }
    $packRoot = [IO.Path]::GetFullPath($TargetingPackRoot)
    Assert-NoPathReparsePoint $packRoot $packRoot
    $fullReferencePath = [IO.Path]::GetFullPath($ReferencePath)
    Assert-NoPathReparsePoint $packRoot $fullReferencePath
    $packRelativePath = [IO.Path]::GetRelativePath($packRoot, $fullReferencePath).Replace('\', '/')
    $expectedPrefix = "$packId/$frameworkVersion/ref/"
    if (-not $packRelativePath.StartsWith($expectedPrefix, [StringComparison]::Ordinal) -or
        -not $packRelativePath.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
        throw "EDGE-SPLIT-LEDGER-001 framework bytes are outside the exact resolved targeting pack: $packRelativePath."
    }
    return [pscustomobject][ordered]@{
        kind = 'framework-reference'
        frameworkName = $frameworkName
        frameworkVersion = $frameworkVersion
        targetingPackId = $packId
        targetingPackPath = $packRelativePath
    }
}

function Get-CountByOwnerFamily {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items)

    return @($Items |
        Group-Ordinal ownerFamily |
        ForEach-Object {
            [pscustomobject][ordered]@{
                ownerFamily = [string]$_.Name
                count = $_.Count
            }
        } |
        Sort-Ordinal ownerFamily)
}

function Get-CarryKey {
    param([Parameter(Mandatory = $true)]$Item)

    return "$([string]$Item.sourcePath)|$([string]$Item.ownerAssembly)|$([string]$Item.symbol)"
}

function Get-CarryOccurrenceCount {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items)

    $sum = ($Items | Measure-Object -Property count -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [int]$sum
}

function Test-CarrySetsEqual {
    param(
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][object[]]$Actual
    )

    $expectedMap = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Expected) { $expectedMap[(Get-CarryKey $item)] = [int]$item.count }
    $actualMap = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Actual) { $actualMap[(Get-CarryKey $item)] = [int]$item.count }
    if ($expectedMap.Count -ne $actualMap.Count) { return $false }
    foreach ($key in $expectedMap.Keys) {
        if (-not $actualMap.ContainsKey($key) -or $actualMap[$key] -ne $expectedMap[$key]) { return $false }
    }
    return $true
}

function Assert-ExactPhaseLayerGate {
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
    $unexpectedProject = @($ProjectForbidden | Where-Object { -not $allowedReferenceFamilies.Contains([string]$_.ownerFamily) })
    $unexpectedPe = @($PeForbidden | Where-Object { -not $allowedReferenceFamilies.Contains([string]$_.ownerFamily) })
    $actualRoslynCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($usage in $RoslynForbidden) {
        $key = Get-CarryKey $usage
        if ($actualRoslynCounts.ContainsKey($key)) { $actualRoslynCounts[$key]++ }
        else { $actualRoslynCounts.Add($key, 1) }
    }
    $roslynMismatch = $actualRoslynCounts.Count -ne $expectedRoslynCounts.Count
    if (-not $roslynMismatch) {
        foreach ($key in $expectedRoslynCounts.Keys) {
            if (-not $actualRoslynCounts.ContainsKey($key) -or $actualRoslynCounts[$key] -ne $expectedRoslynCounts[$key]) {
                $roslynMismatch = $true
                break
            }
        }
    }
    if ($unexpectedProject.Count -ne 0 -or $unexpectedPe.Count -ne 0 -or $roslynMismatch) {
        throw "EDGE-SPLIT-LEDGER-001 exact phase layer gate failed for ${BatchId}: project=$($unexpectedProject.Count) pe=$($unexpectedPe.Count) roslynExact=$(-not $roslynMismatch)."
    }
}

function Get-RepositoryStatus {
    param([Parameter(Mandatory = $true)][string]$ExcludedPath)

    $excludedRelative = ConvertTo-RepositoryPath $ExcludedPath
    $lines = @(& git -c core.quotePath=false status --porcelain=v1 --untracked-files=all 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 git status failed: $($lines -join [Environment]::NewLine)"
    }
    $paths = @($lines | ForEach-Object {
        $line = [string]$_
        if ($line.Length -lt 4) { return }
        $path = $line.Substring(3)
        if ($path.Contains(' -> ', [StringComparison]::Ordinal)) {
            $path = $path.Substring($path.IndexOf(' -> ', [StringComparison]::Ordinal) + 4)
        }
        $path = $path.Trim('"').Replace('\', '/')
        if ($path -cne $excludedRelative) { $path }
    } | Sort-Ordinal -Unique)
    return [pscustomobject][ordered]@{
        cleanObserved = $paths.Count -eq 0
        dirtyPaths = $paths
        excludedPaths = @($excludedRelative)
        observationMethod = 'git-status-porcelain-v1'
    }
}

function Get-Disposition {
    param(
        [Parameter(Mandatory = $true)][string]$OwnerAssembly,
        [AllowEmptyString()][string]$ContainingNamespace,
        [Parameter(Mandatory = $true)][string]$Symbol,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$BatchId,
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, int]]$FrozenCarry020,
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, int]]$FrozenCarry030,
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$ReferenceOwnerByAssembly
    )

    $ownerFamily = Get-OwnerFamily $OwnerAssembly $ReferenceOwnerByAssembly
    $carryKey = "$SourcePath|$OwnerAssembly|$Symbol"

    if ((Get-BatchRank $BatchId) -gt 0 -and $FrozenCarry020.ContainsKey($carryKey)) {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'bounded-carry-set'
            disposition = 'replace-with-domain-neutral-dto-or-port'
            removalBatch = 'EDGE-SPLIT-020'
            replacementContract = 'IIoT.Edge.Module.Contracts hardware/dev-sample DTO and contributor port'
            protectionTest = 'Homogenization hardware/dev-sample canonical snapshot and transactional behavior tests'
            forbiddenForSourceLayer = $true
        }
    }

    if ((Get-BatchRank $BatchId) -gt 0 -and $FrozenCarry030.ContainsKey($carryKey)) {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'bounded-carry-set'
            disposition = 'replace-with-stable-ui-contract'
            removalBatch = 'EDGE-SPLIT-030'
            replacementContract = 'IIoT.Edge.UI.Shared stable View/ViewModel/resource/navigation contract'
            protectionTest = 'EDGE-SPLIT-030 real View runtime gate'
            forbiddenForSourceLayer = $true
        }
    }

    if ((Get-BatchRank $BatchId) -eq 0 -and (($OwnerAssembly -ceq 'IIoT.Edge.Application' -and
            ($ContainingNamespace.StartsWith('IIoT.Edge.Application.Modules.Samples', [StringComparison]::Ordinal) -or
             $Symbol.Contains('DevelopmentSample', [StringComparison]::Ordinal))) -or
        ($OwnerAssembly -ceq 'IIoT.Edge.Domain' -and
            $ContainingNamespace.StartsWith('IIoT.Edge.Domain.Hardware', [StringComparison]::Ordinal)))) {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'bounded-carry-set'
            disposition = 'replace-with-domain-neutral-dto-or-port'
            removalBatch = 'EDGE-SPLIT-020'
            replacementContract = 'IIoT.Edge.Module.Contracts hardware/dev-sample DTO and contributor port'
            protectionTest = 'Homogenization hardware/dev-sample canonical snapshot and transactional behavior tests'
            forbiddenForSourceLayer = $true
        }
    }

    if ((Get-BatchRank $BatchId) -eq 0 -and $OwnerAssembly.StartsWith('IIoT.Edge.Presentation.', [StringComparison]::Ordinal)) {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'bounded-carry-set'
            disposition = 'replace-with-stable-ui-contract'
            removalBatch = 'EDGE-SPLIT-030'
            replacementContract = 'IIoT.Edge.UI.Shared stable View/ViewModel/resource/navigation contract'
            protectionTest = 'EDGE-SPLIT-030 real View runtime gate'
            forbiddenForSourceLayer = $true
        }
    }

    if ($OwnerAssembly -ceq 'IIoT.Edge.Application') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'phase-1-contract-extraction'
            disposition = 'replace-with-contract-or-host-port'
            removalBatch = 'EDGE-SPLIT-010'
            replacementContract = 'IIoT.Edge.Module.Contracts narrow host port or domain-neutral DTO'
            protectionTest = 'Edge project graph + Homogenization Workflow/Conformance required runners'
            forbiddenForSourceLayer = $true
        }
    }

    if ($OwnerAssembly -ceq 'IIoT.Edge.Domain') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'phase-1-contract-extraction'
            disposition = 'remove-domain-aggregate-reference'
            removalBatch = 'EDGE-SPLIT-010'
            replacementContract = 'No Domain aggregate exposure; use a purpose-specific contract DTO/port'
            protectionTest = 'Edge project graph + Domain/Application required runners'
            forbiddenForSourceLayer = $true
        }
    }

    if ($OwnerAssembly -ceq 'IIoT.Edge.SharedKernel') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'phase-1-contract-extraction'
            disposition = 'replace-with-approved-contract-primitive'
            removalBatch = 'EDGE-SPLIT-010'
            replacementContract = 'IIoT.Edge.Module.Contracts domain-neutral primitive or stable enum'
            protectionTest = 'Edge public API analyzer + Homogenization required runners'
            forbiddenForSourceLayer = $true
        }
    }

    if ($ownerFamily -ceq 'SdkContract') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'approved-sdk-contract-consumer'
            disposition = 'retain-formal-sdk-contract-usage'
            removalBatch = $null
            replacementContract = 'IIoT.Edge.Module.Contracts or IIoT.Edge.Module.Sdk formal package surface'
            protectionTest = 'Edge public API analyzer + SDK contract tests'
            forbiddenForSourceLayer = $false
        }
    }

    if ($OwnerAssembly -ceq 'IIoT.Edge.UI.Shared') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'approved-sdk-ui-contract'
            disposition = 'retain-stable-ui-contract'
            removalBatch = $null
            replacementContract = 'IIoT.Edge.UI.Shared approved SDK UI surface'
            protectionTest = 'SDK public API gate + EDGE-SPLIT-030 real View runtime gate'
            forbiddenForSourceLayer = $false
        }
    }

    if ($ownerFamily -ceq 'PluginOwned') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'plugin-owned-contract'
            disposition = 'retain-plugin-owned-dependency'
            removalBatch = $null
            replacementContract = 'Plugin-owned assembly declared by the plugin build/package graph'
            protectionTest = 'Plugin package ownership and PE closure gate'
            forbiddenForSourceLayer = $false
        }
    }

    if ($ownerFamily -ceq 'PlatformOrThirdParty') {
        return [pscustomobject][ordered]@{
            ownerFamily = $ownerFamily
            classification = 'approved-platform-or-third-party'
            disposition = 'retain-approved-external-contract'
            removalBatch = $null
            replacementContract = "Approved external contract owned by $OwnerAssembly"
            protectionTest = 'Release compilation + package dependency ownership gate'
            forbiddenForSourceLayer = $false
        }
    }

    return [pscustomobject][ordered]@{
        ownerFamily = $ownerFamily
        classification = 'unclassified'
        disposition = 'unclassified'
        removalBatch = $null
        replacementContract = ''
        protectionTest = ''
        forbiddenForSourceLayer = (Test-SourceForbiddenOwnerFamily $ownerFamily)
    }
}

function Assert-DownloadedArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][long]$ExpectedSize
    )

    $attemptFailures = [Collections.Generic.List[string]]::new()
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if (Test-Path -LiteralPath $Destination -PathType Leaf) {
            Remove-Item -LiteralPath $Destination -Force
        }
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $Destination -ConnectionTimeoutSeconds 60 -OperationTimeoutSeconds 300
            $file = Get-Item -LiteralPath $Destination
            $sha256 = Get-Sha256 $Destination
            if ($file.Length -ne $ExpectedSize -or $sha256 -cne $ExpectedSha256.ToLowerInvariant()) {
                throw "immutable artifact mismatch: expectedSize=$ExpectedSize actualSize=$($file.Length) expectedSha256=$ExpectedSha256 actualSha256=$sha256"
            }
            return [pscustomobject][ordered]@{
                url = $Uri
                size = $file.Length
                sha256 = $sha256
                verified = $true
            }
        }
        catch {
            $attemptFailures.Add("attempt=$attempt error=$($_.Exception.Message)")
            if ($attempt -lt 3) {
                Start-Sleep -Seconds 1
            }
        }
    }
    throw "EDGE-SPLIT-LEDGER-001 immutable artifact verification failed after 3 attempts: uri=$Uri failures=$($attemptFailures -join ' | ')"
}

function Assert-SafeArtifactUri {
    param([Parameter(Mandatory = $true)][string]$UriValue)

    $uri = $null
    if (-not [Uri]::TryCreate($UriValue, [UriKind]::Absolute, [ref]$uri) -or
        -not (Test-OrdinalEqualsAny $uri.Scheme @('http', 'https')) -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
        throw "EDGE-SPLIT-LEDGER-001 artifact URI must be an absolute credential-free HTTP(S) URL without query or fragment: $UriValue"
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
                throw "EDGE-SPLIT-LEDGER-001 authority input contains a secret-bearing property name at $JsonPath.$($property.Name)."
            }
            Assert-AuthorityInputSafe -Value $property.Value -JsonPath "$JsonPath.$($property.Name)"
        }
        return
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($item in $Value) {
            Assert-AuthorityInputSafe -Value $item -JsonPath "$JsonPath[$index]"
            $index++
        }
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

$resolvedPluginProject = Resolve-RepositoryPath $PluginProject
$resolvedOutputPath = Resolve-RepositoryPath $OutputPath
$canonicalOutputPath = Resolve-RepositoryPath 'eng/baselines/edge-plugin-contract-ledger.json'
$isCanonicalOutput = Test-ResolvedPathIdentityEqual $resolvedOutputPath $canonicalOutputPath
$resolvedInputsPath = Resolve-RepositoryPath $Phase0InputsPath
$resolvedBaselineLedgerPath = Resolve-RepositoryPath $BaselineLedgerPath
$resolvedPluginPackagePath = if ([string]::IsNullOrWhiteSpace($PluginPackagePath)) { '' } else { Resolve-RepositoryPath $PluginPackagePath }
$resolvedPluginOwnedAssemblyPaths = @($PluginOwnedAssemblyPath | ForEach-Object { Resolve-RepositoryPath $_ } | Sort-Ordinal -Unique)
$schemaPath = Resolve-RepositoryPath 'eng/edge-plugin-contract-ledger.schema.json'
$inputsSchemaPath = Resolve-RepositoryPath 'eng/edge-split-phase0-inputs.schema.json'
$roslynHelperPath = Resolve-RepositoryPath 'eng/EdgePluginContractLedger.Roslyn.cs'
$validatorRoslynHelperPath = Resolve-RepositoryPath 'scripts/tests/EdgePluginContractLedger.ValidatorRoslyn.cs'
$deterministicBuildTargetsPath = Resolve-RepositoryPath 'eng/EdgePluginContractDeterministicBuild.targets'
$phaseCloseEvidenceSchemaPath = Resolve-RepositoryPath 'eng/edge-phase-close-evidence.schema.json'
$phaseCloseEvidenceValidatorPath = Resolve-RepositoryPath 'scripts/tests/Test-EdgePhaseCloseEvidence.ps1'
$generatorPath = $PSCommandPath
$globalJsonPath = Resolve-RepositoryPath 'global.json'
$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json -Depth 20
$requiredLedgerSdkVersion = [string]$globalJson.sdk.version
$resolvedLedgerSdkVersion = Invoke-CapturedCommand dotnet @('--version')
if ([string]::IsNullOrWhiteSpace($requiredLedgerSdkVersion) -or
    $resolvedLedgerSdkVersion -cne $requiredLedgerSdkVersion) {
    throw "EDGE-SPLIT-LEDGER-001 ledger generation requires the exact global.json SDK version: required=$requiredLedgerSdkVersion resolved=$resolvedLedgerSdkVersion."
}
$ledgerDotnetCommandPath = (Get-Command dotnet).Source
$ledgerDotnetExecutable = (Get-Item -LiteralPath $ledgerDotnetCommandPath).Target
if ([string]::IsNullOrWhiteSpace($ledgerDotnetExecutable)) { $ledgerDotnetExecutable = $ledgerDotnetCommandPath }
$ledgerDotnetRoot = Split-Path ([IO.Path]::GetFullPath($ledgerDotnetExecutable)) -Parent
$ledgerSdkDirectory = Join-Path $ledgerDotnetRoot "sdk/$resolvedLedgerSdkVersion"
$ledgerCompilerPath = Join-Path $ledgerSdkDirectory 'Roslyn/bincore/csc.dll'
if (-not (Test-Path -LiteralPath $ledgerCompilerPath -PathType Leaf)) {
    throw "EDGE-SPLIT-LEDGER-001 exact SDK compiler does not exist: $ledgerCompilerPath."
}

$observedHead = Invoke-CapturedCommand git @('rev-parse', 'HEAD')
$currentHead = $observedHead
$currentTree = Invoke-CapturedCommand git @('rev-parse', 'HEAD^{tree}')
$repositoryStatus = Get-RepositoryStatus -ExcludedPath $resolvedOutputPath
$hasReplayHead = -not [string]::IsNullOrWhiteSpace($ValidationReplayImplementationHead)
$hasReplayTree = -not [string]::IsNullOrWhiteSpace($ValidationReplayImplementationTree)
if ($hasReplayHead -ne $hasReplayTree -or ($ValidationReplayGateOnly -and -not $hasReplayHead)) {
    throw 'EDGE-SPLIT-LEDGER-001 validation replay requires its implementation HEAD and tree together.'
}
if ($hasReplayHead) {
    $canonicalLedgerPath = Resolve-RepositoryPath 'eng/baselines/edge-plugin-contract-ledger.json'
    if ($ValidationReplayImplementationHead -notmatch '^[0-9a-f]{40}$' -or
        $ValidationReplayImplementationTree -notmatch '^[0-9a-f]{40}$' -or
        (Test-ResolvedPathIdentityEqual $resolvedOutputPath $canonicalLedgerPath)) {
        throw 'EDGE-SPLIT-LEDGER-001 validation replay requires a valid implementation HEAD/tree and a noncanonical output path.'
    }
    [void](Invoke-CapturedCommand git @('merge-base', '--is-ancestor', $ValidationReplayImplementationHead, $observedHead))
    $actualReplayTree = Invoke-CapturedCommand git @('rev-parse', "$ValidationReplayImplementationHead`^{tree}")
    if ($actualReplayTree -cne $ValidationReplayImplementationTree) {
        throw 'EDGE-SPLIT-LEDGER-001 validation replay implementation HEAD/tree mismatch.'
    }
    $replayDistance = Invoke-CapturedCommand git @('rev-list', '--count', "$ValidationReplayImplementationHead..$observedHead")
    $replayParents = @((Invoke-CapturedCommand git @('show', '-s', '--format=%P', $observedHead)).Split(
            ' ', [StringSplitOptions]::RemoveEmptyEntries))
    $replayPaths = @((Invoke-CapturedCommand git @(
            '-c', 'core.quotePath=false', 'diff', '--no-ext-diff', '--name-only',
            $ValidationReplayImplementationHead, $observedHead)) -split "`r?`n" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Replace('\', '/') })
    if ([int]$replayDistance -ne 1 -or $replayParents.Count -ne 1 -or
        [string]$replayParents[0] -cne $ValidationReplayImplementationHead -or
        $replayPaths.Count -ne 1 -or
        [string]$replayPaths[0] -cne 'eng/baselines/edge-plugin-contract-ledger.json') {
        throw 'EDGE-SPLIT-LEDGER-001 validation replay is allowed only across the exact ledger-only evidence commit.'
    }
    $replayTreeEntry = Invoke-CapturedCommand git @('ls-tree', $observedHead, '--', 'eng/baselines/edge-plugin-contract-ledger.json')
    if ($replayTreeEntry -notmatch '^100644 blob [0-9a-f]{40}\t' -or
        (Get-GitBlobSha256 -Commit $observedHead -RepositoryPath 'eng/baselines/edge-plugin-contract-ledger.json') -cne
            (Get-Sha256 $canonicalLedgerPath)) {
        throw 'EDGE-SPLIT-LEDGER-001 validation replay requires canonical ledger mode 100644 and exact committed/worktree bytes.'
    }
    if (-not [bool]$repositoryStatus.cleanObserved -or @($repositoryStatus.dirtyPaths).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 validation replay requires a completely clean final worktree.'
    }
    $currentHead = $ValidationReplayImplementationHead
    $currentTree = $actualReplayTree
    if ($ValidationReplayGateOnly) {
        Write-Host 'EDGE-SPLIT-LEDGER-001 validation replay preflight passed.'
        return
    }
}

foreach ($requiredPath in @($resolvedPluginProject, $resolvedInputsPath, $schemaPath, $inputsSchemaPath, $roslynHelperPath,
        $validatorRoslynHelperPath, $deterministicBuildTargetsPath,
        $phaseCloseEvidenceSchemaPath, $phaseCloseEvidenceValidatorPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 required input does not exist: $requiredPath"
    }
}

$phase0InputsRaw = Get-Content -LiteralPath $resolvedInputsPath -Raw
$inputsSchemaRaw = Get-Content -LiteralPath $inputsSchemaPath -Raw
try {
    if (-not ($phase0InputsRaw | Test-Json -Schema $inputsSchemaRaw -ErrorAction Stop)) {
        throw 'schema validation returned false'
    }
}
catch {
    throw "EDGE-SPLIT-LEDGER-001 Phase 0 authority input schema validation failed: $($_.Exception.Message)"
}
$phase0Inputs = $phase0InputsRaw | ConvertFrom-Json -Depth 40
Assert-AuthorityInputSafe $phase0Inputs
foreach ($uriValue in @(
        [string]$phase0Inputs.publishedComposition.catalogApiBaseUrl,
        [string]$phase0Inputs.publishedComposition.host.manifestUrl,
        [string]$phase0Inputs.publishedComposition.host.artifactUrl,
        [string]$phase0Inputs.publishedComposition.plugin.artifactUrl)) {
    Assert-SafeArtifactUri $uriValue
}

$baselineHead = [string]$phase0Inputs.baselineGit.head
$baselineTree = [string]$phase0Inputs.baselineGit.tree
$actualBaselineTree = Invoke-CapturedCommand git @('rev-parse', "$baselineHead`^{tree}")
if ($actualBaselineTree -cne $baselineTree) {
    throw "EDGE-SPLIT-LEDGER-001 baseline commit/tree mismatch: expected=$baselineTree actual=$actualBaselineTree"
}

$originMain = Invoke-CapturedCommand git @('rev-parse', 'origin/main')

$baselineLedger = $null
$frozenCarry020Map = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
$frozenCarry030Map = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
$predecessorBatchByCurrentBatch = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-010', 'EDGE-SPLIT-000')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-020', 'EDGE-SPLIT-010')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-030', 'EDGE-SPLIT-020')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-040', 'EDGE-SPLIT-030')
$predecessorBatchByCurrentBatch.Add('EDGE-SPLIT-050', 'EDGE-SPLIT-040')
$baselineLedgerSha256 = ''
$baselineLedgerBatchId = ''
$baselineLedgerEvidenceCommit = ''
if ((Get-BatchRank $CurrentBatch) -gt 0) {
    if (-not (Test-Path -LiteralPath $resolvedBaselineLedgerPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 Phase $CurrentBatch requires the immediately preceding canonical ledger."
    }
    $baselineLedgerRaw = Get-Content -LiteralPath $resolvedBaselineLedgerPath -Raw
    $schemaRaw = Get-Content -LiteralPath $schemaPath -Raw
    if (-not ($baselineLedgerRaw | Test-Json -Schema $schemaRaw -ErrorAction Stop)) {
        throw 'EDGE-SPLIT-LEDGER-001 preceding canonical ledger does not satisfy the current schema.'
    }
    $baselineLedger = $baselineLedgerRaw | ConvertFrom-Json -Depth 100
    $expectedPredecessorBatch = [string]$predecessorBatchByCurrentBatch[$CurrentBatch]
    if ([int]$baselineLedger.schemaVersion -ne 2 -or [string]$baselineLedger.batchId -cne $expectedPredecessorBatch) {
        throw "EDGE-SPLIT-LEDGER-001 $CurrentBatch requires immediate predecessor $expectedPredecessorBatch, actual=$($baselineLedger.batchId)."
    }
    $recordedPredecessorPayload = [string]$baselineLedger.integrity.payloadSha256
    $baselineLedger.integrity.payloadSha256 = ''
    $actualPredecessorPayload = Get-TextSha256 (($baselineLedger | ConvertTo-Json -Depth 100) + "`n")
    $baselineLedger.integrity.payloadSha256 = $recordedPredecessorPayload
    if ($actualPredecessorPayload -cne $recordedPredecessorPayload) {
        throw 'EDGE-SPLIT-LEDGER-001 preceding canonical ledger payload digest is invalid.'
    }
    $baselineLedgerSha256 = Get-Sha256 $resolvedBaselineLedgerPath
    $baselineLedgerBatchId = [string]$baselineLedger.batchId
    $canonicalLedgerRelativePath = 'eng/baselines/edge-plugin-contract-ledger.json'
    $predecessorImplementationHead = [string]$baselineLedger.sourceState.head
    [void](Invoke-CapturedCommand git @('merge-base', '--is-ancestor', $predecessorImplementationHead, $currentHead))
    $ledgerChangingCommits = @((Invoke-CapturedCommand git @(
                'rev-list', '--reverse', '--ancestry-path', "$predecessorImplementationHead..$currentHead", '--',
                $canonicalLedgerRelativePath)) -split "`r?`n" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($ledgerChangingCommits.Count -ne 1) {
        throw "EDGE-SPLIT-LEDGER-001 predecessor ledger must have exactly one original evidence commit and no later rewrite: count=$($ledgerChangingCommits.Count)."
    }
    $baselineLedgerEvidenceCommit = [string]$ledgerChangingCommits[0]
    $evidenceParents = @((Invoke-CapturedCommand git @('show', '-s', '--format=%P', $baselineLedgerEvidenceCommit)).Split(
            ' ', [StringSplitOptions]::RemoveEmptyEntries))
    $evidencePaths = @((Invoke-CapturedCommand git @('-c', 'core.quotePath=false', 'diff-tree',
                '--no-commit-id', '--name-only', '-r', $baselineLedgerEvidenceCommit)) -split "`r?`n" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Replace('\', '/') })
    $evidenceTreeEntry = Invoke-CapturedCommand git @('ls-tree', $baselineLedgerEvidenceCommit, '--', $canonicalLedgerRelativePath)
    if ($evidenceParents.Count -ne 1 -or [string]$evidenceParents[0] -cne $predecessorImplementationHead -or
        $evidencePaths.Count -ne 1 -or [string]$evidencePaths[0] -cne $canonicalLedgerRelativePath -or
        $evidenceTreeEntry -notmatch '^100644 blob [0-9a-f]{40}\t') {
        throw 'EDGE-SPLIT-LEDGER-001 predecessor evidence commit must be the direct ledger-only 100644 child of its recorded implementation HEAD.'
    }
    $evidenceBlobSha256 = Get-GitBlobSha256 -Commit $baselineLedgerEvidenceCommit -RepositoryPath $canonicalLedgerRelativePath
    $implementationBlobSha256 = Get-GitBlobSha256 -Commit $currentHead -RepositoryPath $canonicalLedgerRelativePath
    if ($evidenceBlobSha256 -cne $baselineLedgerSha256 -or
        $implementationBlobSha256 -cne $baselineLedgerSha256) {
        throw 'EDGE-SPLIT-LEDGER-001 predecessor canonical ledger bytes were rewritten after their original evidence commit.'
    }
    foreach ($item in @($baselineLedger.carrySets.'EDGE-SPLIT-020'.baselineItems)) {
        $frozenCarry020Map[(Get-CarryKey $item)] = [int]$item.count
    }
    foreach ($item in @($baselineLedger.carrySets.'EDGE-SPLIT-030'.baselineItems)) {
        $frozenCarry030Map[(Get-CarryKey $item)] = [int]$item.count
    }
    if ($frozenCarry020Map.Count -eq 0 -or $frozenCarry030Map.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 the canonical baseline must freeze both non-empty carry sets.'
    }
}

if ((Get-BatchRank $CurrentBatch) -ge 40 -and [string]::IsNullOrWhiteSpace($resolvedPluginPackagePath)) {
    throw "EDGE-SPLIT-LEDGER-001 $CurrentBatch requires -PluginPackagePath so the package layer cannot be reported as zero without evaluation."
}
if (-not [string]::IsNullOrWhiteSpace($resolvedPluginPackagePath) -and
    -not (Test-Path -LiteralPath $resolvedPluginPackagePath -PathType Leaf)) {
    throw "EDGE-SPLIT-LEDGER-001 plugin package does not exist: $resolvedPluginPackagePath"
}
foreach ($path in $resolvedPluginOwnedAssemblyPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 plugin-owned assembly input does not exist: $path"
    }
}

$pluginRoot = Split-Path $resolvedPluginProject -Parent
$pluginRelativeRoot = ConvertTo-RepositoryPath $pluginRoot
$manifestPath = Join-Path $pluginRoot 'plugin.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "EDGE-SPLIT-LEDGER-001 plugin manifest does not exist: $manifestPath"
}
$pluginManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
$pluginDrift = Invoke-CapturedCommand git @('diff', '--no-ext-diff', '--name-only', $baselineHead, '--', $pluginRelativeRoot)
if ((Get-BatchRank $CurrentBatch) -eq 0 -and -not [string]::IsNullOrWhiteSpace($pluginDrift)) {
    throw "EDGE-SPLIT-LEDGER-001 analyzed plugin source differs from the frozen baseline head: $pluginDrift"
}

$formalSurfaceProjectByAssembly = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($surfaceAssemblyName in @($phase0Inputs.decisions.sdkPackages)) {
    $surfaceProjects = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File -Filter "$surfaceAssemblyName.csproj" |
        Where-Object {
            -not (Test-InvariantPattern $_.FullName '[/\\](?:bin|obj|Tests?)[/\\]')
        } | Sort-Ordinal FullName)
    if ($surfaceProjects.Count -eq 0) { continue }
    if ($surfaceProjects.Count -ne 1) {
        throw "EDGE-SPLIT-LEDGER-001 formal SDK/UI surface must have exactly one project: $surfaceAssemblyName count=$($surfaceProjects.Count)."
    }
    $formalSurfaceProjectByAssembly.Add([string]$surfaceAssemblyName, [string]$surfaceProjects[0].FullName)
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-contract-ledger-$([Guid]::NewGuid().ToString('N'))"
$generatedRoot = Join-Path $temporaryRoot 'generated'
$downloadRoot = Join-Path $temporaryRoot 'downloads'
$isolatedPackagesRoot = Join-Path $temporaryRoot 'nuget-packages'
$isolatedHttpCacheRoot = Join-Path $temporaryRoot 'nuget-http-cache'
$isolatedPluginCacheRoot = Join-Path $temporaryRoot 'nuget-plugin-cache'
$isolatedCredentialProvidersRoot = Join-Path $temporaryRoot 'nuget-credential-providers-empty'
$isolatedPluginDiscoveryRoot = Join-Path $temporaryRoot 'nuget-plugin-paths-empty'
$deterministicBuildTargetsSha256 = '24aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
if ((Get-Sha256 $deterministicBuildTargetsPath) -cne $deterministicBuildTargetsSha256) {
    throw 'EDGE-SPLIT-LEDGER-001 deterministic authority build targets digest differs from the pinned contract.'
}
$canonicalRawPathMap = "$(ConvertTo-CanonicalPathMapSourceToken $repositoryRoot)=/_,$(ConvertTo-CanonicalPathMapSourceToken $generatedRoot)=/__edge_contract_generated__"
$canonicalPathMap = ConvertTo-CanonicalMsBuildPropertyValue $canonicalRawPathMap
$canonicalBuildTargetsProperty = ConvertTo-CanonicalMsBuildPropertyValue $deterministicBuildTargetsPath
$canonicalRepositoryRootProperty = ConvertTo-CanonicalMsBuildPropertyValue $repositoryRoot
$canonicalDeterministicBuildArguments = [string[]]@(
    '-p:DebugSymbols=true',
    '-p:DebugType=embedded',
    '-p:Deterministic=true',
    "-p:PathMap=$canonicalPathMap",
    "-p:CustomAfterMicrosoftCSharpTargets=$canonicalBuildTargetsProperty",
    '-p:_EdgeContractAuthorityBuild=true',
    "-p:_EdgeContractRepositoryRoot=$canonicalRepositoryRootProperty"
)
[void](New-Item -ItemType Directory -Path $generatedRoot -Force)
[void](New-Item -ItemType Directory -Path $downloadRoot -Force)
[void](New-Item -ItemType Directory -Path $isolatedPackagesRoot -Force)
[void](New-Item -ItemType Directory -Path $isolatedHttpCacheRoot -Force)
[void](New-Item -ItemType Directory -Path $isolatedPluginCacheRoot -Force)
[void](New-Item -ItemType Directory -Path $isolatedCredentialProvidersRoot -Force)
[void](New-Item -ItemType Directory -Path $isolatedPluginDiscoveryRoot -Force)
$restoreEnvironmentNames = @(
    'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH',
    'NUGET_CREDENTIALPROVIDERS_PATH', 'NUGET_PLUGIN_PATHS',
    'RestoreSources', 'RestoreAdditionalProjectSources', 'RestoreFallbackFolders'
)
$diagnosticLogPaths = [Collections.Generic.List[string]]::new()
$restoreDiagnosticLogPaths = [Collections.Generic.List[string]]::new()
$restoreEnvironmentBefore = Get-ProcessEnvironmentStateSnapshot -Names ([string[]]$restoreEnvironmentNames)

try {
    $restoreConfigPath = Resolve-RepositoryPath 'NuGet.Config'
    $trackedNuGetSources = @(Get-TrackedNuGetSourceSet $restoreConfigPath)
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $isolatedPackagesRoot, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_HTTP_CACHE_PATH', $isolatedHttpCacheRoot, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_PLUGINS_CACHE_PATH', $isolatedPluginCacheRoot, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_CREDENTIALPROVIDERS_PATH', $isolatedCredentialProvidersRoot, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_PLUGIN_PATHS', $isolatedPluginDiscoveryRoot, 'Process')
    foreach ($environmentName in @('RestoreSources', 'RestoreAdditionalProjectSources', 'RestoreFallbackFolders')) {
        [Environment]::SetEnvironmentVariable($environmentName, $null, 'Process')
    }
    $restoreSeedProjects = @($resolvedPluginProject) + @($formalSurfaceProjectByAssembly.Values)
    $restoreOrdinal = 0
    foreach ($restoreProjectPath in @($restoreSeedProjects | Sort-Ordinal -Unique)) {
        $restoreDiagnosticLog = Join-Path $temporaryRoot "nuget-restore-$restoreOrdinal.log"
        $restoreDiagnosticLogPaths.Add($restoreDiagnosticLog)
        [void](Invoke-CapturedCommand dotnet @(
                'restore', $restoreProjectPath, '--force-evaluate', '--disable-parallel',
                '--no-http-cache', '--packages', $isolatedPackagesRoot,
                '--configfile', $restoreConfigPath, "-p:RestoreConfigFile=$restoreConfigPath",
                "-p:RestoreSources=$($trackedNuGetSources -join ';')",
                '-p:RestoreAdditionalProjectSources=', '-p:RestoreFallbackFolders=',
                "-p:RestorePackagesPath=$isolatedPackagesRoot",
                "-flp:logfile=$restoreDiagnosticLog;verbosity=diagnostic",
                '--nologo', '-noAutoResponse'))
        $restoreOrdinal++
    }
    Assert-NuGetPluginDiscoveryIsolation `
        -EmptyDiscoveryDirectories ([string[]]@($isolatedCredentialProvidersRoot, $isolatedPluginDiscoveryRoot)) `
        -RestoreDiagnosticLogPaths ([string[]]$restoreDiagnosticLogPaths.ToArray())

    # Build every available formal SDK/UI surface first.  The plugin rebuild below is deliberately
    # last so that every ReferencePath digest, PE fact, and closure fact observes the same final bytes.
    foreach ($surfaceAssemblyName in @($formalSurfaceProjectByAssembly.Keys | Sort-Ordinal)) {
        $surfaceProjectPath = $formalSurfaceProjectByAssembly[$surfaceAssemblyName]
        $surfaceDiagnosticLog = Join-Path $temporaryRoot "toolchain-$surfaceAssemblyName.log"
        $diagnosticLogPaths.Add($surfaceDiagnosticLog)
        $surfaceBuildArguments = [string[]](@(
            'build', $surfaceProjectPath, '-c', $Configuration, '--no-restore', '--no-incremental', '-t:Rebuild',
            '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false'
        ) + $canonicalDeterministicBuildArguments + @(
            "-p:SourceRevisionId=$currentHead", "-p:RepositoryCommit=$currentHead",
            '-p:ContinuousIntegrationBuild=true',
            "-flp:logfile=$surfaceDiagnosticLog;verbosity=diagnostic",
            '--disable-build-servers', '--nologo', '-noAutoResponse'
        ))
        [void](Invoke-CapturedCommand dotnet $surfaceBuildArguments)
        Assert-CanonicalDeterministicBuildLog `
            -LogPath $surfaceDiagnosticLog -ProjectPath $surfaceProjectPath `
            -EncodedPathMap $canonicalPathMap -EncodedTargetsPath $canonicalBuildTargetsProperty `
            -EncodedRepositoryRoot $canonicalRepositoryRootProperty
    }

    $pluginDiagnosticLog = Join-Path $temporaryRoot 'toolchain-plugin.log'
    $diagnosticLogPaths.Add($pluginDiagnosticLog)
    $buildArguments = [string[]](@(
        'build', $resolvedPluginProject,
        '-c', $Configuration,
        '--no-restore',
        '--no-incremental',
        '-t:Rebuild',
        '-m:1',
        '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false',
        '-p:EmitCompilerGeneratedFiles=false'
    ) + $canonicalDeterministicBuildArguments + @(
        "-p:SourceRevisionId=$currentHead",
        "-p:RepositoryCommit=$currentHead",
        '-p:ContinuousIntegrationBuild=true',
        "-flp:logfile=$pluginDiagnosticLog;verbosity=diagnostic",
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse'
    ))
    [void](Invoke-CapturedCommand dotnet $buildArguments)
    Assert-CanonicalDeterministicBuildLog `
        -LogPath $pluginDiagnosticLog -ProjectPath $resolvedPluginProject `
        -EncodedPathMap $canonicalPathMap -EncodedTargetsPath $canonicalBuildTargetsProperty `
        -EncodedRepositoryRoot $canonicalRepositoryRootProperty

    $pluginOnlyDiagnosticLog = Join-Path $temporaryRoot 'toolchain-plugin-only.log'
    $diagnosticLogPaths.Add($pluginOnlyDiagnosticLog)
    $pluginOnlyBuildArguments = [string[]](@(
        'build', $resolvedPluginProject,
        '-c', $Configuration,
        '--no-restore',
        '--no-incremental',
        '-t:Rebuild',
        '-m:1',
        '-p:BuildInParallel=false',
        '-p:BuildProjectReferences=false',
        '-p:UseSharedCompilation=false',
        '-p:EmitCompilerGeneratedFiles=true',
        "-p:CompilerGeneratedFilesOutputPath=$generatedRoot"
    ) + $canonicalDeterministicBuildArguments + @(
        "-p:SourceRevisionId=$currentHead",
        "-p:RepositoryCommit=$currentHead",
        '-p:ContinuousIntegrationBuild=true',
        "-flp:logfile=$pluginOnlyDiagnosticLog;verbosity=diagnostic",
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse'
    ))
    [void](Invoke-CapturedCommand dotnet $pluginOnlyBuildArguments)
    Assert-CanonicalDeterministicBuildLog `
        -LogPath $pluginOnlyDiagnosticLog -ProjectPath $resolvedPluginProject `
        -EncodedPathMap $canonicalPathMap -EncodedTargetsPath $canonicalBuildTargetsProperty `
        -EncodedRepositoryRoot $canonicalRepositoryRootProperty
    $executedToolchainFacts = @(Get-ExecutedToolchainClosure `
        -DiagnosticLogPaths ([string[]]$diagnosticLogPaths.ToArray()) `
        -SdkDirectory $ledgerSdkDirectory `
        -ExplicitAssemblyPaths ([string[]]@($ledgerCompilerPath)))

    $evaluationArguments = @(
        'msbuild', $resolvedPluginProject,
        '-nologo',
        '-noAutoResponse',
        '-t:ResolveReferences',
        "-p:Configuration=$Configuration",
        "-p:SourceRevisionId=$currentHead",
        "-p:RepositoryCommit=$currentHead",
        '-getProperty:AssemblyName,TargetFramework,TargetPath,DefineConstants,LangVersion,Nullable,ProjectAssetsFile,NuGetPackageRoot,NetCoreTargetingPackRoot',
        '-getItem:Compile,ProjectReference,ReferencePath,AvaloniaResource,Content,None,Page,EmbeddedResource,AdditionalFiles,Analyzer,FrameworkReference,PackageReference'
    )
    $evaluation = (Invoke-CapturedCommand dotnet $evaluationArguments) | ConvertFrom-Json -Depth 100

    $pluginAssemblyPath = [IO.Path]::GetFullPath([string]$evaluation.Properties.TargetPath)
    if (-not (Test-Path -LiteralPath $pluginAssemblyPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 built plugin assembly does not exist: $pluginAssemblyPath"
    }
    Assert-NoRepositoryReparsePoint -FullPath $pluginAssemblyPath
    $evaluatedAssemblyName = [string]$evaluation.Properties.AssemblyName
    $entryAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($pluginAssemblyPath).Name
    if (-not (Test-PluginOwnedIdentityCandidate $entryAssemblyName) -or
        $entryAssemblyName -cne $evaluatedAssemblyName -or
        [IO.Path]::GetFileName($pluginAssemblyPath) -cne [string]$pluginManifest.entryAssembly) {
        throw "EDGE-SPLIT-LEDGER-001 manifest/MSBuild/built entry assembly identities disagree: manifest=$($pluginManifest.entryAssembly) msbuild=$evaluatedAssemblyName bytes=$entryAssemblyName."
    }
    $verifiedPluginOwnedAssemblyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$verifiedPluginOwnedAssemblyNames.Add($entryAssemblyName)
    $requestedPluginOwnedAssemblyByName = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($ownedPath in $resolvedPluginOwnedAssemblyPaths) {
        $ownedAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($ownedPath).Name
        if (-not (Test-PluginOwnedIdentityCandidate $ownedAssemblyName) -or
            -not $requestedPluginOwnedAssemblyByName.TryAdd($ownedAssemblyName, $ownedPath)) {
            throw "EDGE-SPLIT-LEDGER-001 additional plugin-owned assembly must have a unique, non-reserved plugin identity: path=$(ConvertTo-RepositoryPath $ownedPath) assembly=$ownedAssemblyName."
        }
    }

    [string[]]$compileSourcePaths = @($evaluation.Items.Compile | ForEach-Object { [string]$_.FullPath } |
        Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Ordinal -Unique)
    [string[]]$generatedSourcePaths = @(Get-ChildItem -LiteralPath $generatedRoot -Recurse -File -Filter '*.cs' |
        ForEach-Object FullName | Sort-Ordinal -Unique)
    [string[]]$referencePaths = @($evaluation.Items.ReferencePath | ForEach-Object { [string]$_.FullPath } |
        Where-Object { $_.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Ordinal -Unique)
    [string[]]$preprocessorSymbols = @(([string]$evaluation.Properties.DefineConstants).Split(';', [StringSplitOptions]::RemoveEmptyEntries))

    if ($compileSourcePaths.Count -eq 0 -or $referencePaths.Count -eq 0) {
        throw 'EDGE-SPLIT-LEDGER-001 MSBuild did not resolve real Compile/ReferencePath items.'
    }

    $pathStringComparer = if ([OperatingSystem]::IsWindows()) {
        [StringComparer]::OrdinalIgnoreCase
    }
    else { [StringComparer]::Ordinal }
    $referencePathSet = [Collections.Generic.HashSet[string]]::new($pathStringComparer)
    foreach ($referencePath in $referencePaths) { [void]$referencePathSet.Add([IO.Path]::GetFullPath($referencePath)) }
    $projectAuthorityByAssembly = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $projectAuthorityByOutputPath = [Collections.Generic.Dictionary[string, object]]::new($pathStringComparer)
    $projectAuthorityByProjectPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $projectPathCaseGuard = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($projectReference in @($evaluation.Items.ProjectReference)) {
        $projectPath = [string]$projectReference.FullPath
        if ([string]::IsNullOrWhiteSpace($projectPath) -or -not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw 'EDGE-SPLIT-LEDGER-001 ProjectReference must resolve to one existing regular project file.'
        }
        $projectPath = [IO.Path]::GetFullPath($projectPath)
        Assert-NoPathReparsePoint $repositoryRoot $projectPath
        if ($projectPathCaseGuard.ContainsKey($projectPath)) {
            if ([string]$projectPathCaseGuard[$projectPath] -cne $projectPath) {
                throw "EDGE-SPLIT-LEDGER-001 ProjectReference paths collide under Windows case semantics: $($projectPathCaseGuard[$projectPath]) | $projectPath."
            }
            continue
        }
        $projectPathCaseGuard.Add($projectPath, $projectPath)
        $projectEvaluation = (Invoke-CapturedCommand dotnet @(
                'msbuild', $projectPath, '-nologo', '-noAutoResponse',
                "-p:Configuration=$Configuration", "-p:SourceRevisionId=$currentHead", "-p:RepositoryCommit=$currentHead",
                '-getProperty:AssemblyName,TargetPath')) | ConvertFrom-Json -Depth 30
        $projectName = [string]$projectEvaluation.Properties.AssemblyName
        $projectOutputValue = [string]$projectEvaluation.Properties.TargetPath
        if ([string]::IsNullOrWhiteSpace($projectName) -or [string]::IsNullOrWhiteSpace($projectOutputValue)) {
            throw "EDGE-SPLIT-LEDGER-001 ProjectReference lacks exact evaluated assembly/output identity: $projectPath."
        }
        $projectOutputPath = [IO.Path]::GetFullPath($projectOutputValue)
        if (
            -not (Test-Path -LiteralPath $projectOutputPath -PathType Leaf)) {
            throw "EDGE-SPLIT-LEDGER-001 ProjectReference lacks exact evaluated assembly/output bytes: $projectPath."
        }
        Assert-NoPathReparsePoint $repositoryRoot $projectOutputPath
        $projectRelativePath = ConvertTo-RepositoryPath $projectPath
        $projectOutputRelativePath = ConvertTo-RepositoryPath $projectOutputPath
        $ownerFamily = Get-InternalProjectOwnerFamily -ProjectName $projectName -ProjectPath $projectRelativePath
        if ($requestedPluginOwnedAssemblyByName.ContainsKey($projectName)) {
            $expectedModulePrefix = "src/Modules/$entryAssemblyName."
            if (-not $projectRelativePath.StartsWith($expectedModulePrefix, [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-LEDGER-001 additional plugin-owned project is outside the entry plugin's current build closure: $projectRelativePath."
            }
            $ownerFamily = 'PluginOwned'
        }
        if ($ownerFamily -ceq 'Unknown') {
            throw "EDGE-SPLIT-LEDGER-001 ProjectReference has no path-bound owner authority: $projectRelativePath."
        }
        $authority = [pscustomobject][ordered]@{
            projectPath = $projectRelativePath
            projectFullPath = $projectPath
            outputPath = $projectOutputRelativePath
            outputFullPath = $projectOutputPath
            assemblyName = $projectName
            ownerFamily = $ownerFamily
        }
        if ($projectAuthorityByAssembly.ContainsKey($projectName)) {
            $existing = $projectAuthorityByAssembly[$projectName]
            if ([string]$existing.projectPath -cne $projectRelativePath) {
                throw "EDGE-SPLIT-LEDGER-001 ProjectReference assembly identity is ambiguous: $projectName."
            }
        }
        else { $projectAuthorityByAssembly.Add($projectName, $authority) }
        if ($projectAuthorityByOutputPath.ContainsKey($projectOutputPath)) {
            throw "EDGE-SPLIT-LEDGER-001 ProjectReference evaluated outputs are ambiguous: $projectOutputPath."
        }
        $projectAuthorityByOutputPath.Add($projectOutputPath, $authority)
        $projectAuthorityByProjectPath.Add($projectRelativePath, $authority)
    }
    foreach ($ownedName in @($requestedPluginOwnedAssemblyByName.Keys | Sort-Ordinal)) {
        $ownedPath = [IO.Path]::GetFullPath($requestedPluginOwnedAssemblyByName[$ownedName])
        if (-not $referencePathSet.Contains($ownedPath) -or
            -not $projectAuthorityByAssembly.ContainsKey($ownedName) -or
            [string]$projectAuthorityByAssembly[$ownedName].outputFullPath -cne $ownedPath -or
            [string]$projectAuthorityByAssembly[$ownedName].ownerFamily -cne 'PluginOwned') {
            throw "EDGE-SPLIT-LEDGER-001 additional plugin-owned bytes must be an exact current ReferencePath/ProjectReference build-closure output: $ownedName."
        }
        [void]$verifiedPluginOwnedAssemblyNames.Add($ownedName)
    }
    $unverifiedDeclaredPluginOwnedNames = @($PluginOwnedPackageAssembly | Where-Object {
            [string]::IsNullOrWhiteSpace($_) -or -not $verifiedPluginOwnedAssemblyNames.Contains($_)
        } | Sort-Ordinal -Unique)
    if ($unverifiedDeclaredPluginOwnedNames.Count -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 package assembly ownership cannot be declared without matching current build-closure bytes: $($unverifiedDeclaredPluginOwnedNames -join ', ')."
    }
    $declaredPackageOwnedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$declaredPackageOwnedNames.Add($entryAssemblyName)
    foreach ($declaredName in $PluginOwnedPackageAssembly) {
        if (-not $declaredPackageOwnedNames.Add([string]$declaredName)) {
            throw "EDGE-SPLIT-LEDGER-001 package assembly ownership declarations must be unique: $declaredName."
        }
    }

    $sdkDirectory = $ledgerSdkDirectory
    $dotnetRoot = $ledgerDotnetRoot
    $nugetRoot = [string]$evaluation.Properties.NuGetPackageRoot
    if ([string]::IsNullOrWhiteSpace($nugetRoot) -or -not (Test-Path -LiteralPath $nugetRoot -PathType Container)) {
        throw 'EDGE-SPLIT-LEDGER-001 MSBuild did not expose a valid global NuGet package root.'
    }
    $authoritySeedProjects = @($resolvedPluginProject) + @($formalSurfaceProjectByAssembly.Values)
    $msbuildAuthorityInputs = @(Get-MsBuildAuthorityInputs `
        -SeedProjects ([string[]]$authoritySeedProjects) `
        -Configuration $Configuration `
        -TemporaryRoot $temporaryRoot `
        -DotnetRoot $dotnetRoot `
        -NugetRoot $nugetRoot `
        -DeterministicBuildArguments ([string[]]$canonicalDeterministicBuildArguments) `
        -ExecutedToolchainFacts $executedToolchainFacts)
    $authorityProjectionPolicy = [pscustomobject][ordered]@{
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
    $csharpCompilerPath = $ledgerCompilerPath
    if (-not (Test-Path -LiteralPath $csharpCompilerPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 SDK Roslyn compiler does not exist: $csharpCompilerPath"
    }
    $roslynReferences = @(
        [Microsoft.CodeAnalysis.Compilation].Assembly.Location,
        [Microsoft.CodeAnalysis.CSharp.CSharpCompilation].Assembly.Location
    )
    $helperAssemblyPath = Join-Path $temporaryRoot 'EdgePluginContractLedger.Roslyn.dll'
    $helperCompilerArguments = [Collections.Generic.List[string]]::new()
    $helperCompilerArguments.Add($csharpCompilerPath)
    $helperCompilerArguments.Add('/nologo')
    $helperCompilerArguments.Add('/target:library')
    $helperCompilerArguments.Add('/langversion:preview')
    $helperCompilerArguments.Add('/nullable:enable')
    $helperCompilerArguments.Add('/deterministic')
    $helperCompilerArguments.Add("/out:$helperAssemblyPath")
    foreach ($referencePath in @($referencePaths + $roslynReferences | Sort-Ordinal -Unique)) {
        $helperCompilerArguments.Add("/reference:$referencePath")
    }
    $helperCompilerArguments.Add($roslynHelperPath)
    [void](Invoke-CapturedCommand dotnet ([string[]]$helperCompilerArguments))
    [void][Reflection.Assembly]::LoadFrom($helperAssemblyPath)
    $peInputAssemblies = @($pluginAssemblyPath) + $resolvedPluginOwnedAssemblyPaths
    foreach ($peInputAssembly in $peInputAssemblies) {
        Assert-CanonicalEmbeddedDebugIdentity -AssemblyPath $peInputAssembly
    }
    $verifiedAssemblyFactsByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $peInputFacts = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyInputs(
            $repositoryRoot,
            [string[]]$peInputAssemblies) | ForEach-Object {
            $fact = [pscustomobject][ordered]@{
                path = [string]$_.SourcePath
                assemblyName = [string]$_.AssemblyName
                assemblyVersion = [string]$_.AssemblyVersion
                culture = [string]$_.Culture
                publicKeyToken = [string]$_.PublicKeyToken
                mvid = [string]$_.Mvid
                size = [long]$_.Size
                sha256 = [string]$_.Sha256
                verifiedPluginOwned = $true
            }
            if (-not $verifiedPluginOwnedAssemblyNames.Contains([string]$fact.assemblyName) -or
                $verifiedAssemblyFactsByName.ContainsKey([string]$fact.assemblyName)) {
                throw "EDGE-SPLIT-LEDGER-001 verified plugin assembly byte inputs have an unapproved or duplicate identity: $($fact.assemblyName)."
            }
            $verifiedAssemblyFactsByName.Add([string]$fact.assemblyName, $fact)
            $fact
        } | Sort-Ordinal assemblyName, assemblyVersion, culture, publicKeyToken, path)
    $packageStaticInputs = @(Get-PackageStaticInputFacts `
        -PluginRoot $pluginRoot `
        -TargetAssemblyPath $pluginAssemblyPath `
        -ManifestSourcePath $manifestPath `
        -PluginOwnedAssemblyPaths ([string[]]$peInputAssemblies))

    $assetsPath = [string]$evaluation.Properties.ProjectAssetsFile
    if ([string]::IsNullOrWhiteSpace($assetsPath) -or -not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-LEDGER-001 plugin project.assets.json authority is missing.'
    }
    Assert-NoPathReparsePoint $repositoryRoot $assetsPath
    $assetsRepositoryPath = ConvertTo-RepositoryPath $assetsPath
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -Depth 100
    $targetingPackRoot = [string]$evaluation.Properties.NetCoreTargetingPackRoot
    if ([string]::IsNullOrWhiteSpace($targetingPackRoot) -or
        -not (Test-Path -LiteralPath $targetingPackRoot -PathType Container)) {
        throw 'EDGE-SPLIT-LEDGER-001 MSBuild did not expose a valid targeting-pack root.'
    }
    Assert-NoPathReparsePoint $targetingPackRoot $targetingPackRoot

    $referenceItemByPath = [Collections.Generic.Dictionary[string, object]]::new($pathStringComparer)
    $referenceItemCaseGuard = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($referenceItem in @($evaluation.Items.ReferencePath)) {
        $referenceFullPath = [IO.Path]::GetFullPath([string]$referenceItem.FullPath)
        if ($referenceItemCaseGuard.ContainsKey($referenceFullPath)) {
            if ([string]$referenceItemCaseGuard[$referenceFullPath] -cne $referenceFullPath) {
                throw "EDGE-SPLIT-LEDGER-001 ReferencePath inputs collide under Windows case semantics: $($referenceItemCaseGuard[$referenceFullPath]) | $referenceFullPath."
            }
            throw "EDGE-SPLIT-LEDGER-001 ReferencePath has duplicate raw authority items: $referenceFullPath."
        }
        $referenceItemCaseGuard.Add($referenceFullPath, $referenceFullPath)
        $referenceItemByPath.Add($referenceFullPath, $referenceItem)
    }
    $referenceOwnerByAssembly = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $referenceFactByIdentity = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $referenceAssembliesList = [Collections.Generic.List[object]]::new()
    foreach ($referencePath in $referencePaths) {
        $referenceFullPath = [IO.Path]::GetFullPath($referencePath)
        if (-not $referenceItemByPath.ContainsKey($referenceFullPath)) {
            throw "EDGE-SPLIT-LEDGER-001 resolved reference bytes lack their raw MSBuild authority item: $referencePath."
        }
        $referenceItem = $referenceItemByPath[$referenceFullPath]
        $rawReferenceFact = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyInputs(
                $repositoryRoot, [string[]]@($referenceFullPath)))[0]
        $nugetId = Get-OptionalProperty $referenceItem 'NuGetPackageId'
        $nugetVersion = Get-OptionalProperty $referenceItem 'NuGetPackageVersion'
        $pathInPackage = Get-OptionalProperty $referenceItem 'PathInPackage'
        $frameworkName = Get-OptionalProperty $referenceItem 'FrameworkReferenceName'
        $frameworkVersion = Get-OptionalProperty $referenceItem 'FrameworkReferenceVersion'
        $projectSourcePath = Get-OptionalProperty $referenceItem 'MSBuildSourceProjectFile'
        $referenceSourceTarget = Get-OptionalProperty $referenceItem 'ReferenceSourceTarget'
        $origin = ''
        $provenance = $null
        $ownerFamily = 'Unknown'
        if ($projectAuthorityByOutputPath.ContainsKey($referenceFullPath)) {
            $projectAuthority = $projectAuthorityByOutputPath[$referenceFullPath]
            if ([string]$rawReferenceFact.AssemblyName -cne [string]$projectAuthority.assemblyName -or
                $referenceSourceTarget -cne 'ProjectReference' -or [string]::IsNullOrWhiteSpace($projectSourcePath) -or
                -not (Test-ResolvedPathIdentityEqual $projectSourcePath ([string]$projectAuthority.projectFullPath))) {
                throw "EDGE-SPLIT-LEDGER-001 repository ReferencePath is not the exact evaluated ProjectReference output: $referenceFullPath."
            }
            $origin = "repository:$([string]$projectAuthority.outputPath)"
            $provenance = [pscustomobject][ordered]@{
                kind = 'project-reference'
                projectPath = [string]$projectAuthority.projectPath
                outputPath = [string]$projectAuthority.outputPath
            }
            $ownerFamily = [string]$projectAuthority.ownerFamily
        }
        elseif (-not [string]::IsNullOrWhiteSpace((ConvertTo-RepositoryPath $referenceFullPath))) {
            throw "EDGE-SPLIT-LEDGER-001 repository reference bytes are not owned by an exact evaluated ProjectReference output: $referenceFullPath."
        }
        elseif (-not [string]::IsNullOrWhiteSpace($frameworkName)) {
            $provenance = Get-FrameworkReferenceProvenance -ReferenceItem $referenceItem `
                -ReferencePath $referenceFullPath -TargetingPackRoot $targetingPackRoot
            $origin = "framework:$frameworkName/$frameworkVersion"
            $ownerFamily = 'PlatformOrThirdParty'
        }
        elseif (-not [string]::IsNullOrWhiteSpace($nugetId) -and
            -not [string]::IsNullOrWhiteSpace($pathInPackage)) {
            $provenance = Get-NuGetReferenceProvenance -ReferenceItem $referenceItem `
                -ReferencePath $referenceFullPath -Assets $assets -AssetsRepositoryPath $assetsRepositoryPath `
                -NuGetRoot $nugetRoot
            $origin = "nuget:$nugetId/$nugetVersion"
            $ownerFamily = 'PlatformOrThirdParty'
        }
        if ($null -eq $provenance -or $ownerFamily -ceq 'Unknown') {
            throw "EDGE-SPLIT-LEDGER-001 resolved reference has no exact project/restore/targeting-pack authority: assembly=$($rawReferenceFact.AssemblyName) path=$referenceFullPath."
        }
        $referenceFact = [pscustomobject][ordered]@{
            assemblyName = [string]$rawReferenceFact.AssemblyName
            assemblyVersion = [string]$rawReferenceFact.AssemblyVersion
            culture = [string]$rawReferenceFact.Culture
            publicKeyToken = [string]$rawReferenceFact.PublicKeyToken
            mvid = [string]$rawReferenceFact.Mvid
            size = [long]$rawReferenceFact.Size
            origin = $origin
            provenance = $provenance
            sha256 = [string]$rawReferenceFact.Sha256
            ownerFamily = $ownerFamily
        }
        $identityKey = Get-AssemblyIdentityKey $referenceFact
        if (-not $referenceFactByIdentity.TryAdd($identityKey, $referenceFact)) {
            throw "EDGE-SPLIT-LEDGER-001 resolved reference full identity is duplicated: $identityKey."
        }
        if ($referenceOwnerByAssembly.ContainsKey([string]$referenceFact.assemblyName)) {
            throw "EDGE-SPLIT-LEDGER-001 resolved reference simple name is ambiguous across full identities: $($referenceFact.assemblyName)."
        }
        $referenceOwnerByAssembly.Add([string]$referenceFact.assemblyName, $ownerFamily)
        $referenceAssembliesList.Add($referenceFact)
    }
    $referenceAssemblies = @($referenceAssembliesList.ToArray() |
        Sort-Ordinal assemblyName, assemblyVersion, culture, publicKeyToken, origin)
    foreach ($pluginFact in $peInputFacts) {
        if (-not $referenceOwnerByAssembly.TryAdd([string]$pluginFact.assemblyName, 'PluginOwned')) {
            throw "EDGE-SPLIT-LEDGER-001 plugin-owned identity collides with a resolved external reference: $($pluginFact.assemblyName)."
        }
    }

    $analysis = [IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::Analyze(
        [string]$evaluation.Properties.AssemblyName,
        $repositoryRoot,
        $pluginRoot,
        $generatedRoot,
        $compileSourcePaths,
        $generatedSourcePaths,
        $referencePaths,
        $preprocessorSymbols)

    if ($analysis.CompilationErrors.Count -gt 0) {
        $details = @($analysis.CompilationErrors | Select-Object -First 30 | ForEach-Object {
            "$($_.Id) $($_.SourcePath):$($_.Line):$($_.Column) $($_.Message)"
        }) -join "`n"
        throw "EDGE-SPLIT-LEDGER-001 Roslyn Compilation has $($analysis.CompilationErrors.Count) error(s):`n$details"
    }

    $externalSymbolUsages = [Collections.Generic.List[object]]::new()
    foreach ($usage in $analysis.SymbolUsages) {
        $usageIdentityKey = "$([string]$usage.OwnerAssembly), Version=$([string]$usage.OwnerAssemblyVersion), Culture=$([string]$usage.OwnerAssemblyCulture), PublicKeyToken=$([string]$usage.OwnerAssemblyPublicKeyToken)"
        if (-not $referenceFactByIdentity.ContainsKey($usageIdentityKey)) {
            throw "EDGE-SPLIT-LEDGER-001 Roslyn symbol owner lacks an exact resolved-reference full identity: $usageIdentityKey."
        }
        $disposition = Get-Disposition `
            -OwnerAssembly ([string]$usage.OwnerAssembly) `
            -ContainingNamespace ([string]$usage.ContainingNamespace) `
            -Symbol ([string]$usage.Symbol) `
            -SourcePath ([string]$usage.SourcePath) `
            -BatchId $CurrentBatch `
            -FrozenCarry020 $frozenCarry020Map `
            -FrozenCarry030 $frozenCarry030Map `
            -ReferenceOwnerByAssembly $referenceOwnerByAssembly
        $externalSymbolUsages.Add([pscustomobject][ordered]@{
            sourcePath = [string]$usage.SourcePath
            line = [int]$usage.Line
            column = [int]$usage.Column
            symbol = [string]$usage.Symbol
            symbolKind = [string]$usage.SymbolKind
            ownerAssembly = [string]$usage.OwnerAssembly
            ownerAssemblyVersion = [string]$usage.OwnerAssemblyVersion
            ownerAssemblyCulture = [string]$usage.OwnerAssemblyCulture
            ownerAssemblyPublicKeyToken = [string]$usage.OwnerAssemblyPublicKeyToken
            containingNamespace = [string]$usage.ContainingNamespace
            usageKind = [string]$usage.UsageKind
            ownerFamily = [string]$disposition.ownerFamily
            classification = [string]$disposition.classification
            disposition = [string]$disposition.disposition
            removalBatch = $disposition.removalBatch
            replacementContract = [string]$disposition.replacementContract
            protectionTest = [string]$disposition.protectionTest
            forbiddenForSourceLayer = [bool]$disposition.forbiddenForSourceLayer
        })
    }

    $unclassifiedSymbols = @($externalSymbolUsages | Where-Object { $_.classification -eq 'unclassified' })
    $unknownAssemblies = @($unclassifiedSymbols | ForEach-Object ownerAssembly | Sort-Ordinal -Unique)
    if ($unclassifiedSymbols.Count -gt 0 -or $unknownAssemblies.Count -gt 0) {
        $details = @($unclassifiedSymbols | Select-Object -First 40 | ForEach-Object {
            "$($_.ownerAssembly)|$($_.symbol)|$($_.sourcePath):$($_.line)"
        }) -join "`n"
        throw "EDGE-SPLIT-LEDGER-001 external symbol classification is incomplete: unknownAssemblies=$($unknownAssemblies.Count) unclassifiedSymbols=$($unclassifiedSymbols.Count)`n$details"
    }

    $carry020 = @($externalSymbolUsages |
        Where-Object removalBatch -eq 'EDGE-SPLIT-020' |
        Group-Ordinal sourcePath, ownerAssembly, symbol |
        ForEach-Object {
            $first = $_.Group[0]
            [pscustomobject][ordered]@{
                sourcePath = [string]$first.sourcePath
                symbol = [string]$first.symbol
                ownerAssembly = [string]$first.ownerAssembly
                ownerFamily = [string]$first.ownerFamily
                count = $_.Count
                usageKinds = @($_.Group | ForEach-Object usageKind | Sort-Ordinal -Unique)
                removalBatch = 'EDGE-SPLIT-020'
            }
        } | Sort-Ordinal sourcePath, ownerAssembly, symbol)
    $carry030 = @($externalSymbolUsages |
        Where-Object removalBatch -eq 'EDGE-SPLIT-030' |
        Group-Ordinal sourcePath, ownerAssembly, symbol |
        ForEach-Object {
            $first = $_.Group[0]
            [pscustomobject][ordered]@{
                sourcePath = [string]$first.sourcePath
                symbol = [string]$first.symbol
                ownerAssembly = [string]$first.ownerAssembly
                ownerFamily = [string]$first.ownerFamily
                count = $_.Count
                usageKinds = @($_.Group | ForEach-Object usageKind | Sort-Ordinal -Unique)
                removalBatch = 'EDGE-SPLIT-030'
            }
        } | Sort-Ordinal sourcePath, ownerAssembly, symbol)

    $baselineCarry020 = if ((Get-BatchRank $CurrentBatch) -eq 0) { $carry020 } else { @($baselineLedger.carrySets.'EDGE-SPLIT-020'.baselineItems) }
    $baselineCarry030 = if ((Get-BatchRank $CurrentBatch) -eq 0) { $carry030 } else { @($baselineLedger.carrySets.'EDGE-SPLIT-030'.baselineItems) }
    if ($baselineCarry020.Count -eq 0 -or $baselineCarry030.Count -eq 0) {
        throw "EDGE-SPLIT-LEDGER-001 both Phase 0 bounded carry sets must be non-empty: EDGE-SPLIT-020=$($baselineCarry020.Count), EDGE-SPLIT-030=$($baselineCarry030.Count)."
    }

    $batchRank = Get-BatchRank $CurrentBatch
    $carry020Status = if ($batchRank -eq 0) { 'frozen' } elseif ($batchRank -lt 20) { 'retained-exact' } else { 'closed' }
    $carry030Status = if ($batchRank -eq 0) { 'frozen' } elseif ($batchRank -lt 30) { 'retained-exact' } else { 'closed' }
    if ($carry020Status -eq 'retained-exact' -and -not (Test-CarrySetsEqual $baselineCarry020 $carry020)) {
        throw 'EDGE-SPLIT-LEDGER-001 EDGE-SPLIT-020 carry set must remain exactly frozen until its removal batch.'
    }
    if ($carry030Status -eq 'retained-exact' -and -not (Test-CarrySetsEqual $baselineCarry030 $carry030)) {
        throw 'EDGE-SPLIT-LEDGER-001 EDGE-SPLIT-030 carry set must remain exactly frozen until its removal batch.'
    }
    if ($carry020Status -eq 'closed' -and $carry020.Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 EDGE-SPLIT-020 carry set must be zero from EDGE-SPLIT-020 onward.'
    }
    if ($carry030Status -eq 'closed' -and $carry030.Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 EDGE-SPLIT-030 carry set must be zero from EDGE-SPLIT-030 onward.'
    }

    $phase1ResidualUsages = @($externalSymbolUsages | Where-Object removalBatch -eq 'EDGE-SPLIT-010')
    if ($batchRank -ge 10 -and $phase1ResidualUsages.Count -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 EDGE-SPLIT-010 forbidden symbol residuals must be zero after Phase 1: $($phase1ResidualUsages.Count)."
    }

    $evaluatedProjectReferences = @($evaluation.Items.ProjectReference | ForEach-Object {
        $fullPath = [string]$_.FullPath
        if ([string]::IsNullOrWhiteSpace($fullPath) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw 'EDGE-SPLIT-LEDGER-001 evaluated ProjectReference inventory contains a missing project.'
        }
        $projectRelativePath = ConvertTo-RepositoryPath $fullPath
        if (-not $projectAuthorityByProjectPath.ContainsKey($projectRelativePath)) {
            throw "EDGE-SPLIT-LEDGER-001 evaluated ProjectReference lacks its exact independently evaluated authority: $projectRelativePath."
        }
        $projectAuthority = $projectAuthorityByProjectPath[$projectRelativePath]
        $projectName = [string]$projectAuthority.assemblyName
        $ownerFamily = [string]$projectAuthority.ownerFamily
        $referenceOutputAssemblyValue = Get-OptionalProperty $_ 'ReferenceOutputAssembly'
        $outputItemType = Get-OptionalProperty $_ 'OutputItemType'
        $definingProjectFullPath = Get-OptionalProperty $_ 'DefiningProjectFullPath'
        [pscustomobject][ordered]@{
            projectPath = $projectRelativePath
            projectName = $projectName
            ownerFamily = $ownerFamily
            direct = -not [string]::IsNullOrWhiteSpace($definingProjectFullPath) -and
                [IO.Path]::GetFullPath($definingProjectFullPath) -ceq [IO.Path]::GetFullPath($resolvedPluginProject)
            referenceOutputAssembly = $referenceOutputAssemblyValue -cne 'false'
            outputItemType = $outputItemType
            forbiddenForSourceLayer = (Test-SourceForbiddenOwnerFamily $ownerFamily) -and $referenceOutputAssemblyValue -cne 'false'
        }
    } | Sort-Ordinal projectPath, direct -Unique)

    $peAssemblyReferences = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyReferences(
        $repositoryRoot,
        [string[]]$peInputAssemblies) | ForEach-Object {
        $referenceIdentityKey = "$([string]$_.ReferencedAssembly), Version=$([string]$_.ReferencedVersion), Culture=$([string]$_.ReferencedCulture), PublicKeyToken=$([string]$_.ReferencedPublicKeyToken)"
        $ownerFamily = if ($referenceFactByIdentity.ContainsKey($referenceIdentityKey)) {
            [string]$referenceFactByIdentity[$referenceIdentityKey].ownerFamily
        }
        else { 'Unknown' }
        [pscustomobject][ordered]@{
            sourcePath = [string]$_.SourcePath
            sourceAssembly = [string]$_.SourceAssembly
            referencedAssembly = [string]$_.ReferencedAssembly
            referencedVersion = [string]$_.ReferencedVersion
            referencedCulture = [string]$_.ReferencedCulture
            referencedPublicKeyToken = [string]$_.ReferencedPublicKeyToken
            ownerFamily = $ownerFamily
            forbiddenForSourceLayer = Test-SourceForbiddenOwnerFamily $ownerFamily
        }
    } | Sort-Ordinal sourceAssembly, referencedAssembly, referencedVersion, referencedCulture, referencedPublicKeyToken, sourcePath)

    $contractSurfaceClosures = [Collections.Generic.List[object]]::new()
    foreach ($surfaceAssemblyName in @($phase0Inputs.decisions.sdkPackages)) {
        if (-not $formalSurfaceProjectByAssembly.ContainsKey([string]$surfaceAssemblyName)) { continue }
        $surfaceProjectPath = $formalSurfaceProjectByAssembly[[string]$surfaceAssemblyName]
        $surfaceEvaluationArguments = @(
            'msbuild', $surfaceProjectPath, '-nologo', '-noAutoResponse', '-t:ResolveReferences',
            "-p:Configuration=$Configuration", "-p:SourceRevisionId=$currentHead", "-p:RepositoryCommit=$currentHead",
            '-getProperty:AssemblyName,TargetPath', '-getItem:ProjectReference'
        )
        $surfaceEvaluation = (Invoke-CapturedCommand dotnet $surfaceEvaluationArguments) | ConvertFrom-Json -Depth 100
        if ([string]$surfaceEvaluation.Properties.AssemblyName -cne [string]$surfaceAssemblyName) {
            throw "EDGE-SPLIT-LEDGER-001 formal surface project/assembly identity mismatch: expected=$surfaceAssemblyName actual=$($surfaceEvaluation.Properties.AssemblyName)."
        }
        $surfaceAssemblyPath = [IO.Path]::GetFullPath([string]$surfaceEvaluation.Properties.TargetPath)
        Assert-NoRepositoryReparsePoint -FullPath $surfaceAssemblyPath
        Assert-CanonicalEmbeddedDebugIdentity -AssemblyPath $surfaceAssemblyPath
        $surfaceAssemblyFactRaw = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyInputs(
                $repositoryRoot, [string[]]@($surfaceAssemblyPath)))[0]
        $surfaceAssemblyFact = [pscustomobject][ordered]@{
            path = [string]$surfaceAssemblyFactRaw.SourcePath
            assemblyName = [string]$surfaceAssemblyFactRaw.AssemblyName
            assemblyVersion = [string]$surfaceAssemblyFactRaw.AssemblyVersion
            culture = [string]$surfaceAssemblyFactRaw.Culture
            publicKeyToken = [string]$surfaceAssemblyFactRaw.PublicKeyToken
            mvid = [string]$surfaceAssemblyFactRaw.Mvid
            size = [long]$surfaceAssemblyFactRaw.Size
            sha256 = [string]$surfaceAssemblyFactRaw.Sha256
            verifiedPluginOwned = $false
        }
        $surfaceProjectReferences = @($surfaceEvaluation.Items.ProjectReference | ForEach-Object {
                $fullPath = [string]$_.FullPath
                if ([string]::IsNullOrWhiteSpace($fullPath) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return }
                $projectName = [IO.Path]::GetFileNameWithoutExtension($fullPath)
                $projectRelativePath = ConvertTo-RepositoryPath $fullPath
                $ownerFamily = Get-InternalProjectOwnerFamily -ProjectName $projectName -ProjectPath $projectRelativePath
                $referenceOutputAssemblyValue = Get-OptionalProperty $_ 'ReferenceOutputAssembly'
                [pscustomobject][ordered]@{
                    projectPath = $projectRelativePath
                    projectName = $projectName
                    ownerFamily = $ownerFamily
                    referenceOutputAssembly = $referenceOutputAssemblyValue -cne 'false'
                    forbiddenForContractSurface = ((Test-SourceForbiddenOwnerFamily $ownerFamily) -or $ownerFamily -eq 'PluginOwned') -and
                        $referenceOutputAssemblyValue -cne 'false'
                }
            } | Sort-Ordinal projectPath -Unique)
        $surfaceAssemblyReferences = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyReferences(
                $repositoryRoot, [string[]]@($surfaceAssemblyPath)) | ForEach-Object {
                $referenceIdentityKey = "$([string]$_.ReferencedAssembly), Version=$([string]$_.ReferencedVersion), Culture=$([string]$_.ReferencedCulture), PublicKeyToken=$([string]$_.ReferencedPublicKeyToken)"
                $ownerFamily = if ($referenceFactByIdentity.ContainsKey($referenceIdentityKey)) {
                    [string]$referenceFactByIdentity[$referenceIdentityKey].ownerFamily
                }
                else { 'Unknown' }
                [pscustomobject][ordered]@{
                    referencedAssembly = [string]$_.ReferencedAssembly
                    referencedVersion = [string]$_.ReferencedVersion
                    referencedCulture = [string]$_.ReferencedCulture
                    referencedPublicKeyToken = [string]$_.ReferencedPublicKeyToken
                    ownerFamily = $ownerFamily
                    forbiddenForContractSurface = (Test-SourceForbiddenOwnerFamily $ownerFamily) -or $ownerFamily -eq 'PluginOwned'
                }
            } | Sort-Ordinal referencedAssembly, referencedVersion, referencedCulture, referencedPublicKeyToken)
        $surfaceUnknown = @($surfaceProjectReferences + $surfaceAssemblyReferences | Where-Object ownerFamily -eq 'Unknown')
        if ($surfaceUnknown.Count -ne 0) {
            throw "EDGE-SPLIT-LEDGER-001 formal SDK/UI surface closure has unknown owners: surface=$surfaceAssemblyName count=$($surfaceUnknown.Count)."
        }
        $contractSurfaceClosures.Add([pscustomobject][ordered]@{
                projectPath = ConvertTo-RepositoryPath $surfaceProjectPath
                assemblyName = $surfaceAssemblyName
                assemblyInput = $surfaceAssemblyFact
                projectReferences = $surfaceProjectReferences
                assemblyReferences = $surfaceAssemblyReferences
                forbiddenProjectReferenceCount = @($surfaceProjectReferences | Where-Object forbiddenForContractSurface).Count
                forbiddenAssemblyReferenceCount = @($surfaceAssemblyReferences | Where-Object forbiddenForContractSurface).Count
                unknownOwnerCount = 0
            })
    }
    if ($batchRank -ge 10 -and $contractSurfaceClosures.Count -ne @($phase0Inputs.decisions.sdkPackages).Count) {
        throw "EDGE-SPLIT-LEDGER-001 Phase 1+ requires all four formal SDK/UI package projects to participate in closure gates."
    }
    $contractSurfaceForbiddenCount = (@($contractSurfaceClosures | ForEach-Object {
                [int]$_.forbiddenProjectReferenceCount + [int]$_.forbiddenAssemblyReferenceCount
            } | Measure-Object -Sum).Sum)
    if ($null -eq $contractSurfaceForbiddenCount) { $contractSurfaceForbiddenCount = 0 }
    if ($batchRank -ge 10 -and [int]$contractSurfaceForbiddenCount -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 formal SDK/UI surfaces must have zero reverse dependency closure from EDGE-SPLIT-010 onward: $contractSurfaceForbiddenCount."
    }

    $packageEntries = [Collections.Generic.List[object]]::new()
    $packageAssemblies = [Collections.Generic.List[object]]::new()
    $packageEntryPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $packageAssemblyIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $packageAssemblyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $packageLimits = [pscustomobject][ordered]@{
        maxEntryCount = 256
        maxCompressedPackageBytes = 134217728
        maxEntryUncompressedBytes = 67108864
        maxTotalUncompressedBytes = 268435456
    }
    $packageStaticInputByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($staticInput in $packageStaticInputs) {
        if (-not $packageStaticInputByPath.TryAdd([string]$staticInput.packagePath, $staticInput)) {
            throw "EDGE-SPLIT-LEDGER-001 package static input paths must be ordinal-unique: $($staticInput.packagePath)."
        }
    }
    $packageLayerStatus = if ([string]::IsNullOrWhiteSpace($resolvedPluginPackagePath)) { 'not-applicable-before-EDGE-SPLIT-040' } else { 'evaluated' }
    if ($packageLayerStatus -eq 'evaluated') {
        if ((Get-Item -LiteralPath $resolvedPluginPackagePath).Length -gt [long]$packageLimits.maxCompressedPackageBytes) {
            throw 'EDGE-SPLIT-LEDGER-001 plugin package exceeds the compressed-file byte limit.'
        }
        $candidateArchive = [IO.Compression.ZipFile]::OpenRead($resolvedPluginPackagePath)
        try {
            if ($candidateArchive.Entries.Count -gt [int]$packageLimits.maxEntryCount) {
                throw 'EDGE-SPLIT-LEDGER-001 plugin package exceeds the ZIP entry-count limit.'
            }
            [long]$totalUncompressedBytes = 0
            foreach ($entry in @($candidateArchive.Entries | Sort-Ordinal FullName)) {
                if ([long]$entry.Length -gt [long]$packageLimits.maxEntryUncompressedBytes) {
                    throw "EDGE-SPLIT-LEDGER-001 plugin package entry exceeds the uncompressed byte limit: $($entry.FullName)."
                }
                $totalUncompressedBytes += [long]$entry.Length
                if ($totalUncompressedBytes -gt [long]$packageLimits.maxTotalUncompressedBytes) {
                    throw 'EDGE-SPLIT-LEDGER-001 plugin package exceeds the total uncompressed byte limit.'
                }
                $externalAttributes = [uint32]$entry.ExternalAttributes
                $unixFileType = (($externalAttributes -shr 16) -band 0xF000)
                if ($unixFileType -eq 0xA000 -or
                    ($externalAttributes -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "EDGE-SPLIT-LEDGER-001 plugin package contains a symlink/reparse entry: $($entry.FullName)"
                }
                if ([string]::IsNullOrWhiteSpace($entry.Name)) { continue }
                $entryPath = $entry.FullName.Replace('\', '/')
                if ($entryPath.StartsWith('/', [StringComparison]::Ordinal) -or
                    (Test-InvariantPattern $entryPath '^[A-Za-z]:/') -or
                    $entryPath.Contains('../', [StringComparison]::Ordinal) -or
                    $entryPath.Contains('/..', [StringComparison]::Ordinal)) {
                    throw "EDGE-SPLIT-LEDGER-001 plugin package contains an unsafe path: $entryPath"
                }
                if (-not $packageEntryPaths.Add($entryPath)) {
                    throw "EDGE-SPLIT-LEDGER-001 plugin package contains a duplicate entry path: $entryPath"
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
                if ($packageStaticInputByPath.ContainsKey($entryPath)) {
                    $staticInput = $packageStaticInputByPath[$entryPath]
                    $category = [string]$staticInput.category
                    $owner = 'plugin'
                    $allowed = $entrySha256 -ceq [string]$staticInput.sha256 -and
                        [long]$entry.Length -eq [long]$staticInput.size
                }
                elseif ($entryPath.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                    $assemblyTempPath = Join-Path $temporaryRoot "package-$([Guid]::NewGuid().ToString('N')).dll"
                    $entryStream = $entry.Open()
                    $assemblyStream = [IO.File]::Create($assemblyTempPath)
                    try { $entryStream.CopyTo($assemblyStream) }
                    finally { $assemblyStream.Dispose(); $entryStream.Dispose() }
                    try {
                        $rawAssemblyFact = @([IIoT.Edge.ContractLedger.EdgePluginContractLedgerRoslyn]::ReadAssemblyInputs(
                                $repositoryRoot, [string[]]@($assemblyTempPath)))[0]
                    }
                    catch {
                        throw "EDGE-SPLIT-LEDGER-001 packaged DLL has no valid managed assembly identity: $entryPath"
                    }
                    $assemblyFact = [pscustomobject][ordered]@{
                        assemblyName = [string]$rawAssemblyFact.AssemblyName
                        assemblyVersion = [string]$rawAssemblyFact.AssemblyVersion
                        culture = [string]$rawAssemblyFact.Culture
                        publicKeyToken = [string]$rawAssemblyFact.PublicKeyToken
                        mvid = [string]$rawAssemblyFact.Mvid
                        size = [long]$rawAssemblyFact.Size
                        sha256 = [string]$rawAssemblyFact.Sha256
                    }
                    $identityKey = Get-AssemblyIdentityKey $assemblyFact
                    if (-not $packageAssemblyIdentities.Add($identityKey) -or
                        -not $packageAssemblyNames.Add([string]$assemblyFact.assemblyName)) {
                        throw "EDGE-SPLIT-LEDGER-001 plugin package contains duplicate assembly identity/name: $identityKey."
                    }
                    $verifiedFact = if ($verifiedAssemblyFactsByName.ContainsKey([string]$assemblyFact.assemblyName)) {
                        $verifiedAssemblyFactsByName[[string]$assemblyFact.assemblyName]
                    } else { $null }
                    $byteMatchVerifiedInput = $null -ne $verifiedFact -and
                        (Get-AssemblyIdentityKey $assemblyFact) -ceq (Get-AssemblyIdentityKey $verifiedFact) -and
                        [string]$assemblyFact.mvid -ceq [string]$verifiedFact.mvid -and
                        [long]$assemblyFact.size -eq [long]$verifiedFact.size -and
                        [string]$assemblyFact.sha256 -ceq [string]$verifiedFact.sha256
                    $declaredPluginOwned = $byteMatchVerifiedInput -and $declaredPackageOwnedNames.Contains([string]$assemblyFact.assemblyName)
                    $ownerFamily = Get-OwnerFamily ([string]$assemblyFact.assemblyName) $referenceOwnerByAssembly
                    if (-not $declaredPluginOwned -and $ownerFamily -eq 'PluginOwned') { $ownerFamily = 'Unknown' }
                    $forbiddenAssembly = -not $declaredPluginOwned -or (Test-PackageForbiddenOwnerFamily $ownerFamily)
                    $packageAssemblies.Add([pscustomobject][ordered]@{
                        path = $entryPath
                        assemblyName = [string]$assemblyFact.assemblyName
                        assemblyVersion = [string]$assemblyFact.assemblyVersion
                        culture = [string]$assemblyFact.culture
                        publicKeyToken = [string]$assemblyFact.publicKeyToken
                        mvid = [string]$assemblyFact.mvid
                        size = [long]$assemblyFact.size
                        sha256 = [string]$assemblyFact.sha256
                        ownerFamily = $ownerFamily
                        declaredPluginOwned = $declaredPluginOwned
                        byteMatchVerifiedInput = $byteMatchVerifiedInput
                        forbiddenForPackageLayer = $forbiddenAssembly
                    })
                    $category = if ($declaredPluginOwned) { 'plugin-owned-assembly' } else { 'forbidden-assembly' }
                    $owner = if ($declaredPluginOwned) { 'plugin' } else { $ownerFamily }
                    $allowed = -not $forbiddenAssembly
                }
                $packageEntries.Add([pscustomobject][ordered]@{
                    path = $entryPath
                    size = [long]$entry.Length
                    sha256 = $entrySha256
                    category = $category
                    owner = $owner
                    allowed = $allowed
                })
            }
        }
        finally { $candidateArchive.Dispose() }
        foreach ($requiredStaticInput in @($packageStaticInputs | Where-Object required)) {
            if (-not $packageEntryPaths.Contains([string]$requiredStaticInput.packagePath)) {
                throw "EDGE-SPLIT-LEDGER-001 candidate package omits required current source/build bytes: $($requiredStaticInput.packagePath)."
            }
        }
    }

    $unknownProjectReferences = @($evaluatedProjectReferences | Where-Object ownerFamily -eq 'Unknown')
    $unknownPeAssemblyReferences = @($peAssemblyReferences | Where-Object ownerFamily -eq 'Unknown')
    $unknownPackageAssemblies = @($packageAssemblies | Where-Object ownerFamily -eq 'Unknown')
    if ($unknownProjectReferences.Count -gt 0 -or $unknownPeAssemblyReferences.Count -gt 0 -or $unknownPackageAssemblies.Count -gt 0) {
        throw "EDGE-SPLIT-LEDGER-001 dependency layer classification is incomplete: project=$($unknownProjectReferences.Count) pe=$($unknownPeAssemblyReferences.Count) package=$($unknownPackageAssemblies.Count)."
    }

    $projectForbidden = @($evaluatedProjectReferences | Where-Object forbiddenForSourceLayer)
    $roslynForbidden = @($externalSymbolUsages | Where-Object forbiddenForSourceLayer)
    $peForbidden = @($peAssemblyReferences | Where-Object forbiddenForSourceLayer)
    $packageForbidden = @($packageAssemblies | Where-Object forbiddenForPackageLayer)
    $packageForbiddenFiles = @($packageEntries | Where-Object { -not [bool]$_.allowed })
    $packageUnclassifiedFiles = @($packageEntries | Where-Object category -eq 'unclassified')

    Assert-ExactPhaseLayerGate `
        -BatchId $CurrentBatch `
        -ProjectForbidden $projectForbidden `
        -PeForbidden $peForbidden `
        -RoslynForbidden $roslynForbidden `
        -Carry020Baseline $baselineCarry020 `
        -Carry030Baseline $baselineCarry030
    if ($batchRank -ge 40 -and ($packageForbidden.Count -ne 0 -or $packageForbiddenFiles.Count -ne 0 -or $packageUnclassifiedFiles.Count -ne 0)) {
        throw "EDGE-SPLIT-LEDGER-001 package allowlist must be closed from EDGE-SPLIT-040 onward: assemblies=$($packageForbidden.Count) forbiddenFiles=$($packageForbiddenFiles.Count) unclassifiedFiles=$($packageUnclassifiedFiles.Count)."
    }

    [xml]$solution = Get-Content -LiteralPath (Join-Path $repositoryRoot 'IIoT.EdgeClient.slnx') -Raw
    $solutionProjects = @($solution.SelectNodes('//Project') | ForEach-Object {
        ([string]$_.Path).Replace('\', '/')
    } | Sort-Ordinal)

    $testInventoryPath = Join-Path $repositoryRoot 'scripts/tests/edge-test-inventory.json'
    $requiredCountsPath = Join-Path $repositoryRoot 'scripts/tests/required-test-counts.json'
    $discoveredInventoryPath = Join-Path $repositoryRoot 'scripts/tests/discovered-test-inventory.json'
    $testInventory = Get-Content -LiteralPath $testInventoryPath -Raw | ConvertFrom-Json -Depth 40
    $requiredCounts = Get-Content -LiteralPath $requiredCountsPath -Raw | ConvertFrom-Json -Depth 40
    $discoveredInventory = Get-Content -LiteralPath $discoveredInventoryPath -Raw | ConvertFrom-Json -Depth 40
    $baselineTestEvidence = $phase0Inputs.testEvidence.historicalBaseline
    if ([int]$testInventory.solutionProjectCount -ne $solutionProjects.Count -or
        [int]$requiredCounts.caseCount -ne [int]$discoveredInventory.caseCount -or
        [int]$testInventory.testProjectCount -ne @($requiredCounts.projects).Count -or
        [string]$baselineTestEvidence.sourceHead -cne $baselineHead -or
        [int]$baselineTestEvidence.executed -ne [int]$baselineTestEvidence.discovered -or
        [int]$baselineTestEvidence.passed -ne [int]$baselineTestEvidence.discovered -or
        [int]$baselineTestEvidence.failed -ne 0 -or
        [int]$baselineTestEvidence.skipped -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 solution/test inventory does not reconcile with the frozen evidence.'
    }

    if ($batchRank -eq 0 -and ([string]$pluginManifest.moduleId -cne [string]$phase0Inputs.publishedComposition.plugin.moduleId -or
        [string]$pluginManifest.version -cne [string]$phase0Inputs.publishedComposition.plugin.version -or
        [string]$pluginManifest.hostApiVersion -cne [string]$phase0Inputs.publishedComposition.plugin.hostApiVersion)) {
        throw 'EDGE-SPLIT-LEDGER-001 current plugin manifest does not match the frozen old-composition identity.'
    }

    $resourceInventory = [Collections.Generic.List[object]]::new()
    $pageInventory = [Collections.Generic.List[object]]::new()
    foreach ($xamlFile in @(Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter '*.axaml' |
            Where-Object {
                $relative = [IO.Path]::GetRelativePath($pluginRoot, $_.FullName).Replace('\', '/')
                -not $relative.StartsWith('bin/', [StringComparison]::Ordinal) -and
                    -not $relative.StartsWith('obj/', [StringComparison]::Ordinal)
            } | Sort-Ordinal FullName)) {
        [xml]$xaml = Get-Content -LiteralPath $xamlFile.FullName -Raw
        $namespaceManager = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
        $namespaceManager.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
        $relativePath = ConvertTo-RepositoryPath $xamlFile.FullName
        $xamlSize = [long]$xamlFile.Length
        $xamlSha256 = Get-Sha256 $xamlFile.FullName
        foreach ($node in @($xaml.SelectNodes('//*[@x:Key]', $namespaceManager))) {
            $resourceInventory.Add([pscustomobject][ordered]@{
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
                throw "EDGE-SPLIT-LEDGER-001 XAML page lacks its required code-behind: $relativePath."
            }
            $pageInventory.Add([pscustomobject][ordered]@{
                sourcePath = $relativePath
                sourceSize = $xamlSize
                sourceSha256 = $xamlSha256
                className = $className
                codeBehindPath = ConvertTo-RepositoryPath $codeBehindPath
                codeBehindSize = [long](Get-Item -LiteralPath $codeBehindPath).Length
                codeBehindSha256 = Get-Sha256 $codeBehindPath
            })
        }
    }

    $viewContractAssemblyPath = if ([string]::IsNullOrWhiteSpace($ViewIdsAssemblyPath)) {
        $candidates = @($referencePaths | Where-Object {
                [IO.Path]::GetFileName($_) -ceq 'IIoT.Edge.Presentation.Navigation.dll'
            })
        if ($candidates.Count -ne 1) {
            throw "EDGE-SPLIT-LEDGER-001 expected exactly one evaluated view-contract reference, actual=$($candidates.Count)."
        }
        $candidates[0]
    }
    else {
        Resolve-RepositoryPath $ViewIdsAssemblyPath
    }
    if (-not (Test-Path -LiteralPath $viewContractAssemblyPath -PathType Leaf)) {
        throw "EDGE-SPLIT-LEDGER-001 view-contract assembly was not built: $viewContractAssemblyPath"
    }
    $viewContractAssembly = [Reflection.Assembly]::LoadFrom($viewContractAssemblyPath)
    $viewIdsType = $viewContractAssembly.GetType(
        $ViewIdsTypeName,
        $true,
        $false)
    $createMethod = $viewIdsType.GetMethod('Create', [Reflection.BindingFlags]'Public,Static')
    $viewIds = $createMethod.Invoke($null, @([string]$pluginManifest.moduleId))
    $viewInventory = @($viewIdsType.GetProperties([Reflection.BindingFlags]'Public,Instance') |
        Sort-Ordinal Name |
        ForEach-Object {
            $viewId = [string]$_.GetValue($viewIds)
            [pscustomobject][ordered]@{
                viewId = $viewId
                propertyName = [string]$_.Name
                registrationSource = [string]($externalSymbolUsages | Where-Object {
                    $_.symbol.Contains($ViewIdsTypeName, [StringComparison]::Ordinal)
                } | Select-Object -First 1 -ExpandProperty sourcePath)
                viewOwner = if ([string]$_.Name -eq 'DataView') { 'plugin-custom-page' } else { 'host-standard-page' }
            }
        })

    if ([string]$phase0Inputs.publishedComposition.byteVerification.status -cne 'verified' -or
        [string]$phase0Inputs.publishedComposition.byteVerification.method -cne 'download-size-sha256-and-archive-inventory') {
        throw 'EDGE-SPLIT-LEDGER-001 frozen historical composition lacks explicit byte-verification provenance.'
    }
    $hostManifestEvidence = [pscustomobject][ordered]@{
        url = [string]$phase0Inputs.publishedComposition.host.manifestUrl
        size = [long]$phase0Inputs.publishedComposition.host.manifestSize
        sha256 = [string]$phase0Inputs.publishedComposition.host.manifestSha256
        verified = $true
    }
    $hostArtifactEvidence = [pscustomobject][ordered]@{
        url = [string]$phase0Inputs.publishedComposition.host.artifactUrl
        size = [long]$phase0Inputs.publishedComposition.host.size
        sha256 = [string]$phase0Inputs.publishedComposition.host.sha256
        verified = $true
    }
    $pluginArtifactEvidence = [pscustomobject][ordered]@{
        url = [string]$phase0Inputs.publishedComposition.plugin.artifactUrl
        size = [long]$phase0Inputs.publishedComposition.plugin.size
        sha256 = [string]$phase0Inputs.publishedComposition.plugin.sha256
        verified = $true
    }
    $pluginPackageEntries = @($phase0Inputs.publishedComposition.plugin.packageEntries |
        ForEach-Object {
            [pscustomobject][ordered]@{ path = [string]$_.path; size = [long]$_.size }
        } | Sort-Ordinal path)
    if ($pluginPackageEntries.Count -eq 0 -or @($pluginPackageEntries | Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.path) -or [long]$_.size -lt 0
        }).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 frozen historical plugin archive inventory is empty or invalid.'
    }
    $historicalPackagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($historicalEntry in $pluginPackageEntries) { [void]$historicalPackagePaths.Add([string]$historicalEntry.path) }
    $missingRequiredHistoricalEntries = @($phase0Inputs.publishedComposition.plugin.requiredPackageEntries |
        Where-Object { -not $historicalPackagePaths.Contains([string]$_) })
    if ($missingRequiredHistoricalEntries.Count -ne 0) {
        throw "EDGE-SPLIT-LEDGER-001 frozen historical plugin archive inventory lacks required entries: $($missingRequiredHistoricalEntries -join ', ')."
    }

    if ($RefreshHistoricalArtifactEvidence) {
        $hostManifestFile = Join-Path $downloadRoot 'installer-artifact.json'
        $hostArtifactFile = Join-Path $downloadRoot 'IIoT.Edge.Setup.exe'
        $pluginArtifactFile = Join-Path $downloadRoot 'IIoT.EdgePlugin.Homogenization.zip'
        $hostManifestEvidence = Assert-DownloadedArtifact `
            -Uri ([string]$phase0Inputs.publishedComposition.host.manifestUrl) `
            -Destination $hostManifestFile `
            -ExpectedSha256 ([string]$phase0Inputs.publishedComposition.host.manifestSha256) `
            -ExpectedSize ([long]$phase0Inputs.publishedComposition.host.manifestSize)
        $hostArtifactEvidence = Assert-DownloadedArtifact `
            -Uri ([string]$phase0Inputs.publishedComposition.host.artifactUrl) `
            -Destination $hostArtifactFile `
            -ExpectedSha256 ([string]$phase0Inputs.publishedComposition.host.sha256) `
            -ExpectedSize ([long]$phase0Inputs.publishedComposition.host.size)
        $pluginArtifactEvidence = Assert-DownloadedArtifact `
            -Uri ([string]$phase0Inputs.publishedComposition.plugin.artifactUrl) `
            -Destination $pluginArtifactFile `
            -ExpectedSha256 ([string]$phase0Inputs.publishedComposition.plugin.sha256) `
            -ExpectedSize ([long]$phase0Inputs.publishedComposition.plugin.size)

        $hostManifest = Get-Content -LiteralPath $hostManifestFile -Raw | ConvertFrom-Json -Depth 30
        if ([string]$hostManifest.version -cne [string]$phase0Inputs.publishedComposition.host.version -or
            [string]$hostManifest.hostApiVersion -cne [string]$phase0Inputs.publishedComposition.host.hostApiVersion -or
            [string]$hostManifest.sourceCommit -cne [string]$phase0Inputs.publishedComposition.host.sourceCommit -or
            [string]$hostManifest.installerStubSha256 -cne [string]$phase0Inputs.publishedComposition.host.sha256 -or
            [long]$hostManifest.installerStubSize -ne [long]$phase0Inputs.publishedComposition.host.size) {
            throw 'EDGE-SPLIT-LEDGER-001 downloaded host manifest does not bind the expected host artifact/source commit.'
        }
        $hostModule = @($hostManifest.modules | Where-Object moduleId -eq ([string]$pluginManifest.moduleId))
        if ($hostModule.Count -ne 1 -or
            [string]$hostModule[0].version -cne [string]$phase0Inputs.publishedComposition.plugin.version -or
            [string]$hostModule[0].hostApiVersion -cne [string]$phase0Inputs.publishedComposition.plugin.hostApiVersion) {
            throw 'EDGE-SPLIT-LEDGER-001 host manifest does not contain the expected Homogenization old-composition member.'
        }

        $historicalArchive = [IO.Compression.ZipFile]::OpenRead($pluginArtifactFile)
        try {
            $downloadedEntries = @($historicalArchive.Entries |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
                ForEach-Object {
                    [pscustomobject]@{ path = $_.FullName.Replace('\', '/'); size = [long]$_.Length }
                } | Sort-Ordinal path)
        }
        finally { $historicalArchive.Dispose() }
        if (($downloadedEntries | ConvertTo-Json -Depth 10 -Compress) -cne
            ($pluginPackageEntries | ConvertTo-Json -Depth 10 -Compress)) {
            throw 'EDGE-SPLIT-LEDGER-001 downloaded historical plugin archive inventory differs from the frozen byte evidence.'
        }
    }

    $pluginSourceInventory = @($compileSourcePaths |
        Where-Object {
            $relative = ConvertTo-RepositoryPath $_
            -not [string]::IsNullOrWhiteSpace($relative) -and
                $relative.StartsWith("$pluginRelativeRoot/", [StringComparison]::Ordinal) -and
                -not $relative.Contains('/obj/', [StringComparison]::Ordinal)
        } |
        ForEach-Object {
            [pscustomobject][ordered]@{
                path = ConvertTo-RepositoryPath $_
                sha256 = Get-Sha256 $_
            }
        } | Sort-Ordinal path)
    $generatedSourceInventory = @($generatedSourcePaths | ForEach-Object {
        [pscustomobject][ordered]@{
            path = "generated/$([IO.Path]::GetRelativePath($generatedRoot, $_).Replace('\', '/'))"
            sha256 = Get-Sha256 $_
        }
    } | Sort-Ordinal path)
    $compilationInputMap = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($sourcePath in $compileSourcePaths) {
        $relativePath = ConvertTo-RepositoryPath $sourcePath
        if ([string]::IsNullOrWhiteSpace($relativePath) -or $compilationInputMap.ContainsKey($relativePath)) {
            throw "EDGE-SPLIT-LEDGER-001 MSBuild Compile inputs must have unique repository-stable paths: $sourcePath"
        }
        $compilationInputMap.Add($relativePath, [pscustomobject][ordered]@{
                path = $relativePath
                sha256 = Get-Sha256 $sourcePath
            })
    }
    foreach ($generatedSource in $generatedSourceInventory) {
        if ($compilationInputMap.ContainsKey([string]$generatedSource.path)) {
            throw "EDGE-SPLIT-LEDGER-001 emitted generated Compile input path collides with a regular input: $($generatedSource.path)"
        }
        $compilationInputMap.Add([string]$generatedSource.path, $generatedSource)
    }
    $compilationInputInventory = @($compilationInputMap.Values | Sort-Ordinal path)

    $inputDigestLines = [Collections.Generic.List[string]]::new()
    foreach ($source in $compilationInputInventory) {
        $inputDigestLines.Add("source|$($source.path)|$($source.sha256)")
    }
    foreach ($reference in $referenceAssemblies) {
        $provenanceJson = $reference.provenance | ConvertTo-Json -Depth 10 -Compress
        $inputDigestLines.Add("reference|$($reference.assemblyName)|$($reference.assemblyVersion)|$($reference.culture)|$($reference.publicKeyToken)|$($reference.mvid)|$($reference.size)|$($reference.origin)|$provenanceJson|$($reference.sha256)|$($reference.ownerFamily)")
    }
    foreach ($reference in $evaluatedProjectReferences) {
        $inputDigestLines.Add("project-reference|$($reference.projectPath)|$($reference.ownerFamily)|$($reference.direct)|$($reference.referenceOutputAssembly)|$($reference.outputItemType)")
    }
    foreach ($reference in $peAssemblyReferences) {
        $inputDigestLines.Add("pe-reference|$($reference.sourceAssembly)|$($reference.referencedAssembly)|$($reference.referencedVersion)|$($reference.referencedCulture)|$($reference.referencedPublicKeyToken)|$($reference.ownerFamily)")
    }
    foreach ($inputFact in $peInputFacts) {
        $inputDigestLines.Add("pe-input|$($inputFact.path)|$($inputFact.assemblyName)|$($inputFact.assemblyVersion)|$($inputFact.culture)|$($inputFact.publicKeyToken)|$($inputFact.mvid)|$($inputFact.size)|$($inputFact.sha256)")
    }
    foreach ($surface in $contractSurfaceClosures) {
        $inputDigestLines.Add("contract-surface|$($surface.projectPath)|$($surface.assemblyInput.sha256)|$($surface.assemblyInput.mvid)")
        foreach ($reference in $surface.projectReferences) {
            $inputDigestLines.Add("contract-project-reference|$($surface.assemblyName)|$($reference.projectPath)|$($reference.ownerFamily)|$($reference.referenceOutputAssembly)")
        }
        foreach ($reference in $surface.assemblyReferences) {
            $inputDigestLines.Add("contract-pe-reference|$($surface.assemblyName)|$($reference.referencedAssembly)|$($reference.referencedVersion)|$($reference.referencedCulture)|$($reference.referencedPublicKeyToken)|$($reference.ownerFamily)")
        }
    }
    if ($packageLayerStatus -eq 'evaluated') {
        $inputDigestLines.Add("candidate-package|$(ConvertTo-RepositoryPath $resolvedPluginPackagePath)|$(Get-Sha256 $resolvedPluginPackagePath)")
    }
    foreach ($staticInput in $packageStaticInputs) {
        $inputDigestLines.Add("package-static|$($staticInput.packagePath)|$($staticInput.sourcePath)|$($staticInput.size)|$($staticInput.sha256)|$($staticInput.category)|$($staticInput.required)")
    }
    foreach ($ownedPath in $resolvedPluginOwnedAssemblyPaths) {
        $inputDigestLines.Add("plugin-owned-assembly|$(ConvertTo-RepositoryPath $ownedPath)|$(Get-Sha256 $ownedPath)")
    }
    $fixedAuthorityPaths = @($resolvedPluginProject, $manifestPath, $resolvedInputsPath, $inputsSchemaPath, $schemaPath, $roslynHelperPath,
            $validatorRoslynHelperPath, $deterministicBuildTargetsPath,
            $phaseCloseEvidenceSchemaPath, $phaseCloseEvidenceValidatorPath, $generatorPath,
            $testInventoryPath, $requiredCountsPath, $discoveredInventoryPath,
            (Join-Path $repositoryRoot 'scripts/tests/Test-EdgePluginContractLedger.ps1'),
            (Join-Path $repositoryRoot 'scripts/tests/Test-EdgePluginContractLedgerBehavior.ps1'),
            (Join-Path $repositoryRoot 'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs'),
            (Join-Path $repositoryRoot 'src/Tests/IIoT.Edge.Architecture.Tests/IIoT.Edge.Architecture.Tests.csproj'),
            (Join-Path $repositoryRoot 'IIoT.EdgeClient.slnx'))
    foreach ($path in $fixedAuthorityPaths) {
        $inputDigestLines.Add("file|$(ConvertTo-RepositoryPath $path)|$(Get-Sha256 $path)")
    }
    $inputDigestLines.Add("msbuild-authority-policy|$(($authorityProjectionPolicy | ConvertTo-Json -Depth 20 -Compress))")
    foreach ($authorityInput in $msbuildAuthorityInputs) {
        $inputDigestLines.Add("msbuild-authority|$($authorityInput.origin)|$($authorityInput.representation)|$($authorityInput.path)|$(@($authorityInput.roles) -join ',')|$($authorityInput.size)|$($authorityInput.sha256)")
    }
    if ($isCanonicalOutput) {
        $trackedAuthorityPaths = [Collections.Generic.HashSet[string]]::new($pathStringComparer)
        foreach ($path in $fixedAuthorityPaths) {
            [void]$trackedAuthorityPaths.Add([IO.Path]::GetFullPath($path))
        }
        foreach ($authorityInput in @($msbuildAuthorityInputs | Where-Object origin -eq 'tracked-repository')) {
            [void]$trackedAuthorityPaths.Add((Resolve-RepositoryPath ([string]$authorityInput.path)))
        }
        foreach ($path in @($trackedAuthorityPaths | Sort-Ordinal)) {
            Assert-TrackedAuthorityRegularBlob -Commit $currentHead -WorktreePath $path
        }
    }
    if ($batchRank -gt 0) {
        $inputDigestLines.Add("baseline-ledger|$(ConvertTo-RepositoryPath $resolvedBaselineLedgerPath)|$(Get-Sha256 $resolvedBaselineLedgerPath)")
    }
    $analyzedInputsSha256 = Get-TextSha256 ((@($inputDigestLines | Sort-Ordinal) -join "`n") + "`n")

    $dispositionSummary = @($externalSymbolUsages |
        Group-Ordinal disposition |
        ForEach-Object {
            [pscustomobject][ordered]@{ disposition = [string]$_.Name; count = $_.Count }
        } | Sort-Ordinal disposition)
    $ownerAssemblySummary = @($externalSymbolUsages |
        Group-Ordinal ownerAssembly |
        ForEach-Object {
            [pscustomobject][ordered]@{ ownerAssembly = [string]$_.Name; count = $_.Count }
        } | Sort-Ordinal ownerAssembly)
    $ownerFamilySummary = Get-CountByOwnerFamily -Items ([object[]]$externalSymbolUsages)
    $currentPluginDrift = @($repositoryStatus.dirtyPaths | Where-Object {
        $_ -ceq $pluginRelativeRoot -or $_.StartsWith("$pluginRelativeRoot/", [StringComparison]::Ordinal)
    })
    $frozenPluginDrift = @($pluginDrift -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        $_.Replace('\', '/')
    } | Sort-Ordinal -Unique)
    $unknownLayerAssemblyCount = $unknownProjectReferences.Count + $unknownPeAssemblyReferences.Count + $unknownPackageAssemblies.Count

    $ledger = [pscustomobject][ordered]@{
        schemaVersion = 2
        ruleId = 'EDGE-SPLIT-LEDGER-001'
        batchId = $CurrentBatch
        sourceState = [pscustomobject][ordered]@{
            head = $currentHead
            tree = $currentTree
            originMain = $originMain
            cleanObserved = [bool]$repositoryStatus.cleanObserved
            dirtyPaths = @($repositoryStatus.dirtyPaths)
            excludedPaths = @($repositoryStatus.excludedPaths)
            observationMethod = [string]$repositoryStatus.observationMethod
            generationProtocol = 'clean-implementation-head-plus-ledger-evidence-commit'
            pluginSourceDriftFromHead = $currentPluginDrift
        }
        frozenPhase0Source = [pscustomobject][ordered]@{
            head = $baselineHead
            tree = $baselineTree
            originMainAtFreeze = [string]$phase0Inputs.baselineGit.originMain
            pullRequest = [string]$phase0Inputs.baselineGit.pullRequest
            pluginSourceDriftFromFrozenHead = $frozenPluginDrift
        }
        decisions = $phase0Inputs.decisions
        solutionInventory = [pscustomobject][ordered]@{
            solutionPath = 'IIoT.EdgeClient.slnx'
            projectCount = $solutionProjects.Count
            projects = $solutionProjects
        }
        testInventory = [pscustomobject][ordered]@{
            inventorySchemaVersion = [int]$testInventory.schemaVersion
            requiredRunnerCount = [int]$testInventory.testProjectCount
            discoveredCaseCount = [int]$requiredCounts.caseCount
            discoveredInventoryCaseCount = [int]$discoveredInventory.caseCount
            historicalRequiredSuiteBaseline = $phase0Inputs.testEvidence.historicalBaseline
            phaseCloseEvidenceProtocol = [pscustomobject][ordered]@{
                carriedByLedger = $false
                requiredHead = 'exact-ledger-evidence-commit-head'
                acceptedSources = @('local-trx-manifest', 'required-ci')
                requiredReconciliation = 'discovered=executed=passed;failed=skipped=0'
                schemaPath = ConvertTo-RepositoryPath $phaseCloseEvidenceSchemaPath
                schemaSha256 = Get-Sha256 $phaseCloseEvidenceSchemaPath
                validatorPath = ConvertTo-RepositoryPath $phaseCloseEvidenceValidatorPath
                validatorSha256 = Get-Sha256 $phaseCloseEvidenceValidatorPath
            }
            inventorySha256 = Get-Sha256 $testInventoryPath
            requiredCountsSha256 = Get-Sha256 $requiredCountsPath
            discoveredInventorySha256 = Get-Sha256 $discoveredInventoryPath
        }
        pluginManifest = [pscustomobject][ordered]@{
            path = ConvertTo-RepositoryPath $manifestPath
            sha256 = Get-Sha256 $manifestPath
            value = $pluginManifest
        }
        publishedComposition = [pscustomobject][ordered]@{
            evidenceRole = 'immutable-historical-composition'
            byteVerification = $phase0Inputs.publishedComposition.byteVerification
            catalogApiBaseUrl = [string]$phase0Inputs.publishedComposition.catalogApiBaseUrl
            host = [pscustomobject][ordered]@{
                version = [string]$phase0Inputs.publishedComposition.host.version
                hostApiVersion = [string]$phase0Inputs.publishedComposition.host.hostApiVersion
                sourceCommit = [string]$phase0Inputs.publishedComposition.host.sourceCommit
                catalogDownloadUrl = [string]$phase0Inputs.publishedComposition.host.catalogDownloadUrl
                manifest = $hostManifestEvidence
                artifact = $hostArtifactEvidence
            }
            plugin = [pscustomobject][ordered]@{
                moduleId = [string]$phase0Inputs.publishedComposition.plugin.moduleId
                version = [string]$phase0Inputs.publishedComposition.plugin.version
                hostApiVersion = [string]$phase0Inputs.publishedComposition.plugin.hostApiVersion
                sourceCommit = [string]$phase0Inputs.publishedComposition.plugin.sourceCommit
                artifact = $pluginArtifactEvidence
                packageEntries = @($pluginPackageEntries)
            }
        }
        msbuildCompilation = [pscustomobject][ordered]@{
            projectPath = ConvertTo-RepositoryPath $resolvedPluginProject
            dotnetSdkVersion = $resolvedLedgerSdkVersion
            configuration = $Configuration
            targetFramework = [string]$evaluation.Properties.TargetFramework
            langVersion = [string]$evaluation.Properties.LangVersion
            nullable = [string]$evaluation.Properties.Nullable
            assemblyName = [string]$evaluation.Properties.AssemblyName
            msbuildCompileSourceCount = $compileSourcePaths.Count
            emittedGeneratorSourceCount = $generatedSourcePaths.Count
            compilationInputCount = $compilationInputInventory.Count
            metadataReferenceCount = $referencePaths.Count
            compilationErrorCount = $analysis.CompilationErrors.Count
            pluginSources = $pluginSourceInventory
            generatedSources = $generatedSourceInventory
            compilationInputs = $compilationInputInventory
            authorityProjectionPolicy = $authorityProjectionPolicy
            authorityInputCount = $msbuildAuthorityInputs.Count
            authorityInputs = $msbuildAuthorityInputs
            viewIdsAssemblyPath = ConvertTo-RepositoryPath $viewContractAssemblyPath
            viewIdsTypeName = $ViewIdsTypeName
        }
        viewInventory = $viewInventory
        pageInventory = @($pageInventory)
        resourceInventory = @($resourceInventory | Sort-Ordinal sourcePath, key, valueType)
        referenceAssemblies = $referenceAssemblies
        externalSymbolUsages = @($externalSymbolUsages)
        dependencyLayers = [pscustomobject][ordered]@{
            evaluatedProjectReferences = [pscustomobject][ordered]@{
                status = 'evaluated'
                inputProject = ConvertTo-RepositoryPath $resolvedPluginProject
                items = $evaluatedProjectReferences
                totalCount = $evaluatedProjectReferences.Count
                forbiddenCount = $projectForbidden.Count
                forbiddenCountByOwnerFamily = @(Get-CountByOwnerFamily -Items ([object[]]$projectForbidden))
                unknownAssemblyCount = $unknownProjectReferences.Count
            }
            roslynForbiddenSymbols = [pscustomobject][ordered]@{
                status = 'evaluated'
                inputs = @($compilationInputInventory | ForEach-Object path | Sort-Ordinal -Unique)
                totalExternalUsageCount = $externalSymbolUsages.Count
                forbiddenUsageCount = $roslynForbidden.Count
                forbiddenCountByOwnerFamily = @(Get-CountByOwnerFamily -Items ([object[]]$roslynForbidden))
                unclassifiedSymbolCount = $unclassifiedSymbols.Count
            }
            peAssemblyReferences = [pscustomobject][ordered]@{
                status = 'evaluated'
                inputs = $peInputFacts
                items = $peAssemblyReferences
                totalCount = $peAssemblyReferences.Count
                forbiddenCount = $peForbidden.Count
                forbiddenCountByOwnerFamily = @(Get-CountByOwnerFamily -Items ([object[]]$peForbidden))
                unknownAssemblyCount = $unknownPeAssemblyReferences.Count
            }
            sdkUiContractClosures = [pscustomobject][ordered]@{
                status = 'evaluated'
                surfaces = @($contractSurfaceClosures | Sort-Ordinal assemblyName)
                surfaceCount = $contractSurfaceClosures.Count
                forbiddenReferenceCount = [int]$contractSurfaceForbiddenCount
                unknownOwnerCount = 0
            }
            packagedAssemblies = [pscustomobject][ordered]@{
                status = $packageLayerStatus
                declaredPluginOwnedAssemblies = @($declaredPackageOwnedNames | Sort-Ordinal)
                limits = $packageLimits
                staticInputs = $packageStaticInputs
                packagePath = if ($packageLayerStatus -eq 'evaluated') { ConvertTo-RepositoryPath $resolvedPluginPackagePath } else { '' }
                packageSha256 = if ($packageLayerStatus -eq 'evaluated') { Get-Sha256 $resolvedPluginPackagePath } else { '' }
                entries = @($packageEntries)
                totalEntryCount = $packageEntries.Count
                assemblies = @($packageAssemblies)
                totalAssemblyCount = $packageAssemblies.Count
                forbiddenCount = $packageForbidden.Count
                forbiddenCountByOwnerFamily = @(Get-CountByOwnerFamily -Items ([object[]]$packageForbidden))
                unknownAssemblyCount = $unknownPackageAssemblies.Count
                forbiddenFileCount = $packageForbiddenFiles.Count
                unclassifiedFileCount = $packageUnclassifiedFiles.Count
            }
        }
        carrySets = [pscustomobject][ordered]@{
            'EDGE-SPLIT-020' = [pscustomobject][ordered]@{
                removalBatch = 'EDGE-SPLIT-020'
                lifecycleStatus = $carry020Status
                baselineItems = $baselineCarry020
                currentItems = $carry020
                baselineItemCount = $baselineCarry020.Count
                currentItemCount = $carry020.Count
                baselineOccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$baselineCarry020)
                currentOccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$carry020)
            }
            'EDGE-SPLIT-030' = [pscustomobject][ordered]@{
                removalBatch = 'EDGE-SPLIT-030'
                lifecycleStatus = $carry030Status
                baselineItems = $baselineCarry030
                currentItems = $carry030
                baselineItemCount = $baselineCarry030.Count
                currentItemCount = $carry030.Count
                baselineOccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$baselineCarry030)
                currentOccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$carry030)
            }
        }
        summary = [pscustomobject][ordered]@{
            externalSymbolUsageCount = $externalSymbolUsages.Count
            uniqueExternalSymbolCount = @($externalSymbolUsages | ForEach-Object { "$($_.ownerAssembly)|$($_.symbol)" } | Sort-Ordinal -Unique).Count
            dispositionCounts = $dispositionSummary
            ownerAssemblyCounts = $ownerAssemblySummary
            ownerFamilyCounts = $ownerFamilySummary
            carrySet020ItemCount = $carry020.Count
            carrySet030ItemCount = $carry030.Count
            carrySet020OccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$carry020)
            carrySet030OccurrenceCount = Get-CarryOccurrenceCount -Items ([object[]]$carry030)
            viewCount = $viewInventory.Count
            pageCount = $pageInventory.Count
            resourceKeyOccurrenceCount = $resourceInventory.Count
            evaluatedProjectReferenceForbiddenCount = $projectForbidden.Count
            roslynForbiddenSymbolCount = $roslynForbidden.Count
            peForbiddenAssemblyReferenceCount = $peForbidden.Count
            packagedForbiddenAssemblyCount = $packageForbidden.Count
            contractSurfaceForbiddenReferenceCount = [int]$contractSurfaceForbiddenCount
            packagedForbiddenFileCount = $packageForbiddenFiles.Count
            packagedUnclassifiedFileCount = $packageUnclassifiedFiles.Count
            unknownAssemblyCount = $unknownAssemblies.Count + $unknownLayerAssemblyCount
            unclassifiedSymbolCount = $unclassifiedSymbols.Count
        }
        integrity = [pscustomobject][ordered]@{
            schemaSha256 = Get-Sha256 $schemaPath
            generatorSha256 = Get-Sha256 $generatorPath
            roslynHelperSha256 = Get-Sha256 $roslynHelperPath
            validatorRoslynHelperSha256 = Get-Sha256 $validatorRoslynHelperPath
            phaseCloseEvidenceSchemaSha256 = Get-Sha256 $phaseCloseEvidenceSchemaPath
            phaseCloseEvidenceValidatorSha256 = Get-Sha256 $phaseCloseEvidenceValidatorPath
            phase0InputsSha256 = Get-Sha256 $resolvedInputsPath
            phase0InputsSchemaSha256 = Get-Sha256 $inputsSchemaPath
            baselineLedgerSha256 = $baselineLedgerSha256
            baselineLedgerBatchId = $baselineLedgerBatchId
            baselineLedgerEvidenceCommit = $baselineLedgerEvidenceCommit
            analyzedInputsSha256 = $analyzedInputsSha256
            payloadSha256 = ''
        }
    }

    $payloadJson = ($ledger | ConvertTo-Json -Depth 100) + "`n"
    $ledger.integrity.payloadSha256 = Get-TextSha256 $payloadJson
    $finalJson = ($ledger | ConvertTo-Json -Depth 100) + "`n"
    [void](New-Item -ItemType Directory -Path (Split-Path $resolvedOutputPath -Parent) -Force)
    [IO.File]::WriteAllText($resolvedOutputPath, $finalJson, [Text.UTF8Encoding]::new($false))

    Write-Host "Edge plugin contract ledger generated: $resolvedOutputPath"
    Write-Host "MSBuild/Roslyn: sources=$($pluginSourceInventory.Count), references=$($referenceAssemblies.Count), compilationErrors=0"
    Write-Host "External symbols: usages=$($externalSymbolUsages.Count), unique=$($ledger.summary.uniqueExternalSymbolCount), unknown=0, unclassified=0"
    Write-Host "Carry sets: EDGE-SPLIT-020=$($carry020.Count)/$carry020Status, EDGE-SPLIT-030=$($carry030.Count)/$carry030Status"
    Write-Host "Four layers: project=$($projectForbidden.Count), roslyn=$($roslynForbidden.Count), pe=$($peForbidden.Count), package=$($packageForbidden.Count)/$packageLayerStatus"
    Write-Host "Views/resources: views=$($viewInventory.Count), pages=$($pageInventory.Count), resourceOccurrences=$($resourceInventory.Count)"
    Write-Host "Old composition: host=$($phase0Inputs.publishedComposition.host.version), plugin=$($phase0Inputs.publishedComposition.plugin.version), immutableBytes=verified"
    Write-Host "Payload SHA256: $($ledger.integrity.payloadSha256)"
}
finally {
    Restore-ProcessEnvironmentStateSnapshot `
        -Names ([string[]]$restoreEnvironmentNames) -Snapshot $restoreEnvironmentBefore
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
