param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [ValidateSet('AP', 'CP')]
    [string[]]$ExpectedModuleId = @('CP'),

    [hashtable]$ExpectedPluginVersions = @{},

    [hashtable]$ExpectedPluginDirectories = @{},

    [string]$ExpectedGateway,

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
    $matches = @($Archive.Entries |
        Where-Object { $_.FullName.Replace('\', '/') -ceq $normalized })
    if ($matches.Count -gt 1) {
        throw "Installer payload contains duplicate entry: $normalized"
    }
    return $matches | Select-Object -First 1
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

function Get-NormalizedExpectedModuleIds {
    param([Parameter(Mandatory = $true)][string[]]$ModuleIds)

    $moduleIdSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($moduleId in @($ModuleIds)) {
        $normalized = ([string]$moduleId).Trim()
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            throw 'ExpectedModuleId must not contain an empty module id.'
        }
        if (-not $moduleIdSet.Add($normalized)) {
            throw "ExpectedModuleId contains duplicate module '$normalized'."
        }
    }
    if ($moduleIdSet.Count -eq 0) {
        throw 'ExpectedModuleId must contain at least one module.'
    }

    [string[]]$result = @($moduleIdSet)
    [Array]::Sort($result, [StringComparer]::OrdinalIgnoreCase)
    return ,$result
}

function Get-ExpectedMapValue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][int]$ExpectedCount
    )

    if ($Map.Count -eq 0) {
        return $null
    }
    if ($Map.Count -ne $ExpectedCount) {
        throw "$Label must contain exactly one entry for every expected module."
    }

    $matchingKeys = @($Map.Keys | Where-Object {
            [string]::Equals([string]$_, $ModuleId, [StringComparison]::OrdinalIgnoreCase)
        })
    if ($matchingKeys.Count -ne 1) {
        throw "$Label does not contain exactly one entry for module '$ModuleId'."
    }
    $value = ([string]$Map[$matchingKeys[0]]).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Label module '$ModuleId' has an empty value."
    }
    return $value
}

