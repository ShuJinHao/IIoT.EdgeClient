$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "iiot-edge-plugin-envelope-$([Guid]::NewGuid().ToString('N'))"
$pluginRoot = Join-Path $tempRoot 'plugin-repo'
$packageRoot = Join-Path $tempRoot 'package-root'
$wrapperPath = Join-Path $tempRoot 'wrapper.zip'
$privateKeyPath = Join-Path $tempRoot 'release-signing-private.pem'

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $pluginRoot 'docs') | Out-Null
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $pluginRoot 'docs/P1.md') -Encoding utf8NoBOM -Value '# P1'
    Set-Content -LiteralPath (Join-Path $packageRoot 'file-manifest.json') -Encoding utf8NoBOM -Value '{"schemaVersion":1,"files":[{"path":"plugin.json"}]}'
    Set-Content -LiteralPath (Join-Path $packageRoot 'data-capabilities.json') -Encoding utf8NoBOM -Value '{"schemaVersion":1,"moduleId":"P1","capabilities":[]}'
    Set-Content -LiteralPath (Join-Path $packageRoot 'dependency-closure.json') -Encoding utf8NoBOM -Value '{"schemaVersion":2,"dependencies":[{"publishPath":"P1.dll"}]}'
    Set-Content -LiteralPath (Join-Path $packageRoot 'plugin.json') -Encoding utf8NoBOM -Value '{"moduleId":"P1"}'
    $packagePath = Join-Path $tempRoot 'P1.zip'
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $packagePath -Force

    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        Set-Content -LiteralPath $privateKeyPath -Encoding ascii -Value $rsa.ExportRSAPrivateKeyPem()
        $publicKey = [System.Security.Cryptography.RSA]::Create()
        $publicKey.ImportFromPem($rsa.ExportSubjectPublicKeyInfoPem())
    }
    finally {
        $rsa.Dispose()
    }

    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'PublishEdgePluginRelease.ps1') `
        -ModuleId P1 `
        -PluginRepositoryRoot $pluginRoot `
        -SupportedProcessType DIECUT `
        -CloudApiBaseUrl 'https://cloud.example.test/api/v1' `
        -ExpectedSha ('0' * 40) `
        -ReleaseSigningPrivateKeyPath $privateKeyPath `
        -ReleaseSigningKeyId test-key-1

    $metadata = [pscustomobject]@{
        packageSchemaVersion = 2
        moduleId = 'P1'
        processType = 'DIECUT'
        displayName = 'P1'
        description = 'P1 plugin'
        iconKind = 'Cog'
        accentColor = '#000000'
        version = '2.0.12'
        hostApiVersion = '2.0.0'
        minHostVersion = '2.0.12'
        maxHostVersion = '2.0.12'
        dependencies = @('Shared.Foundation')
        targetRuntime = 'win-x64'
        targetFramework = 'net10.0'
        packageFileName = 'P1.zip'
        packageSize = (Get-Item $packagePath).Length
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        publisher = 'IIoT'
        sourceCommit = ('a' * 40)
        businessDocumentRef = 'docs/P1.md'
        businessDocumentSha256 = (Get-FileHash -LiteralPath (Join-Path $pluginRoot 'docs/P1.md') -Algorithm SHA256).Hash
        fileManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'file-manifest.json') -Algorithm SHA256).Hash
        fileManifestFileCount = 1
        dataCapabilitiesSha256 = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'data-capabilities.json') -Algorithm SHA256).Hash
        dependencyClosureSha256 = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'dependency-closure.json') -Algorithm SHA256).Hash
        dependencyCount = 1
        dependencyHostComponent = 'Host'
        dependencyHostVersion = '2.0.12'
        dependencyHostFileManifestSha256 = ('c' * 64)
    }

    New-PluginReleaseWrapper -Metadata $metadata -PackagePath $packagePath -ReleaseNotesText 'test' -OutputZip $wrapperPath | Out-Null
    $extractRoot = Join-Path $tempRoot 'extract'
    Expand-Archive -LiteralPath $wrapperPath -DestinationPath $extractRoot
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $extractRoot 'plugin-release.json') | ConvertFrom-Json
    Assert-True ($manifest.packageSchemaVersion -eq 3) 'Release envelope schema must be 3.'
    Assert-True ($manifest.releaseSignature.algorithm -ceq 'rsa-pss-sha256') 'Release signature algorithm mismatch.'
    Assert-True ($manifest.releaseSignature.keyId -ceq 'test-key-1') 'Release signature key id mismatch.'
    Assert-True (Test-Path -LiteralPath (Join-Path $extractRoot 'evidence/business-document.md')) 'Business document evidence missing.'

    $canonical = Get-PluginReleaseCanonicalBytes -Manifest $manifest
    $canonicalText = [Text.Encoding]::UTF8.GetString($canonical)
    $canonicalCreatedAt = ([DateTimeOffset]$manifest.createdAtUtc).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $expectedCanonical = '{"packageSchemaVersion":3,"channel":"stable","moduleId":"P1","processType":"DIECUT","displayName":"P1","description":"P1 plugin","iconKind":"Cog","accentColor":"#000000","version":"2.0.12","hostApiVersion":"2.0.0","minHostVersion":"2.0.12","maxHostVersion":"2.0.12","dependencies":["Shared.Foundation"],"targetRuntime":"win-x64","targetFramework":"net10.0","packageFileName":"P1.zip","packageSize":' + $metadata.packageSize + ',"sha256":"' + $metadata.sha256.ToLowerInvariant() + '","publisher":"IIoT","sourceCommit":"' + ('a' * 40) + '","businessDocumentRef":"docs/P1.md","businessDocumentSha256":"' + $metadata.businessDocumentSha256.ToLowerInvariant() + '","fileManifestSha256":"' + $metadata.fileManifestSha256.ToLowerInvariant() + '","fileManifestFileCount":1,"dataCapabilitiesFileName":"data-capabilities.json","dataCapabilitiesSha256":"' + $metadata.dataCapabilitiesSha256.ToLowerInvariant() + '","dependencyClosureSha256":"' + $metadata.dependencyClosureSha256.ToLowerInvariant() + '","dependencyCount":1,"dependencyHostComponent":"Host","dependencyHostVersion":"2.0.12","dependencyHostFileManifestSha256":"' + $metadata.dependencyHostFileManifestSha256.ToLowerInvariant() + '","releaseNotes":"test","createdAtUtc":"' + $canonicalCreatedAt + '"}'
    Assert-True ($canonicalText -ceq $expectedCanonical) "Canonical release envelope changed.`nactual=$canonicalText"

    $signature = [Convert]::FromBase64String([string]$manifest.releaseSignature.value)
    Assert-True ($publicKey.VerifyData(
        $canonical,
        $signature,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pss)) 'Release signature verification failed.'
    $mutations = [ordered]@{
        packageSchemaVersion = 4
        channel = 'preview'
        moduleId = 'P2'
        processType = 'OTHER'
        displayName = 'changed'
        description = 'changed'
        iconKind = 'Alert'
        accentColor = '#ffffff'
        version = '9.9.9'
        hostApiVersion = '9.0.0'
        minHostVersion = '9.0.0'
        maxHostVersion = '9.9.9'
        dependencies = @('Changed.Dependency')
        targetRuntime = 'win-arm64'
        targetFramework = 'net99.0'
        packageFileName = 'changed.zip'
        packageSize = ([int64]$metadata.packageSize + 1)
        sha256 = ('b' * 64)
        publisher = 'changed'
        sourceCommit = ('b' * 40)
        businessDocumentRef = 'docs/changed.md'
        businessDocumentSha256 = ('b' * 64)
        fileManifestSha256 = ('b' * 64)
        fileManifestFileCount = 2
        dataCapabilitiesFileName = 'changed.json'
        dataCapabilitiesSha256 = ('b' * 64)
        dependencyClosureSha256 = ('b' * 64)
        dependencyCount = 2
        dependencyHostComponent = 'ChangedHost'
        dependencyHostVersion = '9.9.9'
        dependencyHostFileManifestSha256 = ('b' * 64)
        releaseNotes = 'changed'
        createdAtUtc = '2099-01-01T00:00:00.0000000Z'
    }
    foreach ($entry in $mutations.GetEnumerator()) {
        $tampered = ($manifest | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
        $tampered.($entry.Key) = $entry.Value
        $tamperedCanonical = Get-PluginReleaseCanonicalBytes -Manifest $tampered
        Assert-True (-not $publicKey.VerifyData(
            $tamperedCanonical,
            $signature,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pss)) "Tampering $($entry.Key) unexpectedly verified."
    }
    $signatureOnlyChange = ($manifest | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
    $signatureOnlyChange.releaseSignature.value = [Convert]::ToBase64String([byte[]](1..32))
    Assert-True ([Text.Encoding]::UTF8.GetString((Get-PluginReleaseCanonicalBytes -Manifest $signatureOnlyChange)) -ceq $canonicalText) 'releaseSignature must be the only excluded field.'

    $ReleaseSigningPrivateKeyPath = ''
    $missingKeyFailed = $false
    try { New-PluginReleaseSignature -Manifest $manifest | Out-Null } catch { $missingKeyFailed = $true }
    Assert-True $missingKeyFailed 'Missing production signing material must fail closed.'
    $publicKey.Dispose()
    Write-Host 'Edge plugin release envelope behavior passed.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
