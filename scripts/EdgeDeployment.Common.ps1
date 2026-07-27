$ErrorActionPreference = 'Stop'

function Assert-EdgeWorkspaceDispatch {
    param([Parameter(Mandatory = $true)][ValidateSet('EdgeHost', 'EdgePlugin')][string]$ExpectedTarget)

    if ($env:IIOT_EDGE_WORKSPACE_DISPATCH -ne '1') {
        throw 'Direct Edge release script execution is blocked. Use deploy/Invoke-WorkspaceDeploy.ps1 from the workspace root.'
    }

    if (-not [string]::Equals($env:IIOT_EDGE_WORKSPACE_TARGET, $ExpectedTarget, [System.StringComparison]::Ordinal)) {
        throw "Workspace dispatch target mismatch. Expected '$ExpectedTarget'."
    }

    $invocationId = [string]$env:IIOT_EDGE_WORKSPACE_INVOCATION_ID
    if ($invocationId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$') {
        throw 'Workspace dispatch invocation id is missing or invalid.'
    }

    return $invocationId
}

function Invoke-EdgeGitCapture {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = @(& git -C $RepoRoot @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = [string]($output -join [Environment]::NewLine)
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode. $text"
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Text = $text.Trim()
    }
}

function Assert-EdgeReleaseGitState {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [ValidatePattern('^$|^[0-9A-Fa-f]{40}$')]
        [string]$ExpectedSha = '',
        [switch]$AllowDetachedExactSha
    )

    $status = Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('status', '--porcelain=v1')
    if (-not [string]::IsNullOrWhiteSpace($status.Text)) {
        throw 'Formal Edge release requires a clean work tree. Commit the intended release or use validate/dry-run in the workspace entrypoint.'
    }

    $head = (Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('rev-parse', 'HEAD')).Text
    $branch = (Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')).Text
    $isDetached = [string]::Equals($branch, 'HEAD', [System.StringComparison]::Ordinal)
    $isProductionNowSnapshot = $false
    if ($AllowDetachedExactSha -and
        $isDetached -and
        $env:IIOT_DEPLOY_CONTROL_SNAPSHOT -eq '1' -and
        -not [string]::IsNullOrWhiteSpace($env:IIOT_DEPLOY_WORKSPACE_ROOT)) {
        $workspaceRoot = [IO.Path]::GetFullPath($env:IIOT_DEPLOY_WORKSPACE_ROOT)
        $snapshotRoot = [IO.Path]::GetFullPath(
            (Join-Path $workspaceRoot 'artifacts/deploy/production-now'))
        $repositoryRoot = [IO.Path]::GetFullPath($RepoRoot)
        $relativeRepository = [IO.Path]::GetRelativePath(
            $snapshotRoot,
            $repositoryRoot).Replace('\', '/')
        $segments = @($relativeRepository.Split(
            '/',
            [StringSplitOptions]::RemoveEmptyEntries))
        $isProductionNowSnapshot = (
            $segments.Count -eq 4 -and
            $segments[0] -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$' -and
            $segments[1] -ceq 'source' -and
            $segments[2] -ceq 'edge-workspace' -and
            $segments[3] -cin @(
                'IIoT.EdgeClient',
                'IIoT.Edge.Plugins.Private'
            ))
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        if (-not [string]::Equals($head, $ExpectedSha, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Formal Edge release candidate changed after the deployment plan was frozen: expected='$ExpectedSha' actual='$head'."
        }
        if (-not [string]::Equals($branch, 'main', [System.StringComparison]::Ordinal) -and
            -not ($AllowDetachedExactSha -and $isDetached)) {
            throw "Formal Edge release candidate must remain on main: expectedSha='$ExpectedSha' branch='$branch'."
        }
        if ($isDetached -and -not $isProductionNowSnapshot) {
            $mainHead = (Invoke-EdgeGitCapture `
                -RepoRoot $RepoRoot `
                -Arguments @('rev-parse', '--verify', 'main^{commit}')).Text
            if (-not [string]::Equals(
                    $mainHead,
                    $ExpectedSha,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Formal Edge immutable source snapshot does not match local main: expected='$ExpectedSha' actual='$mainHead'."
            }
        }
    }

    if ($isProductionNowSnapshot) {
        return [PSCustomObject]@{
            Head = $head
            Branch = $branch
            Upstream = 'production-now-snapshot'
            UpstreamHead = $head
        }
    }

    $upstreamSelector = if ($isDetached) { 'main@{upstream}' } else { '@{upstream}' }
    $upstreamResult = Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @(
        'rev-parse', '--abbrev-ref', '--symbolic-full-name', $upstreamSelector
    ) -AllowFailure
    if ($upstreamResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($upstreamResult.Text)) {
        throw "Formal Edge release requires HEAD '$head' to have a configured pushed upstream."
    }

    $upstream = $upstreamResult.Text
    $separator = $upstream.IndexOf('/')
    if ($separator -le 0) {
        throw "Could not resolve remote name from upstream '$upstream'."
    }

    $remoteName = $upstream.Substring(0, $separator)
    Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('fetch', '--quiet', $remoteName) | Out-Null
    $containsResult = Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('merge-base', '--is-ancestor', $head, $upstream) -AllowFailure
    if ($containsResult.ExitCode -ne 0) {
        throw "Formal Edge release requires HEAD '$head' to be pushed to '$upstream'."
    }
    $upstreamHead = (Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('rev-parse', "$upstream^{commit}")).Text
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha) -and
        -not [string]::Equals($upstreamHead, $ExpectedSha, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Formal Edge release upstream changed after the deployment plan was frozen: expected='$ExpectedSha' actual='$upstreamHead' upstream='$upstream'."
    }

    return [PSCustomObject]@{
        Head = $head
        Branch = $branch
        Upstream = $upstream
        UpstreamHead = $upstreamHead
    }
}

function Assert-EdgeResumeAttemptIdentity {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)]
        [ValidateSet('EdgeHost', 'EdgePlugin')]
        [string]$ExpectedTarget,
        [Parameter(Mandatory = $true)][string]$ExpectedInvocationId,
        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9A-Fa-f]{40}$')]
        [string]$ExpectedSha,
        [switch]$AllowPreparedHandoff
    )

    if ([int]$State.schemaVersion -ne 1 -or
        -not [string]::Equals([string]$State.target, $ExpectedTarget, [System.StringComparison]::Ordinal)) {
        throw "Resume attempt does not match the current Edge target: expected='$ExpectedTarget'."
    }
    $invocationMatches = [string]::Equals(
            [string]$State.invocationId,
            $ExpectedInvocationId,
            [System.StringComparison]::Ordinal)
    if (-not $invocationMatches) {
        $uploadedProperty = if (
            $null -ne $State.facts -and
            $null -ne $State.facts.PSObject.Properties['uploaded']) {
            $State.facts.PSObject.Properties['uploaded']
        }
        else {
            $null
        }
        $isPreparedHandoff = $AllowPreparedHandoff -and
            [string]::Equals([string]$State.stage, 'prepared', [System.StringComparison]::Ordinal) -and
            [string]::Equals([string]$State.status, 'succeeded', [System.StringComparison]::Ordinal) -and
            $null -ne $uploadedProperty -and
            $uploadedProperty.Value -is [bool] -and
            -not [bool]$uploadedProperty.Value
        if ($isPreparedHandoff) {
            $invocationMatches = $true
        }
    }
    if (-not $invocationMatches) {
        throw "Resume attempt invocation does not match the current release invocation: expected='$ExpectedInvocationId' actual='$($State.invocationId)'."
    }

    $savedSourceCommit = if (
        $null -ne $State.facts -and
        $null -ne $State.facts.PSObject.Properties['sourceCommit']) {
        [string]$State.facts.sourceCommit
    }
    else {
        ''
    }
    if (-not [string]::Equals(
            $savedSourceCommit,
            $ExpectedSha,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resume attempt sourceCommit does not match the frozen candidate: expected='$ExpectedSha' actual='$savedSourceCommit'."
    }
}

