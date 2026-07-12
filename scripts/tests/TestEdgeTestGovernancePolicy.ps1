[CmdletBinding()]
param(
    [ValidateSet('ValidateProject', 'ValidateRepository', 'ValidateSnapshot', 'ValidateRepositorySnapshot', 'ValidateStatic', 'ValidateDiscovery', 'ValidateRunnerConfiguration', 'ValidateRunnerCaseNormalization', 'ValidateBaselineAnchor', 'GenerateBaseline')]
    [string]$Mode = 'ValidateProject',
    [string]$RepositoryRoot,
    [string]$ProjectPath,
    [string]$ProjectName,
    [string]$AssemblyPath,
    [string]$ReferencePathsFile,
    [string]$RunnerConfigPath,
    [string]$CurrentSnapshotPath,
    [string]$TrustedBaseRevision,
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [string]$BaselinePath,
    [string]$WaiverPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$AllowBaselineWrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ruleId = 'EDGE-TEST-GOV-001'
$baselineSchemaVersion = '1.0'
$waiverSchemaVersion = '1.0'
$maximumWaiverDays = 30
$approvedWaiverApprovers = @('ShuJinHao')
$allowedTestKinds = @('Architecture', 'Unit', 'Aggregate', 'Application', 'Contract', 'Conformance', 'Persistence', 'Workflow', 'Integration', 'EndToEnd', 'UI', 'GoldenEval', 'Deployment', 'Performance', 'SoakChaos', 'Security')
$allowedRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'Avalonia', 'Browser', 'Windows', 'LiveExternal')
$allowedRisks = @('P0', 'P1', 'P2')
$allowedOwners = @('Edge.Architecture', 'Edge.Core', 'Edge.Application', 'Edge.Persistence', 'Edge.PLC', 'Edge.MES', 'Edge.Cloud', 'Edge.Modules', 'Edge.Shell', 'Edge.UI', 'Edge.Deployment', 'Edge.Security', 'Edge.Tests')
$allowedCapabilities = @('Architecture', 'Authentication', 'Authorization', 'Launcher', 'Installer', 'Shell', 'UI.Shared', 'Update', 'Modules', 'PLC', 'MES', 'Cloud', 'DataPipeline', 'Persistence', 'Deployment', 'Startup', 'Diagnostics', 'Configuration', 'Recipes', 'Capacity', 'Device', 'Logging', 'Runtime', 'TestGovernance')
$allowedTestAttributeTypes = @('Xunit.FactAttribute', 'Xunit.TheoryAttribute', 'Avalonia.Headless.XUnit.AvaloniaFactAttribute', 'Avalonia.Headless.XUnit.AvaloniaTheoryAttribute')
$allowedProjectSdkIdentities = @('Microsoft.NET.Sdk')
$nonUiFrozenSourceManifestSha256 = '54baa3fe6565ca416b4d97e0463496837e4dfd8d6265275b8ca609d9facadbd5'
$xunitRunnerConfigSha256 = '3aaf68ea8927dce2c9ee5404088745084d709c1ff2d00bf41c90d9406d31b8a1'
$canonicalTestBuildPropsSha256 = '189a12b9770d01f3b2221675ea6b879ad36c06074bc281f1d2bc52ba662eef63'
$rootBuildPropsSha256 = '4ee0e7d684211afc1a20ba62d57cfdb29f11341f2ef29940b987f4a798128569'
$rootBuildTargetsSha256 = '14cde139656d4b5910d1e96b9e69adca3a7b1aae2cccebd7328500815f9505f8'
$directoryPackagesPropsSha256 = '60f4ae24b34c2d8061a87c36f2d957976de1495bfb2eae2896f6bf6124a9d548'
$nugetConfigSha256 = 'aec93b5637ab6d62470979a008990a4a336a287ab83a3f2541849ae735674055'
$packageVulnerabilityScriptSha256 = 'fbfe7f05db5d465478744ce168198a8d2cae44e94c033a8172b2e8724c2f92a7'
$baselineRepositoryPath = 'scripts/tests/baselines/edge-test-governance.baseline.json'
$approvedMetadataLoadContextSha256ByPlatform = @{
    'macOS-Arm64' = 'd5aeb7ae95e463315d722fb7f22679658b62959c2d6dc0f5f1be9e45f0cb9c39'
    'Windows-X64' = '8e6af791299bb85c94a3fc3eef2b55621a30ca77025d062944f3578e10057ddb'
}
$codeOwnersSha256 = '29b8fd2df7429c6e5f0919e9fc78ea08fce83d7859c7c88ada51d17b38a3f41a'
$gitAttributesSha256 = '24418a20248958b24e4a29b013220a2cdf02d51b897e25455bf4c8c468e3ac46'
$solutionFileSha256 = 'a2eb9a0532c63efb9ebe013eb93e7566b342917fd1792d69cee47af00e54dcc1'
$globalJsonSha256 = '18303059fe920620f05e25d0157b7ed4a74934841e6a34b0b86d713fbf631444'
$governanceBehaviorSha256 = 'dc89fcbdca2e76dec8c27e2e03ba0104a5a29fd1910d1753e2b5316ac2d7225d'
$repositoryProjectRosterCount = 32
$repositoryProjectRosterSha256 = '82f2e2ee50ea7d555304cd80c5d0c97585d03bda9c3227ff65caeed9a21d86ce'
$repositoryProjectContentManifestSha256 = '0d74d4045b053e17a7e3e455b528197047bd73a41a502119831320efda76d24c'
$testProjectAssetManifestCount = 7
$testProjectAssetManifestSha256 = 'd5ac65b6d2f948858cd7dc4a395647dce1793d9dce1edd7ef36bdcac071f5972'
$buildFileManifestCount = 4
$buildFileManifestSha256 = '84e955382a229aa576cebdfa9e266021eb718cb553eff0929f858d3fa22e1fdd'
$workflowManifestCount = 2
$workflowManifestSha256 = '44f4b96362bf357c1f643c0a463ee160f3d1596731c3d871dc2bfa7e1814b718'
$criticalTestSourceManifestCount = 13
$criticalTestSourceManifestSha256 = '155eebf06c3ccd07fb9e0a49b76e87b17d08d29cc16feeb9616b5e8bbfa9d853'
$nonUiFrozenSourceContentManifestCount = 71
$nonUiFrozenSourceContentManifestSha256 = 'ad9cb1c27bdff493972756b109ce1f5c24b5aec765fa83e597b0fce7079e8336'
$allTestSourceManifestCount = 132
$allTestSourceManifestSha256 = '44e705217f4b9100a82be3238316b75ab4d88685fb76ef609128a45256e9f11b'
$criticalTestSourcePaths = @(
    'src/Tests/IIoT.Edge.Shell.Tests/RepositoryHygieneTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ArchitectureBoundaryContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ContractTestPathHelper.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/DieCuttingModuleContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/GlobalUsings.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/HomogenizationModuleContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ModuleContractFixture.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ModuleContractTestBase.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ModuleDataPipelineEnqueueResultMapperContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ModuleDiscoveryContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/ModuleUploadDiagnosticsRecorderContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/PluginCatalogLifecycleContractTests.cs',
    'src/Tests/IIoT.Edge.Module.ContractTests/PluginCommentContractTests.cs'
)
$requiredWorkflowSha256 = @{
    '.github/workflows/edge-smoke-build.yml' = 'a6e8c2e934ff5661b06cf710cc8de3476af98af972970ffcdda0e427ce4c5908'
    '.github/workflows/edge-pack-modules.yml' = 'b739534e1dde66021d37dc062e103eb7223a0c5a7265e431fbdd469f555310fb'
}
$requiredWorkflowJobSha256 = @{
    '.github/workflows/edge-smoke-build.yml' = '43f6a457fe7d5edb1e1101489042b863da53a98f36b08fe59bc4ba814c4df541'
    '.github/workflows/edge-pack-modules.yml' = 'f309c3ad73ac4837c7dedeb0c7591e69eaa577a8e0ed3e6c7eee9816d344c033'
}
$allowedTestProjectTargetHashes = @{
    'src/Edge/IIoT.Edge.Launcher/IIoT.Edge.Launcher.csproj' = 'f96ff44f47f79371365bbb91b63c8875458c04b0a2e87e3dbab86d9f55ce5011'
    'src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj' = 'fc1a7e68c61d988ddc73ee97b08b0335027e1f66e436e1c750fdcd22cb5efc8c'
    'src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj' = '6c2ab444f37f9b5528d64655a6e5b2617655e488201ed01ccf916866bad29b2e'
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$Path
    )
    return [System.IO.Path]::GetRelativePath($BasePath, $Path).Replace('\', '/')
}

function Get-OptionalProperty {
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object]$DefaultValue = $null
    )

    if ($null -eq $InputObject) { return $DefaultValue }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name) -and $null -ne $InputObject[$Name]) {
            return $InputObject[$Name]
        }
        return $DefaultValue
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $DefaultValue }
    return $property.Value
}

function Get-XmlElementsByLocalName {
    param(
        [Parameter(Mandatory)][xml]$Xml,
        [Parameter(Mandatory)][string[]]$Names
    )

    return [object[]]@($Xml.SelectNodes('//*') | Where-Object {
        $localName = [string]$_.LocalName
        @($Names | Where-Object { $localName.Equals($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    })
}

function Get-XmlAttributeValue {
    param(
        [Parameter(Mandatory)][object]$Node,
        [Parameter(Mandatory)][string]$Name
    )

    $attribute = @($Node.Attributes | Where-Object { $_.LocalName.Equals($Name, [StringComparison]::OrdinalIgnoreCase) })
    if ($attribute.Count -ne 1) { return '' }
    return [string]$attribute[0].Value
}

function Add-PolicyError {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )
    $Errors.Add("$Code $Message")
}

function Assert-NoPolicyErrors {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors)
    if ($Errors.Count -gt 0) {
        throw ("Edge test governance failed:`n- " + ($Errors -join "`n- "))
    }
}

function Test-RunnerConfigurationFile {
    param(
        [Parameter(Mandatory)][string]$ResolvedRunnerConfigPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Context
    )

    $resolvedPath = Get-NormalizedPath $ResolvedRunnerConfigPath
    $runnerDirectory = Split-Path $resolvedPath -Parent
    $runnerConfigs = @(if (Test-Path $runnerDirectory -PathType Container) {
        Get-ChildItem -Force $runnerDirectory -File | Where-Object { $_.Name -imatch 'xunit\.runner\.json$' }
    })
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne $resolvedPath) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context must contain exactly one generic xunit.runner.json; assembly-specific runner overrides are forbidden."
        return
    }
    if (-not (Test-Path $resolvedPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context does not contain the required xunit.runner.json: $ResolvedRunnerConfigPath."
        return
    }
    if ((Get-FileHash $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $xunitRunnerConfigSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json differs from the reviewed failSkips configuration."
        return
    }
    try {
        $runnerConfiguration = Get-Content $resolvedPath -Raw | ConvertFrom-Json
        if ([bool](Get-OptionalProperty $runnerConfiguration 'failSkips' $false) -ne $true) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json must set failSkips=true."
        }
    } catch {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json is not valid JSON: $($_.Exception.Message)"
    }
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path $Path -Parent
    if (-not (Test-Path $directory)) {
        [void](New-Item $directory -ItemType Directory -Force)
    }
    $temporaryPath = Join-Path $directory ".$([System.IO.Path]::GetFileName($Path)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 100
        [System.IO.File]::WriteAllText($temporaryPath, "$json`n", [System.Text.UTF8Encoding]::new($false))
        $null = Get-Content $temporaryPath -Raw | ConvertFrom-Json -Depth 100
        Move-Item $temporaryPath $Path -Force
    } finally {
        Remove-Item $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-Sha256 {
    param([Parameter(Mandatory)][string]$Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.Normalize([Text.NormalizationForm]::FormC))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-RepositoryFiles {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][scriptblock]$Predicate
    )

    $gitRoot = if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot) -and (Test-Path (Join-Path $RepositoryRoot '.git'))) {
        $RepositoryRoot
    } elseif (Test-Path (Join-Path $Root '.git')) {
        $Root
    } else { $null }
    $resolvedScanRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $scanPrefix = "$resolvedScanRoot$([IO.Path]::DirectorySeparatorChar)"
    if ($null -ne $gitRoot) {
        $trackedAndVisible = Invoke-CapturedProcess -FileName 'git' `
            -Arguments @('ls-files', '-z', '--cached', '--others', '--exclude-standard') `
            -WorkingDirectory $gitRoot
        if ($trackedAndVisible.TimedOut -or $trackedAndVisible.ExitCode -ne 0) {
            throw "$ruleId-SCAN git ls-files could not enumerate repository assets: $($trackedAndVisible.StandardError.Trim())"
        }
        return [object[]]@($trackedAndVisible.StandardOutput.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { Join-Path $gitRoot $_ } |
            Where-Object { Test-Path $_ -PathType Leaf } |
            ForEach-Object { Get-Item -Force $_ } |
            Where-Object {
                $resolvedPath = [IO.Path]::GetFullPath($_.FullName)
                $resolvedPath.Equals($resolvedScanRoot, $pathComparison) -or $resolvedPath.StartsWith($scanPrefix, $pathComparison)
            } |
            Where-Object { & $Predicate $_ } |
            Sort-Object FullName)
    }

    return [object[]]@(Get-ChildItem -Force $Root -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[/\\]\.git[/\\]' -and
            (& $Predicate $_)
        } |
        Sort-Object FullName)
}

function Get-FileManifestDigest {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Files)

    $items = [string[]]@($Files | ForEach-Object {
        $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
        $fileHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "${relativePath}:${fileHash}"
    })
    [Array]::Sort($items, [StringComparer]::Ordinal)
    $material = $items -join "`n"
    return ConvertTo-Sha256 -Value $material
}

function Get-PathManifestDigest {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Files)

    $items = [string[]]@($Files | ForEach-Object {
        Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
    })
    [Array]::Sort($items, [StringComparer]::Ordinal)
    $material = $items -join "`n"
    return ConvertTo-Sha256 -Value $material
}

function Test-ExactReviewedFile {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$ExpectedSha256,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $path = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path $path -PathType Leaf) -or
        (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ExpectedSha256) {
        Add-PolicyError -Errors $Errors -Code $Code -Message "$Description differs from the exact reviewed asset: $RelativePath."
    }
}

function Get-ActiveSdkMetadataLoadContextPath {
    $activeSdk = (& dotnet --version | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($activeSdk)) {
        throw "$ruleId-SCAN dotnet --version returned no SDK version."
    }

    $sdkDirectories = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& dotnet --list-sdks)) {
        if ($line -match '^(?<version>\S+)\s+\[(?<root>.+)\]$' -and $Matches.version -eq $activeSdk) {
            $sdkDirectories.Add((Join-Path $Matches.root $Matches.version))
        }
    }
    foreach ($sdkDirectory in @($sdkDirectories | Select-Object -Last 1)) {
        $candidate = Join-Path $sdkDirectory 'System.Reflection.MetadataLoadContext.dll'
        if (Test-Path $candidate -PathType Leaf) {
            return (Get-NormalizedPath $candidate)
        }
    }
    throw "$ruleId-SCAN active SDK $activeSdk does not expose System.Reflection.MetadataLoadContext.dll."
}

