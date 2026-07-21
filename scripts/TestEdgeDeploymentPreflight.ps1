param(
    [ValidateSet('Host', 'Plugin', 'GitHubHost')]
    [string]$Mode = 'Host',

    [ValidatePattern('^$|^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '',

    [string]$ModuleId = '',

    [string]$CloudApiBaseUrl = $env:EDGE_CLOUD_API_BASE_URL,

    [string]$CloudToken = '',

    [string]$ReleaseNotes = '',

    [string]$ReleaseNotesPath = '',

    [string]$WorkspaceRoot = '',

    [switch]$SkipCloud,

    [switch]$AllowDirty,

    [switch]$RequirePushedHead
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedWorkspaceRoot = ''
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeDeployment.Common.ps1')
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$workspaceRuleId = 'EDGE-DEPLOY-WORKSPACE-001'
$securityRuleId = 'EDGE-DEPLOY-SECURITY-001'
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

function Add-Failure {
    param([Parameter(Mandatory = $true)][string]$Message)
    $script:failures.Add($Message) | Out-Null
}

function Add-Warning {
    param([Parameter(Mandatory = $true)][string]$Message)
    $script:warnings.Add($Message) | Out-Null
}

function Resolve-PhysicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.Path]::IsPathFullyQualified($Path)) {
        throw "Physical path resolution requires a fully qualified filesystem path: $Path"
    }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) {
        throw "Physical path resolution requires a fully qualified filesystem path: $Path"
    }

    $rootItem = Get-Item -LiteralPath $pathRoot -Force -ErrorAction Stop
    $rootLinkTarget = $rootItem.ResolveLinkTarget($true)
    $currentPath = if ($null -ne $rootLinkTarget) {
        $rootLinkTarget.FullName
    }
    else {
        $rootItem.FullName
    }
    $relativePath = $fullPath.Substring($pathRoot.Length)
    $segments = $relativePath.Split(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.StringSplitOptions]::RemoveEmptyEntries)
    foreach ($segment in $segments) {
        $candidatePath = [System.IO.Path]::Combine($currentPath, $segment)
        $item = Get-Item -LiteralPath $candidatePath -Force -ErrorAction Stop
        $finalLinkTarget = $item.ResolveLinkTarget($true)
        $currentPath = if ($null -ne $finalLinkTarget) {
            $finalLinkTarget.FullName
        }
        else {
            $item.FullName
        }
    }

    return [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($currentPath))
}

function Test-PathEquals {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    $normalizedLeft = Resolve-PhysicalPath -Path $Left
    $normalizedRight = Resolve-PhysicalPath -Path $Right
    return [string]::Equals($normalizedLeft, $normalizedRight, $pathComparison)
}

function Get-UniqueSortedPaths {
    param([AllowEmptyCollection()][string[]]$Paths = @())

    $uniquePaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
    $orderedPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $Paths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and $uniquePaths.Add($path)) {
            $orderedPaths.Add($path)
        }
    }
    $orderedPaths.Sort($pathComparer)
    [string[]]$result = $orderedPaths.ToArray()
    return ,$result
}

function Get-GitAbsolutePath {
    param(
        [Parameter(Mandatory = $true)]$GitCommand,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Selector
    )

    $output = @(& $GitCommand.Source -C $RepositoryRoot rev-parse --path-format=absolute $Selector 2>$null)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$output[0])) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid git-selector=$Selector repository=$RepositoryRoot"
    }

    return [System.IO.Path]::GetFullPath(([string]$output[0]).Trim())
}