function Get-EdgeDeploymentLockPath {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $gitCommonDirectory = (Invoke-EdgeGitCapture -RepoRoot $RepoRoot -Arguments @('rev-parse', '--path-format=absolute', '--git-common-dir')).Text
    return Join-Path (Join-Path $gitCommonDirectory 'iiot-edge-deploy') 'release.lock'
}

function Test-EdgeDeploymentLockOwnerAlive {
    param([Parameter(Mandatory = $true)]$Metadata)

    $ownerPid = 0
    if (-not [int]::TryParse([string]$Metadata.pid, [ref]$ownerPid) -or $ownerPid -le 0) {
        return $false
    }

    $process = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $false
    }

    if ($Metadata.PSObject.Properties['processStartUtc'] -and -not [string]::IsNullOrWhiteSpace([string]$Metadata.processStartUtc)) {
        $expectedStart = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$Metadata.processStartUtc, [ref]$expectedStart)) {
            return $false
        }

        try {
            $actualStart = [DateTimeOffset]$process.StartTime.ToUniversalTime()
            if ([math]::Abs(($actualStart - $expectedStart.ToUniversalTime()).TotalSeconds) -gt 2) {
                return $false
            }
        }
        catch {
            return $false
        }
    }

    return $true
}

function Enter-EdgeDeploymentLock {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$InvocationId,
        [Parameter(Mandatory = $true)][ValidateSet('EdgeHost', 'EdgePlugin')][string]$Target
    )

    $lockPath = Get-EdgeDeploymentLockPath -RepoRoot $RepoRoot
    $lockParent = Split-Path -Parent $lockPath
    New-Item -ItemType Directory -Force -Path $lockParent | Out-Null

    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        try {
            New-Item -ItemType Directory -Path $lockPath -ErrorAction Stop | Out-Null
        }
        catch {
            $metadataPath = Join-Path $lockPath 'owner.json'
            $metadata = $null
            if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
                try {
                    $metadata = Get-Content -Raw -Encoding UTF8 -LiteralPath $metadataPath | ConvertFrom-Json
                }
                catch {
                    $metadata = $null
                }
            }

            if ($null -ne $metadata -and (Test-EdgeDeploymentLockOwnerAlive -Metadata $metadata)) {
                throw "Another Edge release is active: target=$($metadata.target) pid=$($metadata.pid) invocationId=$($metadata.invocationId) startedAtUtc=$($metadata.startedAtUtc)."
            }

            if ($null -eq $metadata) {
                $age = [DateTimeOffset]::UtcNow - [DateTimeOffset](Get-Item -LiteralPath $lockPath).LastWriteTimeUtc
                if ($age.TotalSeconds -lt 120) {
                    throw "Another Edge release lock is being initialized: $lockPath"
                }
            }

            Remove-Item -LiteralPath $lockPath -Recurse -Force
            continue
        }

        $process = Get-Process -Id $PID
        $metadata = [ordered]@{
            schemaVersion = 1
            invocationId = $InvocationId
            target = $Target
            pid = $PID
            processStartUtc = ([DateTimeOffset]$process.StartTime.ToUniversalTime()).ToString('O')
            startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
        }
        $metadataPath = Join-Path $lockPath 'owner.json'
        $metadata | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath $metadataPath
        return [PSCustomObject]@{
            Path = $lockPath
            InvocationId = $InvocationId
            Target = $Target
        }
    }

    throw "Could not acquire Edge release lock: $lockPath"
}

