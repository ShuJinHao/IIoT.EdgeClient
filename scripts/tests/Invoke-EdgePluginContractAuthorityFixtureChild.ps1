[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('success', 'crash', 'timeout', 'parent-exit-pipe-descendant', 'pinned-child-environment')]
    [string]$FixtureMode,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$FixtureNonce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$authorization = [Environment]::GetEnvironmentVariable('EDGE_AUTHORITY_PROTOCOL_FIXTURE', 'Process')
if ([string]$authorization -cne "protocol-test:$FixtureNonce") {
    throw 'EDGE-SPLIT-AUTHORITY-FIXTURE-001 fixture child may run only under the explicit protocol-test nonce.'
}

switch ($FixtureMode) {
    'success' {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes('{"fixture":"success","schemaVersion":1}')
        $stdout = [Console]::OpenStandardOutput()
        $stdout.Write($bytes, 0, $bytes.Length)
        $stdout.Flush()
        exit 0
    }
    'crash' {
        [Console]::Error.WriteLine('EDGE-SPLIT-AUTHORITY-FIXTURE-CRASH expected protocol fixture crash.')
        exit 23
    }
    'timeout' {
        [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds(30)).GetAwaiter().GetResult()
        exit 0
    }
    'parent-exit-pipe-descendant' {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = [Environment]::ProcessPath
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @(
                '-NoLogo', '-NoProfile', '-Command',
                '[Threading.Thread]::Sleep(3000)')) {
            $startInfo.ArgumentList.Add([string]$argument)
        }
        $descendant = [Diagnostics.Process]::new()
        $descendant.StartInfo = $startInfo
        try {
            if (-not $descendant.Start()) {
                throw 'EDGE-SPLIT-AUTHORITY-FIXTURE-PIPE descendant did not start.'
            }
        }
        finally { $descendant.Dispose() }
        exit 0
    }
    'pinned-child-environment' {
        $modulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
        Import-Module $modulePath -Force
        if (-not [bool](Initialize-EdgeAuthorityGitChildEnvironment)) {
            throw 'EDGE-SPLIT-AUTHORITY-FIXTURE-BINDING exact child binding was not accepted.'
        }
        $gitCommand = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
        $dotnetCommand = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0]
        $dotnetPath = Resolve-EdgeFixedExecutable ([string]$dotnetCommand.Source)
        $dotnetVersion = (& $dotnetPath --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-FIXTURE-BINDING fixed dotnet version probe failed.'
        }
        $result = [pscustomobject][ordered]@{
            schemaVersion = 1
            pathFirst = ([Environment]::GetEnvironmentVariable('PATH', 'Process').Split(
                    [IO.Path]::PathSeparator))[0]
            gitPath = Resolve-EdgeFixedExecutable ([string]$gitCommand.Source)
            dotnetPath = $dotnetPath
            dotnetVersion = $dotnetVersion
        }
        $bytes = ConvertTo-EdgeCanonicalBytes $result
        $stdout = [Console]::OpenStandardOutput()
        $stdout.Write($bytes, 0, $bytes.Length)
        $stdout.Flush()
        exit 0
    }
}
