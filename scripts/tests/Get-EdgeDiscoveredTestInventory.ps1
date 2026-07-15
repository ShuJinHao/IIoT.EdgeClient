[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$DiscoveredInventoryPath,
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$PathValue)
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

function Get-ListedTests {
    param([Parameter(Mandatory)][string]$Output)
    $collect = $false
    $tests = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Output, '\r?\n')) {
        if ($line -match 'Tests are available\s*:|测试可用\s*:|Tests disponibles\s*:|Tests disponibles sont\s*:') {
            $collect = $true
            continue
        }
        if ($collect -and $line -match '^\s{2,}\S') {
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^(Test Run|Total tests|Passed!|Failed!|警告|Warning)') { $tests.Add($trimmed) }
        }
    }
    return [string[]]@($tests | Sort-Object)
}

function Get-OverrideValue {
    param(
        [Parameter(Mandatory)][object]$Override,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$DefaultValue
    )
    if ($Override.PSObject.Properties.Name -contains $Name -and $null -ne $Override.$Name -and
        -not [string]::IsNullOrWhiteSpace([string]$Override.$Name)) {
        return $Override.$Name
    }
    return $DefaultValue
}

function Get-SourceClassNames {
    param([Parameter(Mandatory)][string]$SourcePath)
    $text = Get-Content $SourcePath -Raw
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$names.Add([IO.Path]::GetFileNameWithoutExtension($SourcePath))
    foreach ($match in [regex]::Matches($text, '(?m)\b(?:class|record\s+class)\s+([A-Za-z_][A-Za-z0-9_]*)')) {
        [void]$names.Add($match.Groups[1].Value)
    }
    return [string[]]@($names)
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) { $InventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json' }
if ([string]::IsNullOrWhiteSpace($DiscoveredInventoryPath)) { $DiscoveredInventoryPath = Join-Path $PSScriptRoot 'discovered-test-inventory.json' }

& (Join-Path $PSScriptRoot 'Get-EdgeTestInventory.ps1') -RepositoryRoot $RepositoryRoot -InventoryPath $InventoryPath
$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 30
if ($inventory.PSObject.Properties.Name -notcontains 'defaultRegressionIdSource' -or
    [string]$inventory.defaultRegressionIdSource -cne 'project.ruleId') {
    throw 'EDGE-TEST-DISCOVERY-001 defaultRegressionIdSource must be project.ruleId.'
}
$caseKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$cases = [System.Collections.Generic.List[object]]::new()
$projectSummaries = [System.Collections.Generic.List[object]]::new()

foreach ($project in @($inventory.projects)) {
    $projectPath = [string]$project.projectPath
    $projectName = [string]$project.projectName
    $output = & dotnet test $projectPath -c $Configuration --no-build --no-restore --list-tests --nologo -noAutoResponse 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-TEST-DISCOVERY-001 discovery failed for ${projectPath}:`n$(($output | Out-String).Trim())"
    }
    $identities = @(Get-ListedTests -Output ($output | Out-String))
    if ($identities.Count -eq 0) { throw "EDGE-TEST-DISCOVERY-001 discovery returned zero tests for $projectPath." }

    $overrideDescriptors = [System.Collections.Generic.List[object]]::new()
    foreach ($override in @($project.overrides)) {
        if ($null -eq $override) { continue }
        $source = if ($override.PSObject.Properties.Name -contains 'source') { [string]$override.source } else { '' }
        $casePattern = if ($override.PSObject.Properties.Name -contains 'casePattern') { [string]$override.casePattern } else { '' }
        $classNames = @()
        if (-not [string]::IsNullOrWhiteSpace($source)) {
            $sourcePath = Join-Path (Split-Path (Resolve-RepositoryPath $projectPath) -Parent) $source
            $classNames = @(Get-SourceClassNames $sourcePath)
        }
        if (-not [string]::IsNullOrWhiteSpace($casePattern)) {
            try { [void][regex]::new($casePattern) } catch { throw "EDGE-TEST-DISCOVERY-001 invalid casePattern '$casePattern' in $projectPath." }
        }
        $overrideDescriptors.Add([pscustomobject]@{
            Value = $override
            Source = $source
            CasePattern = $casePattern
            ClassNames = $classNames
            MatchCount = 0
        })
    }

    foreach ($identity in $identities) {
        $caseKey = "$projectName|$identity"
        if (-not $caseKeys.Add($caseKey)) {
            throw "EDGE-TEST-DISCOVERY-001 duplicate case identity '$caseKey'."
        }
        $matches = @($overrideDescriptors | Where-Object {
            if (-not [string]::IsNullOrWhiteSpace($_.CasePattern)) { return $identity -match $_.CasePattern }
            foreach ($className in @($_.ClassNames)) {
                if ($identity -match "(^|\.)$([regex]::Escape($className))(?=\.|$)") { return $true }
            }
            return $false
        })
        if ($matches.Count -gt 1) {
            throw "EDGE-TEST-DISCOVERY-001 case '$caseKey' matches multiple classification overrides."
        }
        $overrideDescriptor = if ($matches.Count -eq 1) { $matches[0] } else { $null }
        $override = if ($null -ne $overrideDescriptor) { $overrideDescriptor.Value } else { $null }
        if ($null -ne $overrideDescriptor) { $overrideDescriptor.MatchCount++ }
        $cases.Add([pscustomobject][ordered]@{
            caseKey = $caseKey
            assembly = $projectName
            projectPath = $projectPath
            identity = $identity
            source = if ($null -eq $overrideDescriptor -or [string]::IsNullOrWhiteSpace($overrideDescriptor.Source)) { $null } else { $overrideDescriptor.Source }
            testKind = [string]$project.testKind
            runtime = [string]$project.runtime
            runtimeDependencies = [string[]]@($project.runtimeDependencies)
            runnerMode = [string]$project.runnerMode
            cadence = [string]$project.cadence
            capability = if ($null -eq $override) { [string]$project.capability } else { [string](Get-OverrideValue $override 'capability' $project.capability) }
            risk = if ($null -eq $override) { [string]$project.risk } else { [string](Get-OverrideValue $override 'risk' $project.risk) }
            concern = if ($null -eq $override) { [string]$project.concern } else { [string](Get-OverrideValue $override 'concern' $project.concern) }
            profile = if ($null -eq $override) { [string]$project.profile } else { [string](Get-OverrideValue $override 'profile' $project.profile) }
            owner = if ($null -eq $override) { [string]$project.owner } else { [string](Get-OverrideValue $override 'owner' $project.owner) }
            ruleId = if ($null -eq $override) { [string]$project.ruleId } else { [string](Get-OverrideValue $override 'ruleId' $project.ruleId) }
            regressionId = if ($null -eq $override) { [string]$project.ruleId } else { [string]$override.regressionId }
            required = [bool]$project.required
            skipped = $false
        })
    }
    foreach ($descriptor in $overrideDescriptors) {
        if ($descriptor.MatchCount -eq 0) {
            $label = if (-not [string]::IsNullOrWhiteSpace($descriptor.Source)) { $descriptor.Source } else { $descriptor.CasePattern }
            throw "EDGE-TEST-DISCOVERY-001 override '$label' in $projectPath matches zero discovered cases."
        }
    }
    $projectSummaries.Add([pscustomobject][ordered]@{ projectPath = $projectPath; projectName = $projectName; discovered = $identities.Count })
    Write-Host "Discovered $($identities.Count): $projectPath"
}

$invalidRegressionIds = @($cases | Where-Object {
    [string]::IsNullOrWhiteSpace([string]$_.regressionId) -or
    [string]$_.regressionId -notmatch '^[A-Z0-9][A-Z0-9._-]+$'
})
if ($invalidRegressionIds.Count -gt 0) {
    throw "EDGE-TEST-DISCOVERY-001 every discovered case must have a stable regressionId; invalid=$($invalidRegressionIds.Count)."
}

$document = [pscustomobject][ordered]@{
    schemaVersion = 3
    defaultRegressionIdSource = 'project.ruleId'
    inventoryPath = 'scripts/tests/edge-test-inventory.json'
    configuration = $Configuration
    projectCount = $projectSummaries.Count
    caseCount = $cases.Count
    projects = [object[]]$projectSummaries
    cases = [object[]]$cases
}
$serialized = ($document | ConvertTo-Json -Depth 30) + "`n"
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = Resolve-RepositoryPath $OutputPath
    [void][IO.Directory]::CreateDirectory((Split-Path $resolvedOutputPath -Parent))
    [IO.File]::WriteAllText($resolvedOutputPath, $serialized, [Text.UTF8Encoding]::new($false))
}
if ($Update) {
    [IO.File]::WriteAllText((Resolve-RepositoryPath $DiscoveredInventoryPath), $serialized, [Text.UTF8Encoding]::new($false))
} else {
    $committed = Get-Content (Resolve-RepositoryPath $DiscoveredInventoryPath) -Raw | ConvertFrom-Json -Depth 30
    if ([int]$committed.schemaVersion -ne 3 -or [string]$committed.configuration -ne $Configuration -or
        [int]$committed.caseCount -ne $cases.Count -or
        ((@($committed.cases) | ConvertTo-Json -Depth 30) -cne ((@($cases) | ConvertTo-Json -Depth 30)))) {
        $actualByKey = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($case in @($cases)) { $actualByKey[[string]$case.caseKey] = $case }
        $committedByKey = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($case in @($committed.cases)) { $committedByKey[[string]$case.caseKey] = $case }
        $missing = @($committedByKey.Keys | Where-Object { -not $actualByKey.ContainsKey($_) } | Sort-Object)
        $unexpected = @($actualByKey.Keys | Where-Object { -not $committedByKey.ContainsKey($_) } | Sort-Object)
        $changed = @($committedByKey.Keys | Where-Object {
            $actualByKey.ContainsKey($_) -and
            (($committedByKey[$_] | ConvertTo-Json -Depth 30 -Compress) -cne
                ($actualByKey[$_] | ConvertTo-Json -Depth 30 -Compress))
        } | Sort-Object)
        $firstMissing = if ($missing.Count -eq 0) { '<none>' } else { $missing[0] }
        $firstUnexpected = if ($unexpected.Count -eq 0) { '<none>' } else { $unexpected[0] }
        $firstChanged = if ($changed.Count -eq 0) { '<none>' } else { $changed[0] }
        throw "EDGE-TEST-DISCOVERY-001 committed discovered test inventory is stale or classification changed: committedCases=$($committed.caseCount), actualCases=$($cases.Count), missing=$($missing.Count), unexpected=$($unexpected.Count), changed=$($changed.Count), firstMissing='$firstMissing', firstUnexpected='$firstUnexpected', firstChanged='$firstChanged'."
    }
}

Write-Host "Edge discovered test inventory passed: projects=$($projectSummaries.Count), cases=$($cases.Count), duplicates=0, emptyRegressionIds=0."