function Exit-EdgeDeploymentLock {
    param($Lock)

    if ($null -eq $Lock -or -not (Test-Path -LiteralPath $Lock.Path -PathType Container)) {
        return
    }

    $metadataPath = Join-Path $Lock.Path 'owner.json'
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        return
    }

    try {
        $metadata = Get-Content -Raw -Encoding UTF8 -LiteralPath $metadataPath | ConvertFrom-Json
        if ([string]::Equals([string]$metadata.invocationId, [string]$Lock.InvocationId, [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $Lock.Path -Recurse -Force
        }
    }
    catch {
        Write-Warning "Could not release Edge deployment lock '$($Lock.Path)'. $($_.Exception.Message)"
    }
}

function Get-EdgeHttpFailureText {
    param([string]$Path, [int]$MaximumLength = 4096)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return '<empty>'
    }

    $rawText = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    $text = if ($null -eq $rawText) { '' } else { ([string]$rawText).Trim() }
    if ([string]::IsNullOrWhiteSpace($text)) {
        return '<empty>'
    }

    $text = [regex]::Replace($text, '(?i)(authorization\s*:\s*bearer\s+)[^\s,\"]+', '$1<redacted>')
    $text = [regex]::Replace($text, '(?i)(\"?(?:accessToken|refreshToken|apiKey)\"?\s*[:=]\s*\"?)[^\"\s,}]+', '$1<redacted>')
    $text = [regex]::Replace($text, '[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}', '<redacted-jwt>')

    if ($text.Length -gt $MaximumLength) {
        return $text.Substring(0, $MaximumLength) + '...<truncated>'
    }

    return $text
}

