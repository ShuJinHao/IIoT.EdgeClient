$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishScript = Join-Path $repoRoot 'scripts/PublishEdgeClientInstallerArtifact.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $publishScript,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw "Publish script has parse errors: $($parseErrors[0].Message)"
}

foreach ($functionName in @(
    'Get-ArtifactFilePaths',
    'Get-ArtifactRelativePath',
    'Write-HostFileManifest'
)) {
    $functionAst = $ast.Find(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq $functionName
        },
        $true)
    if ($null -eq $functionAst) {
        throw "Required function was not found in publish script: $functionName"
    }

    . ([scriptblock]::Create($functionAst.Extent.Text))
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("edge-host-manifest-" + [Guid]::NewGuid().ToString('N'))
$hostRoot = Join-Path $testRoot 'host'
$manifestPath = Join-Path $testRoot 'host-file-manifest.json'
try {
    $fixtures = [ordered]@{
        'IIoT.Edge.Shell.exe' = 'shell'
        'managed/IIoT.Edge.Host.dll' = 'managed'
        'native/hostpolicy.dll' = 'native'
    }
    foreach ($entry in $fixtures.GetEnumerator()) {
        $path = Join-Path $hostRoot $entry.Key
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, $entry.Value)
    }

    $result = Write-HostFileManifest `
        -HostDirectory $hostRoot `
        -OutputPath $manifestPath `
        -HostVersion '2.0.16'
    if ($result.FileCount -ne $fixtures.Count) {
        throw "Expected $($fixtures.Count) Host files, got $($result.FileCount)."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.component -ne 'Host' -or
        $manifest.version -ne '2.0.16') {
        throw 'Host file manifest header is invalid.'
    }

    $files = @($manifest.files)
    if ($files.Count -ne $fixtures.Count -or
        @($files | Group-Object path | Where-Object Count -gt 1).Count -ne 0) {
        throw 'Host file manifest did not preserve the complete unique multi-file set.'
    }

    foreach ($entry in $fixtures.GetEnumerator()) {
        $relativePath = $entry.Key.Replace('\', '/')
        $actual = @($files | Where-Object path -eq $relativePath)
        if ($actual.Count -ne 1) {
            throw "Host file manifest is missing exact path: $relativePath"
        }

        $sourcePath = Join-Path $hostRoot $entry.Key
        $expectedHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual[0].size -ne ([IO.FileInfo]$sourcePath).Length -or
            $actual[0].sha256 -ne $expectedHash -or
            $actual[0].component -ne 'Host' -or
            $actual[0].version -ne '2.0.16') {
            throw "Host file facts are invalid for: $relativePath"
        }
    }

    Write-Host 'Edge Host multi-file manifest behavior passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
