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

    [switch]$SkipCloud,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([Parameter(Mandatory = $true)][string]$Message)
    $script:failures.Add($Message) | Out-Null
}

function Add-Warning {
    param([Parameter(Mandatory = $true)][string]$Message)
    $script:warnings.Add($Message) | Out-Null
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return Join-Path $repoRoot $RelativePath
}

function Resolve-WorkspacePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return Join-Path $workspaceRoot $RelativePath
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
                Where-Object {
                    $_.FullName -notmatch '[/\\](bin|obj)[/\\]' -and
                    $_.FullName -notmatch '[/\\]src[/\\]Tests[/\\]'
                } |
                ForEach-Object { $files.Add($_.FullName) | Out-Null }
        }
    }

    return $files.ToArray() | Sort-Object -Unique
}

function Add-ForbiddenTextFailures {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string[]]$Files,
        [Parameter(Mandatory = $true)][string[]]$Needles
    )

    foreach ($path in $Files) {
        $text = Read-FileIfExists $path
        if ([string]::IsNullOrEmpty($text)) {
            continue
        }

        foreach ($needle in $Needles) {
            if ($text.Contains($needle, [System.StringComparison]::OrdinalIgnoreCase)) {
                $relative = [System.IO.Path]::GetRelativePath($repoRoot, $path)
                Add-Failure "$Description found '$needle' in $relative."
            }
        }
    }
}

function Add-ForbiddenRegexFailures {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string[]]$Files,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    foreach ($path in $Files) {
        $text = Read-FileIfExists $path
        if ([string]::IsNullOrEmpty($text)) {
            continue
        }

        foreach ($pattern in $Patterns) {
            if ([regex]::IsMatch($text, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $relative = [System.IO.Path]::GetRelativePath($repoRoot, $path)
                Add-Failure "$Description matched pattern '$pattern' in $relative."
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
    $mesDoc = Resolve-RepoPath 'docs/模切MES对接口径.md'
    if (Test-Path -LiteralPath $mesDoc -PathType Leaf) {
        $mesFiles += $mesDoc
    }
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
        'docs/客户端架构治理清单.md',
        'docs/模切MES对接口径.md'
    )) {
        $resolved = Resolve-RepoPath $docPath
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            $deploymentFiles += $resolved
        }
    }
    $privateNetworkAddressPattern = '\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b'
    Add-ForbiddenRegexFailures `
        -Description 'Hardcoded private network IP in production source or execution docs' `
        -Files (($productionFiles + $deploymentFiles) | Sort-Object -Unique) `
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
    }
    catch {
        Add-Failure "Git state check failed: $($_.Exception.Message)"
    }
}

function Test-CommonDeploymentInputs {
    Assert-FileExists (Resolve-WorkspacePath 'docs/上传部署总览.md') 'Deployment overview'
    Assert-FileExists (Resolve-RepoPath 'docs/客户端部署.md') 'EdgeClient deployment guide'
    Assert-FileExists (Resolve-RepoPath 'docs/Edge安装更新验收.md') 'Edge installer/update acceptance guide'
    Resolve-ReleaseNotes
    Test-ClientSecurityRedLines
    Test-GitState
    Test-CloudAccess
}

function Test-HostMode {
    Assert-FileExists (Resolve-RepoPath 'scripts/LocalPublishAndDeploy.ps1') 'Host HTTP publish script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PublishEdgeClientInstallerArtifact.ps1') 'Installer artifact script'
    Assert-FileExists (Resolve-RepoPath 'scripts/TestEdgeClientInstallerArtifact.ps1') 'Installer artifact validation script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PackEdgeClientVelopack.ps1') 'Velopack package script'

    $scriptText = Read-FileIfExists (Resolve-RepoPath 'scripts/LocalPublishAndDeploy.ps1')
    if ($scriptText -notmatch 'Stable Edge host releases must use -Transport http') {
        Add-Failure 'LocalPublishAndDeploy.ps1 must reject non-HTTP stable host releases.'
    }
}

function Test-PluginMode {
    Assert-FileExists (Resolve-RepoPath 'scripts/PublishEdgePluginRelease.ps1') 'Plugin release script'
    Assert-FileExists (Resolve-RepoPath 'scripts/PackEdgePlugin.ps1') 'Plugin package script'
    Assert-FileExists (Resolve-RepoPath 'scripts/TestEdgePluginPackage.ps1') 'Plugin package validation script'

    if ([string]::IsNullOrWhiteSpace($ModuleId)) {
        Add-Failure 'ModuleId is required for -Mode Plugin.'
        return
    }

    $pluginManifest = Resolve-RepoPath "src/Modules/IIoT.Edge.Module.$ModuleId/plugin.json"
    Assert-FileExists $pluginManifest "Plugin manifest for $ModuleId"
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
            Write-Host '  pwsh ./scripts/LocalPublishAndDeploy.ps1 -Channel stable -Transport http -CloudApiBaseUrl http://<cloud-gateway-host>:<port>/api/v1 -ReleaseNotesPath ./release-notes.md -UploadRateLimitMbps 100'
        }
        'Plugin' {
            Write-Host "  pwsh ./scripts/PublishEdgePluginRelease.ps1 -ModuleId $ModuleId -CloudApiBaseUrl http://<cloud-gateway-host>:<port>/api/v1 -ReleaseNotesPath ./release-notes.md -UploadRateLimitMbps 100"
        }
        'GitHubHost' {
            Write-Host "  gh workflow run edge-pack-modules.yml -f version=$Version -f release_notes='<manual release notes>'"
        }
    }

    Write-Host ''
    Write-Host 'Failure handling rule: reuse generated artifacts or rerun the failed job first; for hash/size mismatch compare downloaded artifact layout, manifest and Cloud verification algorithm before changing code.'
}

Write-Host "Edge deployment preflight: mode=$Mode"
Test-CommonDeploymentInputs

switch ($Mode) {
    'Host' { Test-HostMode }
    'Plugin' { Test-PluginMode }
    'GitHubHost' { Test-GitHubHostMode }
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
