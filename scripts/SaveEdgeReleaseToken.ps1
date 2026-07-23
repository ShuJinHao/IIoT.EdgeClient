param(
    [string]$CloudApiBaseUrl = $env:EDGE_CLOUD_API_BASE_URL,

    [Parameter(Mandatory = $true)]
    [string]$EmployeeNo,

    [securestring]$Password
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')

if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl)) {
    throw 'CloudApiBaseUrl is required. Pass -CloudApiBaseUrl or set $env:EDGE_CLOUD_API_BASE_URL, for example http://<cloud-gateway-host>:<port>/api/v1.'
}

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

Test-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $session.AccessToken

Write-Warning 'Human session credentials were saved for manual recovery only. Standard deployment does not read them.'
Write-Host "Access token expires at: $($session.AccessTokenExpiresAt)"
Write-Host "Refresh token expires at: $($session.RefreshTokenExpiresAt)"