function Resolve-ValidatedWorkspaceRoot {
    param([string]$RequestedWorkspaceRoot = '')

    $gitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $gitCommand) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid git-not-found"
    }

    $repoTopLevel = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $repoRoot -Selector '--show-toplevel'
    $repoGitDirectory = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $repoRoot -Selector '--git-dir'
    $repoCommonDirectory = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $repoRoot -Selector '--git-common-dir'
    if (-not (Test-PathEquals -Left $repoTopLevel -Right $repoRoot)) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid repository-toplevel=$repoTopLevel script-repository=$repoRoot"
    }

    $hasExplicitRoot = -not [string]::IsNullOrWhiteSpace($RequestedWorkspaceRoot)
    if (-not $hasExplicitRoot -and -not (Test-PathEquals -Left $repoGitDirectory -Right $repoCommonDirectory)) {
        throw "$workspaceRuleId reason=linked-worktree-requires-explicit-root pass=-WorkspaceRoot script-repository=$repoRoot"
    }

    if ($hasExplicitRoot) {
        if (-not [System.IO.Path]::IsPathFullyQualified($RequestedWorkspaceRoot)) {
            throw "$workspaceRuleId reason=workspace-root-must-be-absolute workspace-root=$RequestedWorkspaceRoot"
        }
        if (-not (Test-Path -LiteralPath $RequestedWorkspaceRoot -PathType Container)) {
            throw "$workspaceRuleId reason=canonical-checkout-invalid workspace-root=$RequestedWorkspaceRoot"
        }
        $candidate = [System.IO.Path]::GetFullPath((Get-Item -LiteralPath $RequestedWorkspaceRoot -Force).FullName)
    }
    else {
        $candidate = [System.IO.Path]::GetFullPath((Split-Path -Parent $repoRoot))
    }

    foreach ($marker in @('docs/上传部署总览.md', 'deploy/Invoke-WorkspaceDeploy.ps1')) {
        $markerPath = Join-Path $candidate $marker
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            throw "$workspaceRuleId reason=missing-marker marker=$marker workspace-root=$candidate"
        }
    }

    $canonicalRepository = [System.IO.Path]::GetFullPath((Join-Path $candidate 'IIoT.EdgeClient'))
    if (-not (Test-Path -LiteralPath $canonicalRepository -PathType Container)) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid canonical-repository=$canonicalRepository"
    }

    $canonicalTopLevel = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $canonicalRepository -Selector '--show-toplevel'
    $canonicalGitDirectory = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $canonicalRepository -Selector '--git-dir'
    $canonicalCommonDirectory = Get-GitAbsolutePath -GitCommand $gitCommand -RepositoryRoot $canonicalRepository -Selector '--git-common-dir'
    if (-not (Test-PathEquals -Left $canonicalTopLevel -Right $canonicalRepository) -or
        -not (Test-PathEquals -Left $canonicalGitDirectory -Right $canonicalCommonDirectory)) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid canonical-repository=$canonicalRepository"
    }
    if (-not (Test-PathEquals -Left $repoCommonDirectory -Right $canonicalCommonDirectory)) {
        throw "$workspaceRuleId reason=repository-owner-mismatch repository=$repoRoot canonical-repository=$canonicalRepository"
    }
    if (-not $hasExplicitRoot -and -not (Test-PathEquals -Left $repoRoot -Right $canonicalRepository)) {
        throw "$workspaceRuleId reason=canonical-checkout-invalid canonical-default-owner=$canonicalRepository repository=$repoRoot"
    }

    return $candidate
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return Join-Path $repoRoot $RelativePath
}

function Resolve-WorkspacePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return Join-Path $resolvedWorkspaceRoot $RelativePath
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure "$Description was not found: $Path"
    }
}

function Assert-DirectoryExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        Add-Failure "$Description was not found: $Path"
    }
}

function Read-FileIfExists {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ''
    }

    return Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
}

function ConvertTo-NormalizedRepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $fullPath).Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($relativePath) -or
        [string]::Equals($relativePath, '..', $pathComparison) -or
        $relativePath.StartsWith('../', $pathComparison)) {
        throw "$securityRuleId scan path escaped repository root: $fullPath"
    }

    return $relativePath
}

function Test-IsDeploymentScanExcludedPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

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

function Get-TextFiles {
    param(
        [Parameter(Mandatory = $true)][string[]]$Roots,
        [Parameter(Mandatory = $true)][string[]]$Includes
    )

    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($rootPath in $Roots) {
        if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
            continue
        }

        foreach ($include in $Includes) {
            Get-ChildItem -LiteralPath $rootPath -Recurse -File -Include $include |
                ForEach-Object {
                    $relativePath = ConvertTo-NormalizedRepoRelativePath -Path $_.FullName
                    if (-not (Test-IsDeploymentScanExcludedPath -RelativePath $relativePath)) {
                        $files.Add($_.FullName) | Out-Null
                    }
                }
        }
    }

    [string[]]$result = Get-UniqueSortedPaths -Paths $files.ToArray()
    return ,$result
}

