$EdgeReleaseAccessTokenService = 'iiot-edge-release-access-token'
$EdgeReleaseRefreshTokenService = 'iiot-edge-release-refresh-token'
$EdgeReleaseAccessTokenExpiresAtService = 'iiot-edge-release-access-token-expires-at'
$EdgeReleaseRefreshTokenExpiresAtService = 'iiot-edge-release-refresh-token-expires-at'
$EdgeReleaseApiKeyService = 'iiot-edge-release-api-key'
$EdgeReleaseKeychainAccount = 'IIoT.EdgeClient'

function Get-EdgeReleaseKeychainSecret {
    param([Parameter(Mandatory = $true)][string]$Service)

    if (-not $IsMacOS) {
        return ''
    }

    $value = & security find-generic-password -s $Service -w 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ''
    }

    return [string]$value
}

function Set-EdgeReleaseKeychainSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Service,
        [Parameter(Mandatory = $true)][string]$Value,
        [string]$Account = $EdgeReleaseKeychainAccount
    )

    if (-not $IsMacOS) {
        throw 'macOS Keychain is required for persistent Edge release credentials on this workstation.'
    }

    & security add-generic-password -a $Account -s $Service -w $Value -U | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to write macOS Keychain item: $Service"
    }
}

function ConvertFrom-EdgeReleaseSecureString {
    param([Parameter(Mandatory = $true)][securestring]$SecureString)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Get-EdgeReleaseApiRoot {
    param([Parameter(Mandatory = $true)][string]$CloudApiBaseUrl)

    if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl)) {
        throw 'CloudApiBaseUrl is required.'
    }

    return $CloudApiBaseUrl.TrimEnd('/')
}

function Save-EdgeReleaseCloudSession {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)][string]$RefreshToken,
        [Parameter(Mandatory = $true)][string]$AccessTokenExpiresAt,
        [Parameter(Mandatory = $true)][string]$RefreshTokenExpiresAt
    )

    Set-EdgeReleaseKeychainSecret -Service $EdgeReleaseAccessTokenService -Value $AccessToken
    Set-EdgeReleaseKeychainSecret -Service $EdgeReleaseRefreshTokenService -Value $RefreshToken
    Set-EdgeReleaseKeychainSecret -Service $EdgeReleaseAccessTokenExpiresAtService -Value $AccessTokenExpiresAt
    Set-EdgeReleaseKeychainSecret -Service $EdgeReleaseRefreshTokenExpiresAtService -Value $RefreshTokenExpiresAt
}

function ConvertTo-EdgeReleaseSession {
    param([Parameter(Mandatory = $true)]$Response)

    $accessToken = [string]$Response.Content
    $headers = $Response.Headers
    $refreshToken = [string]($headers['x-iiot-refresh-token'] | Select-Object -First 1)
    $refreshTokenExpiresAt = [string]($headers['x-iiot-refresh-token-expires-at'] | Select-Object -First 1)
    $accessTokenExpiresAt = [string]($headers['x-iiot-access-token-expires-at'] | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($accessToken) `
        -or [string]::IsNullOrWhiteSpace($refreshToken) `
        -or [string]::IsNullOrWhiteSpace($refreshTokenExpiresAt) `
        -or [string]::IsNullOrWhiteSpace($accessTokenExpiresAt)) {
        throw 'Cloud authentication response is missing token body or session headers.'
    }

    return [PSCustomObject]@{
        AccessToken = $accessToken
        RefreshToken = $refreshToken
        AccessTokenExpiresAt = $accessTokenExpiresAt
        RefreshTokenExpiresAt = $refreshTokenExpiresAt
    }
}

function New-EdgeReleaseCloudSession {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$EmployeeNo,
        [Parameter(Mandatory = $true)][securestring]$Password
    )

    $apiRoot = Get-EdgeReleaseApiRoot -CloudApiBaseUrl $CloudApiBaseUrl
    $plainPassword = ConvertFrom-EdgeReleaseSecureString -SecureString $Password
    try {
        $body = @{
            employeeNo = $EmployeeNo
            password = $plainPassword
        } | ConvertTo-Json -Compress

        $response = Invoke-WebRequest `
            -Method Post `
            -Uri "$apiRoot/human/identity/login" `
            -ContentType 'application/json' `
            -Body $body `
            -UseBasicParsing `
            -TimeoutSec 30

        return ConvertTo-EdgeReleaseSession -Response $response
    }
    finally {
        $plainPassword = $null
    }
}

function Refresh-EdgeReleaseCloudSession {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$RefreshToken
    )

    $apiRoot = Get-EdgeReleaseApiRoot -CloudApiBaseUrl $CloudApiBaseUrl
    try {
        $response = Invoke-WebRequest `
            -Method Post `
            -Uri "$apiRoot/human/identity/refresh" `
            -Headers @{ 'X-IIoT-Refresh-Token' = $RefreshToken } `
            -UseBasicParsing `
            -TimeoutSec 30
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) {
            throw 'Human refresh token was rejected by Cloud. It is usually revoked by the active-session limit; use scripts/SaveEdgeReleaseApiKey.ps1 to store an Edge Release API key for stable deployments.'
        }

        throw
    }

    $session = ConvertTo-EdgeReleaseSession -Response $response
    Save-EdgeReleaseCloudSession `
        -AccessToken $session.AccessToken `
        -RefreshToken $session.RefreshToken `
        -AccessTokenExpiresAt $session.AccessTokenExpiresAt `
        -RefreshTokenExpiresAt $session.RefreshTokenExpiresAt

    return $session.AccessToken
}