function Assert-ExactModuleSet {
    param(
        [Parameter(Mandatory = $true)][object[]]$Items,
        [Parameter(Mandatory = $true)][string[]]$ExpectedModuleIds,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $actualModuleIds = @($Items | ForEach-Object { ([string]$_.moduleId).Trim() })
    if ($actualModuleIds.Count -ne $ExpectedModuleIds.Count) {
        throw "$Label module count '$($actualModuleIds.Count)' does not match expected '$($ExpectedModuleIds.Count)'."
    }
    foreach ($expectedModuleId in $ExpectedModuleIds) {
        if (@($actualModuleIds | Where-Object {
                    [string]::Equals($_, $expectedModuleId, [StringComparison]::OrdinalIgnoreCase)
                }).Count -ne 1) {
            throw "$Label does not contain exactly one expected module '$expectedModuleId'."
        }
    }
}

function Assert-BindingPayload {
    param(
        [Parameter(Mandatory = $true)][string]$BindingJson,
        [Parameter(Mandatory = $true)][string[]]$ExpectedModuleIds,
        [string]$ExpectedGateway
    )

    $binding = $BindingJson | ConvertFrom-Json
    if ($binding.schemaVersion -ne 2) {
        throw 'iiot-binding.json schemaVersion must be 2.'
    }

    $baseUrl = ([string]$binding.baseUrl).Trim().TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($baseUrl)) {
        throw 'iiot-binding.json baseUrl must not be empty.'
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedGateway) -and
        $baseUrl -cne $ExpectedGateway.Trim().TrimEnd('/')) {
        throw 'iiot-binding.json baseUrl does not match the expected Cloud Gateway.'
    }

    $expectedPaths = [ordered]@{
        deviceInstance = '/api/v1/bootstrap/device-instance'
        clientReleaseCatalogTemplate = '/api/v1/edge/client-releases/device/{deviceId}/catalog'
        clientVersionReport = '/api/v1/edge/client-releases/version-reports'
        runtimeHeartbeat = '/api/v1/edge/runtime-heartbeats'
    }
    if ($null -eq $binding.paths) {
        throw 'iiot-binding.json paths must not be empty.'
    }
    foreach ($propertyName in $expectedPaths.Keys) {
        $actualPath = [string]$binding.paths.$propertyName
        if ($actualPath -cne [string]$expectedPaths[$propertyName]) {
            throw "iiot-binding.json path '$propertyName' is invalid."
        }
    }

    $generatedAtUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string]$binding.generatedAtUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$generatedAtUtc)) {
        throw 'iiot-binding.json generatedAtUtc is invalid.'
    }

    $items = @($binding.bindings)
    if ($items.Count -eq 0) {
        throw 'iiot-binding.json does not contain any bindings.'
    }

    $moduleIdSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $items) {
        $moduleId = ([string]$item.moduleId).Trim()
        if ([string]::IsNullOrWhiteSpace($moduleId) -or -not $moduleIdSet.Add($moduleId)) {
            throw 'iiot-binding.json contains an empty or duplicate moduleId.'
        }
        foreach ($propertyName in @('clientCode', 'bootstrapSecret', 'deviceName', 'processId')) {
            $value = [string]$item.$propertyName
            if ([string]::IsNullOrWhiteSpace($value)) {
                throw "iiot-binding.json module '$moduleId' has empty '$propertyName'."
            }
        }
    }
    Assert-ExactModuleSet `
        -Items $items `
        -ExpectedModuleIds $ExpectedModuleIds `
        -Label 'iiot-binding.json'

    return [pscustomobject]@{
        BaseUrl = $baseUrl
        Paths = $binding.paths
        Bindings = $items
        BootstrapSecrets = @($items | ForEach-Object { [string]$_.bootstrapSecret })
    }
}

function Assert-HostPluginPayload {
    param(
        [Parameter(Mandatory = $true)][string]$HostPluginJson,
        [Parameter(Mandatory = $true)]$BindingBundle,
        [Parameter(Mandatory = $true)][string[]]$ExpectedModuleIds,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedVersions,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedDirectories
    )

    $hostPlugins = $HostPluginJson | ConvertFrom-Json
    if ($hostPlugins.schemaVersion -ne 1) {
        throw 'iiot-enabled-plugins.json schemaVersion is invalid.'
    }

    $plugins = @($hostPlugins.plugins)
    $moduleIdSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $directorySet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($plugin in $plugins) {
        $moduleId = ([string]$plugin.moduleId).Trim()
        $pluginDirectory = ([string]$plugin.pluginDirectory).Trim()
        if ([string]::IsNullOrWhiteSpace($moduleId) -or -not $moduleIdSet.Add($moduleId)) {
            throw 'iiot-enabled-plugins.json contains an empty or duplicate moduleId.'
        }
        if ([string]::IsNullOrWhiteSpace($pluginDirectory) -or
            $pluginDirectory -in @('.', '..') -or
            $pluginDirectory.Contains('/') -or
            $pluginDirectory.Contains('\') -or
            -not $directorySet.Add($pluginDirectory)) {
            throw "iiot-enabled-plugins.json module '$moduleId' has an unsafe or duplicate pluginDirectory."
        }
        if ([string]::IsNullOrWhiteSpace([string]$plugin.version)) {
            throw "iiot-enabled-plugins.json module '$moduleId' has an empty version."
        }

        $bindingMatches = @($BindingBundle.Bindings | Where-Object {
                [string]::Equals(
                    [string]$_.moduleId,
                    $moduleId,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($bindingMatches.Count -ne 1) {
            throw "iiot-enabled-plugins.json module '$moduleId' does not have exactly one iiot-binding.json entry."
        }
        $binding = $bindingMatches[0]
        foreach ($propertyName in @('clientCode', 'deviceName', 'processId')) {
            if ([string]$plugin.$propertyName -cne [string]$binding.$propertyName) {
                throw "iiot-enabled-plugins.json module '$moduleId' property '$propertyName' does not match iiot-binding.json."
            }
        }

        $expectedVersion = Get-ExpectedMapValue `
            -Map $ExpectedVersions `
            -ModuleId $moduleId `
            -Label 'ExpectedPluginVersions' `
            -ExpectedCount $ExpectedModuleIds.Count
        if ($null -ne $expectedVersion -and [string]$plugin.version -cne $expectedVersion) {
            throw "iiot-enabled-plugins.json module '$moduleId' version does not match ExpectedPluginVersions."
        }

        $expectedDirectory = Get-ExpectedMapValue `
            -Map $ExpectedDirectories `
            -ModuleId $moduleId `
            -Label 'ExpectedPluginDirectories' `
            -ExpectedCount $ExpectedModuleIds.Count
        if ($null -ne $expectedDirectory -and $pluginDirectory -cne $expectedDirectory) {
            throw "iiot-enabled-plugins.json module '$moduleId' pluginDirectory does not match ExpectedPluginDirectories."
        }
    }

    Assert-ExactModuleSet `
        -Items $plugins `
        -ExpectedModuleIds $ExpectedModuleIds `
        -Label 'iiot-enabled-plugins.json'
    return $plugins
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

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTargetRuntime) -and $updateConfig.targetRuntime -cne $ExpectedTargetRuntime) {
        throw "launcher.update.json targetRuntime '$($updateConfig.targetRuntime)' does not match expected '$ExpectedTargetRuntime'."
    }
    return $updateConfig
}