function Get-MetadataResolverPaths {
    param(
        [Parameter(Mandatory)][string]$TestAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    $pathsBySimpleName = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pathsBySimpleName[[System.IO.Path]::GetFileNameWithoutExtension($TestAssemblyPath)] = $TestAssemblyPath
    foreach ($referencePath in @($AdditionalReferencePaths)) {
        if ([string]::IsNullOrWhiteSpace($referencePath) -or -not (Test-Path $referencePath -PathType Leaf)) { continue }
        $simpleName = [System.IO.Path]::GetFileNameWithoutExtension($referencePath)
        if (-not $pathsBySimpleName.ContainsKey($simpleName)) {
            $pathsBySimpleName[$simpleName] = [System.IO.Path]::GetFullPath($referencePath)
        }
    }
    $assemblyDirectory = Split-Path $TestAssemblyPath -Parent
    foreach ($file in @(Get-ChildItem $assemblyDirectory -Filter '*.dll' -File -Recurse | Sort-Object { $_.DirectoryName.Length }, FullName)) {
        if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
            $pathsBySimpleName[$file.BaseName] = $file.FullName
        }
    }

    $runtimeDirectories = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @(& dotnet --list-runtimes)) {
        if ($line -match '^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<root>.+)\]$') {
            $candidate = Join-Path $Matches.root $Matches.version
            if (Test-Path $candidate -PathType Container) {
                $parsedVersion = $null
                if ([Version]::TryParse($Matches.version, [ref]$parsedVersion)) {
                    $runtimeDirectories.Add([pscustomobject]@{ Path = $candidate; Version = $parsedVersion })
                }
            }
        }
    }
    foreach ($runtime in @($runtimeDirectories | Sort-Object Version -Descending)) {
        foreach ($file in @(Get-ChildItem $runtime.Path -Filter '*.dll' -File | Sort-Object Name)) {
            if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
                $pathsBySimpleName[$file.BaseName] = $file.FullName
            }
        }
    }

    return [string[]]@($pathsBySimpleName.Values)
}

function Test-TypeDerivesFrom {
    param(
        [AllowNull()][object]$Type,
        [Parameter(Mandatory)][string]$FullName
    )

    $current = $Type
    while ($null -ne $current) {
        if ($current.FullName -eq $FullName) { return $true }
        $current = $current.BaseType
    }
    return $false
}

function Test-TypeExecutesDeclaration {
    param(
        [Parameter(Mandatory)][object]$CandidateType,
        [Parameter(Mandatory)][object]$DeclaringType
    )

    if ($CandidateType.IsAbstract) { return $false }
    if (-not $DeclaringType.IsGenericTypeDefinition) {
        return $DeclaringType.IsAssignableFrom($CandidateType)
    }

    $current = $CandidateType
    while ($null -ne $current) {
        if ($current.IsGenericType -and $current.GetGenericTypeDefinition().FullName -eq $DeclaringType.FullName) {
            return $true
        }
        $current = $current.BaseType
    }
    return $false
}

function Get-TestAttributeCategory {
    param([Parameter(Mandatory)][object]$AttributeType)

    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.TheoryAttribute') { return 'Theory' }
    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.FactAttribute') { return 'Fact' }
    return $null
}

function Add-TraitsFromAttributes {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Attributes,
        [Parameter(Mandatory)][System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]$Traits
    )

    foreach ($attribute in $Attributes) {
        if ($attribute.AttributeType.FullName -ne 'Xunit.TraitAttribute' -or $attribute.ConstructorArguments.Count -lt 2) {
            continue
        }
        $name = [string]$attribute.ConstructorArguments[0].Value
        $value = [string]$attribute.ConstructorArguments[1].Value
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not $Traits.ContainsKey($name)) {
            $Traits[$name] = [System.Collections.Generic.List[string]]::new()
        }
        if (-not $Traits[$name].Contains($value)) {
            $Traits[$name].Add($value)
        }
    }
}

function Get-TestTraits {
    param(
        [Parameter(Mandatory)][object]$ExecutionType,
        [Parameter(Mandatory)][object]$Method
    )

    $traits = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $currentType = $ExecutionType
    while ($null -ne $currentType -and $currentType.FullName -ne 'System.Object') {
        Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($currentType)) -Traits $traits
        $currentType = $currentType.BaseType
    }
    Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($Method)) -Traits $traits

    $ordered = [ordered]@{}
    foreach ($name in @($traits.Keys | Sort-Object)) {
        $ordered[$name] = [string[]]@($traits[$name] | Sort-Object -Unique)
    }
    return [pscustomobject]$ordered
}

function Get-MethodParameterSignature {
    param([Parameter(Mandatory)][object]$Method)
    return (@($Method.GetParameters() | ForEach-Object { $_.ParameterType.ToString() }) -join ',')
}

function Get-TestAttributePolicy {
    param([Parameter(Mandatory)][object]$Attribute)

    $values = [ordered]@{
        Skip = ''
        Explicit = $false
        SkipWhen = ''
        SkipUnless = ''
        SkipType = ''
        SkipExceptions = ''
        Timeout = 0
    }
    foreach ($argument in @($Attribute.NamedArguments)) {
        if (-not $values.Contains($argument.MemberName)) { continue }
        $value = $argument.TypedValue.Value
        $values[$argument.MemberName] = if ($null -eq $value) { '' } else { [string]$value }
    }

    $isDisabled = -not [string]::IsNullOrWhiteSpace([string]$values.Skip) -or
        [string]$values.Explicit -eq 'True' -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipWhen) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipUnless) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipExceptions)
    $signature = @($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '|'
    return [pscustomobject][ordered]@{
        signature = $signature
        isDisabled = $isDisabled
        skip = [string]$values.Skip
        explicit = [string]$values.Explicit -eq 'True'
        skipWhen = [string]$values.SkipWhen
        skipUnless = [string]$values.SkipUnless
        skipType = [string]$values.SkipType
        skipExceptions = [string]$values.SkipExceptions
        timeout = [int]$values.Timeout
    }
}

function ConvertTo-AttributeTypedValueSignature {
    param([Parameter(Mandatory)][object]$Argument)

    $argumentType = [string]$Argument.ArgumentType.FullName
    $value = $Argument.Value
    if ($null -eq $value) { return "$argumentType=<null>" }
    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        $items = @($value | ForEach-Object { ConvertTo-AttributeTypedValueSignature -Argument $_ })
        return "$argumentType=[$($items -join ',')]"
    }
    $valueText = if ($value -is [Type]) { [string]$value.FullName } else { [string]$value }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($valueText.Normalize([Text.NormalizationForm]::FormC)))
    return "$argumentType=$encoded"
}

function Get-CustomAttributeSignature {
    param([Parameter(Mandatory)][object]$Attribute)

    $constructor = @($Attribute.ConstructorArguments | ForEach-Object { ConvertTo-AttributeTypedValueSignature -Argument $_ }) -join ';'
    $named = @($Attribute.NamedArguments | Sort-Object MemberName | ForEach-Object {
        "$($_.MemberName):$(ConvertTo-AttributeTypedValueSignature -Argument $_.TypedValue)"
    }) -join ';'
    return "$($Attribute.AttributeType.FullName)|ctor=$constructor|named=$named"
}

function Get-TestAssemblySnapshot {
    param(
        [Parameter(Mandatory)][string]$ResolvedProjectPath,
        [Parameter(Mandatory)][string]$ResolvedProjectName,
        [Parameter(Mandatory)][string]$ResolvedAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    if (-not (Test-Path $ResolvedAssemblyPath -PathType Leaf)) {
        throw "$ruleId-SCAN test assembly does not exist: $ResolvedAssemblyPath"
    }

    $metadataLoadContextPath = Get-ActiveSdkMetadataLoadContextPath
    if ($null -eq ('System.Reflection.MetadataLoadContext' -as [type])) {
        Add-Type -Path $metadataLoadContextPath
    }
    [string[]]$resolverPaths = @(Get-MetadataResolverPaths -TestAssemblyPath $ResolvedAssemblyPath -AdditionalReferencePaths $AdditionalReferencePaths)
    $resolver = [System.Reflection.PathAssemblyResolver]::new($resolverPaths)
    $context = [System.Reflection.MetadataLoadContext]::new($resolver)
    $tests = [System.Collections.Generic.List[object]]::new()
    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    try {
        $assembly = $context.LoadFromAssemblyPath($ResolvedAssemblyPath)
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        $allTypes = @($assembly.GetTypes() | Sort-Object FullName)
        foreach ($declaringType in $allTypes) {
            $bindingFlags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
            foreach ($method in @($declaringType.GetMethods($bindingFlags) | Sort-Object Name, MetadataToken)) {
                if ($method.IsSpecialName) { continue }
                $methodAttributes = @([System.Reflection.CustomAttributeData]::GetCustomAttributes($method))
                $testAttributes = @($methodAttributes | Where-Object { $null -ne (Get-TestAttributeCategory -AttributeType $_.AttributeType) })
                if ($testAttributes.Count -eq 0) { continue }
                if ($testAttributes.Count -ne 1) {
                    throw "$ruleId-SCAN $($declaringType.FullName).$($method.Name) has $($testAttributes.Count) Fact/Theory-derived attributes."
                }

                $testAttribute = $testAttributes[0]
                $attributeCategory = Get-TestAttributeCategory -AttributeType $testAttribute.AttributeType
                $parameterSignature = Get-MethodParameterSignature -Method $method
                $declaringTypeName = [string]$method.DeclaringType.FullName
                $testAttributeType = [string]$testAttribute.AttributeType.FullName
                if ($testAttributeType -notin $allowedTestAttributeTypes) {
                    throw "$ruleId-SCAN unregistered Fact/Theory-derived attribute '$testAttributeType' on $declaringTypeName.$($method.Name)."
                }
                $genericArity = @($method.GetGenericArguments()).Count
                $symbol = "$declaringTypeName.$($method.Name)($parameterSignature)"
                $logicalKey = "edge-test-decl-v1|$declaringTypeName|$($method.Name)``$genericArity|$parameterSignature"
                $logicalId = "edge-test-decl-v1:$(ConvertTo-Sha256 $logicalKey)"
                $physicalKey = "edge-test-physical-v1|$relativeProjectPath|$logicalId"
                $id = "edge-test-physical-v1:$(ConvertTo-Sha256 $physicalKey)"
                if (-not $seenIds.Add($id)) {
                    throw "$ruleId-SCAN duplicate test identity '$id' in $ResolvedAssemblyPath."
                }

                $inlineDataAttributes = @($methodAttributes | Where-Object { $_.AttributeType.FullName -eq 'Xunit.InlineDataAttribute' })
                $inlineDataRows = $inlineDataAttributes.Count
                $inlineDataSignatures = @($inlineDataAttributes | ForEach-Object { Get-CustomAttributeSignature -Attribute $_ } | Sort-Object)
                $dynamicDataSources = @($methodAttributes |
                    Where-Object {
                        (Test-TypeDerivesFrom -Type $_.AttributeType -FullName 'Xunit.v3.DataAttribute') -and
                        $_.AttributeType.FullName -ne 'Xunit.InlineDataAttribute'
                    } |
                    ForEach-Object { Get-CustomAttributeSignature -Attribute $_ } |
                    Sort-Object -Unique)
                $rowProjection = if ($attributeCategory -eq 'Theory' -and $inlineDataRows -gt 0) { $inlineDataRows } else { 1 }
                $executionTypes = [System.Collections.Generic.List[object]]::new()
                $executionCandidates = if ($method.IsStatic) {
                    @($declaringType)
                } else {
                    @($allTypes | Where-Object { Test-TypeExecutesDeclaration -CandidateType $_ -DeclaringType $declaringType })
                }
                foreach ($executionType in $executionCandidates) {
                    $executionTypeName = [string]$executionType.FullName
                    $executionKey = "edge-test-execution-v1|$relativeProjectPath|$executionTypeName|$logicalId"
                    $executionTypes.Add([pscustomobject][ordered]@{
                        id = "edge-test-execution-v1:$(ConvertTo-Sha256 $executionKey)"
                        name = $executionTypeName
                        traits = Get-TestTraits -ExecutionType $executionType -Method $method
                    })
                }
                if ($executionTypes.Count -eq 0) {
                    throw "$ruleId-SCAN $symbol has no concrete execution type and would not be discovered by the required runner."
                }
                $attributePolicy = Get-TestAttributePolicy -Attribute $testAttribute
                $dataAttributePolicies = @($methodAttributes |
                    Where-Object { Test-TypeDerivesFrom -Type $_.AttributeType -FullName 'Xunit.v3.DataAttribute' } |
                    ForEach-Object { Get-TestAttributePolicy -Attribute $_ })
                $dataPolicySignature = @($dataAttributePolicies | ForEach-Object { $_.signature } | Sort-Object) -join '||'
                $attributePolicy.signature = "$($attributePolicy.signature)|DataPolicies=$dataPolicySignature"
                $attributePolicy.isDisabled = [bool]$attributePolicy.isDisabled -or @($dataAttributePolicies | Where-Object { $_.isDisabled }).Count -gt 0

                $tests.Add([pscustomobject][ordered]@{
                    id = $id
                    logicalId = $logicalId
                    symbol = $symbol
                    executionType = $declaringTypeName
                    declaringType = $declaringTypeName
                    methodName = [string]$method.Name
                    parameterSignature = $parameterSignature
                    attributeCategory = $attributeCategory
                    testAttributeType = $testAttributeType
                    testAttributePolicy = $attributePolicy
                    inlineDataRows = $inlineDataRows
                    inlineDataSignatures = [string[]]$inlineDataSignatures
                    dynamicDataSources = [string[]]$dynamicDataSources
                    executionTypes = [object[]]@($executionTypes | Sort-Object name)
                    projectedCases = $rowProjection * $executionTypes.Count
                    traits = Get-TestTraits -ExecutionType $declaringType -Method $method
                })
            }
        }
    } finally {
        $context.Dispose()
    }

    return [pscustomobject][ordered]@{
        projectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        projectName = $ResolvedProjectName
        assemblyPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedAssemblyPath
        assemblySha256 = (Get-FileHash $ResolvedAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        declarations = $tests.Count
        executionTemplates = [int](($tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
        projectedCases = [int](($tests | Measure-Object -Property projectedCases -Sum).Sum)
        tests = [object[]]@($tests | Sort-Object id)
    }
}

function Get-TestProjectSpecifications {
    param(
        [Parameter(Mandatory)][string]$RequestedConfiguration,
        [switch]$AllowMissingAssembly
    )

    $specifications = [System.Collections.Generic.List[object]]::new()
    $testRoot = Join-Path $RepositoryRoot 'src/Tests'
    foreach ($projectFile in @(Get-ChildItem -Force $testRoot -Recurse -Filter '*.csproj' -File | Sort-Object FullName)) {
        [xml]$projectXml = Get-Content $projectFile.FullName -Raw
        $isTestProject = @($projectXml.SelectNodes('/Project/PropertyGroup/IsTestProject') | Where-Object { [string]$_.InnerText -eq 'true' }).Count -gt 0
        if (-not $isTestProject) { continue }
        $projectNameValue = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
        $assemblyNameValue = @($projectXml.SelectNodes('/Project/PropertyGroup/AssemblyName') | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($assemblyNameValue.Count -eq 0) { $assemblyNameValue = @($projectNameValue) }
        $targetFramework = @($projectXml.SelectNodes('/Project/PropertyGroup/TargetFramework') | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($targetFramework.Count -ne 1) {
            throw "$ruleId-BASELINE cannot resolve one TargetFramework from $($projectFile.FullName)."
        }
        $resolvedAssembly = Join-Path $projectFile.Directory.FullName "bin/$RequestedConfiguration/$($targetFramework[0])/$($assemblyNameValue[0]).dll"
        if (-not $AllowMissingAssembly -and -not (Test-Path $resolvedAssembly -PathType Leaf)) {
            throw "$ruleId-SCAN build $($projectFile.FullName) for $RequestedConfiguration before running this mode. Missing: $resolvedAssembly"
        }
        $specifications.Add([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $projectFile.FullName
            ProjectName = $projectNameValue
            AssemblyPath = Get-NormalizedPath $resolvedAssembly
            RunnerConfigPath = Get-NormalizedPath (Join-Path (Split-Path $resolvedAssembly -Parent) 'xunit.runner.json')
        })
    }
    return [object[]]@($specifications)
}

function Get-ProjectSourceFiles {
    param([Parameter(Mandatory)][string]$ResolvedProjectPath)

    $projectDirectory = Split-Path $ResolvedProjectPath -Parent
    return [string[]]@(Get-ChildItem -Force $projectDirectory -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName } |
        Sort-Object -Unique)
}

function Get-CanonicalRequiredCommandPrefixes {
    return [string[]]@(
        './scripts/tests/TestEdgeTestGovernanceBehavior.ps1',
        './scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic',
        'dotnet build IIoT.EdgeClient.slnx -c Release',
        './scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRepository',
        './scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateDiscovery',
        'dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~RepositoryHygieneTests"',
        'dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj',
        'dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName!~RepositoryHygieneTests"'
    )
}

function Get-CanonicalWorkflowRunSteps {
    param([Parameter(Mandatory)][string]$WorkflowPath)

    $steps = [System.Collections.Generic.List[object]]::new()
    if ($WorkflowPath -eq '.github/workflows/edge-smoke-build.yml') {
        $steps.Add([pscustomobject]@{
            Name = 'Validate immutable test baseline anchor'
            Run = './scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateBaselineAnchor -TrustedBaseRevision ''${{ github.event.pull_request.base.sha || github.event.before }}'''
        })
    } elseif ($WorkflowPath -eq '.github/workflows/edge-pack-modules.yml') {
        $steps.Add([pscustomobject]@{
            Name = 'Validate protected-main test baseline anchor'
            Run = "git fetch origin main --no-tags`n`$trustedMain = (git rev-parse origin/main | Out-String).Trim()`nif (`$env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and `$env:GITHUB_REF -ne 'refs/heads/main') {`n  throw 'Manual Edge release validation must run from refs/heads/main.'`n}`n./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateBaselineAnchor -TrustedBaseRevision `$trustedMain -AnchorRelationship HeadAncestorOfBase"
        })
    }
    $steps.Add([pscustomobject]@{
        Name = 'Validate reviewed restore and build inputs'
        Run = './scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release'
    })
    foreach ($step in @(
        [pscustomobject]@{
            Name = 'Run Edge test governance self-tests'
            Run = "./scripts/tests/TestEdgeTestGovernanceBehavior.ps1`n./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRunnerCaseNormalization`n./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release"
        },
        [pscustomobject]@{
            Name = 'Build Edge solution and test assemblies'
            Run = 'dotnet build IIoT.EdgeClient.slnx -c Release --no-restore -m:1 -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Validate Edge test repository and legacy discovery ceilings'
            Run = "./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release`n./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release"
        },
        [pscustomobject]@{
            Name = 'Run architecture policy tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~RepositoryHygieneTests" -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run plugin conformance tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run remaining shell behavior tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName!~RepositoryHygieneTests" -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run shared UI behavior tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.UI.Shared.Tests/IIoT.Edge.UI.Shared.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run launcher tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run installer tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Installer.Tests/IIoT.Edge.Installer.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run infrastructure update tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.Infrastructure.Update.Tests/IIoT.Edge.Infrastructure.Update.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Run non-UI regression tests'
            Run = 'dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse'
        }
    )) {
        $steps.Add($step)
    }
    return [object[]]$steps
}

function Get-WorkflowRunSteps {
    param([Parameter(Mandatory)][string]$WorkflowContent)

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $steps = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $nameMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)-\s+name:\s*(?<name>.+?)\s*$')
        if (-not $nameMatch.Success) { continue }
        $stepIndent = $nameMatch.Groups['indent'].Value.Length
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextStep = [regex]::Match($lines[$end], '^(?<indent>\s*)-\s+')
            if ($nextStep.Success -and $nextStep.Groups['indent'].Value.Length -eq $stepIndent) { break }
            $end++
        }

        $run = $null
        $shell = $null
        $hasIf = $false
        $ambiguous = $false
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $unexpectedKeys = [System.Collections.Generic.List[string]]::new()
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($stepIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            if (-not $seenDirectKeys.Add($directKey)) {
                $ambiguous = $true
                continue
            }
            if ($directKey -notin @('run', 'shell')) {
                $unexpectedKeys.Add($directKey)
            }
            switch ($directKey) {
                'shell' { $shell = $propertyMatch.Groups['value'].Value.Trim() }
                'if' { $hasIf = $true }
                'run' {
                    $value = $propertyMatch.Groups['value'].Value.Trim()
                    if ($value -ne '|') {
                        $run = $value
                        continue
                    }
                    $runIndent = $propertyMatch.Groups['indent'].Value.Length
                    $blockLines = [System.Collections.Generic.List[string]]::new()
                    $blockCursor = $cursor + 1
                    while ($blockCursor -lt $end) {
                        $line = $lines[$blockCursor]
                        $leading = [regex]::Match($line, '^\s*').Value.Length
                        if (-not [string]::IsNullOrWhiteSpace($line) -and $leading -le $runIndent) { break }
                        $blockLines.Add($line)
                        $blockCursor++
                    }
                    while ($blockLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($blockLines[$blockLines.Count - 1])) {
                        $blockLines.RemoveAt($blockLines.Count - 1)
                    }
                    $contentIndent = @($blockLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { [regex]::Match($_, '^\s*').Value.Length } | Measure-Object -Minimum).Minimum
                    $run = (@($blockLines | ForEach-Object {
                        if ([string]::IsNullOrWhiteSpace($_)) { return '' }
                        return $_.Substring([int]$contentIndent).TrimEnd()
                    }) -join "`n")
                }
            }
        }
        $name = $nameMatch.Groups['name'].Value.Trim().Trim('"', "'")
        $steps.Add([pscustomobject]@{ Name = $name; Run = $run; Shell = $shell; HasIf = $hasIf; Ambiguous = $ambiguous; UnexpectedKeys = [string[]]$unexpectedKeys })
        $index = $end - 1
    }
    return [object[]]@($steps)
}

function Get-WorkflowJobEnvelope {
    param(
        [Parameter(Mandatory)][string]$WorkflowContent,
        [Parameter(Mandatory)][string]$JobName
    )

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $matches = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $jobMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)' + [regex]::Escape($JobName) + ':\s*$')
        if (-not $jobMatch.Success -or $jobMatch.Groups['indent'].Value.Length -ne 2) { continue }
        $jobIndent = 2
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextJob = [regex]::Match($lines[$end], '^(?<indent>\s*)[A-Za-z0-9_-]+:\s*$')
            if ($nextJob.Success -and $nextJob.Groups['indent'].Value.Length -eq $jobIndent) { break }
            $end++
        }
        $timeoutValues = [System.Collections.Generic.List[string]]::new()
        $runsOnValues = [System.Collections.Generic.List[string]]::new()
        $directKeys = [System.Collections.Generic.List[string]]::new()
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $ambiguous = $false
        $hasIf = $false
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($jobIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            $directKeys.Add($directKey)
            if (-not $seenDirectKeys.Add($directKey)) { $ambiguous = $true }
            if ($directKey -eq 'timeout-minutes') {
                $timeoutValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'runs-on') {
                $runsOnValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'if') {
                $hasIf = $true
            }
        }
        $unexpectedKeys = @($directKeys | Where-Object { $_ -notin @('runs-on', 'timeout-minutes', 'steps') } | Sort-Object -Unique)
        $jobContent = @($lines[$index..($end - 1)]) -join "`n"
        $matches.Add([pscustomobject]@{
            TimeoutValues = [string[]]$timeoutValues
            RunsOnValues = [string[]]$runsOnValues
            HasIf = $hasIf
            Ambiguous = $ambiguous
            UnexpectedKeys = [string[]]$unexpectedKeys
            Content = $jobContent
        })
    }
    return [object[]]@($matches)
}