function Get-EdgeReleaseApiKeyToken {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$ApiKey
    )

    $apiRoot = Get-EdgeReleaseApiRoot -CloudApiBaseUrl $CloudApiBaseUrl
    try {
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri "$apiRoot/machine/edge-release/token" `
            -Headers @{ 'X-IIoT-Edge-Release-Key' = $ApiKey } `
            -TimeoutSec 30
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) {
            throw 'Edge Release API key was rejected by Cloud. The key is invalid, revoked, expired, or missing ClientRelease.Publish permission.'
        }

        throw
    }

    $token = [string]$response.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw 'Cloud machine token response did not include accessToken.'
    }

    return $token
}

function Test-EdgeReleaseTokenExpiry {
    param(
        [string]$ExpiresAt,
        [int]$RefreshSkewMinutes = 5
    )

    if ([string]::IsNullOrWhiteSpace($ExpiresAt)) {
        return $true
    }

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($ExpiresAt, [ref]$parsed)) {
        return $true
    }

    return $parsed.ToUniversalTime() -le [DateTimeOffset]::UtcNow.AddMinutes($RefreshSkewMinutes)
}

function Resolve-EdgeReleaseCloudToken {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [string]$CloudToken = ''
    )

    if (-not [string]::IsNullOrWhiteSpace($CloudToken)) {
        return $CloudToken
    }

    if (-not [string]::IsNullOrWhiteSpace($env:IIOT_CLOUD_RELEASE_TOKEN)) {
        return $env:IIOT_CLOUD_RELEASE_TOKEN
    }

    $apiKey = if (-not [string]::IsNullOrWhiteSpace($env:IIOT_EDGE_RELEASE_API_KEY)) {
        $env:IIOT_EDGE_RELEASE_API_KEY
    }
    else {
        Get-EdgeReleaseKeychainSecret -Service $EdgeReleaseApiKeyService
    }

    if (-not [string]::IsNullOrWhiteSpace($apiKey)) {
        return Get-EdgeReleaseApiKeyToken -CloudApiBaseUrl $CloudApiBaseUrl -ApiKey $apiKey
    }

    $accessToken = Get-EdgeReleaseKeychainSecret -Service $EdgeReleaseAccessTokenService
    $refreshToken = Get-EdgeReleaseKeychainSecret -Service $EdgeReleaseRefreshTokenService
    $accessTokenExpiresAt = Get-EdgeReleaseKeychainSecret -Service $EdgeReleaseAccessTokenExpiresAtService

    if (-not [string]::IsNullOrWhiteSpace($refreshToken) `
        -and (Test-EdgeReleaseTokenExpiry -ExpiresAt $accessTokenExpiresAt)) {
        return Refresh-EdgeReleaseCloudSession -CloudApiBaseUrl $CloudApiBaseUrl -RefreshToken $refreshToken
    }

    if (-not [string]::IsNullOrWhiteSpace($accessToken)) {
        return $accessToken
    }

    $legacyToken = Get-EdgeReleaseKeychainSecret -Service 'iiot-cloud-release'
    if (-not [string]::IsNullOrWhiteSpace($legacyToken)) {
        return $legacyToken
    }

    return ''
}

function Test-EdgeReleaseCloudToken {
    param(
        [Parameter(Mandatory = $true)][string]$CloudApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$CloudToken,
        [string]$Channel = 'stable',
        [string]$TargetRuntime = 'win-x64'
    )

    $apiRoot = Get-EdgeReleaseApiRoot -CloudApiBaseUrl $CloudApiBaseUrl
    $encodedChannel = [Uri]::EscapeDataString($Channel)
    $encodedRuntime = [Uri]::EscapeDataString($TargetRuntime)
    $uri = "$apiRoot/human/client-releases/catalog?channel=$encodedChannel&targetRuntime=$encodedRuntime&onlyPublished=true"
    Invoke-WebRequest `
        -Method Get `
        -Uri $uri `
        -Headers @{ Authorization = "Bearer $CloudToken" } `
        -UseBasicParsing `
        -TimeoutSec 15 | Out-Null
}
