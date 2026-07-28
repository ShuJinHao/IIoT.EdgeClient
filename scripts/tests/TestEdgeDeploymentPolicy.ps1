Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ruleId = 'EDGE-DEPLOY-SECURITY-001'
$pathComparison = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$pathComparer = if ($pathComparison -eq [System.StringComparison]::OrdinalIgnoreCase) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}
function Forbid-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -match $Pattern) { throw $Message }
}
function ConvertTo-NormalizedRelativePath([string]$Path) {
    $relativePath = [IO.Path]::GetRelativePath($root, [IO.Path]::GetFullPath($Path)).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relativePath) -or
        [string]::Equals($relativePath, '..', $pathComparison) -or
        $relativePath.StartsWith('../', $pathComparison)) {
        throw "$ruleId deployment scan path escaped the repository root: $Path"
    }
    return $relativePath
}
function Test-IsDeploymentScanExcludedPath([string]$RelativePath) {
    $normalizedPath = $RelativePath.Replace('\', '/')
    foreach ($testOwnedRoot in @('scripts/tests', 'src/Tests', 'src/Testing')) {
        if ([string]::Equals($normalizedPath, $testOwnedRoot, $pathComparison) -or
            $normalizedPath.StartsWith("$testOwnedRoot/", $pathComparison)) {
            return $true
        }
    }
    foreach ($segment in $normalizedPath.Split('/')) {
        if ([string]::Equals($segment, 'bin', $pathComparison) -or
            [string]::Equals($segment, 'obj', $pathComparison)) {
            return $true
        }
    }
    return $false
}
function Get-UniqueSortedPaths {
    param([AllowEmptyCollection()][string[]]$Paths = @())

    $uniquePaths = [Collections.Generic.HashSet[string]]::new($pathComparer)
    $orderedPaths = [Collections.Generic.List[string]]::new()
    foreach ($path in $Paths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and $uniquePaths.Add($path)) {
            $orderedPaths.Add($path)
        }
    }
    $orderedPaths.Sort($pathComparer)
    [string[]]$result = $orderedPaths.ToArray()
    return ,$result
}

function Assert-PreflightVersionContract {
    $preflightPath = Join-Path $root 'scripts/TestEdgeDeploymentPreflight.ps1'
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $preflightPath,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "Edge deployment preflight cannot be parsed: $($parseErrors.Message -join '; ')"
    }

    $versionParameter = @($ast.ParamBlock.Parameters | Where-Object {
        $_.Name.VariablePath.UserPath -ceq 'Version'
    })
    if ($versionParameter.Count -ne 1 -or
        $versionParameter[0].StaticType -ne [string]) {
        throw 'Edge deployment preflight must expose exactly one string Version parameter.'
    }

    $validatePattern = @($versionParameter[0].Attributes | Where-Object {
        $_.TypeName.FullName -ceq 'ValidatePattern'
    })
    $patternValue = if ($validatePattern.Count -eq 1 -and
        $validatePattern[0].PositionalArguments.Count -eq 1) {
        $validatePattern[0].PositionalArguments[0].SafeGetValue()
    }
    else {
        ''
    }
    if ([string]$patternValue -cne '^$|^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Edge deployment preflight Version must use the workspace SemVer contract; actual='$patternValue'."
    }
}

Assert-PreflightVersionContract