function Add-ForbiddenTextFailures {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Files,
        [Parameter(Mandatory = $true)][string[]]$Needles
    )

    foreach ($path in $Files) {
        $text = Read-FileIfExists $path
        if ([string]::IsNullOrEmpty($text)) {
            continue
        }

        foreach ($needle in $Needles) {
            if ($text.Contains($needle, [System.StringComparison]::OrdinalIgnoreCase)) {
                $relative = ConvertTo-NormalizedRepoRelativePath -Path $path
                Add-Failure "$securityRuleId $Description found '$needle' in $relative."
            }
        }
    }
}

function Add-ForbiddenRegexFailures {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Files,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    foreach ($path in $Files) {
        $text = Read-FileIfExists $path
        if ([string]::IsNullOrEmpty($text)) {
            continue
        }

        foreach ($pattern in $Patterns) {
            if ([regex]::IsMatch($text, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $relative = ConvertTo-NormalizedRepoRelativePath -Path $path
                Add-Failure "$securityRuleId $Description matched pattern '$pattern' in $relative."
            }
        }
    }
}

function Test-ClientSecurityRedLines {
    $shellConfigFiles = Get-ChildItem -LiteralPath (Resolve-RepoPath 'src/Edge/IIoT.Edge.Shell') -File -Filter 'appsettings*.json' |
        Select-Object -ExpandProperty FullName
    $launcherSample = Resolve-RepoPath 'src/Edge/IIoT.Edge.Launcher/launcher.accounts.sample.json'
    $accountFiles = @($shellConfigFiles)
    if (Test-Path -LiteralPath $launcherSample -PathType Leaf) {
        $accountFiles += $launcherSample
    }
    $defaultHashPattern = '"PasswordHash"\s*:\s*"[0-9A-Fa-f]{64}"'
    $defaultPasswordPattern = '"Password"\s*:\s*"' + '123' + '456' + '"'

    Add-ForbiddenRegexFailures `
        -Description 'Committed default local account credential' `
        -Files $accountFiles `
        -Patterns @($defaultHashPattern, $defaultPasswordPattern)

    $authFiles = Get-TextFiles `
        -Roots @(
            (Resolve-RepoPath 'src/Edge/IIoT.Edge.Launcher/Services'),
            (Resolve-RepoPath 'src/Infrastructure/IIoT.Edge.Infrastructure.Integration/Auth')
        ) `
        -Includes @('*.cs')
    Add-ForbiddenTextFailures `
        -Description 'Legacy SHA256 password login compatibility' `
        -Files $authFiles `
        -Needles @('Compute' + 'Sha256')

    $scriptFiles = Get-TextFiles `
        -Roots @((Resolve-RepoPath 'scripts')) `
        -Includes @('*.ps1')
    foreach ($path in $scriptFiles) {
        $relative = ConvertTo-NormalizedRepoRelativePath -Path $path
        if ([string]::Equals($relative, 'scripts/TestEdgeDeploymentPreflight.ps1', $pathComparison)) {
            continue
        }

        $text = Read-FileIfExists $path
        if ([string]::IsNullOrEmpty($text)) {
            continue
        }

        $hasPasswordContext = $text.Contains('Password', [System.StringComparison]::OrdinalIgnoreCase) -or
            $text.Contains('PasswordHash', [System.StringComparison]::OrdinalIgnoreCase)
        $hasSha256Hashing = $text.Contains('SHA256Managed', [System.StringComparison]::Ordinal) -or
            $text.Contains('.ComputeHash(', [System.StringComparison]::Ordinal)
        if ($hasPasswordContext -and $hasSha256Hashing) {
            Add-Failure "$securityRuleId SHA256 password hash generation script content found in $relative."
        }
    }

    $jwtFiles = Get-TextFiles `
        -Roots @((Resolve-RepoPath 'src/Infrastructure/IIoT.Edge.Infrastructure.Integration/Auth')) `
        -Includes @('*.cs')
    Add-ForbiddenTextFailures `
        -Description 'Unvalidated JWT claim parsing in auth path' `
        -Files $jwtFiles `
        -Needles @('Read' + 'JwtToken')

    $mesFiles = Get-TextFiles `
        -Roots @(
            (Resolve-RepoPath 'src/Application/IIoT.Edge.Application/Modules/Mes'),
            (Resolve-RepoPath 'src/Modules')
        ) `
        -Includes @('*.cs')
    Add-ForbiddenTextFailures `
        -Description 'Legacy MES fixed token or MD5 signature' `
        -Files $mesFiles `
        -Needles @(
            'hdc' + '2023',
            'Default' + 'MesSignToken',
            'MD5' + '.HashData'
        )

    $productionFiles = Get-TextFiles `
        -Roots @((Resolve-RepoPath 'src')) `
        -Includes @('*.cs')
    Add-ForbiddenTextFailures `
        -Description 'Production debug output' `
        -Files $productionFiles `
        -Needles @(
            'Debug' + '.WriteLine',
            'System.Diagnostics.Debug' + '.WriteLine'
        )

    $deploymentFiles = Get-TextFiles `
        -Roots @((Resolve-RepoPath 'scripts')) `
        -Includes @('*.ps1')
    foreach ($docPath in @(
        'docs/客户端部署.md',
        'docs/Edge安装更新验收.md',
        'docs/Edge客户端宿主插件分发契约.md',
        'docs/客户端规则.md',
        'docs/客户端架构治理清单.md'
    )) {
        $resolved = Resolve-RepoPath $docPath
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            $deploymentFiles += $resolved
        }
    }
    $privateNetworkAddressPattern = '\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b'
    Add-ForbiddenRegexFailures `
        -Description 'Hardcoded private network IP in production source or execution docs' `
        -Files (Get-UniqueSortedPaths -Paths @($productionFiles + $deploymentFiles)) `
        -Patterns @($privateNetworkAddressPattern)

    $certificateFiles = $productionFiles + $deploymentFiles
    Add-ForbiddenTextFailures `
        -Description 'Certificate validation bypass' `
        -Files $certificateFiles `
        -Needles @(
            'DangerousAcceptAnyServer' + 'CertificateValidator',
            'ServerCertificate' + 'CustomValidationCallback',
            'TrustAll' + 'Certificates',
            'Skip' + 'CertificateValidation',
            '忽略' + '证书',
            '跳过 TLS ' + '校验',
            '信任所有' + '证书'
        )
}

function Resolve-ReleaseNotes {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotes) -and -not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        Add-Failure 'Use either -ReleaseNotes or -ReleaseNotesPath, not both.'
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $path = if ([System.IO.Path]::IsPathRooted($ReleaseNotesPath)) {
            $ReleaseNotesPath
        }
        else {
            Resolve-RepoPath $ReleaseNotesPath
        }

        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Add-Failure "Release notes file was not found: $path"
            return
        }

        $text = (Get-Content -Raw -Encoding UTF8 -LiteralPath $path).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            Add-Failure "Release notes file is empty: $path"
        }
        return
    }

    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        Add-Failure 'Production Edge release notes are required. Pass -ReleaseNotes or -ReleaseNotesPath.'
    }
}

function Resolve-CloudRootUrl {
    $builder = [System.UriBuilder]::new([Uri]$CloudApiBaseUrl)
    $path = $builder.Path.TrimEnd('/')
    if ($path.EndsWith('/api/v1', [System.StringComparison]::OrdinalIgnoreCase)) {
        $builder.Path = $path.Substring(0, $path.Length - '/api/v1'.Length)
    }
    elseif ($path.EndsWith('/api', [System.StringComparison]::OrdinalIgnoreCase)) {
        $builder.Path = $path.Substring(0, $path.Length - '/api'.Length)
    }

    return $builder.Uri.AbsoluteUri.TrimEnd('/')
}

function Test-CloudAccess {
    if ($SkipCloud) {
        Add-Warning 'Cloud checks skipped by -SkipCloud. Do not deploy until Cloud API and token are verified.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl)) {
        Add-Failure 'CloudApiBaseUrl is required.'
        return
    }

    try {
        $script:CloudToken = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $CloudToken
    }
    catch {
        Add-Failure "Cloud token resolution failed: $($_.Exception.Message)"
        return
    }

    if ([string]::IsNullOrWhiteSpace($script:CloudToken)) {
        Add-Failure 'CloudToken is required. Pass -CloudToken, set $env:IIOT_CLOUD_RELEASE_TOKEN, set $env:IIOT_EDGE_RELEASE_API_KEY, or run scripts/SaveEdgeReleaseApiKey.ps1 to store the Edge Release API key in macOS Keychain.'
        return
    }

    try {
        Test-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $script:CloudToken
    }
    catch {
        Add-Failure "Cloud release API token check failed: $($_.Exception.Message)"
    }
}

function Test-GitState {
    $git = @(Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($null -eq $git) {
        Add-Failure 'git is required for deployment preflight.'
        return
    }

    try {
        $inside = (& $git.Source -C $repoRoot rev-parse --is-inside-work-tree 2>$null).Trim()
        if ($inside -ne 'true') {
            Add-Failure "EdgeClient path is not a git work tree: $repoRoot"
            return
        }

        $branch = (& $git.Source -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
        $commit = (& $git.Source -C $repoRoot rev-parse --short HEAD).Trim()
        $dirty = @(& $git.Source -C $repoRoot status --porcelain)

        Write-Host "Git: branch=$branch commit=$commit dirty=$($dirty.Count)"
        if ($dirty.Count -gt 0 -and -not $AllowDirty) {
            Add-Failure "EdgeClient work tree has uncommitted changes. Commit/stash them or rerun preflight with -AllowDirty for local dry checks."
        }
        if ($RequirePushedHead) {
            if ($dirty.Count -gt 0) {
                Add-Failure 'Formal Edge release preflight requires a clean work tree; -AllowDirty is only valid for validate/dry-run.'
            }
            else {
                try {
                    $pushedState = Assert-EdgeReleaseGitState -RepoRoot $repoRoot
                    Write-Host "Git release state: head=$($pushedState.Head) upstream=$($pushedState.Upstream)"
                }
                catch {
                    Add-Failure $_.Exception.Message
                }
            }
        }
    }
    catch {
        Add-Failure "Git state check failed: $($_.Exception.Message)"
    }
}

function Test-CommonDeploymentInputs {
    Assert-FileExists (Resolve-WorkspacePath 'docs/上传部署总览.md') 'Deployment overview'
    Assert-FileExists (Resolve-WorkspacePath 'deploy/Invoke-WorkspaceDeploy.ps1') 'Workspace deployment entrypoint'
    Assert-FileExists (Resolve-RepoPath 'docs/客户端部署.md') 'EdgeClient deployment guide'
    Assert-FileExists (Resolve-RepoPath 'docs/Edge安装更新验收.md') 'Edge installer/update acceptance guide'
    Resolve-ReleaseNotes
    Test-ClientSecurityRedLines
    Test-GitState
    Test-CloudAccess
}

function Test-HostMode {
    Assert-FileExists (Resolve-RepoPath 'scripts/EdgeDeployment.Common.ps1') 'Shared Edge deployment guard script'
    Assert-FileExists (Resolve-RepoPath 'scripts/LocalPublishAndDeploy.ps1') 'Host HTTP publish script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PublishEdgeClientInstallerArtifact.ps1') 'Installer artifact script'
    Assert-FileExists (Resolve-RepoPath 'scripts/TestEdgeClientInstallerArtifact.ps1') 'Installer artifact validation script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PackEdgeClientVelopack.ps1') 'Velopack package script'

    $scriptText = Read-FileIfExists (Resolve-RepoPath 'scripts/LocalPublishAndDeploy.ps1')
    if ($scriptText -notmatch 'Stable Edge host releases must use -Transport http') {
        Add-Failure 'LocalPublishAndDeploy.ps1 must reject non-HTTP stable host releases.'
    }
    foreach ($requiredText in @('Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost', 'Enter-EdgeDeploymentLock', 'ResumeReleaseRoot', 'UploadTimeoutSeconds')) {
        if (-not $scriptText.Contains($requiredText, [System.StringComparison]::Ordinal)) {
            Add-Failure "LocalPublishAndDeploy.ps1 is missing deployment guard '$requiredText'."
        }
    }
}

function Test-PluginMode {
    Assert-FileExists (Resolve-RepoPath 'scripts/EdgeDeployment.Common.ps1') 'Shared Edge deployment guard script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PublishEdgePluginRelease.ps1') 'Plugin release script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PackEdgePlugin.ps1') 'Plugin package script'
    Assert-FileExists (Resolve-RepoPath 'scripts/TestEdgePluginPackage.ps1') 'Plugin package validation script'

    if ([string]::IsNullOrWhiteSpace($ModuleId)) {
        Add-Failure 'ModuleId is required for -Mode Plugin.'
        return
    }

    $pluginManifest = Resolve-RepoPath "src/Modules/IIoT.Edge.Module.$ModuleId/plugin.json"
    Assert-FileExists $pluginManifest "Plugin manifest for $ModuleId"

    $scriptText = Read-FileIfExists (Resolve-RepoPath 'scripts/PublishEdgePluginRelease.ps1')
    foreach ($requiredText in @('Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin', 'Enter-EdgeDeploymentLock', 'ResumeReleaseRoot', 'UploadTimeoutSeconds')) {
        if (-not $scriptText.Contains($requiredText, [System.StringComparison]::Ordinal)) {
            Add-Failure "PublishEdgePluginRelease.ps1 is missing deployment guard '$requiredText'."
        }
    }
}

function Test-GitHubHostMode {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        Add-Failure 'Version is required for -Mode GitHubHost.'
    }

    $workflowPath = Resolve-RepoPath '.github/workflows/edge-pack-modules.yml'
    Assert-FileExists $workflowPath 'Edge host GitHub workflow'
    $workflow = Read-FileIfExists $workflowPath
    if ([string]::IsNullOrWhiteSpace($workflow)) {
        return
    }

    if ($workflow -notmatch 'EDGE_PACK_ID:\s*IIoT\.EdgeClient') {
        Add-Failure 'edge-pack-modules.yml must use EDGE_PACK_ID: IIoT.EdgeClient.'
    }

    if ($workflow -match 'IIoT\.EdgeClient\.Homogenization') {
        Add-Failure 'edge-pack-modules.yml still contains stale IIoT.EdgeClient.Homogenization package id.'
    }

    if ($workflow -notmatch 'publish-edge-updates:') {
        Add-Failure 'edge-pack-modules.yml must contain publish-edge-updates job.'
    }

    if ($workflow -notmatch 'Normalize installer artifact manifest after download') {
        Add-Failure 'edge-pack-modules.yml must normalize installer manifest after artifact download before Cloud upload.'
    }

    if ($workflow -notmatch 'release_notes:' -or $workflow -notmatch 'required:\s*true') {
        Add-Failure 'edge-pack-modules.yml must require explicit release_notes for workflow_dispatch.'
    }

    if ($workflow -notmatch 'iiot-linux-prod') {
        Add-Failure 'publish-edge-updates must run on the internal iiot-linux-prod runner.'
    }

    if ($workflow -match '\bscp\b|\brsync\b') {
        Add-Failure 'edge-pack-modules.yml must not publish stable Edge releases through scp or rsync.'
    }
}

function Write-NextCommand {
    Write-Host ''
    Write-Host 'Recommended next command:'
    switch ($Mode) {
        'Host' {
            Write-Host '  cd <workspace-root> && pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgeHost -ReleaseNotesPath ./release-notes.md'
        }
        'Plugin' {
            Write-Host "  cd <workspace-root> && pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgePlugin -ModuleId $ModuleId -ReleaseNotesPath ./release-notes.md"
        }
        'GitHubHost' {
            Write-Host "  gh workflow run edge-pack-modules.yml -f version=$Version -f release_notes='<manual release notes>'"
        }
    }

    Write-Host ''
    Write-Host 'Failure handling rule: use the workspace summary and preserved edge-deployment-attempt.json; retry with -ResumeReleaseRoot instead of rebuilding. For hash/size mismatch compare artifact layout, manifest and Cloud verification algorithm first.'
}

Write-Host "Edge deployment preflight: mode=$Mode"
try {
    $resolvedWorkspaceRoot = Resolve-ValidatedWorkspaceRoot -RequestedWorkspaceRoot $WorkspaceRoot
}
catch {
    $workspaceFailure = $_.Exception.Message
    if (-not $workspaceFailure.StartsWith("$workspaceRuleId reason=", [System.StringComparison]::Ordinal)) {
        $workspaceFailure = "$workspaceRuleId reason=canonical-checkout-invalid detail=$workspaceFailure"
    }
    Add-Failure $workspaceFailure
}

if ($failures.Count -eq 0) {
    Test-CommonDeploymentInputs

    switch ($Mode) {
        'Host' { Test-HostMode }
        'Plugin' { Test-PluginMode }
        'GitHubHost' { Test-GitHubHostMode }
    }
}

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host 'Warnings:'
    foreach ($warning in $warnings) {
        Write-Host "  - $warning"
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'Deployment preflight failed:'
    foreach ($failure in $failures) {
        Write-Host "  - $failure"
    }
    exit 1
}

Write-Host ''
Write-Host 'Deployment preflight passed.'
Write-NextCommand