function Get-CanonicalWorkflowStepNames {
    param([Parameter(Mandatory)][string]$WorkflowPath)

    if ($WorkflowPath -eq '.github/workflows/edge-smoke-build.yml') {
        return [string[]]@(
            'Checkout',
            'Validate immutable test baseline anchor',
            'Setup .NET',
            'Validate reviewed restore and build inputs',
            'Restore Edge solution',
            'Run Edge test governance self-tests',
            'Setup Python for deployment behavior tests',
            'Run Edge deployment behavior tests',
            'Enforce shared UI baseline',
            'Build Edge solution and test assemblies',
            'Validate Edge test repository and legacy discovery ceilings',
            'Run architecture policy tests',
            'Run plugin conformance tests',
            'Run remaining shell behavior tests',
            'Run shared UI behavior tests',
            'Run launcher tests',
            'Run installer tests',
            'Run infrastructure update tests',
            'Run non-UI regression tests',
            'Scan vulnerable NuGet packages'
        )
    }
    return [string[]]@(
        'Checkout',
        'Validate protected-main test baseline anchor',
        'Setup .NET',
        'Validate reviewed restore and build inputs',
        'Restore edge solution',
        'Run Edge test governance self-tests',
        'Resolve release metadata',
        'Build Edge solution and test assemblies',
        'Validate Edge test repository and legacy discovery ceilings',
        'Run architecture policy tests',
        'Run plugin conformance tests',
        'Run remaining shell behavior tests',
        'Run launcher tests',
        'Run installer tests',
        'Run infrastructure update tests',
        'Run shared UI behavior tests',
        'Run non-UI regression tests',
        'Scan vulnerable NuGet packages'
    )
}

function Get-WorkflowStepNames {
    param([Parameter(Mandatory)][string]$JobContent)

    $lines = [regex]::Split($JobContent.Replace("`r`n", "`n"), "`n")
    $stepsMarkers = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $stepMatch = [regex]::Match($line, '^\s{6}-\s+(?<body>.+)$')
        if (-not $stepMatch.Success) { continue }
        $nameMatch = [regex]::Match($stepMatch.Groups['body'].Value, '^name:\s*(?<name>.+?)\s*$')
        if ($nameMatch.Success) {
            $stepsMarkers.Add($nameMatch.Groups['name'].Value.Trim().Trim('"', "'"))
        } else {
            $stepsMarkers.Add('<unnamed>')
        }
    }
    return [string[]]$stepsMarkers
}

function Get-GeneratedProjectPolicy {
    param(
        [Parameter(Mandatory)][string]$GeneratedProjectName,
        [string]$GeneratedProjectPath
    )

    $freezeMode = 'None'
    $frozenTypePatterns = @()
    $allowedNewTestKinds = @()
    $allowedNewRuntimes = @()
    $forbiddenNewTestKinds = @()
    $discoveryCeilings = @()
    $frozenSourceFiles = @()
    $protectBaselineRemovals = $true

    if ($GeneratedProjectName -eq 'IIoT.Edge.NonUiRegressionTests') {
        $freezeMode = 'All'
        $allowedNewTestKinds = $allowedTestKinds
        $allowedNewRuntimes = $allowedRuntimes
        $discoveryCeilings = @([pscustomobject]@{ displayNameContains = ''; maximum = 584 })
        if (-not [string]::IsNullOrWhiteSpace($GeneratedProjectPath)) {
            $frozenSourceFiles = Get-ProjectSourceFiles -ResolvedProjectPath $GeneratedProjectPath
        }
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.Shell.Tests') {
        $freezeMode = 'Types'
        $frozenTypePatterns = @('*.RepositoryHygieneTests')
        $allowedNewTestKinds = @('Unit', 'Application', 'Workflow', 'Integration', 'UI', 'Security')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Avalonia')
        $forbiddenNewTestKinds = @('Architecture', 'Deployment')
        $discoveryCeilings = @([pscustomobject]@{ displayNameContains = 'RepositoryHygieneTests.'; maximum = 74 })
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.Module.ContractTests') {
        $allowedNewTestKinds = @('Conformance')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem')
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.Infrastructure.Update.Tests') {
        $allowedNewTestKinds = @('Unit', 'Contract', 'Integration', 'Deployment')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem')
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.Installer.Tests') {
        $allowedNewTestKinds = @('Unit', 'UI', 'Deployment')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'Avalonia', 'Windows')
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.Launcher.Tests') {
        $allowedNewTestKinds = @('Unit', 'Integration', 'UI', 'Deployment')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'Avalonia', 'Windows')
    } elseif ($GeneratedProjectName -eq 'IIoT.Edge.UI.Shared.Tests') {
        $allowedNewTestKinds = @('Unit', 'UI')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Avalonia')
    }

    return [pscustomobject][ordered]@{
        isLegacy = $true
        freezeMode = $freezeMode
        frozenTypePatterns = [string[]]$frozenTypePatterns
        allowedNewTestKinds = [string[]]$allowedNewTestKinds
        allowedNewRuntimes = [string[]]$allowedNewRuntimes
        forbiddenNewTestKinds = [string[]]$forbiddenNewTestKinds
        discoveryCeilings = [object[]]$discoveryCeilings
        frozenSourceFiles = [string[]]$frozenSourceFiles
        protectBaselineRemovals = $protectBaselineRemovals
    }
}

function Get-TraitValues {
    param(
        [AllowNull()][object]$Traits,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Traits) { return @() }
    if ($Traits -is [System.Collections.IDictionary]) {
        if (-not $Traits.Contains($Name)) { return @() }
        return [string[]]@($Traits[$Name] | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    }
    $property = $Traits.PSObject.Properties[$Name]
    if ($null -eq $property) { return @() }
    return [string[]]@($property.Value | ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Test-NewTestMetadata {
    param(
        [Parameter(Mandatory)][object]$Test,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Location
    )

    $singleValueTraits = @('TestKind', 'Risk', 'Owner')
    $multiValueTraits = @('Capability', 'Runtime')
    foreach ($name in $singleValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires exactly one $name trait; found $($values.Count)."
        }
    }
    foreach ($name in $multiValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -lt 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires at least one $name trait."
        }
    }

    $testKind = @(Get-TraitValues -Traits $Test.traits -Name 'TestKind')
    $runtime = @(Get-TraitValues -Traits $Test.traits -Name 'Runtime')
    $risk = @(Get-TraitValues -Traits $Test.traits -Name 'Risk')
    if ([bool](Get-OptionalProperty (Get-OptionalProperty $Test 'testAttributePolicy' $null) 'isDisabled' $false)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Location is Skip/Explicit/conditional-skip and cannot enter a required Edge test lane."
    }
    if ($testKind.Count -eq 1 -and $testKind[0] -notin @($Baseline.allowedMetadata.testKinds)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported TestKind '$($testKind[0])'."
    }
    if ($testKind.Count -eq 1 -and $testKind[0] -match '^(?:Regression|NonUi|General|Misc|Phase.*|Batch.*)$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location uses forbidden legacy TestKind '$($testKind[0])'."
    }
    foreach ($value in $runtime) {
        if ($value -notin @($Baseline.allowedMetadata.runtimes)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Runtime '$value'."
        }
    }
    if ($risk.Count -eq 1 -and $risk[0] -notin @($Baseline.allowedMetadata.risks)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Risk '$($risk[0])'."
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Capability')) {
        if ($value -notin $allowedCapabilities) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Capability '$value'."
        }
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Owner')) {
        if ($value -notin $allowedOwners) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Owner '$value'."
        }
    }
}