Require-Text 'scripts/LocalPublishAndDeploy.ps1' 'Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost' 'Edge host publication must require the workspace dispatch gate.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' "Formal stable Edge host releases only support Cloud Human HTTP publication" 'Edge host publication must remain a Mac-build-to-Cloud-HTTP workflow.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' "Invoke-EdgeScript 'PublishEdgeRuntime\.ps1'[\s\S]*?'-SelfContained'" 'Production Edge runtime publication must carry the Windows .NET runtime.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' "Invoke-EdgeScript 'PackEdgeClientVelopack\.ps1'[\s\S]*?'-SelfContained'" 'Production Velopack publication must carry the Windows .NET runtime.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' '\[switch\]\$PrepareOnly[\s\S]*?\[string\]\$PreparedResultPath' 'Edge Host preparation must expose an explicit prepare-only result contract.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' 'if\s*\(\$PrepareOnly\)[\s\S]*?uploaded\s*=\s*\$false[\s\S]*?return' 'Edge Host prepare-only mode must stop before production upload and record uploaded=false.'
Require-Text 'scripts/TestEdgeClientInstallerArtifact.ps1' "coreclr\.dll[\s\S]*?hostfxr\.dll[\s\S]*?hostpolicy\.dll[\s\S]*?System\.Private\.CoreLib\.dll" 'Installer validation must reject framework-dependent Launcher/Host payloads.'
Require-Text 'scripts/TestEdgeVelopackPackage.ps1' "coreclr\.dll[\s\S]*?hostfxr\.dll[\s\S]*?hostpolicy\.dll[\s\S]*?System\.Private\.CoreLib\.dll" 'Velopack validation must reject framework-dependent packages.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin' 'Edge plugin publication must remain independent from the host release.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '\[string\]\$PluginRepositoryRoot' 'Edge plugin publication must require the independent Private Plugins repository root.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'eng/PackEdgePlugin\.ps1' 'Edge plugin publication must call the Private Plugins unique pack implementation.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'function\s+Invoke-PluginPack\s*\{[\s\S]*?\[hashtable\]\$Parameters[\s\S]*?&\s+\$scriptPath\s+@Parameters' 'PowerShell plugin pack dispatch must use named hashtable splatting.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'Get-PreservedPluginMetadataPath[\s\S]*?if\s*\(\$null\s+-eq\s+\$metadataPath\)[\s\S]*?Invoke-PluginPack\s+-Parameters' 'A resumed plugin release must rebuild when the failed attempt has no complete package.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '\[switch\]\$ReconcileExistingRelease' 'From-zero deployment must explicitly opt into restored plugin release reconciliation.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '\[switch\]\$PrepareOnly[\s\S]*?\[string\]\$PreparedResultPath' 'Edge plugin preparation must expose an explicit prepare-only result contract.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'if\s*\(\$PrepareOnly\)[\s\S]*?uploaded\s*=\s*\$false[\s\S]*?return' 'Edge plugin prepare-only mode must stop before production upload and record uploaded=false.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '\$ReconcileExistingRelease[\s\S]*?existingRelease\.sha256[\s\S]*?metadata\.sha256[\s\S]*?existingRelease\.packageSize[\s\S]*?metadata\.packageSize' 'A restored plugin release must be rebuilt and compared by hash and size before it can be reused.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '\$ReconcileExistingRelease[\s\S]*?Test-EdgeCurlUrl[\s\S]*?rebuiltAndCompared\s*=\s*\$true' 'A matching restored plugin release must still pass the real download contract before reconciliation succeeds.'
Forbid-Text 'scripts/PublishEdgePluginRelease.ps1' 'isLegacyEmptyPackFailure|legacy empty pack' 'The exact-SHA resume path must not restore the retired legacy empty-pack compatibility.'
Forbid-Text 'Directory.Build.targets' 'ValidateEdgeDeploymentPolicy|scripts/tests/TestEdgeDeploymentPolicy\.ps1' 'Ordinary Edge builds must not run DeploymentContract tests through an MSBuild hook.'
Forbid-Text 'Directory.Build.targets' '<Target\s+Name="ValidateEdgeArchitectureProjectGraph"' 'Ordinary Edge builds must not hide an entire repository project-graph test behind the Shell build.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' '-Status\s+failed[\s\S]*?moduleId\s*=\s*\$ModuleId[\s\S]*?version\s*=\s*\$declaredVersion[\s\S]*?sourceCommit\s*=\s*\$gitFacts\.Head' 'Plugin failure state must preserve the immutable source identity required by resume.'
Forbid-Text 'scripts/PublishEdgePluginRelease.ps1' 'Invoke-PluginPack\s+@\(' 'PowerShell array splatting is positional and must not be used for named plugin pack parameters.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'RequirePushedHead' 'Edge production publication must require a pushed Git HEAD.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'mode=\$Mode version=\$versionDisplay' 'Edge deployment preflight must report the validated explicit or automatic version input.'
Require-Text 'scripts/InitializeEdgeReleaseApiKey.ps1' '\[string\]\$RevokeNamePrefix[\s\S]*?-Method\s+Get[\s\S]*?-Method\s+Delete[\s\S]*?-Method\s+Post' 'From-zero Edge Release API key retries must revoke earlier active derived keys before issuing the replacement.'
Require-Text 'scripts/InitializeEdgeReleaseApiKey.ps1' '\^iiot-edgehost-\[0-9a-f\]\{32\}\$' 'Derived-key revocation must be bounded to the deterministic from-zero invocation prefix.'
Forbid-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'GitHubHost' 'Edge deployment preflight must not retain a second GitHub-hosted production mode.'
Require-Text '.github/workflows/edge-pack-modules.yml' 'offline' 'The explicit Edge artifact workflow must identify itself as offline-only.'
Forbid-Text '.github/workflows/edge-pack-modules.yml' 'self-hosted|publish-edge-updates|CLOUD_ADMIN_(?:USERNAME|PASSWORD)|EDGE_RELEASE_API_KEY' 'The Edge artifact workflow must stop at hosted offline artifacts and must not contain production publication credentials or jobs.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\[string\]\$WorkspaceRoot' 'Edge deployment preflight must expose an explicit workspace root.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'EDGE-DEPLOY-WORKSPACE-001' 'Edge deployment preflight must retain its stable workspace ownership failure code.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$pathComparison\s*=\s*if\s*\(\$IsWindows\)[\s\S]*?OrdinalIgnoreCase[\s\S]*?Ordinal' 'Edge deployment path identity must be case-insensitive only on Windows and ordinal on other operating systems.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'function\s+Resolve-PhysicalPath' 'Edge deployment path identity must normalize physical filesystem paths.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'IsPathFullyQualified\(\$Path\)' 'Edge deployment physical path identity must reject context-dependent relative paths.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$rootItem\s*=\s*Get-Item\s+-LiteralPath\s+\$pathRoot\s+-Force\s+-ErrorAction\s+Stop[\s\S]*?\$rootLinkTarget\s*=\s*\$rootItem\.ResolveLinkTarget\(\$true\)[\s\S]*?\$currentPath\s*=\s*if\s*\(\$null\s+-ne\s+\$rootLinkTarget\)' 'Edge deployment physical path identity must resolve and fail closed on the filesystem root even when no relative segments exist.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$rootResolutionException\s*=\s*\$_\.Exception[\s\S]*?\$null\s+-ne\s+\$rootItem\.LinkType[\s\S]*?\$rootResolutionException\s+-isnot\s+\[System\.IO\.DirectoryNotFoundException\][\s\S]*?throw' 'The Windows drive-root workaround must remain limited to a confirmed non-link DirectoryNotFoundException.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\.ResolveLinkTarget\(\$true\)' 'Edge deployment physical path identity must resolve filesystem links instead of special-casing path text.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'Get-Item\s+-LiteralPath\s+\$candidatePath\s+-Force\s+-ErrorAction\s+Stop' 'Edge deployment physical path identity must fail closed when a filesystem segment cannot be resolved.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$normalizedLeft\s*=\s*Resolve-PhysicalPath\s+-Path\s+\$Left[\s\S]*?\$normalizedRight\s*=\s*Resolve-PhysicalPath\s+-Path\s+\$Right[\s\S]*?\[string\]::Equals\(\$normalizedLeft,\s*\$normalizedRight,\s*\$pathComparison\)' 'Every deployment path equality check must compare physical paths with the operating-system path comparer.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$pathComparer\s*=\s*if\s*\(\$pathComparison\s+-eq\s+\[System\.StringComparison\]::OrdinalIgnoreCase\)[\s\S]*?\[System\.StringComparer\]::OrdinalIgnoreCase[\s\S]*?\[System\.StringComparer\]::Ordinal' 'Edge deployment path uniqueness and ordering must derive from the path identity comparison.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' '\$orderedPaths\.Sort\(\$pathComparer\)' 'Edge deployment path lists must use deterministic ordinal comparer sorting.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'function\s+Get-UniqueSortedPaths\s*\{[\s\S]*?\[string\[\]\]\$result\s*=\s*\$orderedPaths\.ToArray\(\)[\s\S]*?return\s+,\s*\$result' 'Edge deployment unique path lists must preserve a non-null string array for zero, one, or many files.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'function\s+Get-TextFiles\s*\{[\s\S]*?\[string\[\]\]\$result\s*=\s*Get-UniqueSortedPaths\s+-Paths\s+\$files\.ToArray\(\)[\s\S]*?return\s+,\s*\$result' 'Edge deployment text-file discovery must preserve a non-null string array for zero, one, or many files.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'function\s+Add-ForbiddenTextFailures\s*\{[\s\S]*?\[Parameter\(Mandatory\s*=\s*\$true\)\]\s*\[AllowEmptyCollection\(\)\]\s*\[string\[\]\]\$Files' 'Edge deployment text scanning must accept a real empty file collection without accepting null.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'function\s+Add-ForbiddenRegexFailures\s*\{[\s\S]*?\[Parameter\(Mandatory\s*=\s*\$true\)\]\s*\[AllowEmptyCollection\(\)\]\s*\[string\[\]\]\$Files' 'Edge deployment regex scanning must accept a real empty file collection without accepting null.'
Require-Text 'scripts/tests/TestEdgeDeploymentPolicy.ps1' 'function\s+Get-UniqueSortedPaths\s*\{[\s\S]*?\[string\[\]\]\$result\s*=\s*\$orderedPaths\.ToArray\(\)[\s\S]*?return\s+,\s*\$result' 'Edge deployment policy file discovery must preserve the same zero, one, or many string-array contract.'
$defaultUniquePathSortPattern = 'Sort-' + 'Object\s+(?:FullName\s+)?-Unique'
Forbid-Text 'scripts/TestEdgeDeploymentPreflight.ps1' $defaultUniquePathSortPattern 'Edge deployment preflight must not use culture-sensitive default unique path sorting.'
Forbid-Text 'scripts/tests/TestEdgeDeploymentPolicy.ps1' $defaultUniquePathSortPattern 'Edge deployment policy must not use culture-sensitive default unique path sorting.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'docs/上传部署总览\.md' 'Edge deployment preflight must validate the workspace deployment overview marker.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'deploy/Invoke-WorkspaceDeploy\.ps1' 'Edge deployment preflight must validate the workspace deployment entrypoint marker.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'scripts/tests' 'Edge deployment preflight must classify script tests as test-owned.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'src/Tests' 'Edge deployment preflight must classify test projects as test-owned.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'src/Testing' 'Edge deployment preflight must classify testing helpers as test-owned.'
Require-Text 'scripts/tests/TestEdgeDeploymentBehavior.ps1' '\$canonicalFixtureAuthFiles\.Count\s+-eq\s+0' 'Edge deployment canonical behavior fixture must exercise the real zero-auth-file scanner contract.'
Require-Text 'scripts/tests/TestEdgeDeploymentBehavior.ps1' 'canonical checkout default workspace root' 'Edge deployment behavior must retain the same-physical-directory canonical checkout result.'
Require-Text 'scripts/tests/TestEdgeDeploymentBehavior.ps1' 'workspace root with markers but wrong repository owner' 'Edge deployment behavior must retain the different-physical-owner rejection result.'
Require-Text 'scripts/GetEdgePublishedState.ps1' 'hostSourceCommit' 'Edge incremental planning must inspect the published Windows-download baseline.'
Require-Text 'scripts/EdgeDeployment.Common.ps1' '\[switch\]\$AllowDetachedExactSha' 'Detached exact-SHA release execution must require explicit immutable snapshot authorization.'
Require-Text 'scripts/EdgeDeployment.Common.ps1' 'main@\{upstream\}' 'Detached exact-SHA release execution must still verify the pushed main upstream.'
Require-Text 'scripts/EdgeDeployment.Common.ps1' '\[switch\]\$AllowPreparedHandoff' 'Prepare-to-Deploy attempt handoff must require explicit prepared snapshot authorization.'
Require-Text 'scripts/EdgeDeployment.Common.ps1' "\[string\]::Equals\(\[string\]\`$State\.stage,\s*'prepared'[\s\S]*?\[string\]::Equals\(\[string\]\`$State\.status,\s*'succeeded'[\s\S]*?uploadedProperty\.Value\s+-is\s+\[bool\]" 'Prepare-to-Deploy attempt handoff must accept only successful, never-uploaded prepared state.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' 'AllowDetachedExactSha:\$PreparedSourceSnapshot' 'Host upload must pass immutable snapshot authority explicitly.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'AllowDetachedExactSha:\$PreparedSourceSnapshot' 'Plugin upload must pass immutable snapshot authority explicitly.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' 'AllowPreparedHandoff:\$PreparedSourceSnapshot' 'Host upload must authorize prepare-to-deploy handoff only for a prepared source snapshot.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'AllowPreparedHandoff:\$PreparedSourceSnapshot' 'Plugin upload must authorize prepare-to-deploy handoff only for a prepared source snapshot.'
Require-Text 'docs/客户端规则.md' 'Windows.*下载|下载.*Windows' 'Edge deployment definition must say that the server exposes artifacts for Windows download.'
Forbid-Text 'scripts/LocalPublishAndDeploy.ps1' "'scp'|'rsync'" 'The formal Edge host implementation must not restore scp/rsync transport.'
Forbid-Text 'scripts/EdgeRuntime.Common.ps1' 'src[\\/]Modules|Publish-EdgeModulesToPluginsRoot|Build-EdgeModuleProjects' 'Host runtime publication must not scan, build, or copy business plugin source.'
Forbid-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'src[\\/]Modules' 'Host deployment preflight must not inspect the retired monorepo plugin source root.'
Forbid-Text 'scripts/PublishEdgeClientInstallerArtifact.ps1' 'Read-ArtifactPluginManifest' 'Host installer publication must not rebuild a business plugin manifest from Host-owned files.'
foreach ($retiredHostPluginEntry in @(
    'scripts/PublishEdgeModules.ps1',
    'scripts/TestEdgePluginPackage.ps1'
)) {
    if (Test-Path -LiteralPath (Join-Path $root $retiredHostPluginEntry)) {
        throw "Host must not retain retired business plugin pack entry: $retiredHostPluginEntry"
    }
}

$runtimePublishManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'scripts/edge-runtime.publish.json') | ConvertFrom-Json
foreach ($profile in @($runtimePublishManifest.profiles)) {
    if (@($profile.moduleIds).Count -ne 0) {
        throw "Host runtime publish profile must not own business plugin modules: $($profile.profileId)"
    }
}

$privateAddressPattern = '\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b'
$certificateBypassPattern = 'DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback|TrustAllCertificates|SkipCertificateValidation'
$deploymentCandidates = @(
    Get-ChildItem (Join-Path $root 'scripts') -Recurse -File -Filter '*.ps1'
    Get-ChildItem (Join-Path $root 'src') -Recurse -File -Filter '*.cs'
) | Where-Object {
    $relativePath = ConvertTo-NormalizedRelativePath $_.FullName
    -not (Test-IsDeploymentScanExcludedPath $relativePath)
} | ForEach-Object { $_.FullName }
$deploymentFiles = Get-UniqueSortedPaths -Paths @($deploymentCandidates)
$findings = [Collections.Generic.List[string]]::new()
foreach ($filePath in $deploymentFiles) {
    $text = [IO.File]::ReadAllText($filePath)
    $relativePath = ConvertTo-NormalizedRelativePath $filePath
    if ($text -match $privateAddressPattern) {
        $findings.Add("$relativePath hard-codes a private production address.")
    }
    if ($text -match $certificateBypassPattern) {
        $findings.Add("$relativePath contains a certificate-validation bypass.")
    }
}
if ($findings.Count -gt 0) {
    throw "$ruleId deployment security policy failed:`n - $($findings -join "`n - ")"
}

Write-Host "$ruleId Edge deployment policy architecture test passed."
