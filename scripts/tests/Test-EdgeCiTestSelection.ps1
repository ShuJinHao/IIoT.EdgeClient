[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $RepositoryRoot).Path
$selector = Join-Path $root 'scripts/tests/Select-EdgeCiTests.ps1'
$allowedCategories = @('Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')
function Assert-ValidCategories([object]$Selection) {
    $invalid = @($Selection.selectedDotNetProjects.categories | Where-Object {
            $_ -notin $allowedCategories
        })
    if ($invalid.Count -gt 0) {
        throw "Selector emitted non-canonical categories: $($invalid -join ', ')"
    }
}

function Set-FixtureFile {
    param(
        [Parameter(Mandatory)][string]$FixtureRoot,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $target = Join-Path $FixtureRoot $Path
    [void](New-Item (Split-Path $target -Parent) -ItemType Directory -Force)
    Set-Content -LiteralPath $target -Value $Content -Encoding utf8
}

function Invoke-FixtureGit {
    param(
        [Parameter(Mandatory)][string]$FixtureRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = @(& git -C $FixtureRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: git $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function New-TestProjectXml {
    param(
        [Parameter(Mandatory)][string]$Kind,
        [Parameter(Mandatory)][string]$Owner,
        [string]$ProjectReference
    )

    $referenceXml = if ([string]::IsNullOrWhiteSpace($ProjectReference)) {
        ''
    } else {
        "<ItemGroup><ProjectReference Include=`"$ProjectReference`" /></ItemGroup>"
    }
    return @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <TestKind>$Kind</TestKind>
    <TestConcern>Reliability</TestConcern>
    <TestOwner>$Owner</TestOwner>
  </PropertyGroup>
  $referenceXml
</Project>
"@
}

function New-SolutionXml {
    param([Parameter(Mandatory)][string[]]$ProjectPaths)

    $projects = @($ProjectPaths | Sort-Object | ForEach-Object {
            "  <Project Path=`"$_`" />"
        }) -join [Environment]::NewLine
    return "<Solution>$([Environment]::NewLine)$projects$([Environment]::NewLine)</Solution>"
}

function New-DynamicBusinessFixture {
    param(
        [Parameter(Mandatory)][string]$Parent,
        [Parameter(Mandatory)][string]$Name,
        [switch]$IncludeRemainingOwner
    )

    $fixtureRoot = Join-Path $Parent $Name
    [void](New-Item $fixtureRoot -ItemType Directory -Force)
    [void](Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('init', '-q'))
    [void](Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('config', 'user.name', 'Edge Selector Fixture'))
    [void](Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('config', 'user.email', 'edge-selector@example.invalid'))

    Set-FixtureFile -FixtureRoot $fixtureRoot -Path 'src/Core/Widget/Widget.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
'@
    Set-FixtureFile -FixtureRoot $fixtureRoot -Path 'src/Core/Other/Other.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
'@
    Set-FixtureFile -FixtureRoot $fixtureRoot `
        -Path 'src/Tests/Architecture.Tests/Architecture.Tests.csproj' `
        -Content (New-TestProjectXml -Kind Architecture -Owner Edge.Architecture)
    Set-FixtureFile -FixtureRoot $fixtureRoot `
        -Path 'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj' `
        -Content (New-TestProjectXml -Kind Unit -Owner Edge.Widget `
            -ProjectReference '../../Core/Widget/Widget.csproj')
    Set-FixtureFile -FixtureRoot $fixtureRoot `
        -Path 'src/Tests/Widget.Retiring.Tests/RetiringCase.cs' `
        -Content 'internal sealed class RetiringCase { }'
    Set-FixtureFile -FixtureRoot $fixtureRoot `
        -Path 'src/Tests/Other.Tests/Other.Tests.csproj' `
        -Content (New-TestProjectXml -Kind Unit -Owner Edge.Other `
            -ProjectReference '../../Core/Other/Other.csproj')
    Set-FixtureFile -FixtureRoot $fixtureRoot `
        -Path 'src/Orphans/UnownedBaselineFile.cs' `
        -Content 'internal sealed class UnownedBaselineFile { }'

    $solutionProjects = [Collections.Generic.List[string]]::new()
    foreach ($path in @(
            'src/Core/Widget/Widget.csproj',
            'src/Core/Other/Other.csproj',
            'src/Tests/Architecture.Tests/Architecture.Tests.csproj',
            'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj',
            'src/Tests/Other.Tests/Other.Tests.csproj')) {
        $solutionProjects.Add($path)
    }
    if ($IncludeRemainingOwner) {
        Set-FixtureFile -FixtureRoot $fixtureRoot `
            -Path 'src/Tests/Widget.Remaining.Tests/Widget.Remaining.Tests.csproj' `
            -Content (New-TestProjectXml -Kind Unit -Owner Edge.Widget `
                -ProjectReference '../../Core/Widget/Widget.csproj')
        $solutionProjects.Add('src/Tests/Widget.Remaining.Tests/Widget.Remaining.Tests.csproj')
    }
    Set-FixtureFile -FixtureRoot $fixtureRoot -Path 'IIoT.EdgeClient.slnx' `
        -Content (New-SolutionXml -ProjectPaths @($solutionProjects))

    [void](Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('add', '.'))
    [void](Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('commit', '-qm', 'baseline'))
    $baseline = [string](@(Invoke-FixtureGit -FixtureRoot $fixtureRoot -Arguments @('rev-parse', 'HEAD'))[0])
    return [pscustomobject]@{
        Root = $fixtureRoot
        Baseline = $baseline.Trim()
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-ci-selector-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $temporaryRoot -ItemType Directory -Force)
try {
    $positiveOutput = Join-Path $temporaryRoot 'positive.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('src/Core/IIoT.Edge.Domain/Config/Aggregates/SystemConfigEntity.cs') `
        -OutputPath $positiveOutput `
        -GitHubOutputPath ''
    $positive = Get-Content $positiveOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $positive
    $positiveNames = @($positive.selectedDotNetProjects.projectName)
    if ($positiveNames -notcontains 'IIoT.Edge.Architecture.Tests' -or
        $positiveNames -notcontains 'IIoT.Edge.DeviceBootstrap.IntegrationTests' -or
        $positiveNames -notcontains 'IIoT.Edge.Domain.Tests') {
        throw "Positive selector fixture omitted mandatory or affected projects: $($positiveNames -join ', ')"
    }
    if (@($positive.selectedDotNetProjects.categories) -contains 'DeploymentContract') {
        throw 'Business source selection included DeploymentContract without a deployment change.'
    }

    $utf8Fixture = New-DynamicBusinessFixture `
        -Parent $temporaryRoot `
        -Name 'utf8-git-paths'
    Set-FixtureFile -FixtureRoot $utf8Fixture.Root `
        -Path 'src/Core/Widget/中文 文件.cs' `
        -Content 'internal sealed class Utf8PathFixture { }'
    Set-FixtureFile -FixtureRoot $utf8Fixture.Root `
        -Path 'docs/生产 发布说明.md' `
        -Content '# UTF-8 path fixture'
    [void](Invoke-FixtureGit -FixtureRoot $utf8Fixture.Root -Arguments @('add', '.'))
    [void](Invoke-FixtureGit -FixtureRoot $utf8Fixture.Root -Arguments @('commit', '-qm', 'utf8 paths'))
    $utf8Output = Join-Path $temporaryRoot 'utf8-paths.json'
    & $selector `
        -RepositoryRoot $utf8Fixture.Root `
        -BaseRef $utf8Fixture.Baseline `
        -HeadRef HEAD `
        -OutputPath $utf8Output `
        -GitHubOutputPath ''
    $utf8Selection = Get-Content $utf8Output -Raw | ConvertFrom-Json
    $utf8ChangedFiles = @($utf8Selection.changedFiles)
    $utf8BusinessNames = @($utf8Selection.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'Business' } |
        Select-Object -ExpandProperty projectName)
    if ($utf8ChangedFiles -notcontains 'src/Core/Widget/中文 文件.cs' -or
        $utf8ChangedFiles -notcontains 'docs/生产 发布说明.md' -or
        $utf8BusinessNames -notcontains 'Widget.Retiring.Tests' -or
        @($utf8Selection.unclassifiedFiles).Count -ne 0 -or
        @($utf8ChangedFiles | Where-Object {
                $_ -match '^"' -or $_ -match '\\[0-7]{3}'
            }).Count -ne 0) {
        throw "Git UTF-8 path selection did not preserve exact repository paths: $($utf8ChangedFiles -join ', ')"
    }

    $docsOutput = Join-Path $temporaryRoot 'docs.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('docs/example.md') `
        -OutputPath $docsOutput `
        -GitHubOutputPath ''
    $docs = Get-Content $docsOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $docs
    if (@($docs.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Security')
            }).Count -ne 0) {
        throw 'Documentation-only changes selected a non-red-line category.'
    }

    $sdkPackageSetManifest = Get-Content `
        (Join-Path $root 'eng/local-package-feed/sdk-package-set.json') `
        -Raw | ConvertFrom-Json
    $sdkPackageSetFiles = @(
        'Directory.Packages.props',
        'eng/local-package-feed/README.md',
        'eng/local-package-feed/sdk-package-set.json'
        @($sdkPackageSetManifest.packages | ForEach-Object {
                "eng/local-package-feed/$($_.fileName)"
            })
    )
    $sdkPackageSetOutput = Join-Path $temporaryRoot 'sdk-package-set.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles $sdkPackageSetFiles `
        -OutputPath $sdkPackageSetOutput `
        -GitHubOutputPath ''
    $sdkPackageSet = Get-Content $sdkPackageSetOutput -Raw | ConvertFrom-Json
    $sdkPackageSetCategories = @($sdkPackageSet.selectedCategories)
    if (@($sdkPackageSet.unclassifiedFiles).Count -ne 0 -or
        @($sdkPackageSet.requiredExplicitModes) -contains 'Full' -or
        -not [bool]$sdkPackageSet.deploymentAffected -or
        @('Architecture', 'Security', 'Business', 'DeploymentContract' |
            Where-Object { $sdkPackageSetCategories -notcontains $_ }).Count -ne 0 -or
        @($sdkPackageSetCategories | Where-Object {
                $_ -in @('Quality', 'CrossProject')
            }).Count -ne 0) {
        throw 'Governed SDK package-set inputs were not attributed to only the automatic release lanes.'
    }

    $unknownSdkPackage = 'eng/local-package-feed/IIoT.Edge.Module.Unknown.9.9.9.nupkg'
    $unknownSdkPackageOutput = Join-Path $temporaryRoot 'unknown-sdk-package.json'
    $unknownSdkPackageFailed = $false
    try {
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @($unknownSdkPackage) `
            -OutputPath $unknownSdkPackageOutput `
            -GitHubOutputPath ''
    } catch {
        $unknownSdkPackageFailed = $_.Exception.Message -match 'cannot safely attribute' -and
            $_.Exception.Message -match [regex]::Escape($unknownSdkPackage)
    }
    if (-not $unknownSdkPackageFailed) {
        throw 'An SDK package absent from the governed package-set manifest did not fail closed.'
    }
    $unknownSdkPackageSelection = Get-Content $unknownSdkPackageOutput -Raw | ConvertFrom-Json
    if (@($unknownSdkPackageSelection.unclassifiedFiles) -notcontains $unknownSdkPackage -or
        @($unknownSdkPackageSelection.requiredExplicitModes) -notcontains 'Full') {
        throw 'Unknown SDK package rejection is absent from selector evidence.'
    }

    $manualOutput = Join-Path $temporaryRoot 'manual.json'
    & $selector `
        -RepositoryRoot $root `
        -Mode Quality `
        -OutputPath $manualOutput `
        -GitHubOutputPath ''
    $manual = Get-Content $manualOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $manual
    if ([string]$manual.mode -cne 'Quality' -or
        @($manual.unclassifiedFiles).Count -ne 0 -or
        @($manual.selectedDotNetProjects.categories) -contains 'Business' -or
        @($manual.selectedDotNetProjects.categories) -contains 'DeploymentContract') {
        throw 'Explicit Quality selection leaked Business or DeploymentContract runners.'
    }

    $deploymentOutput = Join-Path $temporaryRoot 'deployment.json'
    & $selector -RepositoryRoot $root -Mode Deployment -ChangedFiles @(
        'src/Core/IIoT.Edge.Domain/Config/Aggregates/SystemConfigEntity.cs',
        'scripts/PublishEdgeRuntime.ps1') `
        -OutputPath $deploymentOutput -GitHubOutputPath ''
    $deployment = Get-Content $deploymentOutput -Raw | ConvertFrom-Json
    $deploymentNames = @($deployment.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'DeploymentContract' } |
        Select-Object -ExpandProperty projectName)
    if ([string]$deployment.mode -cne 'Deployment' -or
        -not [bool]$deployment.deploymentAffected -or
        $deploymentNames -notcontains 'IIoT.Edge.Deployment.Tests' -or
        $deploymentNames -notcontains 'IIoT.Edge.Platform.WindowsTests' -or
        @($deployment.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Security', 'DeploymentContract')
            }).Count -ne 0) {
        throw 'Edge deployment selection omitted an affected DeploymentContract runner or leaked Quality.'
    }

    $crossOutput = Join-Path $temporaryRoot 'cross.json'
    & $selector -RepositoryRoot $root -Mode CrossProject -ChangedFiles @() `
        -OutputPath $crossOutput -GitHubOutputPath ''
    $cross = Get-Content $crossOutput -Raw | ConvertFrom-Json
    if (@($cross.selectedDotNetProjects).Count -ne 2 -or
        @($cross.selectedDotNetProjects.categories | Where-Object { $_ -cne 'CrossProject' }).Count -ne 0) {
        throw 'CrossProject mode emitted a non-cross-project runner.'
    }

    $negativeOutput = Join-Path $temporaryRoot 'negative.json'
    $negativeFailed = $false
    try {
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @('src/Unowned.Business/Unknown.cs') `
            -OutputPath $negativeOutput `
            -GitHubOutputPath ''
    } catch {
        $negativeFailed = $_.Exception.Message -match 'cannot safely attribute' -and
            $_.Exception.Message -match 'src/Unowned\.Business/Unknown\.cs'
    }
    if (-not $negativeFailed) {
        throw 'Unknown business path did not fail closed with the file listed.'
    }
    $negative = Get-Content $negativeOutput -Raw | ConvertFrom-Json
    if (@($negative.unclassifiedFiles) -notcontains 'src/Unowned.Business/Unknown.cs') {
        throw 'Unknown business path is absent from selector evidence.'
    }

    $migrationFixture = New-DynamicBusinessFixture `
        -Parent $temporaryRoot `
        -Name 'business-migration'
    Remove-Item (Join-Path $migrationFixture.Root 'src/Tests/Widget.Retiring.Tests') -Recurse -Force
    Set-FixtureFile -FixtureRoot $migrationFixture.Root `
        -Path 'src/Tests/Widget.Moved.Tests/Widget.Moved.Tests.csproj' `
        -Content (New-TestProjectXml -Kind Unit -Owner Edge.Widget `
            -ProjectReference '../../Core/Widget/Widget.csproj')
    Set-FixtureFile -FixtureRoot $migrationFixture.Root `
        -Path 'src/Tests/NewCapability.Tests/NewCapability.Tests.csproj' `
        -Content (New-TestProjectXml -Kind Unit -Owner Edge.NewCapability `
            -ProjectReference '../../Core/Widget/Widget.csproj')
    Set-FixtureFile -FixtureRoot $migrationFixture.Root -Path 'IIoT.EdgeClient.slnx' -Content (
        New-SolutionXml -ProjectPaths @(
            'src/Core/Widget/Widget.csproj',
            'src/Core/Other/Other.csproj',
            'src/Tests/Architecture.Tests/Architecture.Tests.csproj',
            'src/Tests/Widget.Moved.Tests/Widget.Moved.Tests.csproj',
            'src/Tests/NewCapability.Tests/NewCapability.Tests.csproj',
            'src/Tests/Other.Tests/Other.Tests.csproj'))
    $migrationOutput = Join-Path $temporaryRoot 'business-migration.json'
    & $selector `
        -RepositoryRoot $migrationFixture.Root `
        -BaseRef $migrationFixture.Baseline `
        -ChangedFiles @(
            'IIoT.EdgeClient.slnx',
            'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj',
            'src/Tests/Widget.Retiring.Tests/RetiringCase.cs',
            'src/Tests/Widget.Moved.Tests/Widget.Moved.Tests.csproj',
            'src/Tests/NewCapability.Tests/NewCapability.Tests.csproj') `
        -OutputPath $migrationOutput `
        -GitHubOutputPath ''
    $migration = Get-Content $migrationOutput -Raw | ConvertFrom-Json
    $migrationBusinessNames = @($migration.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'Business' } |
        Select-Object -ExpandProperty projectName)
    if ($migrationBusinessNames -notcontains 'Widget.Moved.Tests' -or
        $migrationBusinessNames -notcontains 'NewCapability.Tests' -or
        $migrationBusinessNames -contains 'Other.Tests' -or
        @($migration.unclassifiedFiles).Count -ne 0 -or
        @($migration.requiredExplicitModes) -contains 'Full' -or
        @($migration.retiredBusinessTestProjects) -notcontains 'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj') {
        throw "Business runner add/move selection was not dynamic and owner-scoped: $($migrationBusinessNames -join ', ')"
    }

    $deploymentMigrationOutput = Join-Path $temporaryRoot 'business-migration-deployment.json'
    & $selector `
        -RepositoryRoot $migrationFixture.Root `
        -Mode Deployment `
        -BaseRef $migrationFixture.Baseline `
        -ChangedFiles @(
            'IIoT.EdgeClient.slnx',
            'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj',
            'src/Tests/Widget.Retiring.Tests/RetiringCase.cs',
            'src/Tests/Widget.Moved.Tests/Widget.Moved.Tests.csproj',
            'src/Tests/NewCapability.Tests/NewCapability.Tests.csproj') `
        -OutputPath $deploymentMigrationOutput `
        -GitHubOutputPath ''
    $deploymentMigration = Get-Content $deploymentMigrationOutput -Raw | ConvertFrom-Json
    if (@($deploymentMigration.selectedDotNetProjects).Count -ne 0 -or
        @($deploymentMigration.selectedCategories).Count -ne 0 -or
        @($deploymentMigration.unclassifiedFiles).Count -ne 0 -or
        @($deploymentMigration.requiredExplicitModes) -contains 'Full' -or
        @($deploymentMigration.deferredExplicitFiles | Where-Object {
                $_.StartsWith('Business:', [StringComparison]::Ordinal)
            }).Count -eq 0) {
        throw 'Deployment mode did not defer a Business-only runner migration without leaking Business or requiring Full.'
    }

    $retirementFixture = New-DynamicBusinessFixture `
        -Parent $temporaryRoot `
        -Name 'business-retirement' `
        -IncludeRemainingOwner
    Remove-Item (Join-Path $retirementFixture.Root 'src/Tests/Widget.Retiring.Tests') -Recurse -Force
    Remove-Item (Join-Path $retirementFixture.Root 'src/Orphans/UnownedBaselineFile.cs') -Force
    Set-FixtureFile -FixtureRoot $retirementFixture.Root -Path 'IIoT.EdgeClient.slnx' -Content (
        New-SolutionXml -ProjectPaths @(
            'src/Core/Widget/Widget.csproj',
            'src/Core/Other/Other.csproj',
            'src/Tests/Architecture.Tests/Architecture.Tests.csproj',
            'src/Tests/Widget.Remaining.Tests/Widget.Remaining.Tests.csproj',
            'src/Tests/Other.Tests/Other.Tests.csproj'))
    $retirementOutput = Join-Path $temporaryRoot 'business-retirement.json'
    & $selector `
        -RepositoryRoot $retirementFixture.Root `
        -BaseRef $retirementFixture.Baseline `
        -ChangedFiles @(
            'IIoT.EdgeClient.slnx',
            'src/Tests/Widget.Retiring.Tests/Widget.Retiring.Tests.csproj',
            'src/Tests/Widget.Retiring.Tests/RetiringCase.cs') `
        -OutputPath $retirementOutput `
        -GitHubOutputPath ''
    $retirement = Get-Content $retirementOutput -Raw | ConvertFrom-Json
    $retirementBusinessNames = @($retirement.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'Business' } |
        Select-Object -ExpandProperty projectName)
    if ($retirementBusinessNames.Count -ne 1 -or
        $retirementBusinessNames -notcontains 'Widget.Remaining.Tests' -or
        @($retirement.unclassifiedFiles).Count -ne 0 -or
        @($retirement.requiredExplicitModes) -contains 'Full') {
        throw "Deleted Business runner did not select only its surviving owner scope: $($retirementBusinessNames -join ', ')"
    }

    $baselineUnknownOutput = Join-Path $temporaryRoot 'baseline-unknown.json'
    $baselineUnknownFailed = $false
    try {
        & $selector `
            -RepositoryRoot $retirementFixture.Root `
            -BaseRef $retirementFixture.Baseline `
            -ChangedFiles @('src/Orphans/UnownedBaselineFile.cs') `
            -OutputPath $baselineUnknownOutput `
            -GitHubOutputPath ''
    } catch {
        $baselineUnknownFailed = $_.Exception.Message -match 'cannot safely attribute' -and
            $_.Exception.Message -match 'src/Orphans/UnownedBaselineFile\.cs'
    }
    if (-not $baselineUnknownFailed) {
        throw 'A deleted baseline file without a project/owner mapping did not fail closed.'
    }
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item $temporaryRoot -Recurse -Force
    }
}

$workflowText = Get-Content (Join-Path $root '.github/workflows/edge-smoke-build.yml') -Raw
$selectorText = Get-Content $selector -Raw
if ($selectorText -notmatch "'core\.quotepath=false'" -or
    $selectorText -notmatch 'StandardOutputEncoding\s*=\s*\$utf8' -or
    $selectorText -notmatch "'-z'" -or
    $selectorText -notmatch 'Split\(\[char\]0') {
    throw 'Edge selector Git path protocol is not fixed to unquoted UTF-8 NUL-delimited output.'
}
if ($workflowText -notmatch '\$selectorInputs\.Count\s+-gt\s+0[\s\S]*?Test-EdgeCiTestSelection\.ps1') {
    throw 'Edge default CI does not gate selector behavior tests on affected selector inputs.'
}
if ($workflowText -notmatch 'git\s+-c\s+core\.quotepath=false\s+diff\s+--name-only' -or
    $workflowText -notmatch '\[Console\]::OutputEncoding\s*=\s*\$utf8' -or
    $workflowText -notmatch '\$OutputEncoding\s*=\s*\$utf8') {
    throw 'Edge default CI does not force unquoted Git paths and UTF-8 PowerShell decoding.'
}
if ($workflowText -match "\`$env:CI_MODE\s+-ne\s+'default'" -or
    ($workflowText.Split('Test-EdgeCiTestSelection.ps1', [StringSplitOptions]::None).Length - 1) -ne 2) {
    throw 'Edge selector behavior tests are still wired to an unrelated explicit mode or cross-project job.'
}
if ($workflowText -notmatch 'if\s*\(\[string\]::IsNullOrWhiteSpace\(\$baseRef\)\)\s*\{\s*\$baseRef\s*=\s*''HEAD\^''') {
    throw 'Edge manual CI modes do not have a deterministic base ref.'
}
if ($workflowText -notmatch "\`$env:CI_MODE\s+-eq\s+'full'[\s\S]*?\`$parameters\.CollectCoverage\s*=\s*\`$true" -or
    $workflowText -match "\`$env:CI_MODE\s+-in\s+@\('quality',\s*'full'\)") {
    throw 'Edge CI coverage must remain exclusive to explicitly selected Full mode.'
}
if ($workflowText -notmatch [regex]::Escape(
        'src/Testing/IIoT.Edge.Module.TestPlugin.Companion/IIoT.Edge.Module.TestPlugin.Companion.csproj')) {
    throw 'Edge default CI does not restore the plugin-owned companion required by conformance tests.'
}
$offlineWorkflowText = Get-Content (Join-Path $root '.github/workflows/edge-pack-modules.yml') -Raw
if ($offlineWorkflowText.Contains('Test-EdgeCiTestSelection.ps1', [StringComparison]::Ordinal)) {
    throw 'The explicit offline artifact workflow must not rerun selector behavior tests.'
}

Write-Host 'EDGE_CI_SELECTION_BEHAVIOR_OK positive=1 utf8GitPaths=1 docs=1 sdkPackageSet=1 unknownSdkPackage=1 quality=1 deployment=1 cross=1 negative=1 businessTopology=1 deploymentBusinessTopology=1 retiredBusiness=1 baselineUnknown=1 workflowGate=1 fullCoverageOnly=1 companionRestore=1'
