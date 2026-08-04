param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [string]$LayoutRoot = '.',

    [string]$ReferenceSourcePath = $env:IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE,

    [string]$ReferenceLayoutRoot = 'lib/app',

    [switch]$RequireReferenceComparison,

    [string]$CandidateVersion = '',

    [string]$RemovalApprovalPath = (Join-Path $PSScriptRoot 'edge-dependency-removal-approvals.json'),

    [string[]]$RequiredAssemblies = @(
        'Velopack.dll',
        'IIoT.Edge.UI.Shared.dll',
        'IIoT.Edge.Module.Contracts.dll'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoots = [System.Collections.Generic.List[string]]::new()

function Resolve-DependencySourceRoot {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$InnerRoot
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($PathValue)
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "Dependency closure source was not found: $resolvedPath"
    }

    $sourceRoot = $resolvedPath
    if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
            "iiot-edge-dependency-closure-$([System.Guid]::NewGuid().ToString('N'))")
        [System.IO.Directory]::CreateDirectory($extractRoot) | Out-Null
        $temporaryRoots.Add($extractRoot)
        [System.IO.Compression.ZipFile]::ExtractToDirectory($resolvedPath, $extractRoot)
        $sourceRoot = $extractRoot
    }

    $layoutPath = [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $InnerRoot))
    if (-not (Test-Path -LiteralPath $layoutPath -PathType Container)) {
        throw "Dependency closure layout root was not found: $layoutPath"
    }

    return $layoutPath
}

function Resolve-DefaultReferencePackage {
    if (-not [string]::IsNullOrWhiteSpace($ReferenceSourcePath)) {
        return [System.IO.Path]::GetFullPath($ReferenceSourcePath)
    }

    $searchRoot = [System.IO.DirectoryInfo]$repoRoot
    for ($level = 0; $level -lt 8 -and $null -ne $searchRoot; $level++) {
        $artifactRoot = Join-Path $searchRoot.FullName 'artifacts/deploy'
        if (Test-Path -LiteralPath $artifactRoot -PathType Container) {
            $matches = @(Get-ChildItem -LiteralPath $artifactRoot `
                -Filter 'IIoT.EdgeClient-2.0.8-stable-full.nupkg' `
                -File `
                -Recurse `
                -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTimeUtc -Descending)
            if ($matches.Count -gt 0) {
                return $matches[0].FullName
            }
        }

        $searchRoot = $searchRoot.Parent
    }

    return ''
}