function Test-IsFrozenTest {
    param(
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][object]$Test
    )

    if ([string]$Project.freezeMode -eq 'All') { return $true }
    if ([string]$Project.freezeMode -ne 'Types') { return $false }
    foreach ($pattern in @($Project.frozenTypePatterns)) {
        if ([string]$Test.executionType -like $pattern -or [string]$Test.declaringType -like $pattern) {
            return $true
        }
    }
    return $false
}

function Get-ProjectedCasesPerExecution {
    param([Parameter(Mandatory)][object]$Test)

    $executionCount = @($Test.executionTypes).Count
    if ($executionCount -le 0) { return 0 }
    return [int]([int]$Test.projectedCases / $executionCount)
}

function Get-ProjectedCaseDecreaseDeltaTest {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $baselinePerExecution = Get-ProjectedCasesPerExecution -Test $BaselineTest
    $currentPerExecution = Get-ProjectedCasesPerExecution -Test $CurrentTest
    if ($currentPerExecution -ge $baselinePerExecution) { return $null }

    $removedInlineDataSignatures = @($BaselineTest.inlineDataSignatures | Where-Object { $_ -notin @($CurrentTest.inlineDataSignatures) } | Sort-Object -Unique)
    if ($removedInlineDataSignatures.Count -gt 0) { return $null }
    $identityMaterial = @(
        [string]$BaselineTest.id,
        [string]$baselinePerExecution,
        [string]$currentPerExecution,
        ($removedInlineDataSignatures -join '|')
    ) -join '|'
    $deltaTest = $BaselineTest.PSObject.Copy()
    $deltaTest.id = "edge-test-case-decrease-v1:$(ConvertTo-Sha256 $identityMaterial)"
    $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
    $deltaTest | Add-Member -NotePropertyName baselineCasesPerExecution -NotePropertyValue $baselinePerExecution -Force
    $deltaTest | Add-Member -NotePropertyName currentCasesPerExecution -NotePropertyValue $currentPerExecution -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLostPerExecution -NotePropertyValue ($baselinePerExecution - $currentPerExecution) -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (($baselinePerExecution - $currentPerExecution) * @($CurrentTest.executionTypes).Count) -Force
    $deltaTest | Add-Member -NotePropertyName removedInlineDataSignatures -NotePropertyValue ([string[]]$removedInlineDataSignatures) -Force
    return $deltaTest
}

function Get-InlineDataRemovalDeltaTests {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $executionCount = @($CurrentTest.executionTypes).Count
    foreach ($signature in @($BaselineTest.inlineDataSignatures | Where-Object { $_ -notin @($CurrentTest.inlineDataSignatures) } | Sort-Object -Unique)) {
        $deltaTest = $BaselineTest.PSObject.Copy()
        $deltaTest.id = "edge-test-inline-removal-v1:$(ConvertTo-Sha256 "$($BaselineTest.id)|$signature")"
        $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
        $deltaTest | Add-Member -NotePropertyName removedInlineDataSignature -NotePropertyValue ([string]$signature) -Force
        $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue $executionCount -Force
        Write-Output $deltaTest
    }
}

function Test-WaiverManifest {
    param(
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    if ([string]$WaiverManifest.schemaVersion -ne $waiverSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "unsupported waiver schemaVersion '$($WaiverManifest.schemaVersion)'."
    }
    if ([string]$WaiverManifest.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver manifest ruleId must be $ruleId."
    }

    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $seenRegressionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $today = [DateOnly]::FromDateTime([DateTime]::UtcNow)
    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath })
    foreach ($waiver in @($WaiverManifest.waivers)) {
        $required = @('id', 'projectPath', 'symbol', 'changeKind', 'regressionId', 'targetProject', 'testKind', 'owner', 'reason', 'approvedBy', 'expiresOn')
        foreach ($name in $required) {
            if ([string]::IsNullOrWhiteSpace([string](Get-OptionalProperty $waiver $name ''))) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver is missing '$name'."
            }
        }
        $id = [string](Get-OptionalProperty $waiver 'id' '')
        if (-not [string]::IsNullOrWhiteSpace($id) -and -not $seenIds.Add($id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "duplicate waiver id '$id'."
        }
        $regressionId = [string](Get-OptionalProperty $waiver 'regressionId' '')
        if (-not [string]::IsNullOrWhiteSpace($regressionId) -and -not $seenRegressionIds.Add($regressionId)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "regressionId '$regressionId' is claimed by more than one waiver."
        }
        foreach ($name in @('projectPath', 'symbol', 'regressionId', 'targetProject')) {
            $value = [string](Get-OptionalProperty $waiver $name '')
            if ($value -match '[*?\[\]]') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' must not use wildcard $name '$value'."
            }
        }
        $targetProject = [string](Get-OptionalProperty $waiver 'targetProject' '')
        $projectPathValue = [string](Get-OptionalProperty $waiver 'projectPath' '')
        if ($projectPathValue -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' source project '$projectPathValue' is not a reviewed test project."
        }
        if ($targetProject -eq $projectPathValue -or $targetProject -match 'NonUiRegression|RepositoryHygiene') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject must leave the frozen legacy bucket."
        }
        if ($targetProject -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject '$targetProject' is not a reviewed test project."
        }
        $changeKind = [string](Get-OptionalProperty $waiver 'changeKind' '')
        if ($changeKind -notin @('Add', 'AttributeChange', 'InlineDataIncrease', 'InlineDataChange', 'InlineDataRemoval', 'DynamicDataSourceChange', 'ExecutionTypeIncrease', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'Remove')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unsupported changeKind '$changeKind'."
        }
        $testKind = [string](Get-OptionalProperty $waiver 'testKind' '')
        $owner = [string](Get-OptionalProperty $waiver 'owner' '')
        $approvedBy = [string](Get-OptionalProperty $waiver 'approvedBy' '')
        if ($testKind -notin $allowedTestKinds) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered testKind '$testKind'."
        }
        if ($owner -notin $allowedOwners) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered owner '$owner'."
        }
        if ($approvedBy -notin $approvedWaiverApprovers) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' approver '$approvedBy' is not registered."
        }
        $expiresOnValue = [string](Get-OptionalProperty $waiver 'expiresOn' '')
        try {
            $expiresOn = [DateOnly]::ParseExact($expiresOnValue, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            if ($expiresOn -lt $today) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expired on $expiresOnValue."
            } elseif ($expiresOn.DayNumber - $today.DayNumber -gt $maximumWaiverDays) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' exceeds the $maximumWaiverDays-day maximum."
            }
        } catch {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expiresOn '$expiresOnValue' is not yyyy-MM-dd."
        }
    }
}

