Set-StrictMode -Version Latest

function Get-EdgeLoopbackFakeCloudPortState {
    param([Parameter(Mandatory = $true)][string]$PortFile)

    if (-not (Test-Path -LiteralPath $PortFile -PathType Leaf)) {
        return [pscustomobject]@{ IsReady = $false; Port = 0; Description = 'missing' }
    }

    try {
        $rawPort = (Get-Content -Raw -Encoding ASCII -LiteralPath $PortFile).Trim()
    }
    catch {
        return [pscustomobject]@{ IsReady = $false; Port = 0; Description = 'unreadable' }
    }

    if ([string]::IsNullOrWhiteSpace($rawPort)) {
        return [pscustomobject]@{ IsReady = $false; Port = 0; Description = 'empty' }
    }

    $port = 0
    if (-not [int]::TryParse($rawPort, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        return [pscustomobject]@{ IsReady = $false; Port = 0; Description = 'invalid' }
    }

    return [pscustomobject]@{ IsReady = $true; Port = $port; Description = "valid:$port" }
}

function Get-EdgeLoopbackFakeCloudOutputTail {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 'missing'
    }

    try {
        $text = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    }
    catch {
        return 'unreadable'
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        return 'empty'
    }

    $normalized = [System.Text.RegularExpressions.Regex]::Replace($text, '\s+', ' ').Trim()
    if ($normalized.Length -gt 512) {
        $normalized = $normalized.Substring($normalized.Length - 512)
    }
    return $normalized
}

function Get-EdgeLoopbackFakeCloudDiagnostic {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$PortFile,
        [Parameter(Mandatory = $true)][string]$StandardOutputPath,
        [Parameter(Mandatory = $true)][string]$StandardErrorPath
    )

    $Process.Refresh()
    $processState = if ($Process.HasExited) { "exited:$($Process.ExitCode)" } else { 'running' }
    $portState = Get-EdgeLoopbackFakeCloudPortState -PortFile $PortFile
    $stdoutTail = Get-EdgeLoopbackFakeCloudOutputTail -Path $StandardOutputPath
    $stderrTail = Get-EdgeLoopbackFakeCloudOutputTail -Path $StandardErrorPath
    return "process=$processState; portFile=$($portState.Description); stdout=$stdoutTail; stderr=$stderrTail"
}

function Stop-EdgeLoopbackFakeCloud {
    param([AllowNull()][object]$Server)

    if ($null -eq $Server -or $null -eq $Server.Process) {
        return
    }

    $process = [System.Diagnostics.Process]$Server.Process
    try {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(5000) | Out-Null
        }
    }
    catch {
        # 测试收尾不得覆盖原始失败。
    }
    finally {
        $process.Dispose()
    }
}

function Start-EdgeLoopbackFakeCloud {
    param(
        [Parameter(Mandatory = $true)][string]$PortFile,
        [Parameter(Mandatory = $true)][string]$RequestLog,
        [string]$PluginVersion,
        [ValidateRange(1, 60)][int]$ReadyTimeoutSeconds = 10
    )

    $python = Get-Command python3 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $python) {
        $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($null -eq $python) {
        throw 'Python 3 is required for the loopback fake Cloud behavior tests.'
    }

    $serverScript = Join-Path $PSScriptRoot 'fake_edge_release_cloud.py'
    $diagnosticRoot = Split-Path -Parent $PortFile
    if ([string]::IsNullOrWhiteSpace($diagnosticRoot)) {
        throw 'Loopback fake Cloud port file must have a parent directory.'
    }
    New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
    Remove-Item -LiteralPath $PortFile -Force -ErrorAction SilentlyContinue

    $standardOutputPath = Join-Path $diagnosticRoot 'fake-cloud.stdout.log'
    $standardErrorPath = Join-Path $diagnosticRoot 'fake-cloud.stderr.log'
    Remove-Item -LiteralPath $standardOutputPath, $standardErrorPath -Force -ErrorAction SilentlyContinue

    $arguments = @($serverScript, '--port-file', $PortFile, '--request-log', $RequestLog)
    if (-not [string]::IsNullOrWhiteSpace($PluginVersion)) {
        $arguments += @('--plugin-version', $PluginVersion)
    }

    $process = $null
    try {
        $process = Start-Process -FilePath $python.Source -ArgumentList $arguments -PassThru `
            -RedirectStandardOutput $standardOutputPath -RedirectStandardError $standardErrorPath
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadyTimeoutSeconds)

        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) {
                $diagnostic = Get-EdgeLoopbackFakeCloudDiagnostic -Process $process -PortFile $PortFile `
                    -StandardOutputPath $standardOutputPath -StandardErrorPath $standardErrorPath
                throw "Loopback fake Cloud exited before readiness. $diagnostic"
            }

            $portState = Get-EdgeLoopbackFakeCloudPortState -PortFile $PortFile
            if ($portState.IsReady) {
                return [pscustomobject]@{
                    Process = $process
                    Port = $portState.Port
                    BaseUrl = "http://127.0.0.1:$($portState.Port)"
                    StandardOutputPath = $standardOutputPath
                    StandardErrorPath = $standardErrorPath
                }
            }

            Start-Sleep -Milliseconds 50
        }

        $diagnostic = Get-EdgeLoopbackFakeCloudDiagnostic -Process $process -PortFile $PortFile `
            -StandardOutputPath $standardOutputPath -StandardErrorPath $standardErrorPath
        throw "Loopback fake Cloud readiness timed out after $ReadyTimeoutSeconds seconds. $diagnostic"
    }
    catch {
        if ($null -ne $process) {
            Stop-EdgeLoopbackFakeCloud -Server ([pscustomobject]@{ Process = $process })
        }
        throw
    }
}
