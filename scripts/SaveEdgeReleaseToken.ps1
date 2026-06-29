param(
    [string]$CloudApiBaseUrl = 'http://10.98.90.154:81/api/v1',

    [Parameter(Mandatory = $true)]
    [string]$EmployeeNo,

    [securestring]$Password
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')

if ($null -eq $Password) {
    $Password = Read-Host -Prompt 'Cloud password' -AsSecureString
}

$session = New-EdgeReleaseCloudSession `
    -CloudApiBaseUrl $CloudApiBaseUrl `
    -EmployeeNo $EmployeeNo `
    -Password $Password

Save-EdgeReleaseCloudSession `
    -AccessToken $session.AccessToken `
    -RefreshToken $session.RefreshToken `
    -AccessTokenExpiresAt $session.AccessTokenExpiresAt `
    -RefreshTokenExpiresAt $session.RefreshTokenExpiresAt

$token = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl
Test-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $token

Write-Host 'Edge release Cloud token saved to macOS Keychain.'
Write-Host "Access token expires at: $($session.AccessTokenExpiresAt)"
Write-Host "Refresh token expires at: $($session.RefreshTokenExpiresAt)"
