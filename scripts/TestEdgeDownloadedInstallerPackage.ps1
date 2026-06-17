param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [string]$ExpectedModuleId = 'Homogenization',

    [string]$ExpectedUpdateSource,

    [string]$ExpectedChannel,

    [string]$ExpectedTargetRuntime,

    [string]$ExpectedHostDirectory = 'host',

    [string]$ExpectedPluginsRoot = 'plugins',

    [switch]$ExtractPayload,

    [string]$PayloadOutputDirectory
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$magic = [System.Text.Encoding]::ASCII.GetBytes('IIOTEDG1')
$trailerLength = 16

function Resolve-TestPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return [System.IO.Path]::GetFullPath($PathValue)
}

function Read-ExactBytes {
    param(
        [Parameter(Mandatory = $true)][System.IO.Stream]$Stream,
        [Parameter(Mandatory = $true)][byte[]]$Buffer,
        [Parameter(Mandatory = $true)][int]$Count
    )

    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($Buffer, $offset, $Count - $offset)
        if ($read -le 0) {
            throw 'Unexpected end of installer file.'
        }
        $offset += $read
    }
}

function Read-AppendedPayload {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $stream = [System.IO.File]::OpenRead($PathValue)
    try {
        if ($stream.Length -lt $trailerLength) {
            throw 'Installer file is too small to contain an appended payload trailer.'
        }

        $trailer = [byte[]]::new($trailerLength)
        [void]$stream.Seek(-$trailerLength, [System.IO.SeekOrigin]::End)
        Read-ExactBytes -Stream $stream -Buffer $trailer -Count $trailerLength

        for ($i = 0; $i -lt $magic.Length; $i++) {
            if ($trailer[8 + $i] -ne $magic[$i]) {
                throw 'Installer does not contain the IIoT appended payload marker. This is probably the empty setup stub, not the Cloud-generated installer package.'
            }
        }

        $lengthBytes = [byte[]]::new(8)
        [Array]::Copy($trailer, 0, $lengthBytes, 0, 8)
        if (-not [BitConverter]::IsLittleEndian) {
            [Array]::Reverse($lengthBytes)
        }

        $payloadLength = [BitConverter]::ToInt64($lengthBytes, 0)
        if ($payloadLength -le 0 -or $payloadLength -gt ($stream.Length - $trailerLength)) {
            throw "Invalid appended payload length: $payloadLength."
        }

        if ($payloadLength -gt [int]::MaxValue) {
            throw "Appended payload is too large for this verifier: $payloadLength bytes."
        }

        $payload = [byte[]]::new([int]$payloadLength)
        [void]$stream.Seek(-($trailerLength + $payloadLength), [System.IO.SeekOrigin]::End)
        Read-ExactBytes -Stream $stream -Buffer $payload -Count ([int]$payloadLength)
        return $payload
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ZipEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    $normalized = $EntryName.Replace('\', '/')
    return $Archive.Entries |
        Where-Object { $_.FullName -eq $normalized } |
        Select-Object -First 1
}

function Assert-ZipEntryExists {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    if ($null -eq (Get-ZipEntry -Archive $Archive -EntryName $EntryName)) {
        throw "Required installer payload entry was not found: $EntryName"
    }
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    $entry = Get-ZipEntry -Archive $Archive -EntryName $EntryName
    if ($null -eq $entry) {
        throw "Required installer payload entry was not found: $EntryName"
    }

    $reader = [System.IO.StreamReader]::new($entry.Open(), [System.Text.Encoding]::UTF8)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Assert-ZipEntriesSafe {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive)

    foreach ($entry in $Archive.Entries) {
        $normalized = $entry.FullName.Replace('\', '/')
        if ($normalized.StartsWith('/') -or ($normalized.Split('/') | Where-Object { $_ -eq '..' -or $_ -eq '.' })) {
            throw "Unsafe installer payload zip entry was found: $($entry.FullName)"
        }
    }
}

function Assert-BindingPayload {
    param(
        [Parameter(Mandatory = $true)][string]$BindingJson,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $binding = $BindingJson | ConvertFrom-Json
    if ($binding.schemaVersion -lt 1) {
        throw 'iiot-binding.json schemaVersion is invalid.'
    }

    $items = @($binding.bindings)
    if ($items.Count -eq 0) {
        throw 'iiot-binding.json does not contain any bindings.'
    }

    $match = @($items | Where-Object { $_.moduleId -eq $ModuleId }) | Select-Object -First 1
    if ($null -eq $match) {
        throw "iiot-binding.json does not contain module '$ModuleId'."
    }

    foreach ($propertyName in @('clientCode', 'bootstrapSecret', 'deviceName', 'processId')) {
        $value = [string]$match.$propertyName
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "iiot-binding.json module '$ModuleId' has empty '$propertyName'."
        }
    }

    return $match
}

function Assert-HostPluginPayload {
    param(
        [Parameter(Mandatory = $true)][string]$HostPluginJson,
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [Parameter(Mandatory = $true)][string]$PluginDirectory,
        [Parameter(Mandatory = $true)][string]$ClientCode
    )

    $hostPlugins = $HostPluginJson | ConvertFrom-Json
    if ($hostPlugins.schemaVersion -lt 1) {
        throw 'iiot-enabled-plugins.json schemaVersion is invalid.'
    }

    $match = @($hostPlugins.plugins | Where-Object { $_.moduleId -eq $ModuleId }) | Select-Object -First 1
    if ($null -eq $match) {
        throw "iiot-enabled-plugins.json does not contain module '$ModuleId'."
    }

    if ($match.pluginDirectory -ne $PluginDirectory) {
        throw "iiot-enabled-plugins.json module '$ModuleId' pluginDirectory '$($match.pluginDirectory)' does not match '$PluginDirectory'."
    }

    if ($match.clientCode -ne $ClientCode) {
        throw "iiot-enabled-plugins.json module '$ModuleId' clientCode does not match iiot-binding.json."
    }
}

function Assert-UpdateConfigPayload {
    param(
        [Parameter(Mandatory = $true)][string]$UpdateConfigJson,
        [string]$ExpectedSource,
        [string]$ExpectedChannel,
        [string]$ExpectedTargetRuntime
    )

    $updateConfig = $UpdateConfigJson | ConvertFrom-Json
    $propertyNames = @($updateConfig.PSObject.Properties.Name)
    foreach ($propertyName in @('source', 'channel', 'targetRuntime')) {
        if (-not ($propertyNames -ccontains $propertyName)) {
            throw "launcher.update.json must contain camelCase property '$propertyName'."
        }

        $value = [string]$updateConfig.$propertyName
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "launcher.update.json property '$propertyName' must not be empty."
        }
    }

    foreach ($legacyName in @('Source', 'Channel', 'TargetRuntime')) {
        if ($propertyNames -ccontains $legacyName) {
            throw "launcher.update.json must use camelCase, but found legacy property '$legacyName'."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSource) -and $updateConfig.source -ne $ExpectedSource) {
        throw "launcher.update.json source '$($updateConfig.source)' does not match expected '$ExpectedSource'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedChannel) -and $updateConfig.channel -ne $ExpectedChannel) {
        throw "launcher.update.json channel '$($updateConfig.channel)' does not match expected '$ExpectedChannel'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTargetRuntime) -and $updateConfig.targetRuntime -ne $ExpectedTargetRuntime) {
        throw "launcher.update.json targetRuntime '$($updateConfig.targetRuntime)' does not match expected '$ExpectedTargetRuntime'."
    }
}

function Assert-PluginBindingPayload {
    param(
        [Parameter(Mandatory = $true)][string]$PluginBindingJson,
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [Parameter(Mandatory = $true)][string]$ClientCode,
        [Parameter(Mandatory = $true)][string]$BootstrapSecret
    )

    $pluginBinding = $PluginBindingJson | ConvertFrom-Json
    if ($pluginBinding.schemaVersion -lt 1) {
        throw 'iiot-plugin-binding.json schemaVersion is invalid.'
    }

    if ($pluginBinding.moduleId -ne $ModuleId) {
        throw "iiot-plugin-binding.json moduleId '$($pluginBinding.moduleId)' does not match '$ModuleId'."
    }

    if ($pluginBinding.clientCode -ne $ClientCode) {
        throw "iiot-plugin-binding.json clientCode does not match iiot-binding.json."
    }

    if ($pluginBinding.bootstrapSecret -ne $BootstrapSecret) {
        throw "iiot-plugin-binding.json bootstrapSecret does not match iiot-binding.json."
    }
}

$resolvedInstallerPath = Resolve-TestPath -PathValue $InstallerPath
if (-not (Test-Path $resolvedInstallerPath)) {
    throw "Installer package was not found: $resolvedInstallerPath"
}

$payload = Read-AppendedPayload -PathValue $resolvedInstallerPath
$payloadStream = [System.IO.MemoryStream]::new($payload, $false)
$archive = [System.IO.Compression.ZipArchive]::new(
    $payloadStream,
    [System.IO.Compression.ZipArchiveMode]::Read,
    $true)
try {
    Assert-ZipEntriesSafe -Archive $archive
    $velopackSetupEntry = $archive.Entries |
        Where-Object {
            $_.FullName -match '(^|/)velopack/.+Setup\.exe$' -or
            $_.FullName -match '^[^/]+Setup\.exe$'
        } |
        Select-Object -First 1

    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/iiot-binding.json'
    $bindingJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/iiot-binding.json'
    $bindingItem = Assert-BindingPayload -BindingJson $bindingJson -ModuleId $ExpectedModuleId
    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/launcher.update.json'
    $updateConfigJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/launcher.update.json'
    Assert-UpdateConfigPayload `
        -UpdateConfigJson $updateConfigJson `
        -ExpectedSource $ExpectedUpdateSource `
        -ExpectedChannel $ExpectedChannel `
        -ExpectedTargetRuntime $ExpectedTargetRuntime

    if ($null -ne $velopackSetupEntry) {
        Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
        Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedPluginsRoot/$ExpectedModuleId/plugin.json"

        $hostPluginJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
        Assert-HostPluginPayload `
            -HostPluginJson $hostPluginJson `
            -ModuleId $ExpectedModuleId `
            -PluginDirectory $ExpectedModuleId `
            -ClientCode $bindingItem.clientCode
    }
    else {
        Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/IIoT.Edge.Launcher.exe'
        Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
        Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedHostDirectory/IIoT.Edge.Shell.dll"
        Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedPluginsRoot/$ExpectedModuleId/plugin.json"
        Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedPluginsRoot/$ExpectedModuleId/iiot-plugin-binding.json"

        $hostPluginJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
        Assert-HostPluginPayload `
            -HostPluginJson $hostPluginJson `
            -ModuleId $ExpectedModuleId `
            -PluginDirectory $ExpectedModuleId `
            -ClientCode $bindingItem.clientCode

        $pluginBindingJson = Read-ZipEntryText -Archive $archive -EntryName "$ExpectedPluginsRoot/$ExpectedModuleId/iiot-plugin-binding.json"
        Assert-PluginBindingPayload `
            -PluginBindingJson $pluginBindingJson `
            -ModuleId $ExpectedModuleId `
            -ClientCode $bindingItem.clientCode `
            -BootstrapSecret $bindingItem.bootstrapSecret
    }

    if ($ExtractPayload) {
        if ([string]::IsNullOrWhiteSpace($PayloadOutputDirectory)) {
            throw 'PayloadOutputDirectory is required when ExtractPayload is set.'
        }

        $resolvedPayloadOutputDirectory = Resolve-TestPath -PathValue $PayloadOutputDirectory
        if (Test-Path $resolvedPayloadOutputDirectory) {
            Remove-Item -Path $resolvedPayloadOutputDirectory -Recurse -Force
        }

        $tempZipPath = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(),
            "iiot-edge-installer-payload-$([Guid]::NewGuid().ToString('N')).zip")
        try {
            [System.IO.File]::WriteAllBytes($tempZipPath, $payload)
            [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZipPath, $resolvedPayloadOutputDirectory)
            Write-Host "Extracted installer payload to: $resolvedPayloadOutputDirectory"
        }
        finally {
            if (Test-Path $tempZipPath) {
                Remove-Item -Path $tempZipPath -Force
            }
        }
    }
}
finally {
    $archive.Dispose()
    $payloadStream.Dispose()
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $resolvedInstallerPath).Hash.ToLowerInvariant()
Write-Host "Edge downloaded installer package verification passed: $resolvedInstallerPath"
Write-Host "sha256=$hash"
