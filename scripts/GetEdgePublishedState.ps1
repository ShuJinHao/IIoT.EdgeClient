[CmdletBinding()]
param(
    [string]$CloudApiBaseUrl = 'http://10.98.90.154:81/api/v1',
    [string]$CloudToken = '',
    [string]$Channel = 'stable',
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EdgeDeployment.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')

$token = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $CloudToken
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Edge published-state inspection requires the standard Edge Release API key or an explicit Cloud token.'
}

$apiRoot = $CloudApiBaseUrl.TrimEnd('/')
$catalog = Invoke-EdgeCurlJsonGet `
    -Uri "$apiRoot/human/client-releases/catalog?channel=$Channel&targetRuntime=$RuntimeIdentifier&onlyPublished=true" `
    -Token $token -ConnectTimeoutSeconds 10 -RequestTimeoutSeconds 60 -LowSpeedTimeSeconds 30 -LowSpeedLimitBytesPerSecond 128

$hostEntries = @($catalog.host.versions | Where-Object { $_.status -eq 'Published' -and $_.version -match '^\d+\.\d+\.\d+$' })
$latestHost = $hostEntries | Sort-Object { [version]$_.version } | Select-Object -Last 1
$hostSourceCommit = ''
if ($null -ne $latestHost) {
    $downloadUrl = [string]$latestHost.downloadUrl
    if (-not $downloadUrl.StartsWith('http', [StringComparison]::OrdinalIgnoreCase)) {
        $uri = [Uri]$CloudApiBaseUrl
        $builder = [UriBuilder]::new($uri)
        $path = $builder.Path.TrimEnd('/')
        if ($path.EndsWith('/api/v1', [StringComparison]::OrdinalIgnoreCase)) {
            $builder.Path = $path.Substring(0, $path.Length - '/api/v1'.Length)
        }
        $downloadUrl = "$($builder.Uri.AbsoluteUri.TrimEnd('/'))/$($downloadUrl.TrimStart('/'))"
    }
    $manifest = Invoke-EdgeCurlJsonGet -Uri $downloadUrl -ConnectTimeoutSeconds 10 -RequestTimeoutSeconds 60 `
        -LowSpeedTimeSeconds 30 -LowSpeedLimitBytesPerSecond 128
    $hostSourceCommit = [string]$manifest.sourceCommit
}

$plugins = [ordered]@{}
foreach ($plugin in @($catalog.plugins)) {
    $versions = @($plugin.versions | Where-Object { $_.status -eq 'Published' } | ForEach-Object { [string]$_.version })
    $plugins[[string]$plugin.moduleId] = @($versions | Sort-Object -Unique)
}

[ordered]@{
    schemaVersion = 1
    hostVersion = if ($null -eq $latestHost) { '' } else { [string]$latestHost.version }
    hostSourceCommit = $hostSourceCommit
    pluginVersions = $plugins
} | ConvertTo-Json -Depth 8