function Invoke-EdgeCurlRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('GET', 'POST', 'HEAD')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Token = '',
        [string]$UploadFile = '',
        [Parameter(Mandatory = $true)][string]$ResponsePath,
        [int64]$RateLimitBytesPerSecond = 0,
        [ValidateRange(1, 300)][int]$ConnectTimeoutSeconds = 10,
        [ValidateRange(1, 86400)][int]$RequestTimeoutSeconds = 1800,
        [ValidateRange(1, 86400)][int]$LowSpeedTimeSeconds = 60,
        [ValidateRange(1, 104857600)][int]$LowSpeedLimitBytesPerSecond = 1024
    )

    # Windows runners can expose both system32\curl.exe and Git\mingw64\bin\curl.exe.
    # Select one command object explicitly; invoking an array joins both Source paths.
    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $curl) {
        throw 'curl is required for Edge HTTP release operations.'
    }

    $responseDirectory = Split-Path -Parent $ResponsePath
    if (-not [string]::IsNullOrWhiteSpace($responseDirectory)) {
        New-Item -ItemType Directory -Force -Path $responseDirectory | Out-Null
    }
    Remove-Item -LiteralPath $ResponsePath -Force -ErrorAction SilentlyContinue
    $stderrPath = "$ResponsePath.stderr"
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue

    $arguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in @(
        '--fail-with-body',
        '--silent',
        '--show-error',
        '--connect-timeout', [string]$ConnectTimeoutSeconds,
        '--max-time', [string]$RequestTimeoutSeconds,
        '--speed-time', [string]$LowSpeedTimeSeconds,
        '--speed-limit', [string]$LowSpeedLimitBytesPerSecond,
        '--retry', '0',
        '--stderr', $stderrPath,
        '--output', $ResponsePath,
        '--write-out', '%{http_code}'
    )) {
        $arguments.Add($argument) | Out-Null
    }

    if ($Method -eq 'HEAD') {
        $arguments.Add('--head') | Out-Null
    }
    else {
        $arguments.Add('--request') | Out-Null
        $arguments.Add($Method) | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $arguments.Add('--header') | Out-Null
        $arguments.Add("Authorization: Bearer $Token") | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($UploadFile)) {
        if (-not (Test-Path -LiteralPath $UploadFile -PathType Leaf)) {
            throw "Edge upload file was not found: $UploadFile"
        }
        $arguments.Add('--header') | Out-Null
        $arguments.Add('Content-Type: application/zip') | Out-Null
        if ($RateLimitBytesPerSecond -gt 0) {
            $arguments.Add('--limit-rate') | Out-Null
            $arguments.Add([string]$RateLimitBytesPerSecond) | Out-Null
        }
        $arguments.Add('--data-binary') | Out-Null
        $arguments.Add("@$UploadFile") | Out-Null
    }

    $arguments.Add($Uri) | Out-Null
    $statusOutput = @(& $curl.Source @($arguments.ToArray()))
    $exitCode = $LASTEXITCODE
    $httpStatus = ([string]($statusOutput -join '')).Trim()
    $stderrText = Get-EdgeHttpFailureText -Path $stderrPath

    if ($exitCode -ne 0) {
        $bodyText = Get-EdgeHttpFailureText -Path $ResponsePath
        throw "Edge HTTP request failed: method=$Method uri=$Uri curlExit=$exitCode httpStatus=$httpStatus stderr=$stderrText body=$bodyText"
    }

    if ($httpStatus -notmatch '^2\d\d$') {
        $bodyText = Get-EdgeHttpFailureText -Path $ResponsePath
        throw "Edge HTTP request returned an unexpected status: method=$Method uri=$Uri httpStatus=$httpStatus body=$bodyText"
    }

    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    return [PSCustomObject]@{
        StatusCode = [int]$httpStatus
        ResponsePath = $ResponsePath
    }
}

