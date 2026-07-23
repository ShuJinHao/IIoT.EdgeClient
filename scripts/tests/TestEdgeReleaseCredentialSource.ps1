$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $scriptsRoot 'EdgeReleaseCredential.Common.ps1')

$script:keychainValues = @{}
$script:keychainLookups = [System.Collections.Generic.List[string]]::new()
$script:machineTokenRequests = 0

function Get-EdgeReleaseKeychainSecret {
    param([Parameter(Mandatory = $true)][string]$Service)

    $script:keychainLookups.Add($Service)
    if ($script:keychainValues.ContainsKey($Service)) {
        return [string]$script:keychainValues[$Service]
    }

    return ''
}

function Get-EdgeReleaseApiKeyToken {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$ApiKey
    )

    if ($CloudApiBaseUrl -ne 'https://cloud.example/api/v1' -or $ApiKey -ne 'canonical-api-key') {
        throw 'Unexpected machine-token request.'
    }

    $script:machineTokenRequests++
    return 'machine-access-token'
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

$oldCloudToken = $env:IIOT_CLOUD_RELEASE_TOKEN
$oldApiKey = $env:IIOT_EDGE_RELEASE_API_KEY
try {
    $env:IIOT_CLOUD_RELEASE_TOKEN = 'environment-access-token'
    $env:IIOT_EDGE_RELEASE_API_KEY = 'environment-api-key'
    $script:keychainValues[$EdgeReleaseAccessTokenService] = 'human-access-token'
    $script:keychainValues[$EdgeReleaseRefreshTokenService] = 'human-refresh-token'
    $script:keychainValues['iiot-cloud-release'] = 'legacy-access-token'

    $resolved = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl 'https://cloud.example/api/v1'
    Assert-Equal -Actual $resolved -Expected '' -Message 'Standard resolution must ignore environment, Human-session, and legacy credentials.'
    Assert-Equal -Actual $script:keychainLookups.Count -Expected 1 -Message 'Standard resolution must perform exactly one Keychain lookup.'
    Assert-Equal -Actual $script:keychainLookups[0] -Expected $EdgeReleaseApiKeyService -Message 'Standard resolution must read only the canonical Edge Release API key.'

    $script:keychainLookups.Clear()
    $script:keychainValues[$EdgeReleaseApiKeyService] = 'canonical-api-key'
    $resolved = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl 'https://cloud.example/api/v1'
    Assert-Equal -Actual $resolved -Expected 'machine-access-token' -Message 'Canonical Keychain API key must be exchanged for a machine token.'
    Assert-Equal -Actual $script:machineTokenRequests -Expected 1 -Message 'Canonical API key must be exchanged exactly once.'

    $script:keychainLookups.Clear()
    $resolved = Resolve-EdgeReleaseCloudToken `
        -CloudApiBaseUrl 'https://cloud.example/api/v1' `
        -CloudToken 'explicit-recovery-token'
    Assert-Equal -Actual $resolved -Expected 'explicit-recovery-token' -Message 'Explicit recovery token must be honored.'
    Assert-Equal -Actual $script:keychainLookups.Count -Expected 0 -Message 'Explicit recovery token must not read Keychain.'

    Write-Host 'Edge release credential-source tests passed.'
}
finally {
    if ($null -eq $oldCloudToken) {
        Remove-Item Env:IIOT_CLOUD_RELEASE_TOKEN -ErrorAction SilentlyContinue
    }
    else {
        $env:IIOT_CLOUD_RELEASE_TOKEN = $oldCloudToken
    }

    if ($null -eq $oldApiKey) {
        Remove-Item Env:IIOT_EDGE_RELEASE_API_KEY -ErrorAction SilentlyContinue
    }
    else {
        $env:IIOT_EDGE_RELEASE_API_KEY = $oldApiKey
    }
}