function Get-SelectedTarget {
    param(
        [Parameter(Mandatory = $true)]$DependencyManifest,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $targetName = [string]$DependencyManifest.runtimeTarget.name
    $targetProperty = $DependencyManifest.targets.PSObject.Properties[$targetName]
    if ($null -eq $targetProperty) {
        throw "Dependency manifest target '$targetName' was not found: $ManifestPath"
    }

    return $targetProperty.Value
}

function Get-AssetCandidates {
    param(
        [Parameter(Mandatory = $true)][string]$AssetPath,
        $AssetMetadata
    )

    $normalized = $AssetPath.Replace('\', '/').TrimStart('/')
    $candidates = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $candidates.Add($normalized) | Out-Null
    $fileName = [System.IO.Path]::GetFileName($normalized)
    $candidates.Add($fileName) | Out-Null

    foreach ($pattern in @(
        '^lib/[^/]+/(.+)$',
        '^runtimes/[^/]+/native/(.+)$',
        '^runtimes/[^/]+/lib/[^/]+/(.+)$'
    )) {
        if ($normalized -match $pattern) {
            $candidates.Add($Matches[1]) | Out-Null
        }
    }

    if ($null -ne $AssetMetadata) {
        $localeProperty = $AssetMetadata.PSObject.Properties['locale']
        if ($null -ne $localeProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$localeProperty.Value)) {
            $candidates.Add("$([string]$localeProperty.Value)/$fileName") | Out-Null
        }
    }

    return @($candidates)
}

function Get-DependencyClosure {
    param([Parameter(Mandatory = $true)][string]$Root)

    $manifests = @(Get-ChildItem -LiteralPath $Root -Filter '*.deps.json' -File -Recurse |
        Where-Object {
            $_.Name -in @('IIoT.Edge.Launcher.deps.json', 'IIoT.Edge.Shell.deps.json')
        } |
        Sort-Object Name)
    if ($manifests.Count -ne 2) {
        throw "Expected exactly two application dependency manifests under '$Root'; found $($manifests.Count)."
    }

    $result = [ordered]@{}
    foreach ($manifestFile in $manifests) {
        $applicationRoot = $manifestFile.Directory.FullName
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestFile.FullName |
            ConvertFrom-Json
        $target = Get-SelectedTarget -DependencyManifest $manifest -ManifestPath $manifestFile.FullName
        $assetNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $libraryNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $assetsByLibrary = [ordered]@{}
        $missingAssets = [System.Collections.Generic.List[string]]::new()

        foreach ($libraryProperty in $target.PSObject.Properties) {
            $library = $libraryProperty.Value
            $hasRuntimeAsset = $false
            $libraryAssets = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($assetGroupName in @('runtime', 'native', 'resources', 'runtimeTargets')) {
                $assetGroupProperty = $library.PSObject.Properties[$assetGroupName]
                if ($null -eq $assetGroupProperty) {
                    continue
                }

                foreach ($assetProperty in $assetGroupProperty.Value.PSObject.Properties) {
                    $assetPath = [string]$assetProperty.Name
                    if ($assetPath.EndsWith('/_._', [System.StringComparison]::Ordinal) -or
                        $assetPath -eq '_._' -or
                        $assetPath.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) -or
                        [System.IO.Path]::GetFileName($assetPath) -eq 'createdump.exe') {
                        continue
                    }

                    $hasRuntimeAsset = $true
                    $assetName = [System.IO.Path]::GetFileName($assetPath)
                    $assetNames.Add($assetName) | Out-Null
                    $libraryAssets.Add($assetName) | Out-Null
                    $resolved = $false
                    foreach ($candidate in Get-AssetCandidates `
                        -AssetPath $assetPath `
                        -AssetMetadata $assetProperty.Value) {
                        if (Test-Path -LiteralPath (Join-Path $applicationRoot $candidate) -PathType Leaf) {
                            $resolved = $true
                            break
                        }
                    }

                    if (-not $resolved) {
                        $missingAssets.Add("$($libraryProperty.Name):$assetPath")
                    }
                }
            }

            if ($hasRuntimeAsset) {
                $libraryName = [string]$libraryProperty.Name
                $normalizedLibraryName = $libraryName -replace '/[^/]+$', ''
                $libraryNames.Add($normalizedLibraryName) | Out-Null
                $assetsByLibrary[$normalizedLibraryName] = @(
                    $libraryAssets | Sort-Object)
            }
        }

        if ($missingAssets.Count -gt 0) {
            $sample = @($missingAssets | Select-Object -First 10) -join ', '
            throw "Dependency manifest assets are missing beside '$($manifestFile.Name)': $sample"
        }

        foreach ($requiredAssembly in $RequiredAssemblies) {
            $requiredPath = Join-Path $applicationRoot $requiredAssembly
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                throw "Required runtime dependency was not found beside '$($manifestFile.Name)': $requiredAssembly"
            }
            if (-not $assetNames.Contains($requiredAssembly)) {
                throw "Required runtime dependency is absent from '$($manifestFile.Name)': $requiredAssembly"
            }
        }

        $result[$manifestFile.Name] = [pscustomobject]@{
            Libraries = @($libraryNames | Sort-Object)
            Assets = @($assetNames | Sort-Object)
            AssetsByLibrary = $assetsByLibrary
        }
    }

    return $result
}

function Read-DependencyRemovalApprovalContract {
    $resolvedPath = [System.IO.Path]::GetFullPath($RemovalApprovalPath)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Dependency removal approval contract was not found: $resolvedPath"
    }

    $contract = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedPath |
        ConvertFrom-Json
    if ([int]$contract.schemaVersion -ne 1 -or
        [string]$contract.referenceVersion -cne '2.0.8') {
        throw 'Dependency removal approval contract must use schemaVersion=1 and referenceVersion=2.0.8.'
    }
    if ($null -eq $contract.PSObject.Properties['approvals']) {
        throw 'Dependency removal approval contract must declare approvals.'
    }

    return $contract
}