function Assert-PluginManifestPayloads {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][object[]]$Plugins,
        [Parameter(Mandatory = $true)][string]$PluginsRoot
    )

    $normalizedRoot = $PluginsRoot.Replace('\', '/').Trim('/')
    $payloadManifestEntries = @($Archive.Entries | Where-Object {
            $entryName = $_.FullName.Replace('\', '/')
            if (-not $entryName.StartsWith("$normalizedRoot/", [StringComparison]::Ordinal)) {
                return $false
            }
            $relative = $entryName.Substring($normalizedRoot.Length + 1)
            $segments = $relative.Split('/')
            return $segments.Count -eq 2 -and $segments[1] -ceq 'plugin.json'
        })
    if ($payloadManifestEntries.Count -ne $Plugins.Count) {
        throw 'Installer payload plugin.json count does not match iiot-enabled-plugins.json.'
    }

    foreach ($plugin in $Plugins) {
        $moduleId = [string]$plugin.moduleId
        $pluginDirectory = [string]$plugin.pluginDirectory
        $entryName = "$normalizedRoot/$pluginDirectory/plugin.json"
        $pluginManifest = (Read-ZipEntryText -Archive $Archive -EntryName $entryName) |
            ConvertFrom-Json
        if ([string]$pluginManifest.moduleId -cne $moduleId) {
            throw "plugin.json in directory '$pluginDirectory' does not match selected module '$moduleId'."
        }
        if ([string]$pluginManifest.version -cne [string]$plugin.version) {
            throw "plugin.json version for module '$moduleId' does not match iiot-enabled-plugins.json."
        }
    }
}

function Assert-NoLegacyPluginBindingEntries {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive)

    $legacyEntries = @($Archive.Entries | Where-Object {
            [IO.Path]::GetFileName($_.FullName.Replace('\', '/')) -ieq 'iiot-plugin-binding.json'
        })
    if ($legacyEntries.Count -ne 0) {
        throw "Installer payload must contain zero iiot-plugin-binding.json entries; found $($legacyEntries.Count)."
    }
}

function Assert-TextContainsNoBootstrapSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$BootstrapSecrets
    )

    if ($Text -match '(?i)"bootstrapSecret"\s*:') {
        throw "$Label must not declare bootstrapSecret."
    }
    foreach ($secret in $BootstrapSecrets) {
        if ($Text.Contains($secret, [StringComparison]::Ordinal)) {
            throw "$Label contains a bootstrap secret from iiot-binding.json."
        }
    }
}

function Test-StreamContainsByteSequence {
    param(
        [Parameter(Mandatory = $true)][System.IO.Stream]$Stream,
        [Parameter(Mandatory = $true)][byte[]]$Needle
    )

    if ($Needle.Length -eq 0) {
        return $false
    }

    $chunkSize = 64KB
    $buffer = [byte[]]::new($chunkSize + $Needle.Length - 1)
    $carryLength = 0
    while ($true) {
        $read = $Stream.Read($buffer, $carryLength, $chunkSize)
        if ($read -le 0) {
            return $false
        }

        $available = $carryLength + $read
        $lastStart = $available - $Needle.Length
        for ($start = 0; $start -le $lastStart; $start++) {
            $matches = $true
            for ($offset = 0; $offset -lt $Needle.Length; $offset++) {
                if ($buffer[$start + $offset] -ne $Needle[$offset]) {
                    $matches = $false
                    break
                }
            }
            if ($matches) {
                return $true
            }
        }

        $carryLength = [Math]::Min($Needle.Length - 1, $available)
        if ($carryLength -gt 0) {
            [Array]::Copy(
                $buffer,
                $available - $carryLength,
                $buffer,
                0,
                $carryLength)
        }
    }
}