function Test-BaselineStructure {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [switch]$AllowSyntheticPolicy
    )

    if ([string]$Baseline.schemaVersion -ne $baselineSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unsupported baseline schemaVersion '$($Baseline.schemaVersion)'."
    }
    if ([string]$Baseline.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "baseline ruleId must be $ruleId."
    }
    foreach ($metadata in @(
        @{ Name = 'testKinds'; Expected = $allowedTestKinds },
        @{ Name = 'runtimes'; Expected = $allowedRuntimes },
        @{ Name = 'risks'; Expected = $allowedRisks },
        @{ Name = 'owners'; Expected = $allowedOwners },
        @{ Name = 'capabilities'; Expected = $allowedCapabilities }
    )) {
        $actual = @((Get-OptionalProperty $Baseline.allowedMetadata $metadata.Name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
        $expected = @($metadata.Expected | Sort-Object -Unique)
        if (($actual -join '|') -ne ($expected -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "allowedMetadata.$($metadata.Name) differs from the canonical registry."
        }
    }
    $seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($project in @($Baseline.projects)) {
        if (-not $seenProjects.Add([string]$project.projectPath)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate project '$($project.projectPath)'."
        }
        if (-not $AllowSyntheticPolicy) {
            $expectedPolicy = Get-GeneratedProjectPolicy -GeneratedProjectName ([string]$project.projectName)
            foreach ($name in @('freezeMode', 'protectBaselineRemovals')) {
                if ([string](Get-OptionalProperty $project $name '') -ne [string](Get-OptionalProperty $expectedPolicy $name '')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            foreach ($name in @('frozenTypePatterns', 'allowedNewTestKinds', 'allowedNewRuntimes', 'forbiddenNewTestKinds')) {
                $actual = @((Get-OptionalProperty $project $name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
                $expected = @((Get-OptionalProperty $expectedPolicy $name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
                if (($actual -join '|') -ne ($expected -join '|')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            $actualCeilings = (Get-OptionalProperty $project 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            $expectedCeilings = (Get-OptionalProperty $expectedPolicy 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            if ($actualCeilings -ne $expectedCeilings) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) discoveryCeilings differs from the canonical project policy."
            }
        }
        $seenTests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $executionTemplateCount = 0
        $projectedCaseCount = 0
        foreach ($test in @($project.tests)) {
            if (-not $seenTests.Add([string]$test.id)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate test id '$($test.id)' in $($project.projectPath)."
            }
            if ([string]$test.id -notmatch '^edge-test-physical-v1:[0-9a-f]{64}$' -or [string]$test.logicalId -notmatch '^edge-test-decl-v1:[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid stable identity for '$($test.symbol)' in $($project.projectPath)."
            }
            if ([string]$test.testAttributeType -notin $allowedTestAttributeTypes) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unregistered test attribute '$($test.testAttributeType)' for '$($test.symbol)'."
            }
            if (@($test.executionTypes).Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "test declaration '$($test.symbol)' has no concrete execution type."
            }
            $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($executionType in @($test.executionTypes)) {
                if ([string]$executionType.id -notmatch '^edge-test-execution-v1:[0-9a-f]{64}$' -or -not $executionIds.Add([string]$executionType.id)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid or duplicate execution identity for '$($test.symbol)'."
                }
                $executionTemplateCount++
            }
            if ([int]$test.projectedCases % @($test.executionTypes).Count -ne 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "projected case count for '$($test.symbol)' is not divisible by its concrete execution count."
            }
            $projectedCaseCount += [int]$test.projectedCases
        }
        if ([int](Get-OptionalProperty $project 'baselineDeclarations' -1) -ne @($project.tests).Count -or
            [int](Get-OptionalProperty $project 'baselineExecutionTemplates' -1) -ne $executionTemplateCount -or
            [int](Get-OptionalProperty $project 'baselineProjectedCases' -1) -ne $projectedCaseCount) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) summary counts do not match its immutable test records."
        }
        if (-not $AllowSyntheticPolicy) {
            $runnerCases = [int](Get-OptionalProperty $project 'baselineRunnerCases' -1)
            $runnerDigest = [string](Get-OptionalProperty $project 'runnerCaseDigest' '')
            if ($runnerCases -lt $projectedCaseCount -or $runnerDigest -notmatch '^[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) must carry a reviewed normalized runner-case count and digest."
            }
        }
        if ([string]$project.projectName -eq 'IIoT.Edge.NonUiRegressionTests') {
            $sourceManifest = ((@($project.frozenSourceFiles | Sort-Object -Unique) -join "`n") + "`n")
            if ((ConvertTo-Sha256 $sourceManifest) -ne $nonUiFrozenSourceManifestSha256) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'NonUI frozen source manifest differs from the reviewed canonical hash.'
            }
        }
    }

    if (-not $AllowSyntheticPolicy) {
        $baselineProjectPaths = @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)
        $expectedWorkflowPaths = @('.github/workflows/edge-pack-modules.yml', '.github/workflows/edge-smoke-build.yml')
        $actualWorkflowPaths = @($Baseline.ciRequirements | ForEach-Object { [string]$_.workflowPath } | Sort-Object -Unique)
        if (($actualWorkflowPaths -join '|') -ne ($expectedWorkflowPaths -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'ciRequirements must protect both canonical Edge workflows.'
        }
        foreach ($requirement in @($Baseline.ciRequirements)) {
            $requiredProjects = @($requirement.requiredTestProjects | ForEach-Object { [string]$_ } | Sort-Object -Unique)
            if (($requiredProjects -join '|') -ne ($baselineProjectPaths -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredTestProjects differs from the reviewed project set."
            }
            $requiredCommands = @($requirement.requiredCommandPrefixes | ForEach-Object { [string]$_ })
            $canonicalCommands = @(Get-CanonicalRequiredCommandPrefixes)
            if (($requiredCommands -join '|') -ne ($canonicalCommands -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredCommandPrefixes differs from the canonical gate order."
            }
        }
    }
}

function Test-StaticPolicy {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    Test-BaselineStructure -Baseline $Baseline -Errors $Errors
    Test-WaiverManifest -WaiverManifest $WaiverManifest -Baseline $Baseline -Errors $Errors

    $baselineScanner = Get-OptionalProperty $Baseline 'scanner' $null
    $activeSdk = (& dotnet --version | Out-String).Trim()
    $activeMetadataLoadContextHash = (Get-FileHash (Get-ActiveSdkMetadataLoadContextPath) -Algorithm SHA256).Hash.ToLowerInvariant()
    $operatingSystem = if ($IsWindows) { 'Windows' } elseif ($IsMacOS) { 'macOS' } elseif ($IsLinux) { 'Linux' } else { 'Unknown' }
    $platformKey = "$operatingSystem-$([Runtime.InteropServices.RuntimeInformation]::OSArchitecture)"
    $approvedPlatformHash = [string]$approvedMetadataLoadContextSha256ByPlatform[$platformKey]
    $baselineScannerHash = [string](Get-OptionalProperty $baselineScanner 'metadataLoadContextSha256' '')
    $approvedScannerHashes = [string[]]@($approvedMetadataLoadContextSha256ByPlatform.Values)
    if ($null -eq $baselineScanner -or
        [string](Get-OptionalProperty $baselineScanner 'engine' '') -ne 'System.Reflection.MetadataLoadContext' -or
        [string](Get-OptionalProperty $baselineScanner 'activeDotnetSdk' '') -ne $activeSdk -or
        [string]::IsNullOrWhiteSpace($approvedPlatformHash) -or
        $activeMetadataLoadContextHash -ne $approvedPlatformHash -or
        $baselineScannerHash -notin $approvedScannerHashes) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "active scanner toolchain is not approved: platform=$platformKey sdk=$activeSdk metadataLoadContextSha256=$activeMetadataLoadContextHash baselineRecordedSha256=$baselineScannerHash."
    }

    Test-ExactReviewedFile -RelativePath '.gitattributes' -ExpectedSha256 $gitAttributesSha256 -Code "$ruleId-CONFIG" -Description 'LF normalization policy' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'global.json' -ExpectedSha256 $globalJsonSha256 -Code "$ruleId-CONFIG" -Description '.NET SDK selection policy' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'Directory.Build.props' -ExpectedSha256 $rootBuildPropsSha256 -Code "$ruleId-BYPASS" -Description 'root MSBuild props graph' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'Directory.Build.targets' -ExpectedSha256 $rootBuildTargetsSha256 -Code "$ruleId-BYPASS" -Description 'root MSBuild hard-gate graph' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'Directory.Packages.props' -ExpectedSha256 $directoryPackagesPropsSha256 -Code "$ruleId-CONFIG" -Description 'central test SDK and runner dependency versions' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'NuGet.Config' -ExpectedSha256 $nugetConfigSha256 -Code "$ruleId-CONFIG" -Description 'NuGet source and restore policy' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'scripts/TestEdgePackageVulnerabilities.ps1' -ExpectedSha256 $packageVulnerabilityScriptSha256 -Code "$ruleId-CONFIG" -Description 'NuGet vulnerability scan implementation' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'src/Tests/Directory.Build.props' -ExpectedSha256 $canonicalTestBuildPropsSha256 -Code "$ruleId-CONFIG" -Description 'shared test analyzer/runner configuration' -Errors $Errors
    Test-ExactReviewedFile -RelativePath '.github/CODEOWNERS' -ExpectedSha256 $codeOwnersSha256 -Code "$ruleId-CODEOWNER" -Description 'test-governance ownership graph' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'IIoT.EdgeClient.slnx' -ExpectedSha256 $solutionFileSha256 -Code "$ruleId-PROJECT" -Description 'solution build graph' -Errors $Errors
    Test-ExactReviewedFile -RelativePath 'scripts/tests/TestEdgeTestGovernanceBehavior.ps1' -ExpectedSha256 $governanceBehaviorSha256 -Code "$ruleId-FROZEN" -Description 'governance negative-test suite' -Errors $Errors

    $codeOwnersPath = Join-Path $RepositoryRoot '.github/CODEOWNERS'
    if (Test-Path $codeOwnersPath -PathType Leaf) {
        $codeOwnersContent = (Get-Content $codeOwnersPath -Raw).Replace("`r`n", "`n")
        foreach ($requiredCodeOwnerRule in @(
            '/.github/workflows/** @ShuJinHao',
            '/.gitattributes @ShuJinHao',
            '/global.json @ShuJinHao',
            '/Directory.Build.props @ShuJinHao',
            '/Directory.Build.targets @ShuJinHao',
            '/Directory.Packages.props @ShuJinHao',
            '/NuGet.Config @ShuJinHao',
            '/scripts/TestEdgePackageVulnerabilities.ps1 @ShuJinHao',
            '/scripts/tests/TestEdgeTestGovernancePolicy.ps1 @ShuJinHao',
            '/scripts/tests/TestEdgeTestGovernanceBehavior.ps1 @ShuJinHao',
            '**/*.csproj @ShuJinHao',
            '**/*.fsproj @ShuJinHao',
            '**/*.vbproj @ShuJinHao',
            '**/*.props @ShuJinHao',
            '**/*.targets @ShuJinHao',
            '/scripts/tests/baselines/ @ShuJinHao',
            '/src/Tests/**/*.cs @ShuJinHao',
            '/src/Tests/**/*.csproj @ShuJinHao',
            '/src/Tests/Directory.Build.props @ShuJinHao',
            '/src/Tests/xunit.runner.json @ShuJinHao'
        )) {
            if ($codeOwnersContent -notmatch "(?m)^$([regex]::Escape($requiredCodeOwnerRule))$") {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message "CODEOWNERS does not exactly protect '$requiredCodeOwnerRule'."
            }
        }
    }

    $repositoryProjectFiles = @(Get-RepositoryFiles -Root $RepositoryRoot -Predicate {
        param($file)
        $file.Name -match '(?i)\.(?:cs|fs|vb)proj$'
    })
    if ($repositoryProjectFiles.Count -ne $repositoryProjectRosterCount -or
        (Get-PathManifestDigest -Files $repositoryProjectFiles) -ne $repositoryProjectRosterSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "repository project roster differs from the reviewed $repositoryProjectRosterCount-project graph."
    }
    if ($repositoryProjectFiles.Count -ne $repositoryProjectRosterCount -or
        (Get-FileManifestDigest -Files $repositoryProjectFiles) -ne $repositoryProjectContentManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'repository project contents differ from the exact reviewed restore/build graph.'
    }

    $testProjectAssetFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($projectPath in @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)) {
        $projectAsset = Join-Path $RepositoryRoot $projectPath
        if (Test-Path $projectAsset -PathType Leaf) {
            $testProjectAssetFiles.Add((Get-Item -Force $projectAsset))
        }
    }
    if ($testProjectAssetFiles.Count -ne $testProjectAssetManifestCount -or
        (Get-FileManifestDigest -Files @($testProjectAssetFiles)) -ne $testProjectAssetManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'reviewed test csproj roster or content differs from the exact dependency graph.'
    }

    $directoryBuildFiles = @(Get-RepositoryFiles -Root $RepositoryRoot -Predicate {
        param($file)
        $file.Name -match '(?i)\.(?:props|targets)$'
    })
    if ($directoryBuildFiles.Count -ne $buildFileManifestCount -or
        (Get-FileManifestDigest -Files $directoryBuildFiles) -ne $buildFileManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'MSBuild props/targets roster or content differs from the exact reviewed restore/build graph.'
    }
    foreach ($responseFile in @(Get-RepositoryFiles -Root $RepositoryRoot -Predicate {
        param($file)
        $file.Extension -ieq '.rsp'
    })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-RESPONSE" -Message "MSBuild response files are forbidden in the reviewed repository: $(Get-RelativePath -BasePath $RepositoryRoot -Path $responseFile.FullName)."
    }

    $workflowFiles = @(Get-RepositoryFiles -Root (Join-Path $RepositoryRoot '.github/workflows') -Predicate {
        param($file)
        $file.Extension -match '(?i)^\.ya?ml$'
    })
    if ($workflowFiles.Count -ne $workflowManifestCount -or
        (Get-FileManifestDigest -Files $workflowFiles) -ne $workflowManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message 'workflow roster or content differs from the reviewed two-workflow execution graph.'
    }

    $criticalTestSourceFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($relativeSource in $criticalTestSourcePaths) {
        $sourcePath = Join-Path $RepositoryRoot $relativeSource
        if (Test-Path $sourcePath -PathType Leaf) {
            $criticalTestSourceFiles.Add((Get-Item -Force $sourcePath))
        }
    }
    if ($criticalTestSourceFiles.Count -ne $criticalTestSourceManifestCount -or
        (Get-FileManifestDigest -Files @($criticalTestSourceFiles)) -ne $criticalTestSourceManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message 'legacy architecture/conformance gate source bodies differ from the reviewed content freeze.'
    }

    $nonUiSourceRoot = Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.NonUiRegressionTests'
    $nonUiSourceFiles = if (Test-Path $nonUiSourceRoot -PathType Container) {
        @(Get-RepositoryFiles -Root $nonUiSourceRoot -Predicate { param($file) $file.Extension -ieq '.cs' })
    } else { @() }
    $nonUiSourceContentDigest = Get-FileManifestDigest -Files $nonUiSourceFiles
    if ($nonUiSourceFiles.Count -ne $nonUiFrozenSourceContentManifestCount -or
        $nonUiSourceContentDigest -ne $nonUiFrozenSourceContentManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "NonUI frozen source roster or test bodies differ from the reviewed content freeze: count=$($nonUiSourceFiles.Count), digest=$nonUiSourceContentDigest."
    }

    $allTestSourceFiles = @(Get-RepositoryFiles -Root (Join-Path $RepositoryRoot 'src/Tests') -Predicate {
        param($file)
        $file.Extension -ieq '.cs'
    })
    $allTestSourceDigest = Get-FileManifestDigest -Files $allTestSourceFiles
    if ($allTestSourceFiles.Count -ne $allTestSourceManifestCount -or
        $allTestSourceDigest -ne $allTestSourceManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "reviewed test source roster or bodies differ from the exact Phase 0 content freeze: count=$($allTestSourceFiles.Count), digest=$allTestSourceDigest."
    }

    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)
    $currentProjects = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration -AllowMissingAssembly |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.ProjectPath } |
        Sort-Object -Unique)
    if (($baselineProjects -join '|') -ne ($currentProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "test project set differs from the reviewed baseline. Current=[$($currentProjects -join ', ')], baseline=[$($baselineProjects -join ', ')]."
    }

    $solutionPath = Join-Path $RepositoryRoot 'IIoT.EdgeClient.slnx'
    [xml]$solutionXml = Get-Content $solutionPath -Raw
    $solutionProjects = @($solutionXml.SelectNodes('//Project') | ForEach-Object { [string]$_.Path })
    foreach ($projectPathValue in $currentProjects) {
        if ($projectPathValue -notin $solutionProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$projectPathValue is not included in IIoT.EdgeClient.slnx."
        }
    }

    $nestedTargets = @(Get-ChildItem -Force (Join-Path $RepositoryRoot 'src/Tests') -Recurse -File |
        Where-Object { $_.Name -imatch '^Directory\.Build\.targets$' })
    foreach ($nestedTarget in $nestedTargets) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "nested targets file shadows the root hard gate: $(Get-RelativePath -BasePath $RepositoryRoot -Path $nestedTarget.FullName)."
    }
    $testRoot = Get-NormalizedPath (Join-Path $RepositoryRoot 'src/Tests')
    $canonicalTestBuildProps = Get-NormalizedPath (Join-Path $testRoot 'Directory.Build.props')
    $unsupportedTestProjects = @(Get-ChildItem -Force $testRoot -Recurse -File | Where-Object {
        $_.Name -match '\.[A-Za-z0-9]+proj$' -and $_.Extension -ne '.csproj' -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })
    foreach ($unsupportedTestProject in $unsupportedTestProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $unsupportedTestProject.FullName) is a non-C# test project; Phase 0 permits only reviewed csproj projects."
    }
    $unsupportedRepositoryProjects = @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object {
        $_.Extension -in @('.fsproj', '.vbproj') -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })
    foreach ($unsupportedRepositoryProject in $unsupportedRepositoryProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $unsupportedRepositoryProject.FullName) uses an unreviewed project language that could hide a test project."
    }
    foreach ($runSettingsFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -Filter '*.runsettings' -File | Where-Object { $_.FullName -notmatch '[/\\](?:\.git|bin|obj|node_modules)[/\\]' })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $runSettingsFile.FullName) is an alternate VSTest configuration source; required lanes use only the canonical failSkips JSON."
    }
    $allTestRootProjects = @(Get-ChildItem -Force $testRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName } |
        Sort-Object -Unique)
    if (($allTestRootProjects -join '|') -ne ($baselineProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "every csproj under src/Tests must be an explicitly reviewed baseline project. Found=[$($allTestRootProjects -join ', ')]."
    }
    foreach ($projectFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -Filter '*.csproj' -File | Where-Object { $_.FullName -notmatch '[/\\](?:\.git|bin|obj|node_modules)[/\\]' })) {
        $projectContent = Get-Content $projectFile.FullName -Raw
        [xml]$projectXml = $projectContent
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $projectFile.FullName
        $normalizedProjectPath = Get-NormalizedPath $projectFile.FullName
        $isInsideTestRoot = $normalizedProjectPath.StartsWith("$testRoot/", [StringComparison]::Ordinal)
        $allProjectElements = @($projectXml.SelectNodes('//*'))
        $forbiddenGatePropertyNodes = @($allProjectElements | Where-Object {
            $_.LocalName -in @(
                'DesignTimeBuild',
                'IsCrossTargetingBuild',
                'ImportDirectoryBuildProps',
                'DirectoryBuildPropsPath',
                'ImportDirectoryBuildTargets',
                'DirectoryBuildTargetsPath',
                'RunSettingsFilePath',
                'VSTestSetting',
                'VSTestTestAdapterPath',
                'VSTestTestCaseFilter',
                'RestoreSources',
                'RestoreAdditionalProjectSources',
                'MSBuildExtensionsPath',
                'MSBuildExtensionsPath32',
                'MSBuildExtensionsPath64',
                'MSBuildUserExtensionsPath',
                'MSBuildProjectExtensionsPath',
                'ProjectExtensionsPath',
                'BaseIntermediateOutputPath',
                'IntermediateOutputPath',
                'ImportProjectExtensionProps',
                'ImportProjectExtensionTargets'
            ) -or $_.LocalName -match '^Custom(?:Before|After).+Targets$'
        })
        if ($forbiddenGatePropertyNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-AUTOIMPORT" -Message "$relativeProjectPath declares an MSBuild/VSTest property that can alter the reviewed test gate."
        }
        $usingTaskNodes = @($allProjectElements | Where-Object { $_.LocalName -in @('UsingTask', 'TaskFactory') })
        $initialTargetsAttributes = @($projectXml.Project.Attributes | Where-Object { $_.LocalName -ieq 'InitialTargets' })
        if ($usingTaskNodes.Count -gt 0 -or $initialTargetsAttributes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath embeds unreviewed MSBuild task/initial-target execution."
        }
        $rawReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Reference'))
        if ($rawReferenceNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses raw assembly Reference items; the reviewed graph permits explicit PackageReference/ProjectReference dependencies only."
        }
        $rawAnalyzerNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Analyzer'))
        if ($rawAnalyzerNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-ANALYZER" -Message "$relativeProjectPath imports a raw compiler analyzer; analyzers must enter through the exact reviewed central package graph."
        }
        $projectReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('ProjectReference'))
        $indirectProjectReferenceNodes = @($projectReferenceNodes | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        if ($indirectProjectReferenceNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an MSBuild expression as ProjectReference identity."
        }
        if (-not $isInsideTestRoot) {
            foreach ($projectReference in $projectReferenceNodes) {
                $referenceInclude = Get-XmlAttributeValue -Node $projectReference -Name 'Include'
                if ([string]::IsNullOrWhiteSpace($referenceInclude) -or $referenceInclude -match '\$\(') { continue }
                $resolvedReference = Get-NormalizedPath (Join-Path $projectFile.Directory.FullName $referenceInclude)
                $relativeReference = Get-RelativePath -BasePath $RepositoryRoot -Path $resolvedReference
                if ($relativeReference -in $baselineProjects) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath references reviewed test project '$relativeReference' from production code."
                }
            }
        }
        if (@(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Import')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an explicit MSBuild import; project-local import indirection is not allowed in the reviewed graph."
        }
        $allIsTestProjectNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('IsTestProject'))
        $directIsTestProjectNodes = @($allIsTestProjectNodes | Where-Object {
            $_.ParentNode.LocalName -ieq 'PropertyGroup' -and
            $_.ParentNode.ParentNode.LocalName -ieq 'Project' -and
            [string]$_.InnerText -match '^\s*true\s*$' -and
            [string]::IsNullOrWhiteSpace((Get-XmlAttributeValue -Node $_ -Name 'Condition'))
        })
        $packageReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('PackageReference'))
        $testPackageNodes = @($packageReferenceNodes | Where-Object {
            (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '^(?i)(?:xunit|Microsoft\.NET\.Test\.Sdk|Microsoft\.TestPlatform|Microsoft\.Testing\.Platform|MSTest|NUnit|TUnit)'
        })
        $indirectPackageIdentityNodes = @($packageReferenceNodes | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        $projectVersionOverrideNodes = @($packageReferenceNodes | Where-Object {
            -not [string]::IsNullOrWhiteSpace((Get-XmlAttributeValue -Node $_ -Name 'Version')) -or
            -not [string]::IsNullOrWhiteSpace((Get-XmlAttributeValue -Node $_ -Name 'VersionOverride')) -or
            @($_.ChildNodes | Where-Object { $_.LocalName -in @('Version', 'VersionOverride') -and -not [string]::IsNullOrWhiteSpace([string]$_.InnerText) }).Count -gt 0
        })
        if ($projectVersionOverrideNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-PACKAGEVERSION" -Message "$relativeProjectPath bypasses exact central package versions with a project-local Version/VersionOverride."
        }
        $rawTestIdentity = $projectContent -match '(?is)<\s*(?:IsTestProject|IsTestingPlatformApplication|TestingPlatformApplication)(?:\s|>)' -or
            $projectContent -match '(?is)<\s*Project\b[^>]*\bSdk\s*=\s*["''][^"'']*(?:MSTest\.Sdk|Microsoft\.Testing\.Platform|TUnit)[^"'']*["'']' -or
            $projectContent -match '(?is)<\s*PackageReference\b[^>]*\bInclude\s*=\s*["''][^"'']*(?:xunit|Microsoft\.NET\.Test\.Sdk|Microsoft\.TestPlatform|Microsoft\.Testing\.Platform|MSTest|NUnit|TUnit)[^"'']*["'']'
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        $projectSdkIdentities = [System.Collections.Generic.List[string]]::new()
        $rootSdkIdentity = Get-XmlAttributeValue -Node $projectXml.Project -Name 'Sdk'
        if ($rootSdkIdentity -match '\$\(') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an indirect Project SDK identity."
        }
        foreach ($sdkIdentity in @($rootSdkIdentity -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($sdkIdentity)) { $projectSdkIdentities.Add($sdkIdentity.Trim()) }
        }
        foreach ($sdkNode in @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Sdk') | Where-Object { $_.ParentNode.LocalName -ieq 'Project' })) {
            $sdkName = Get-XmlAttributeValue -Node $sdkNode -Name 'Name'
            if ([string]::IsNullOrWhiteSpace($sdkName)) { $sdkName = [string]$sdkNode.InnerText }
            if (-not [string]::IsNullOrWhiteSpace($sdkName)) { $projectSdkIdentities.Add($sdkName.Trim()) }
        }
        foreach ($sdkIdentity in $projectSdkIdentities) {
            $sdkName = ($sdkIdentity -split '/', 2)[0]
            if ($sdkName -notin $allowedProjectSdkIdentities) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses unreviewed Project SDK '$sdkIdentity'."
            }
        }
        if ($projectContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$relativeProjectPath configures an alternate VSTest runsettings source that can override failSkips."
        }
        if ($projectContent -match '(?is)<\s*(?:ImportDirectoryBuildTargets|DirectoryBuildTargetsPath)(?:\s|>)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath declares a case-insensitive Directory.Build gate override."
        }
        if (-not $isInsideTestRoot -and ($rawTestIdentity -or $allIsTestProjectNodes.Count -gt 0 -or $projectFile.BaseName -match '(?i)(?:^|\.)Tests?$')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath declares or is named as a test project outside src/Tests."
        }
        if ($testPackageNodes.Count -gt 0 -and $directIsTestProjectNodes.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses a test package without one explicit IsTestProject=true."
        }
        if ($testPackageNodes.Count -gt 0 -and -not $isInsideTestRoot) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath is a test project outside src/Tests."
        }
        if ($directIsTestProjectNodes.Count -gt 0) {
            if (-not $isInsideTestRoot) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath explicitly declares IsTestProject=true outside src/Tests."
            }
            if ($relativeProjectPath -notin $baselineProjects) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$relativeProjectPath is an unreviewed test project outside the baseline/CI matrix."
            }
        }
        if ($relativeProjectPath -in $baselineProjects -and ($directIsTestProjectNodes.Count -ne 1 -or $allIsTestProjectNodes.Count -ne 1)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath must own exactly one unconditional, direct IsTestProject=true declaration."
        }
        $runnerConfigOverrides = @($projectXml.SelectNodes('//*[@Include or @Update or @Remove]') | Where-Object {
            @($_.Attributes | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$relativeProjectPath overrides xunit.runner.json; only src/Tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Target') | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Name') -match '^ValidateEdgeTestGovernance' })
        if ($gateTargetOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath overrides an Edge test-governance MSBuild target."
        }
        $projectTargets = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Target'))
        $expectedTargetHash = [string]$allowedTestProjectTargetHashes[$relativeProjectPath]
        if ($projectTargets.Count -eq 0) {
            if (-not [string]::IsNullOrWhiteSpace($expectedTargetHash)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath removed its reviewed MSBuild target set."
            }
        } elseif ([string]::IsNullOrWhiteSpace($expectedTargetHash)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath introduces an unreviewed MSBuild target."
        } else {
            $targetMaterial = @($projectTargets | ForEach-Object { $_.OuterXml }) -join "`n"
            if ((ConvertTo-Sha256 -Value $targetMaterial) -ne $expectedTargetHash) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath changed its reviewed MSBuild target set."
            }
        }
        $disabledTargetImports = @($projectXml.SelectNodes('//ImportDirectoryBuildTargets') | Where-Object { [string]$_.InnerText -match '^\s*false\s*$' })
        $targetPathOverrides = @($projectXml.SelectNodes('//DirectoryBuildTargetsPath') | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.InnerText) })
        if ($disabledTargetImports.Count -gt 0 -or $targetPathOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $projectFile.FullName) disables or redirects the root Directory.Build.targets hard gate."
        }
    }
    foreach ($buildFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object { $_.Name -match '(?i)\.(?:props|targets)$' -and $_.FullName -notmatch '[/\\](?:\.git|bin|obj|node_modules)[/\\]' })) {
        $buildContent = Get-Content $buildFile.FullName -Raw
        [xml]$buildXml = $buildContent
        $normalizedBuildFile = Get-NormalizedPath $buildFile.FullName
        $relativeBuildFile = Get-RelativePath -BasePath $RepositoryRoot -Path $buildFile.FullName
        if ($normalizedBuildFile.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile is an unreviewed test props/targets indirection; only src/Tests/Directory.Build.props is allowed."
        }
        $buildElements = @($buildXml.SelectNodes('//*'))
        $forbiddenBuildGateNodes = @($buildElements | Where-Object {
            $_.LocalName -in @(
                'DesignTimeBuild',
                'IsCrossTargetingBuild',
                'ImportDirectoryBuildProps',
                'DirectoryBuildPropsPath',
                'ImportDirectoryBuildTargets',
                'DirectoryBuildTargetsPath',
                'RunSettingsFilePath',
                'VSTestSetting',
                'VSTestTestAdapterPath',
                'VSTestTestCaseFilter',
                'RestoreSources',
                'RestoreAdditionalProjectSources',
                'UsingTask',
                'TaskFactory'
            )
        })
        if ($normalizedBuildFile -notin @(
                (Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.props')),
                (Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')),
                $canonicalTestBuildProps
            ) -and $forbiddenBuildGateNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile declares an unreviewed MSBuild/VSTest lifecycle override."
        }
        $declaresTestProject = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('IsTestProject', 'IsTestingPlatformApplication', 'TestingPlatformApplication') | Where-Object { [string]$_.InnerText -match '^\s*true\s*$' }).Count -gt 0
        $declaresTestPackage = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('PackageReference') | Where-Object {
            (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '^(?i)(?:xunit|Microsoft\.NET\.Test\.Sdk|Microsoft\.TestPlatform|Microsoft\.Testing\.Platform|MSTest|NUnit|TUnit)'
        }).Count -gt 0
        $declaresRawTestIdentity = $buildContent -match '(?is)<\s*(?:IsTestProject|IsTestingPlatformApplication|TestingPlatformApplication)(?:\s|>)' -or
            $buildContent -match '(?is)<\s*PackageReference\b[^>]*\bInclude\s*=\s*["''][^"'']*(?:xunit|Microsoft\.NET\.Test\.Sdk|Microsoft\.TestPlatform|Microsoft\.Testing\.Platform|MSTest|NUnit|TUnit)[^"'']*["'']'
        $indirectPackageIdentityNodes = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('PackageReference') | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        if ($buildContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$relativeBuildFile configures an alternate VSTest runsettings source that can override failSkips."
        }
        if ($normalizedBuildFile -ne (Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')) -and
            $buildContent -match '(?is)<\s*(?:ImportDirectoryBuildTargets|DirectoryBuildTargetsPath)(?:\s|>)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile declares a case-insensitive Directory.Build gate override."
        }
        if (($declaresTestProject -or $declaresTestPackage -or $declaresRawTestIdentity) -and -not (Get-NormalizedPath $buildFile.FullName).StartsWith("$testRoot/", [StringComparison]::Ordinal)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile defines imported test identity/packages outside src/Tests."
        }
        if (@(Get-XmlElementsByLocalName -Xml $buildXml -Names @('IsTestProject')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile must not define IsTestProject; every reviewed test csproj must own that identity directly."
        }
        $runnerConfigOverrides = @($buildElements | Where-Object {
            @($_.Attributes | Where-Object { $_.LocalName -in @('Include', 'Update', 'Remove') } | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0 -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$relativeBuildFile overrides xunit.runner.json; only src/Tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('Target') | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Name') -match '^ValidateEdgeTestGovernance' })
        $rootTargets = Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')
        if ($gateTargetOverrides.Count -gt 0 -and $normalizedBuildFile -ne $rootTargets) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile overrides an Edge test-governance MSBuild target."
        }
        $disabledTargetImports = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('ImportDirectoryBuildTargets') | Where-Object { [string]$_.InnerText -match '^\s*false\s*$' })
        $targetPathOverrides = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('DirectoryBuildTargetsPath') | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.InnerText) })
        if ($disabledTargetImports.Count -gt 0 -or $targetPathOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $buildFile.FullName) disables or redirects the root Directory.Build.targets hard gate."
        }
    }

    foreach ($project in @($Baseline.projects | Where-Object { [string]$_.freezeMode -eq 'All' })) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $currentSources = @(Get-ProjectSourceFiles -ResolvedProjectPath $projectFile)
        $newSources = @($currentSources | Where-Object { $_ -notin @($project.frozenSourceFiles) })
        foreach ($source in $newSources) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$source is a new source file inside frozen project $($project.projectName)."
        }
    }

    $runnerConfigs = @(Get-ChildItem -Force $testRoot -Recurse -File | Where-Object {
        $_.Name -imatch 'xunit\.runner\.json$' -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })
    $canonicalRunnerConfig = Join-Path $testRoot 'xunit.runner.json'
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne (Get-NormalizedPath $canonicalRunnerConfig)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'src/Tests must contain exactly one shared xunit.runner.json; project-local overrides are forbidden.'
    } else {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $canonicalRunnerConfig -Errors $Errors -Context 'shared source configuration'
    }
    $testBuildProps = Join-Path $testRoot 'Directory.Build.props'
    [xml]$testBuildPropsXml = Get-Content $testBuildProps -Raw
    $runnerConfigItems = @($testBuildPropsXml.SelectNodes('//None') | Where-Object {
        [string]$_.Include -eq '$(MSBuildThisFileDirectory)xunit.runner.json' -and
        [string]$_.Link -eq 'xunit.runner.json' -and
        [string]$_.CopyToOutputDirectory -eq 'PreserveNewest'
    })
    if ($runnerConfigItems.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'src/Tests/Directory.Build.props must copy the single failSkips runner configuration into every test output.'
    }

    foreach ($requirement in @($Baseline.ciRequirements)) {
        $workflowPath = Join-Path $RepositoryRoot $requirement.workflowPath
        if (-not (Test-Path $workflowPath -PathType Leaf)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "required workflow does not exist: $($requirement.workflowPath)."
            continue
        }
        $expectedWorkflowSha256 = [string]$requiredWorkflowSha256[[string]$requirement.workflowPath]
        if ([string]::IsNullOrWhiteSpace($expectedWorkflowSha256) -or
            (Get-FileHash $workflowPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedWorkflowSha256) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) differs from the exact reviewed workflow, including triggers, permissions and path filters."
        }
        $workflowContent = (Get-Content $workflowPath -Raw).Replace('\', '/')
        $unpinnedActionReferences = @([regex]::Matches($workflowContent, '(?mi)^\s*uses:\s*[^@\s]+@(?<ref>[^\s#]+)') | Where-Object {
            $_.Groups['ref'].Value -notmatch '^[0-9a-f]{40}$'
        })
        if ($unpinnedActionReferences.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) contains an external Action reference that is not pinned to one full commit SHA."
        }
        foreach ($dotnetCommand in @([regex]::Matches($workflowContent, '(?mi)^\s*(?:run:\s*)?dotnet\s+(?:restore|build|test)\b[^\r\n]*$'))) {
            if ($dotnetCommand.Value -notmatch '(?:^|\s)-noAuto(?:Response|Rsp)(?:\s|$)') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-RESPONSE" -Message "$($requirement.workflowPath) runs restore/build/test without disabling automatic MSBuild response files."
            }
        }
        $requiredJobName = if ([string]$requirement.workflowPath -eq '.github/workflows/edge-smoke-build.yml') { 'smoke-build' } else { 'validate-runtime' }
        $jobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $workflowContent -JobName $requiredJobName)
        if ($jobEnvelopes.Count -ne 1 -or $jobEnvelopes[0].HasIf -or $jobEnvelopes[0].Ambiguous -or
            @($jobEnvelopes[0].UnexpectedKeys).Count -gt 0 -or
            @($jobEnvelopes[0].RunsOnValues).Count -ne 1 -or [string]$jobEnvelopes[0].RunsOnValues[0] -ne 'windows-latest' -or
            @($jobEnvelopes[0].TimeoutValues).Count -ne 1 -or [string]$jobEnvelopes[0].TimeoutValues[0] -ne '25') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' must be an unconditional windows-latest job with exact reviewed direct properties and timeout-minutes: 25."
        }
        if ($jobEnvelopes.Count -eq 1) {
            $actualJobSha256 = ConvertTo-Sha256 -Value (([string]$jobEnvelopes[0].Content).TrimEnd())
            $expectedJobSha256 = [string]$requiredWorkflowJobSha256[[string]$requirement.workflowPath]
            if ([string]::IsNullOrWhiteSpace($expectedJobSha256) -or $actualJobSha256 -ne $expectedJobSha256) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' differs from the exact reviewed job body: actual=$actualJobSha256."
            }
        }
        if ($workflowContent -match '(?mi)^\s*continue-on-error:\s*true\s*$' -or $workflowContent -match '(?mi)^\s*if:\s*(?:false|\$\{\{\s*false\s*\}\})\s*$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) contains a disabled or continue-on-error step that can hide a required gate."
        }
        if ($workflowContent -match '(?i)(?:IsTestProject|ImportDirectoryBuildTargets)\s*=\s*false' -or
            $workflowContent -match '(?i)(?:DirectoryBuildTargetsPath|DesignTimeBuild)\s*=') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) passes an MSBuild property that can bypass the required test gate."
        }
        if ($workflowContent -match '(?i)(?:--settings(?:\s|=)|RunSettingsFilePath|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) configures alternate VSTest runsettings and can override failSkips."
        }
        $workflowSteps = if ($jobEnvelopes.Count -eq 1) { @(Get-WorkflowRunSteps -WorkflowContent $jobEnvelopes[0].Content) } else { @() }
        $actualStepNames = if ($jobEnvelopes.Count -eq 1) { @(Get-WorkflowStepNames -JobContent $jobEnvelopes[0].Content) } else { @() }
        $expectedStepNames = @(Get-CanonicalWorkflowStepNames -WorkflowPath ([string]$requirement.workflowPath))
        if (($actualStepNames -join '|') -cne ($expectedStepNames -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) required job step names/order differ from the exact reviewed sequence."
        }
        foreach ($expectedStep in @(Get-CanonicalWorkflowRunSteps -WorkflowPath ([string]$requirement.workflowPath))) {
            $matchingSteps = @($workflowSteps | Where-Object { $_.Name -eq $expectedStep.Name })
            if ($matchingSteps.Count -ne 1) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) must contain exactly one '$($expectedStep.Name)' step."
                continue
            }
            $step = $matchingSteps[0]
            if ($step.HasIf -or $step.Ambiguous -or @($step.UnexpectedKeys).Count -gt 0 -or [string]$step.Shell -ne 'pwsh' -or [string]$step.Run -cne [string]$expectedStep.Run) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) step '$($expectedStep.Name)' must be an unconditional pwsh step with the exact reviewed run command."
            }
        }
        foreach ($requiredProject in @($requirement.requiredTestProjects)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?dotnet[ \t]+test[ \t]+' + [regex]::Escape([string]$requiredProject) + '(?=[ \t]|$)'
            $matches = [regex]::Matches($workflowContent, $pattern)
            if ($matches.Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) does not schedule '$requiredProject'."
            } elseif ([string]$requiredProject -notmatch 'IIoT\.Edge\.Shell\.Tests' ) {
                foreach ($match in $matches) {
                    $lineEnd = $workflowContent.IndexOf("`n", $match.Index)
                    if ($lineEnd -lt 0) { $lineEnd = $workflowContent.Length }
                    $commandLine = $workflowContent.Substring($match.Index, $lineEnd - $match.Index)
                    if ($commandLine -match '(?:^|\s)--filter(?:\s|=)') {
                        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) filters required project '$requiredProject' and may execute zero tests."
                    }
                }
            }
        }
        $lastCommandPosition = -1
        foreach ($commandPrefix in @($requirement.requiredCommandPrefixes)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?' + [regex]::Escape([string]$commandPrefix) + '(?=[ \t]|$)'
            $match = @([regex]::Matches($workflowContent, $pattern) | Where-Object { $_.Index -gt $lastCommandPosition } | Select-Object -First 1)
            if ($match.Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) is missing required command '$commandPrefix'."
            } else {
                $lastCommandPosition = $match[0].Index
            }
        }
    }
}