function Get-ExactApprovedRemovedLibraries {
    param(
        [Parameter(Mandatory = $true)]$ApprovalContract,
        [Parameter(Mandatory = $true)][string]$ManifestName,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$MissingLibraries
    )

    $matching = @()
    if (-not [string]::IsNullOrWhiteSpace($CandidateVersion)) {
        $matching = @($ApprovalContract.approvals | Where-Object {
            [string]$_.candidateVersion -ceq $CandidateVersion -and
            [string]$_.manifest -ceq $ManifestName
        })
    }

    if ($MissingLibraries.Count -eq 0) {
        if ($matching.Count -gt 0) {
            throw "Dependency removal approval is stale for candidate '$CandidateVersion' in '$ManifestName': no dependency disappeared."
        }
        return @()
    }

    $missingText = $MissingLibraries -join ', '
    if ([string]::IsNullOrWhiteSpace($CandidateVersion) -or $matching.Count -ne 1) {
        throw "Dependencies disappeared relative to runnable 2.0.8 in '$ManifestName': $missingText. No exact candidate-version removal approval was found."
    }

    $approval = $matching[0]
    $rationale = [string]$approval.rationale
    if (-not $rationale.Contains(
            'EDGE-DEPLOY-SECURITY-001',
            [System.StringComparison]::Ordinal)) {
        throw "Dependency removal approval for '$ManifestName' must cite EDGE-DEPLOY-SECURITY-001."
    }

    $declared = @($approval.removedLibraries | ForEach-Object {
        ([string]$_).Trim()
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $declaredUnique = @($declared | Sort-Object -Unique)
    if ($declared.Count -ne $declaredUnique.Count) {
        throw "Dependency removal approval contains duplicate libraries for '$ManifestName'."
    }

    $missingSorted = @($MissingLibraries | Sort-Object -Unique)
    if (($declaredUnique -join "`n") -cne ($missingSorted -join "`n")) {
        throw "Dependency removal approval does not exactly match disappeared libraries in '$ManifestName'. Expected: $($missingSorted -join ', '); approved: $($declaredUnique -join ', ')."
    }

    Write-Host "Approved dependency removals applied: candidate=$CandidateVersion manifest=$ManifestName libraries=$($missingSorted -join ',')"
    return $missingSorted
}

function Compare-DependencyClosure {
    param(
        [Parameter(Mandatory = $true)]$Current,
        [Parameter(Mandatory = $true)]$Reference,
        [Parameter(Mandatory = $true)]$ApprovalContract
    )

    foreach ($manifestName in $Reference.Keys) {
        if (-not $Current.Contains($manifestName)) {
            throw "Current dependency closure is missing reference manifest: $manifestName"
        }

        $missingLibraries = @($Reference[$manifestName].Libraries |
            Where-Object { $_ -notin $Current[$manifestName].Libraries })
        $approvedRemovedLibraries = @(Get-ExactApprovedRemovedLibraries `
            -ApprovalContract $ApprovalContract `
            -ManifestName $manifestName `
            -MissingLibraries $missingLibraries)
        $approvedSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($libraryName in $approvedRemovedLibraries) {
            $approvedSet.Add($libraryName) | Out-Null
        }

        $missingAssets = @($Reference[$manifestName].Assets |
            Where-Object { $_ -notin $Current[$manifestName].Assets })
        $retainedReferenceAssets = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($libraryName in $Reference[$manifestName].AssetsByLibrary.Keys) {
            if ($approvedSet.Contains([string]$libraryName)) {
                continue
            }
            foreach ($assetName in $Reference[$manifestName].AssetsByLibrary[$libraryName]) {
                $retainedReferenceAssets.Add([string]$assetName) | Out-Null
            }
        }
        $unapprovedMissingAssets = @($missingAssets | Where-Object {
            $retainedReferenceAssets.Contains([string]$_)
        })
        if ($unapprovedMissingAssets.Count -gt 0) {
            throw "Runtime assets disappeared relative to runnable 2.0.8 in '$manifestName': $($unapprovedMissingAssets -join ', ')"
        }
    }
}

try {
    $currentRoot = Resolve-DependencySourceRoot -PathValue $SourcePath -InnerRoot $LayoutRoot
    $currentClosure = Get-DependencyClosure -Root $currentRoot
    $approvalContract = Read-DependencyRemovalApprovalContract

    $resolvedReference = Resolve-DefaultReferencePackage
    if ($RequireReferenceComparison -and [string]::IsNullOrWhiteSpace($resolvedReference)) {
        throw 'Runnable 2.0.8 reference package was not found. Set IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE to its full nupkg path.'
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedReference)) {
        $referenceRoot = Resolve-DependencySourceRoot `
            -PathValue $resolvedReference `
            -InnerRoot $ReferenceLayoutRoot
        $referenceClosure = Get-DependencyClosure -Root $referenceRoot
        Compare-DependencyClosure `
            -Current $currentClosure `
            -Reference $referenceClosure `
            -ApprovalContract $approvalContract
        Write-Host "Dependency closure matches runnable 2.0.8 reference: $resolvedReference"
    }

    Write-Host "Edge dependency closure passed: $currentRoot"
}
finally {
    foreach ($temporaryRoot in $temporaryRoots) {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
