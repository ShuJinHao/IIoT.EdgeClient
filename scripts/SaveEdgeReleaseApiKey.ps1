param(
    [string]$CloudApiBaseUrl = 'http://10.98.90.154:81/api/v1',

    [securestring]$ApiKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')

if ($null -eq $ApiKey) {
    $ApiKey = Read-Host -Prompt 'Edge Release API key' -AsSecureString
}

$plainApiKey = ConvertFrom-EdgeReleaseSecureString -SecureString $ApiKey
try {
    if ([string]::IsNullOrWhiteSpace($plainApiKey)) {
        throw 'Edge Release API key cannot be empty.'
    }

    $token = Get-EdgeReleaseApiKeyToken -CloudApiBaseUrl $CloudApiBaseUrl -ApiKey $plainApiKey
    Test-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $token
    Set-EdgeReleaseKeychainSecret -Service $EdgeReleaseApiKeyService -Value $plainApiKey
}
finally {
    $plainApiKey = $null
}

Write-Host 'Edge Release API key saved to macOS Keychain.'
Write-Host 'Cloud machine-token and client-release catalog checks passed.'