function Test-ProjectSnapshot {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $projectEntries = @($Baseline.projects | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    if ($projectEntries.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "snapshot project '$($Snapshot.projectPath)' does not map to exactly one baseline project."
        return
    }
    $project = $projectEntries[0]
    if ([string]$project.projectName -ne [string]$Snapshot.projectName) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "project name mismatch for $($Snapshot.projectPath): current=$($Snapshot.projectName), baseline=$($project.projectName)."
    }

    $baselineById = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($test in @($project.tests)) { $baselineById[[string]$test.id] = $test }
    $currentIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $deltas = [System.Collections.Generic.List[object]]::new()
    foreach ($test in @($Snapshot.tests)) {
        if (-not $currentIds.Add([string]$test.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "duplicate current test id '$($test.id)'."
            continue
        }
        if (-not $baselineById.ContainsKey([string]$test.id)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Add'; Test = $test })
            continue
        }
        $baselineTest = $baselineById[[string]$test.id]
        if ([string]$test.testAttributeType -ne [string]$baselineTest.testAttributeType -or
            [string]$test.attributeCategory -ne [string]$baselineTest.attributeCategory -or
            [string](Get-OptionalProperty $test.testAttributePolicy 'signature' '') -ne [string](Get-OptionalProperty $baselineTest.testAttributePolicy 'signature' '')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'AttributeChange'; Test = $test })
        }
        if ([int]$test.inlineDataRows -gt [int]$baselineTest.inlineDataRows) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataIncrease'; Test = $test })
        } elseif ((@($test.inlineDataSignatures) -join '|') -ne (@($baselineTest.inlineDataSignatures) -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataChange'; Test = $test })
        }
        foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $test)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataRemoval'; Test = $inlineDataRemoval })
        }
        $oldDynamicSources = @($baselineTest.dynamicDataSources)
        $newDynamicSources = @($test.dynamicDataSources)
        if (($newDynamicSources -join '|') -ne ($oldDynamicSources -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'DynamicDataSourceChange'; Test = $test })
        }
        $baselineExecutionIds = @($baselineTest.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($test.executionTypes | Where-Object { [string]$_.id -notin $baselineExecutionIds })) {
            $executionDeltaTest = $test.PSObject.Copy()
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeIncrease'; Test = $executionDeltaTest })
        }
        $currentExecutionIds = @($test.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($baselineTest.executionTypes | Where-Object { [string]$_.id -notin $currentExecutionIds })) {
            $executionDeltaTest = $baselineTest.PSObject.Copy()
            $executionDeltaTest.id = [string]$executionType.id
            $executionDeltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$baselineTest.id) -Force
            $executionDeltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (Get-ProjectedCasesPerExecution -Test $baselineTest) -Force
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeDecrease'; Test = $executionDeltaTest })
        }
        $projectedCaseDecrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $test
        if ($null -ne $projectedCaseDecrease) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ProjectedCaseDecrease'; Test = $projectedCaseDecrease })
        }
    }

    $projectWaivers = @($WaiverManifest.waivers | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    $usedWaiverIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($baselineTest in @($project.tests | Where-Object { [string]$_.id -notin $currentIds })) {
        if ([bool]$project.protectBaselineRemovals -or (Test-IsFrozenTest -Project $project -Test $baselineTest)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Remove'; Test = $baselineTest })
        }
    }
    foreach ($delta in $deltas) {
        $test = $delta.Test
        $location = "$($Snapshot.projectPath) :: $($test.symbol) [$($test.id)]"
        if ($delta.ChangeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval')) {
            $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
            if ($matchingWaivers.Count -ne 1) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location is a protected '$($delta.ChangeKind)' and requires one exact verified-migration waiver."
            } else {
                [void]$usedWaiverIds.Add([string]$matchingWaivers[0].id)
            }
            continue
        }
        Test-NewTestMetadata -Test $test -Baseline $Baseline -Errors $Errors -Location $location
        $testKind = @(Get-TraitValues -Traits $test.traits -Name 'TestKind')
        $runtime = @(Get-TraitValues -Traits $test.traits -Name 'Runtime')

        if (@($project.allowedNewTestKinds).Count -gt 0 -and ($testKind.Count -ne 1 -or $testKind[0] -notin @($project.allowedNewTestKinds))) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location must use one of [$(@($project.allowedNewTestKinds) -join ', ')]."
        }
        foreach ($runtimeValue in $runtime) {
            if (@($project.allowedNewRuntimes).Count -gt 0 -and $runtimeValue -notin @($project.allowedNewRuntimes)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location Runtime '$runtimeValue' is not allowed in $($project.projectName)."
            }
        }
        if ($testKind.Count -eq 1 -and $testKind[0] -in @($project.forbiddenNewTestKinds)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location must leave $($project.projectName); TestKind '$($testKind[0])' is forbidden here."
        }

        $isFrozen = Test-IsFrozenTest -Project $project -Test $test
        if (-not $isFrozen) { continue }

        $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
        if ($matchingWaivers.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location is frozen for '$($delta.ChangeKind)' and requires one exact waiver."
            continue
        }
        $waiver = $matchingWaivers[0]
        [void]$usedWaiverIds.Add([string]$waiver.id)
        $owner = @(Get-TraitValues -Traits $test.traits -Name 'Owner')
        if ($testKind.Count -ne 1 -or [string]$waiver.testKind -ne $testKind[0] -or $owner.Count -ne 1 -or [string]$waiver.owner -ne $owner[0]) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$($waiver.id)' does not match the test TestKind/Owner metadata."
        }
    }

    foreach ($waiver in $projectWaivers) {
        if (-not $usedWaiverIds.Contains([string]$waiver.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "stale waiver '$($waiver.id)' matches no current frozen delta."
        }
    }

    $removedCount = @($project.tests | Where-Object { $_.id -notin $currentIds }).Count
    Write-Host "Validated $($Snapshot.projectName): current=$(@($Snapshot.tests).Count), new/expanded=$($deltas.Count), removed=$removedCount"
}