function Assert-PluginEntriesContainNoBootstrapSecret {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$PluginsRoot,
        [Parameter(Mandatory = $true)][string[]]$BootstrapSecrets
    )

    $normalizedRoot = $PluginsRoot.Replace('\', '/').Trim('/')
    $textExtensions = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @('.json', '.config', '.xml', '.txt', '.yaml', '.yml', '.toml', '.ini', '.props', '.targets')) {
        [void]$textExtensions.Add($extension)
    }

    foreach ($entry in $Archive.Entries) {
        $entryName = $entry.FullName.Replace('\', '/')
        if (-not $entryName.StartsWith("$normalizedRoot/", [StringComparison]::Ordinal)) {
            continue
        }

        foreach ($secret in $BootstrapSecrets) {
            $entryStream = $entry.Open()
            try {
                if (Test-StreamContainsByteSequence `
                        -Stream $entryStream `
                        -Needle ([Text.Encoding]::UTF8.GetBytes($secret))) {
                    throw "Plugin payload entry '$entryName' contains a bootstrap secret from iiot-binding.json."
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }

        if ($textExtensions.Contains([IO.Path]::GetExtension($entryName))) {
            $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
            try {
                $text = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            Assert-TextContainsNoBootstrapSecret `
                -Text $text `
                -Label "Plugin payload entry '$entryName'" `
                -BootstrapSecrets $BootstrapSecrets
        }
    }
}

$resolvedInstallerPath = Resolve-TestPath -PathValue $InstallerPath
if (-not (Test-Path $resolvedInstallerPath)) {
    throw "Installer package was not found: $resolvedInstallerPath"
}
$expectedModuleIds = Get-NormalizedExpectedModuleIds -ModuleIds $ExpectedModuleId

$payload = Read-AppendedPayload -PathValue $resolvedInstallerPath
$payloadStream = [System.IO.MemoryStream]::new($payload, $false)
$archive = [System.IO.Compression.ZipArchive]::new(
    $payloadStream,
    [System.IO.Compression.ZipArchiveMode]::Read,
    $true)
try {
    Assert-ZipEntriesSafe -Archive $archive
    Assert-NoLegacyPluginBindingEntries -Archive $archive
    $velopackSetupEntry = $archive.Entries |
        Where-Object {
            $_.FullName -match '(^|/)velopack/.+Setup\.exe$' -or
            $_.FullName -match '^[^/]+Setup\.exe$'
        } |
        Select-Object -First 1
    if ($null -eq $velopackSetupEntry) {
        throw 'Installer payload does not contain the required Velopack Setup executable.'
    }

    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/iiot-binding.json'
    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/launcher.update.json'
    Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedHostDirectory/IIoT.Edge.Shell.dll"

    $bindingJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/iiot-binding.json'
    $bindingBundle = Assert-BindingPayload `
        -BindingJson $bindingJson `
        -ExpectedModuleIds $expectedModuleIds `
        -ExpectedGateway $ExpectedGateway

    $updateConfigJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/launcher.update.json'
    $updateConfig = Assert-UpdateConfigPayload `
        -UpdateConfigJson $updateConfigJson `
        -ExpectedSource $ExpectedUpdateSource `
        -ExpectedChannel $ExpectedChannel `
        -ExpectedTargetRuntime $ExpectedTargetRuntime

    $expectedSourceFromGateway = (
        "$($bindingBundle.BaseUrl)/edge-updates/velopack/$([string]$updateConfig.channel)/")
    if ([string]$updateConfig.source -cne $expectedSourceFromGateway) {
        throw 'launcher.update.json source is not consistent with iiot-binding.json baseUrl and channel.'
    }

    $hostPluginJson = Read-ZipEntryText -Archive $archive -EntryName 'launcher/iiot-enabled-plugins.json'
    Assert-TextContainsNoBootstrapSecret `
        -Text $hostPluginJson `
        -Label 'iiot-enabled-plugins.json' `
        -BootstrapSecrets $bindingBundle.BootstrapSecrets
    Assert-TextContainsNoBootstrapSecret `
        -Text $updateConfigJson `
        -Label 'launcher.update.json' `
        -BootstrapSecrets $bindingBundle.BootstrapSecrets

    $plugins = Assert-HostPluginPayload `
        -HostPluginJson $hostPluginJson `
        -BindingBundle $bindingBundle `
        -ExpectedModuleIds $expectedModuleIds `
        -ExpectedVersions $ExpectedPluginVersions `
        -ExpectedDirectories $ExpectedPluginDirectories
    Assert-PluginManifestPayloads `
        -Archive $archive `
        -Plugins $plugins `
        -PluginsRoot $ExpectedPluginsRoot
    Assert-PluginEntriesContainNoBootstrapSecret `
        -Archive $archive `
        -PluginsRoot $ExpectedPluginsRoot `
        -BootstrapSecrets $bindingBundle.BootstrapSecrets

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