function Invoke-EdgeCurlJsonGet {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Token = '',
        [ValidateRange(1, 300)][int]$ConnectTimeoutSeconds = 10,
        [ValidateRange(1, 3600)][int]$RequestTimeoutSeconds = 60,
        [ValidateRange(1, 3600)][int]$LowSpeedTimeSeconds = 30,
        [ValidateRange(1, 104857600)][int]$LowSpeedLimitBytesPerSecond = 128
    )

    $responsePath = Join-Path ([System.IO.Path]::GetTempPath()) ("edge-http-{0}.json" -f ([Guid]::NewGuid().ToString('N')))
    try {
        Invoke-EdgeCurlRequest -Method GET -Uri $Uri -Token $Token -ResponsePath $responsePath `
            -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds $RequestTimeoutSeconds `
            -LowSpeedTimeSeconds $LowSpeedTimeSeconds -LowSpeedLimitBytesPerSecond $LowSpeedLimitBytesPerSecond | Out-Null
        return Get-Content -Raw -Encoding UTF8 -LiteralPath $responsePath | ConvertFrom-Json
    }
    finally {
        Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$responsePath.stderr" -Force -ErrorAction SilentlyContinue
    }
}

function Test-EdgeCurlUrl {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [ValidateRange(1, 300)][int]$ConnectTimeoutSeconds = 10,
        [ValidateRange(1, 3600)][int]$RequestTimeoutSeconds = 60
    )

    $responsePath = Join-Path ([System.IO.Path]::GetTempPath()) ("edge-head-{0}.txt" -f ([Guid]::NewGuid().ToString('N')))
    try {
        Invoke-EdgeCurlRequest -Method HEAD -Uri $Uri -ResponsePath $responsePath `
            -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds $RequestTimeoutSeconds `
            -LowSpeedTimeSeconds $RequestTimeoutSeconds -LowSpeedLimitBytesPerSecond 1 | Out-Null
    }
    finally {
        Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$responsePath.stderr" -Force -ErrorAction SilentlyContinue
    }
}

function Write-EdgeDeploymentAttemptState {
    param(
        [Parameter(Mandatory = $true)][string]$ReleaseRoot,
        [Parameter(Mandatory = $true)][ValidateSet('EdgeHost', 'EdgePlugin')][string]$Target,
        [Parameter(Mandatory = $true)][string]$InvocationId,
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][ValidateSet('running', 'succeeded', 'failed', 'cancelled')][string]$Status,
        [hashtable]$Facts = @{}
    )

    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    $statePath = Join-Path $ReleaseRoot 'edge-deployment-attempt.json'
    $temporaryPath = "$statePath.tmp-$([Guid]::NewGuid().ToString('N'))"
    $state = [ordered]@{
        schemaVersion = 1
        target = $Target
        invocationId = $InvocationId
        stage = $Stage
        status = $Status
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        facts = $Facts
    }
    $state | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $temporaryPath
    Move-Item -LiteralPath $temporaryPath -Destination $statePath -Force
    return $statePath
}