function Test-RepositorySnapshotPolicies {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$SnapshotsByProject,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $seenLogicalIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $seenRegressionIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $protectedCaseLosses = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)

    foreach ($projectPathValue in @($SnapshotsByProject.Keys | Sort-Object)) {
        $snapshot = $SnapshotsByProject[$projectPathValue]
        foreach ($test in @($snapshot.tests)) {
            if ($seenLogicalIds.ContainsKey([string]$test.logicalId) -and $seenLogicalIds[[string]$test.logicalId] -ne [string]$snapshot.projectPath) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DUPLICATE" -Message "logical declaration '$($test.symbol)' exists in both '$($seenLogicalIds[[string]$test.logicalId])' and '$($snapshot.projectPath)'."
            } else {
                $seenLogicalIds[[string]$test.logicalId] = [string]$snapshot.projectPath
            }

            $regressionIds = @(Get-TraitValues -Traits $test.traits -Name 'RegressionId')
            if ($regressionIds.Count -gt 1) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "$($snapshot.projectPath) :: $($test.symbol) declares more than one RegressionId."
            } elseif ($regressionIds.Count -eq 1) {
                $regressionId = [string]$regressionIds[0]
                $location = "$($snapshot.projectPath) :: $($test.symbol)"
                if ($seenRegressionIds.ContainsKey($regressionId) -and $seenRegressionIds[$regressionId] -ne $location) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "RegressionId '$regressionId' is duplicated by '$($seenRegressionIds[$regressionId])' and '$location'."
                } else {
                    $seenRegressionIds[$regressionId] = $location
                }
            }
        }

        $baselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $snapshot.projectPath })
        if ($baselineProject.Count -ne 1) { continue }
        foreach ($baselineTest in @($baselineProject[0].tests)) {
            $currentTest = @($snapshot.tests | Where-Object { $_.id -eq $baselineTest.id })
            if ($currentTest.Count -ne 1) { continue }
            $decrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $currentTest[0]
            if ($null -ne $decrease) {
                $protectedCaseLosses[[string]$decrease.id] = $decrease
            }
            foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $currentTest[0])) {
                $protectedCaseLosses[[string]$inlineDataRemoval.id] = $inlineDataRemoval
            }
        }
    }

    foreach ($waiver in @($WaiverManifest.waivers | Where-Object { $_.changeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval') })) {
        $sourceProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.projectPath })
        $sourceTest = @()
        $sourceLogicalId = $null
        $projectedCasesLost = 0
        if ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'Remove') {
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $waiver.symbol })
            if ($sourceTest.Count -eq 1) {
                $sourceLogicalId = [string]$sourceTest[0].logicalId
                $projectedCasesLost = [int]$sourceTest[0].projectedCases
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'ExecutionTypeDecrease') {
            $sourceTest = @($sourceProject[0].tests | Where-Object {
                @($_.executionTypes | Where-Object { $_.id -eq $waiver.symbol }).Count -eq 1
            })
            if ($sourceTest.Count -eq 1) {
                $projectedCasesLost = Get-ProjectedCasesPerExecution -Test $sourceTest[0]
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -in @('ProjectedCaseDecrease', 'InlineDataRemoval') -and $protectedCaseLosses.ContainsKey([string]$waiver.symbol)) {
            $decrease = $protectedCaseLosses[[string]$waiver.symbol]
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $decrease.declarationId })
            $projectedCasesLost = [int]$decrease.projectedCasesLost
        }

        if ($sourceTest.Count -ne 1 -or $projectedCasesLost -le 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' does not resolve one concrete baseline loss."
            continue
        }
        if (-not $SnapshotsByProject.ContainsKey([string]$waiver.targetProject)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' target project was not scanned."
            continue
        }

        $targetBaselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.targetProject })
        $targetMatches = @($SnapshotsByProject[[string]$waiver.targetProject].tests | Where-Object {
            $candidate = $_
            $regressionIds = @(Get-TraitValues -Traits $candidate.traits -Name 'RegressionId')
            $testKinds = @(Get-TraitValues -Traits $candidate.traits -Name 'TestKind')
            $owners = @(Get-TraitValues -Traits $candidate.traits -Name 'Owner')
            if ($regressionIds.Count -ne 1 -or $regressionIds[0] -ne [string]$waiver.regressionId -or
                $testKinds.Count -ne 1 -or $testKinds[0] -ne [string]$waiver.testKind -or
                $owners.Count -ne 1 -or $owners[0] -ne [string]$waiver.owner) {
                return $false
            }
            if ($waiver.changeKind -eq 'Remove' -and [string]$candidate.logicalId -ne $sourceLogicalId) {
                return $false
            }

            $targetBaselineTest = if ($targetBaselineProject.Count -eq 1) {
                @($targetBaselineProject[0].tests | Where-Object { $_.id -eq $candidate.id })
            } else { @() }
            $addedProjectedCases = if ($targetBaselineTest.Count -eq 0) {
                [int]$candidate.projectedCases
            } elseif ($targetBaselineTest.Count -eq 1) {
                [int]$candidate.projectedCases - [int]$targetBaselineTest[0].projectedCases
            } else { 0 }
            return $addedProjectedCases -ge $projectedCasesLost
        })
        if ($targetMatches.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' is not backed by one uniquely classified RegressionId '$($waiver.regressionId)' with at least $projectedCasesLost newly added case(s) in '$($waiver.targetProject)'."
        }
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [ValidateRange(1, 600)][int]$TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
    }
    [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))
    return [pscustomobject]@{
        ExitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        TimedOut = $timedOut
        StandardOutput = $stdoutTask.Result
        StandardError = $stderrTask.Result
    }
}

function Get-DotNetListedTests {
    param([Parameter(Mandatory)][string]$Output)

    $collect = $false
    $tests = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Output, '\r?\n')) {
        if ($line -match 'Tests are available\s*:|测试可用\s*:|Tests disponibles\s*:|Tests disponibles sont\s*:') {
            $collect = $true
            continue
        }
        if (-not $collect) { continue }
        if ($line -match '^\s{2,}\S') {
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^(Test Run|Total tests|Passed!|Failed!|警告|Warning)') {
                $tests.Add($trimmed)
            }
        }
    }
    return [string[]]@($tests)
}

function Get-NormalizedRunnerCases {
    param(
        [Parameter(Mandatory)][string[]]$Cases,
        [string]$WorkspaceRoot = $RepositoryRoot
    )

    if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
        throw "$ruleId-DISCOVERY runner-case normalization requires an explicit workspace root."
    }
    $rootWithForwardSlashes = $WorkspaceRoot.Replace('\', '/').TrimEnd('/')
    $quotedWorkspacePathPattern = '"' + [regex]::Escape($rootWithForwardSlashes) + '(?:/[^\"]*)?"'
    $pathRegexOptions = [Text.RegularExpressions.RegexOptions]::CultureInvariant
    if ($IsWindows) {
        $pathRegexOptions = $pathRegexOptions -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase
    }
    $normalized = [string[]]@($Cases | ForEach-Object {
        $forwardSlashes = $_.Replace('\', '/')
        $withoutWorkspacePaths = [regex]::Replace(
            $forwardSlashes,
            $quotedWorkspacePathPattern,
            '"<ABSOLUTE_PATH>"',
            $pathRegexOptions)
        $withoutWorkspacePaths.Normalize([Text.NormalizationForm]::FormC)
    })
    [Array]::Sort($normalized, [StringComparer]::Ordinal)
    return $normalized
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '../..')
} else {
    $RepositoryRoot = Get-NormalizedPath $RepositoryRoot
}
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $RepositoryRoot $baselineRepositoryPath
}
if ([string]::IsNullOrWhiteSpace($WaiverPath)) {
    $WaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/edge-test-governance.waivers.json'
}
$BaselinePath = Get-NormalizedPath $BaselinePath
$WaiverPath = Get-NormalizedPath $WaiverPath

