param(
    [string]$CloudApiBaseUrl = $env:EDGE_CLOUD_API_BASE_URL,

    [Parameter(Mandatory = $true)]
    [string]$EmployeeNo,

    [securestring]$Password,

    [string]$Name = '',

    [string]$RevokeNamePrefix = '',

    [string]$ExpiresAtUtc = ''
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

$resolvedName = if ([string]::IsNullOrWhiteSpace($Name)) {
    $userName = if ([string]::IsNullOrWhiteSpace($env:USER)) { 'local' } else { $env:USER }
    "edge-release-$userName"
}
else {
    $Name.Trim()
}

$session = New-EdgeReleaseCloudSession `
    -CloudApiBaseUrl $CloudApiBaseUrl `
    -EmployeeNo $EmployeeNo `
    -Password $Password

$body = @{
    name = $resolvedName
    permissions = @('ClientRelease.Read', 'ClientRelease.Publish')
}

if (-not [string]::IsNullOrWhiteSpace($ExpiresAtUtc)) {
    $body.expiresAtUtc = [DateTimeOffset]::Parse($ExpiresAtUtc).ToUniversalTime().ToString('O')
}

$apiRoot = Get-EdgeReleaseApiRoot -CloudApiBaseUrl $CloudApiBaseUrl
if (-not [string]::IsNullOrWhiteSpace($RevokeNamePrefix)) {
    $resolvedRevokePrefix = $RevokeNamePrefix.Trim()
    if ($resolvedRevokePrefix -notmatch '^iiot-edgehost-[0-9a-f]{32}$') {
        throw 'RevokeNamePrefix must be the deterministic from-zero EdgeHost invocation id.'
    }

    try {
        $existingKeys = @(
            Invoke-RestMethod `
                -Method Get `
                -Uri "$apiRoot/human/client-release-api-keys" `
                -Headers @{ Authorization = "Bearer $($session.AccessToken)" } `
                -TimeoutSec 30
        )
        foreach ($existingKey in $existingKeys) {
            $existingName = [string]$existingKey.name
            $existingStatus = [string]$existingKey.status
            if ($existingStatus -cne 'Active' -or
                $existingName -notmatch "^$([Regex]::Escape($resolvedRevokePrefix))-a[1-9][0-9]*$") {
                continue
            }

            $existingId = [string]$existingKey.id
            if ($existingId -notmatch '^[0-9a-fA-F-]{36}$') {
                throw "Cloud returned an invalid Edge Release API key id for rotation: $existingId"
            }
            Invoke-RestMethod `
                -Method Delete `
                -Uri "$apiRoot/human/client-release-api-keys/$existingId" `
                -Headers @{ Authorization = "Bearer $($session.AccessToken)" } `
                -ContentType 'application/json' `
                -Body (@{ reason = 'from-zero retry rotation' } | ConvertTo-Json -Compress) `
                -TimeoutSec 30 | Out-Null
        }
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 403) {
            throw 'Current Cloud user does not have ClientRelease.Manage permission to rotate Edge Release API keys.'
        }

        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) {
            throw 'Cloud login succeeded but the access token was rejected while rotating Edge Release API keys.'
        }

        throw
    }
}

try {
    $response = Invoke-RestMethod `
        -Method Post `
        -Uri "$apiRoot/human/client-release-api-keys" `
        -Headers @{ Authorization = "Bearer $($session.AccessToken)" } `
        -ContentType 'application/json' `
        -Body ($body | ConvertTo-Json -Compress) `
        -TimeoutSec 30
}
catch {
    if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 403) {
        throw 'Current Cloud user does not have ClientRelease.Manage permission to create Edge Release API keys.'
    }

    if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) {
        throw 'Cloud login succeeded but the access token was rejected while creating the Edge Release API key.'
    }

    throw
}

$apiKey = [string]$response.apiKey
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw 'Cloud did not return apiKey when creating the Edge Release API key.'
}

$secureApiKey = ConvertTo-SecureString -String $apiKey -AsPlainText -Force
& (Join-Path $PSScriptRoot 'SaveEdgeReleaseApiKey.ps1') `
    -CloudApiBaseUrl $CloudApiBaseUrl `
    -ApiKey $secureApiKey

Write-Host "Created and saved Edge Release API key: $($response.name)"
Write-Host "ExpiresAtUtc: $($response.expiresAtUtc)"
