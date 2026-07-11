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

function Get-EdgeLoopbackFakeCloudDiagnostic {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$PortFile,
        [Parameter(Mandatory = $true)][string]$PythonPath
    )

    $Process.Refresh()
    $processState = if ($Process.HasExited) { "exited:$($Process.ExitCode)" } else { 'running' }
    $portState = Get-EdgeLoopbackFakeCloudPortState -PortFile $PortFile
    return "process=$processState; portFile=$($portState.Description); python=$PythonPath"
}

function Resolve-EdgeLoopbackFakeCloudPython {
    if (-not [string]::IsNullOrWhiteSpace($env:pythonLocation)) {
        $configuredPath = if ([OperatingSystem]::IsWindows()) {
            Join-Path $env:pythonLocation 'python.exe'
        }
        else {
            Join-Path $env:pythonLocation 'bin/python3'
        }
        if (Test-Path -LiteralPath $configuredPath -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($configuredPath)
        }
    }

    $commandNames = if ([OperatingSystem]::IsWindows()) { @('python', 'python3') } else { @('python3', 'python') }
    foreach ($commandName in $commandNames) {
        $command = Get-Command $commandName -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $command) {
            continue
        }
        if ([OperatingSystem]::IsWindows() -and
            $command.Source.Contains('\WindowsApps\', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        return [System.IO.Path]::GetFullPath($command.Source)
    }

    throw 'Python 3 is required for the loopback fake Cloud behavior tests.'
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
        [ValidateRange(1, 60)][int]$ReadyTimeoutSeconds = 30
    )

    $pythonPath = Resolve-EdgeLoopbackFakeCloudPython
    $serverScript = Join-Path $PSScriptRoot 'fake_edge_release_cloud.py'
    $diagnosticRoot = Split-Path -Parent $PortFile
    if ([string]::IsNullOrWhiteSpace($diagnosticRoot)) {
        throw 'Loopback fake Cloud port file must have a parent directory.'
    }
    New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
    Remove-Item -LiteralPath $PortFile -Force -ErrorAction SilentlyContinue

    $arguments = @('-u', $serverScript, '--port-file', $PortFile, '--request-log', $RequestLog)
    if (-not [string]::IsNullOrWhiteSpace($PluginVersion)) {
        $arguments += @('--plugin-version', $PluginVersion)
    }

    $process = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $pythonPath
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        foreach ($argument in $arguments) {
            $startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw 'Loopback fake Cloud process could not be started.'
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadyTimeoutSeconds)

        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) {
                $diagnostic = Get-EdgeLoopbackFakeCloudDiagnostic -Process $process -PortFile $PortFile `
                    -PythonPath $pythonPath
                throw "Loopback fake Cloud exited before readiness. $diagnostic"
            }

            $portState = Get-EdgeLoopbackFakeCloudPortState -PortFile $PortFile
            if ($portState.IsReady) {
                return [pscustomobject]@{
                    Process = $process
                    Port = $portState.Port
                    BaseUrl = "http://127.0.0.1:$($portState.Port)"
                    PythonPath = $pythonPath
                }
            }

            Start-Sleep -Milliseconds 50
        }

        $diagnostic = Get-EdgeLoopbackFakeCloudDiagnostic -Process $process -PortFile $PortFile `
            -PythonPath $pythonPath
        throw "Loopback fake Cloud readiness timed out after $ReadyTimeoutSeconds seconds. $diagnostic"
    }
    catch {
        if ($null -ne $process) {
            Stop-EdgeLoopbackFakeCloud -Server ([pscustomobject]@{ Process = $process })
        }
        throw
    }
}