if ($Mode -eq 'ValidateBaselineAnchor') {
    $canonicalBaselinePath = Get-NormalizedPath (Join-Path $RepositoryRoot $baselineRepositoryPath)
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $BaselinePath.Equals($canonicalBaselinePath, $pathComparison)) {
        throw "$ruleId-BASELINE ValidateBaselineAnchor only accepts the canonical repository baseline."
    }
    if ($TrustedBaseRevision -notmatch '^[0-9A-Fa-f]{40}$' -or $TrustedBaseRevision -match '^0{40}$') {
        throw "$ruleId-BASELINE ValidateBaselineAnchor requires one non-zero full 40-character trusted base revision."
    }
    if (-not (Test-Path $BaselinePath -PathType Leaf)) {
        throw "$ruleId-BASELINE baseline does not exist: $BaselinePath"
    }

    $baseCommit = Invoke-CapturedProcess -FileName 'git' -Arguments @('cat-file', '-e', "$TrustedBaseRevision^{commit}") -WorkingDirectory $RepositoryRoot
    if ($baseCommit.TimedOut -or $baseCommit.ExitCode -ne 0) {
        throw "$ruleId-BASELINE trusted base revision is not available as a commit: $TrustedBaseRevision."
    }
    $ancestorArguments = if ($AnchorRelationship -eq 'HeadAncestorOfBase') {
        @('merge-base', '--is-ancestor', 'HEAD', $TrustedBaseRevision)
    } else {
        @('merge-base', '--is-ancestor', $TrustedBaseRevision, 'HEAD')
    }
    $ancestorCheck = Invoke-CapturedProcess -FileName 'git' -Arguments $ancestorArguments -WorkingDirectory $RepositoryRoot
    if ($ancestorCheck.TimedOut -or $ancestorCheck.ExitCode -ne 0) {
        throw "$ruleId-BASELINE checked-out commit and trusted revision violate required relationship '$AnchorRelationship': $TrustedBaseRevision."
    }

    $currentBaselineText = (Get-Content $BaselinePath -Raw).Replace("`r`n", "`n")
    $currentBaselineDigest = ConvertTo-Sha256 -Value $currentBaselineText
    $baseBaseline = Invoke-CapturedProcess -FileName 'git' -Arguments @('show', "${TrustedBaseRevision}:$baselineRepositoryPath") -WorkingDirectory $RepositoryRoot
    if (-not $baseBaseline.TimedOut -and $baseBaseline.ExitCode -eq 0) {
        $baseBaselineDigest = ConvertTo-Sha256 -Value $baseBaseline.StandardOutput.Replace("`r`n", "`n")
        if ($currentBaselineDigest -ne $baseBaselineDigest) {
            throw "$ruleId-BASELINE trusted-base transition is forbidden during Phase 0: base=$baseBaselineDigest current=$currentBaselineDigest. Use the separately reviewed migration-receipt batch."
        }
        Write-Host "Edge immutable baseline anchor passed: base=$TrustedBaseRevision digest=$currentBaselineDigest"
        exit 0
    }

    throw "$ruleId-BASELINE trusted base has no reviewed baseline; Phase 0 bootstrap is closed: $TrustedBaseRevision."
}

if ($Mode -eq 'ValidateRunnerCaseNormalization') {
    $macCases = [string[]]@(
        'Fixture.Case(value: "z")',
        'Fixture.Golden(path: "/Users/example/work/Edge/src/t"···)',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(value: "A")'
    )
    $linuxCases = [string[]]@(
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Golden(path: "/home/runner/work/Edge/Edge/src/tests/T"···)',
        'Fixture.Case(value: "A")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(value: "z")',
        'Fixture.Case(value: "A")'
    )
    $windowsCases = [string[]]@(
        'Fixture.Case(value: "A")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Golden(path: "C:\work\Edge\src\tests\T"···)',
        'Fixture.Case(value: "z")',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(value: "A")'
    )
    $expectedCases = [string[]]@(
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(value: "z")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Golden(path: "<ABSOLUTE_PATH>"···)'
    )
    $normalizedMacCases = @(Get-NormalizedRunnerCases -Cases $macCases -WorkspaceRoot '/Users/example/work/Edge')
    $normalizedLinuxCases = @(Get-NormalizedRunnerCases -Cases $linuxCases -WorkspaceRoot '/home/runner/work/Edge/Edge')
    $normalizedWindowsCases = @(Get-NormalizedRunnerCases -Cases $windowsCases -WorkspaceRoot 'C:\work\Edge')
    $expectedText = $expectedCases -join "`n"
    foreach ($actual in @($normalizedMacCases, $normalizedLinuxCases, $normalizedWindowsCases)) {
        if (($actual -join "`n") -cne $expectedText) {
            throw "$ruleId-DISCOVERY workspace-path normalization, ordinal ordering and business-value preservation must match the reviewed cross-OS sequence."
        }
    }
    if ($normalizedMacCases.Count -ne $macCases.Count -or
        @($normalizedMacCases | Where-Object { $_ -ceq 'Fixture.Case(value: "A")' }).Count -ne 2) {
        throw "$ruleId-DISCOVERY runner-case normalization must preserve exact duplicate multiplicity."
    }
    Write-Host 'Edge runner display-name normalization fixture passed.'
    exit 0
}

if ($Mode -eq 'ValidateRunnerConfiguration') {
    if ([string]::IsNullOrWhiteSpace($RunnerConfigPath)) {
        throw "$ruleId-DISABLED ValidateRunnerConfiguration requires RunnerConfigPath."
    }
    $runnerErrors = [System.Collections.Generic.List[string]]::new()
    Test-RunnerConfigurationFile -ResolvedRunnerConfigPath (Get-NormalizedPath $RunnerConfigPath) -Errors $runnerErrors -Context 'built test output'
    Assert-NoPolicyErrors -Errors $runnerErrors
    Write-Host "Edge failSkips runner configuration passed: $RunnerConfigPath"
    exit 0
}

if ($Mode -eq 'GenerateBaseline') {
    if (-not $AllowBaselineWrite) {
        throw "$ruleId-BASELINE baseline generation requires -AllowBaselineWrite and reviewed output."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CI)) {
        throw "$ruleId-BASELINE CI must never regenerate the reviewed baseline."
    }

    $specifications = if (-not [string]::IsNullOrWhiteSpace($ProjectPath) -or -not [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
            throw "$ruleId-BASELINE single-project generation requires ProjectPath, ProjectName, and AssemblyPath."
        }
        @([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $ProjectPath
            ProjectName = $ProjectName
            AssemblyPath = Get-NormalizedPath $AssemblyPath
        })
    } else {
        @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)
    }

    $projects = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $specifications) {
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification.ProjectPath -ResolvedProjectName $specification.ProjectName -ResolvedAssemblyPath $specification.AssemblyPath
        $projectFileForDiscovery = Get-RelativePath -BasePath $RepositoryRoot -Path $specification.ProjectPath
        $discoveryRun = Invoke-CapturedProcess -FileName 'dotnet' -Arguments @('test', $projectFileForDiscovery, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests', '--nologo') -WorkingDirectory $RepositoryRoot
        if ($discoveryRun.TimedOut -or $discoveryRun.ExitCode -ne 0) {
            throw "$ruleId-DISCOVERY baseline generation could not list $($specification.ProjectName): $($discoveryRun.StandardError.Trim())"
        }
        $runnerCases = @(Get-NormalizedRunnerCases -Cases (Get-DotNetListedTests -Output $discoveryRun.StandardOutput))
        $policy = Get-GeneratedProjectPolicy -GeneratedProjectName $specification.ProjectName -GeneratedProjectPath $specification.ProjectPath
        $projects.Add([pscustomobject][ordered]@{
            projectPath = $snapshot.projectPath
            projectName = $snapshot.projectName
            isLegacy = $policy.isLegacy
            freezeMode = $policy.freezeMode
            frozenTypePatterns = $policy.frozenTypePatterns
            frozenSourceFiles = $policy.frozenSourceFiles
            allowedNewTestKinds = $policy.allowedNewTestKinds
            allowedNewRuntimes = $policy.allowedNewRuntimes
            forbiddenNewTestKinds = $policy.forbiddenNewTestKinds
            discoveryCeilings = $policy.discoveryCeilings
            protectBaselineRemovals = $policy.protectBaselineRemovals
            sourceAssemblySha256 = $snapshot.assemblySha256
            baselineDeclarations = $snapshot.declarations
            baselineExecutionTemplates = $snapshot.executionTemplates
            baselineProjectedCases = $snapshot.projectedCases
            baselineRunnerCases = $runnerCases.Count
            runnerCaseDigest = ConvertTo-Sha256 -Value ($runnerCases -join "`n")
            tests = $snapshot.tests
        })
    }
    $projectPaths = [string[]]@($projects | ForEach-Object { $_.projectPath } | Sort-Object -Unique)
    $baseline = [pscustomobject][ordered]@{
        schemaVersion = $baselineSchemaVersion
        ruleId = $ruleId
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        scanner = [pscustomobject]@{
            engine = 'System.Reflection.MetadataLoadContext'
            activeDotnetSdk = (& dotnet --version | Out-String).Trim()
            metadataLoadContextSha256 = (Get-FileHash (Get-ActiveSdkMetadataLoadContextPath) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        allowedMetadata = [pscustomobject]@{
            testKinds = $allowedTestKinds
            runtimes = $allowedRuntimes
            risks = $allowedRisks
            owners = $allowedOwners
            capabilities = $allowedCapabilities
        }
        ciRequirements = @(
            [pscustomobject]@{
                workflowPath = '.github/workflows/edge-smoke-build.yml'
                requiredTestProjects = $projectPaths
                requiredCommandPrefixes = Get-CanonicalRequiredCommandPrefixes
            },
            [pscustomobject]@{
                workflowPath = '.github/workflows/edge-pack-modules.yml'
                requiredTestProjects = $projectPaths
                requiredCommandPrefixes = Get-CanonicalRequiredCommandPrefixes
            }
        )
        projects = [object[]]@($projects | Sort-Object projectPath)
    }
    Write-JsonAtomically -Value $baseline -Path $BaselinePath
    Write-Host "Generated reviewed baseline candidate: $BaselinePath"
    Write-Host "Projects: $($projects.Count)"
    Write-Host "Expanded declarations: $(($projects | Measure-Object -Property baselineDeclarations -Sum).Sum)"
    Write-Host "Execution templates: $(($projects | Measure-Object -Property baselineExecutionTemplates -Sum).Sum)"
    Write-Host "Projected cases: $(($projects | Measure-Object -Property baselineProjectedCases -Sum).Sum)"
    exit 0
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "$ruleId-BASELINE baseline does not exist: $BaselinePath"
}
if (-not (Test-Path $WaiverPath -PathType Leaf)) {
    throw "$ruleId-WAIVER waiver manifest does not exist: $WaiverPath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 100
$waiverManifest = Get-Content $WaiverPath -Raw | ConvertFrom-Json -Depth 100
$errors = [System.Collections.Generic.List[string]]::new()

if ($Mode -eq 'ValidateSnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $snapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic Edge test governance snapshot passed.'
    exit 0
}

if ($Mode -eq 'ValidateRepositorySnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "repository snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $repositorySnapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        foreach ($snapshot in @((Get-OptionalProperty $repositorySnapshot 'snapshots' @()))) {
            if ($snapshotsByProject.ContainsKey([string]$snapshot.projectPath)) {
                Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "duplicate repository snapshot for '$($snapshot.projectPath)'."
                continue
            }
            $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
            Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
        }
        Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic Edge repository migration snapshot passed.'
    exit 0
}

Test-StaticPolicy -Baseline $baseline -WaiverManifest $waiverManifest -Errors $errors
if ($Mode -eq 'ValidateStatic') {
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Edge test governance static policy passed.'
    exit 0
}

if ($Mode -eq 'ValidateDiscovery') {
    foreach ($project in @($baseline.projects)) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $specification = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration | Where-Object { $_.ProjectPath -eq (Get-NormalizedPath $projectFile) })
        if ($specification.Count -ne 1) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) cannot resolve one built test assembly."
            continue
        }
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification[0].RunnerConfigPath -Errors $errors -Context $project.projectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification[0].ProjectPath -ResolvedProjectName $specification[0].ProjectName -ResolvedAssemblyPath $specification[0].AssemblyPath
        $arguments = @('test', $projectFile, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests', '--nologo')
        $run = Invoke-CapturedProcess -FileName 'dotnet' -Arguments $arguments -WorkingDirectory $RepositoryRoot
        if ($run.TimedOut) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests exceeded the 120-second hard timeout."
            continue
        }
        if ($run.ExitCode -ne 0) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests failed: $($run.StandardError.Trim())."
            continue
        }
        $listedTests = @(Get-NormalizedRunnerCases -Cases (Get-DotNetListedTests -Output $run.StandardOutput))
        if ($listedTests.Count -ne [int]$snapshot.projectedCases) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) runner discovered $($listedTests.Count), metadata projected $($snapshot.projectedCases)."
        } else {
            Write-Host "Discovery reconciliation $($project.projectName): $($listedTests.Count)/$($snapshot.projectedCases)"
        }
        $runnerDigest = ConvertTo-Sha256 -Value ($listedTests -join "`n")
        if ($listedTests.Count -ne [int]$project.baselineRunnerCases -or $runnerDigest -ne [string]$project.runnerCaseDigest) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) runner discovery differs from the reviewed baseline: current=$($listedTests.Count), baseline=$($project.baselineRunnerCases)."
        }
        foreach ($ceiling in @($project.discoveryCeilings)) {
            $filter = [string]$ceiling.displayNameContains
            $matchingTests = if ([string]::IsNullOrWhiteSpace($filter)) { $listedTests } else { @($listedTests | Where-Object { $_.Contains($filter, [StringComparison]::Ordinal) }) }
            if ($matchingTests.Count -eq 0) {
                Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) discovery ceiling '$filter' matched zero tests."
            } elseif ($matchingTests.Count -gt [int]$ceiling.maximum) {
                Add-PolicyError -Errors $errors -Code "$ruleId-FROZEN" -Message "$($project.projectName) discovery ceiling '$filter' grew to $($matchingTests.Count), maximum=$($ceiling.maximum)."
            } else {
                Write-Host "Discovery ceiling $($project.projectName) '$filter': $($matchingTests.Count)/$($ceiling.maximum)"
            }
        }
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Edge legacy discovery ceilings passed.'
    exit 0
}

if ($Mode -eq 'ValidateProject') {
    if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        throw "$ruleId-SCAN ValidateProject requires ProjectPath, ProjectName, and AssemblyPath."
    }
    $additionalReferencePaths = if ([string]::IsNullOrWhiteSpace($ReferencePathsFile)) {
        @()
    } elseif (-not (Test-Path $ReferencePathsFile -PathType Leaf)) {
        throw "$ruleId-SCAN reference-path response file does not exist: $ReferencePathsFile"
    } else {
        [string[]]@(Get-Content $ReferencePathsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath (Get-NormalizedPath $ProjectPath) -ResolvedProjectName $ProjectName -ResolvedAssemblyPath (Get-NormalizedPath $AssemblyPath) -AdditionalReferencePaths $additionalReferencePaths
    Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host "Edge test governance assembly policy passed: $ProjectName"
    exit 0
}

if ($Mode -eq 'ValidateRepository') {
    $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($specification in @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)) {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification.RunnerConfigPath -Errors $errors -Context $specification.ProjectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification.ProjectPath -ResolvedProjectName $specification.ProjectName -ResolvedAssemblyPath $specification.AssemblyPath
        $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Edge test governance repository policy passed.'
    exit 0
}

throw "$ruleId unsupported mode '$Mode'."
