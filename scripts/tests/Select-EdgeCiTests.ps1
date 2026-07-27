[CmdletBinding()]
param(
    [ValidateSet('Default', 'Deployment', 'Quality', 'CrossProject', 'Full')]
    [string]$Mode = 'Default',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$BaseRef,
    [string]$HeadRef = 'HEAD',
    [string[]]$ChangedFiles,
    [string]$OutputPath = 'artifacts/ci-selection.json',
    [string]$GitHubOutputPath = $env:GITHUB_OUTPUT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path.Replace('\', '/').Trim()
    while ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('../', [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($normalized)) {
        throw "Changed file must be a repository-relative path: '$Path'."
    }
    return $normalized
}

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    return [IO.Path]::GetRelativePath($Root, [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Invoke-GitUtf8 {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage,
        [switch]$NullDelimited
    )

    $utf8 = [Text.UTF8Encoding]::new($false)
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $Root
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8
    foreach ($argument in @('-c', 'core.quotepath=false') + $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "$FailureMessage Git did not start."
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $standardOutput.GetAwaiter().GetResult()
        $errorOutput = $standardError.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        $detail = $errorOutput.Trim()
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $output.Trim()
        }
        throw "$FailureMessage`n$detail"
    }
    if ([string]::IsNullOrEmpty($output)) {
        return @()
    }
    if ($NullDelimited) {
        return @($output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
    }
    return @([regex]::Split($output, '\r?\n') |
        Where-Object { -not [string]::IsNullOrEmpty($_) })
}

function ConvertFrom-SdkPackageSetManifest {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Label
    )

    try {
        $manifest = $Content | ConvertFrom-Json
    } catch {
        throw "$Label is not valid JSON: $($_.Exception.Message)"
    }

    $requiredProperties = @(
        'schemaVersion',
        'sdkRepository',
        'sdkSourceCommit',
        'version',
        'packages'
    )
    foreach ($property in $requiredProperties) {
        if ($manifest.PSObject.Properties.Name -notcontains $property) {
            throw "$Label is missing required property '$property'."
        }
    }
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.sdkRepository -cne 'ShuJinHao/IIoT.Edge.Sdk' -or
        [string]$manifest.sdkSourceCommit -notmatch '^[0-9a-f]{40}$' -or
        [string]$manifest.version -notmatch '^\d+\.\d+\.\d+$') {
        throw "$Label has an invalid SDK package-set identity."
    }

    $expectedIds = @(
        'IIoT.Edge.Module.Analyzers',
        'IIoT.Edge.Module.Contracts',
        'IIoT.Edge.Module.Sdk',
        'IIoT.Edge.UI.Shared'
    )
    $entries = @($manifest.packages)
    if ($entries.Count -ne $expectedIds.Count) {
        throw "$Label must list exactly the four governed SDK packages."
    }

    $validatedEntries = [Collections.Generic.List[object]]::new()
    foreach ($id in $expectedIds) {
        $matches = @($entries | Where-Object {
                $_.PSObject.Properties.Name -contains 'id' -and
                [string]$_.id -ceq $id
            })
        if ($matches.Count -ne 1) {
            throw "$Label must contain exactly one entry for '$id'."
        }
        $entry = $matches[0]
        foreach ($property in @('fileName', 'sha256', 'size')) {
            if ($entry.PSObject.Properties.Name -notcontains $property) {
                throw "$Label package '$id' is missing required property '$property'."
            }
        }

        $expectedFileName = "$id.$($manifest.version).nupkg"
        if ([string]$entry.fileName -cne $expectedFileName -or
            [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$' -or
            [int64]$entry.size -le 0) {
            throw "$Label package '$id' has an invalid governed filename or digest."
        }
        $validatedEntries.Add([pscustomobject]@{
                Id = $id
                FileName = $expectedFileName
                Sha256 = [string]$entry.sha256
                Size = [int64]$entry.size
            })
    }

    return [pscustomobject]@{
        Version = [string]$manifest.version
        Entries = @($validatedEntries)
    }
}

function Get-ControlledSdkPackageSetPaths {
    param(
        [Parameter(Mandatory)][string]$Root,
        [string]$BaselineRevision
    )

    $manifestRepositoryPath = 'eng/local-package-feed/sdk-package-set.json'
    $manifestPath = Join-Path $Root $manifestRepositoryPath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Governed SDK package-set manifest was not found: $manifestRepositoryPath"
    }
    $current = ConvertFrom-SdkPackageSetManifest `
        -Content (Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8) `
        -Label 'Current SDK package-set manifest'

    [xml]$centralPackages = Get-Content `
        -LiteralPath (Join-Path $Root 'Directory.Packages.props') `
        -Raw `
        -Encoding utf8
    foreach ($entry in $current.Entries) {
        $matches = @($centralPackages.SelectNodes(
                "/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='PackageVersion' and @Include='$($entry.Id)']"))
        if ($matches.Count -ne 1 -or
            [string]$matches[0].Version -cne $current.Version) {
            throw "Directory.Packages.props does not pin '$($entry.Id)' to governed SDK version '$($current.Version)'."
        }

        $packagePath = Join-Path $Root "eng/local-package-feed/$($entry.FileName)"
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            throw "Governed SDK package is missing: eng/local-package-feed/$($entry.FileName)"
        }
        $package = Get-Item -LiteralPath $packagePath
        $actualSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($package.Length -ne $entry.Size -or $actualSha256 -cne $entry.Sha256) {
            throw "Governed SDK package bytes do not match the manifest: eng/local-package-feed/$($entry.FileName)"
        }
    }

    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($fixedPath in @(
            'Directory.Packages.props',
            'eng/local-package-feed/README.md',
            $manifestRepositoryPath
        )) {
        [void]$allowed.Add($fixedPath)
    }
    foreach ($entry in $current.Entries) {
        [void]$allowed.Add("eng/local-package-feed/$($entry.FileName)")
    }

    if (-not [string]::IsNullOrWhiteSpace($BaselineRevision) -and
        $BaselineRevision -notmatch '^0+$') {
        [void](Invoke-GitUtf8 -Root $Root `
            -Arguments @('cat-file', '-e', "${BaselineRevision}^{commit}") `
            -FailureMessage "Unable to read SDK package-set baseline revision '$BaselineRevision':")
        $baselineContent = @()
        try {
            $baselineContent = @(Invoke-GitUtf8 -Root $Root `
                -Arguments @('show', "${BaselineRevision}:$manifestRepositoryPath") `
                -FailureMessage "Unable to read baseline SDK package-set manifest:")
        } catch {
            $baselineContent = @()
        }
        if ($baselineContent.Count -gt 0) {
            $baseline = ConvertFrom-SdkPackageSetManifest `
                -Content ($baselineContent -join [Environment]::NewLine) `
                -Label "Baseline SDK package-set manifest at '$BaselineRevision'"
            foreach ($entry in $baseline.Entries) {
                [void]$allowed.Add("eng/local-package-feed/$($entry.FileName)")
            }
        }
    }

    return $allowed
}

function Get-DirectProjectProperty {
    param(
        [Parameter(Mandatory)][xml]$Project,
        [Parameter(Mandatory)][string]$Name
    )

    $nodes = @($Project.SelectNodes(
        "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']"))
    $values = @($nodes | ForEach-Object { ([string]$_.InnerText).Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($values.Count -eq 0) {
        return ''
    }
    return [string]$values[-1]
}

function Get-CiCategory {
    param(
        [Parameter(Mandatory)][string]$TestKind,
        [Parameter(Mandatory)][string]$Concern
    )

    if ($TestKind -ceq 'Architecture') {
        return 'Architecture'
    }
    if ($Concern -ceq 'Security') {
        return 'Security'
    }
    if ($TestKind -ceq 'Deployment') {
        return 'DeploymentContract'
    }
    return 'Business'
}

function Get-ProjectReferences {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][object]$Project
    )

    $references = [Collections.Generic.List[string]]::new()
    foreach ($node in @($Project.Xml.SelectNodes("//*[local-name()='ProjectReference']"))) {
        $include = ([string]$node.Include).Trim()
        if ([string]::IsNullOrWhiteSpace($include) -or $include.Contains('$(')) {
            continue
        }
        $resolved = [IO.Path]::GetFullPath((Join-Path $Project.Directory $include))
        if (-not $resolved.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
            $resolved -cne $Root) {
            throw "ProjectReference escapes the repository: project=$($Project.Path) include=$include"
        }
        $references.Add((Get-RepositoryRelativePath -Root $Root -Path $resolved))
    }
    return @($references | Sort-Object -Unique)
}

function Get-ReferenceClosure {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][hashtable]$ProjectsByPath
    )

    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($ProjectPath)
    while ($pending.Count -gt 0) {
        $candidate = $pending.Pop()
        if (-not $visited.Add($candidate) -or -not $ProjectsByPath.ContainsKey($candidate)) {
            continue
        }
        foreach ($reference in @($ProjectsByPath[$candidate].References)) {
            $pending.Push([string]$reference)
        }
    }
    return @($visited | Sort-Object)
}

function Get-ProjectDefinition {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][xml]$ProjectXml
    )

    $fullPath = [IO.Path]::GetFullPath((Join-Path $Root $Path))
    return [pscustomobject]@{
        Path = $Path
        Name = [IO.Path]::GetFileNameWithoutExtension($Path)
        Directory = (Split-Path $fullPath -Parent)
        RelativeDirectory = (Split-Path $Path -Parent).Replace('\', '/')
        Xml = $ProjectXml
        IsTest = (Get-DirectProjectProperty -Project $ProjectXml -Name 'IsTestProject') -ceq 'true'
        TestKind = Get-DirectProjectProperty -Project $ProjectXml -Name 'TestKind'
        Runtime = Get-DirectProjectProperty -Project $ProjectXml -Name 'TestRuntime'
        Concern = Get-DirectProjectProperty -Project $ProjectXml -Name 'TestConcern'
        Owner = Get-DirectProjectProperty -Project $ProjectXml -Name 'TestOwner'
        Category = ''
        References = @()
    }
}

function Get-SolutionProjectPaths {
    param([Parameter(Mandatory)][xml]$Solution)

    return @($Solution.SelectNodes("//*[local-name()='Project' and @Path]") |
        ForEach-Object { ConvertTo-RepositoryPath ([string]$_.Path) } |
        Sort-Object -Unique)
}

function Get-BaselineState {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Revision
    )

    [void](Invoke-GitUtf8 -Root $Root `
        -Arguments @('cat-file', '-e', "${Revision}^{commit}") `
        -FailureMessage "Unable to read Edge CI baseline revision '$Revision':")
    $treeOutput = @(Invoke-GitUtf8 -Root $Root `
        -Arguments @('ls-tree', '-r', '-z', '--name-only', $Revision, '--', 'src') `
        -FailureMessage "Unable to enumerate Edge CI baseline tree '$Revision':" `
        -NullDelimited)

    $projects = [Collections.Generic.List[object]]::new()
    foreach ($path in @($treeOutput | Where-Object { ([string]$_).EndsWith('.csproj', [StringComparison]::Ordinal) })) {
        $projectPath = ConvertTo-RepositoryPath ([string]$path)
        $projectContent = @(Invoke-GitUtf8 -Root $Root `
            -Arguments @('show', "${Revision}:$projectPath") `
            -FailureMessage "Unable to read baseline project '$projectPath' from '$Revision':")
        try {
            [xml]$projectXml = ($projectContent -join [Environment]::NewLine)
        } catch {
            throw "Baseline project is not valid XML: revision=$Revision project=$projectPath error=$($_.Exception.Message)"
        }
        $projects.Add((Get-ProjectDefinition -Root $Root -Path $projectPath -ProjectXml $projectXml))
    }

    $projectsByPath = @{}
    foreach ($project in $projects) {
        if ($project.IsTest) {
            $project.Category = Get-CiCategory `
                -TestKind $project.TestKind `
                -Concern $project.Concern
        }
        $projectsByPath[$project.Path] = $project
    }
    foreach ($project in $projects) {
        $project.References = @(Get-ProjectReferences -Root $Root -Project $project)
    }

    $solutionContent = @(Invoke-GitUtf8 -Root $Root `
        -Arguments @('show', "${Revision}:IIoT.EdgeClient.slnx") `
        -FailureMessage "Unable to read baseline Edge solution from '$Revision':")
    try {
        [xml]$solutionXml = ($solutionContent -join [Environment]::NewLine)
    } catch {
        throw "Baseline Edge solution is not valid XML: revision=$Revision error=$($_.Exception.Message)"
    }

    return [pscustomobject]@{
        Projects = @($projects)
        ProjectsByPath = $projectsByPath
        ProjectDirectories = @($projects |
            Sort-Object @{ Expression = { $_.RelativeDirectory.Length }; Descending = $true })
        SolutionProjectPaths = @(Get-SolutionProjectPaths -Solution $solutionXml)
    }
}

function Add-SelectedProject {
    param(
        [Parameter(Mandatory)][hashtable]$Selected,
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][string]$Reason,
        [string]$Category = $Project.Category
    )

    if (-not $Selected.ContainsKey($Project.Path)) {
        $Selected[$Project.Path] = [ordered]@{
            path = $Project.Path
            projectName = $Project.Name
            runtime = $Project.Runtime
            categories = [Collections.Generic.List[string]]::new()
            testFilter = ''
            reasons = [Collections.Generic.List[string]]::new()
        }
    }
    if (-not $Selected[$Project.Path].categories.Contains($Category)) {
        $Selected[$Project.Path].categories.Add($Category)
    }
    if (-not $Selected[$Project.Path].reasons.Contains($Reason)) {
        $Selected[$Project.Path].reasons.Add($Reason)
    }
}

function Get-BusinessReplacementProjects {
    param(
        [Parameter(Mandatory)][object]$BaselineProject,
        [Parameter(Mandatory)][object[]]$CurrentTestProjects,
        [Parameter(Mandatory)][hashtable]$CurrentProjectClosures
    )

    $businessProjects = @($CurrentTestProjects | Where-Object Category -ceq 'Business')
    $matches = [Collections.Generic.List[object]]::new()
    foreach ($project in @($businessProjects | Where-Object {
                $_.Name -ceq $BaselineProject.Name -or
                (-not [string]::IsNullOrWhiteSpace([string]$BaselineProject.Owner) -and
                    $_.Owner -ceq $BaselineProject.Owner)
            })) {
        $matches.Add($project)
    }

    if ($matches.Count -eq 0) {
        $ownedSourceReferences = @($BaselineProject.References | Where-Object {
                -not $_.StartsWith('src/Tests/', [StringComparison]::Ordinal) -and
                -not $_.StartsWith('src/Testing/', [StringComparison]::Ordinal)
            })
        if ($ownedSourceReferences.Count -gt 0) {
            foreach ($project in $businessProjects) {
                $closure = @($CurrentProjectClosures[$project.Path])
                if (@($ownedSourceReferences | Where-Object { $closure -contains $_ }).Count -gt 0) {
                    $matches.Add($project)
                }
            }
        }
    }

    return @($matches | Sort-Object Path -Unique)
}

function Add-BaselineBusinessImpact {
    param(
        [Parameter(Mandatory)][hashtable]$Selected,
        [Parameter(Mandatory)][object]$BaselineProject,
        [Parameter(Mandatory)][object[]]$CurrentTestProjects,
        [Parameter(Mandatory)][hashtable]$CurrentProjectClosures,
        [Parameter(Mandatory)][string]$Mode,
        [Parameter(Mandatory)][string]$Reason,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[string]]$DeferredFiles,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.HashSet[string]]$RetiredBusinessProjects
    )

    if ($Mode -eq 'Deployment') {
        $DeferredFiles.Add("Business:$Reason")
        return $true
    }

    $ownedSourceReferences = @($BaselineProject.References | Where-Object {
            -not $_.StartsWith('src/Tests/', [StringComparison]::Ordinal) -and
            -not $_.StartsWith('src/Testing/', [StringComparison]::Ordinal)
        })
    $hasOwnerEvidence = -not [string]::IsNullOrWhiteSpace([string]$BaselineProject.Owner) -or
        $ownedSourceReferences.Count -gt 0
    if (-not $hasOwnerEvidence) {
        return $false
    }

    foreach ($replacement in @(Get-BusinessReplacementProjects `
                -BaselineProject $BaselineProject `
                -CurrentTestProjects $CurrentTestProjects `
                -CurrentProjectClosures $CurrentProjectClosures)) {
        Add-SelectedProject -Selected $Selected -Project $replacement `
            -Category Business -Reason "affected-retired-test:$($BaselineProject.Path)"
    }
    [void]$RetiredBusinessProjects.Add($BaselineProject.Path)
    return $true
}

function Add-AutomaticReleaseLaneImpact {
    param(
        [Parameter(Mandatory)][hashtable]$Selected,
        [Parameter(Mandatory)][object[]]$TestProjects,
        [Parameter(Mandatory)][string]$Mode,
        [Parameter(Mandatory)][string]$Reason,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[string]]$DeferredFiles
    )

    foreach ($project in @($TestProjects | Where-Object {
                $_.Category -in @(
                    'Architecture',
                    'Security',
                    'Business',
                    'DeploymentContract')
            })) {
        if ($Mode -eq 'Deployment' -and $project.Category -ceq 'Business') {
            $DeferredFiles.Add("Business:${Reason}:$($project.Path)")
            continue
        }
        Add-SelectedProject -Selected $Selected -Project $project `
            -Category $project.Category -Reason $Reason
    }
}

$root = (Resolve-Path $RepositoryRoot).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path (Join-Path $root 'IIoT.EdgeClient.slnx') -PathType Leaf)) {
    throw "Edge repository root is invalid: $root"
}

if (-not $PSBoundParameters.ContainsKey('ChangedFiles')) {
    if ($Mode -eq 'Deployment') {
        throw 'Deployment CI selection requires an explicit ChangedFiles set from the exact-SHA deployment impact plan.'
    } elseif ($Mode -ne 'Default') {
        $ChangedFiles = @()
    } else {
        if ([string]::IsNullOrWhiteSpace($BaseRef) -or $BaseRef -match '^0+$') {
            throw 'Default CI selection requires a non-zero BaseRef. Use workflow_dispatch mode Full for an initial branch history.'
        }
        $ChangedFiles = @(Invoke-GitUtf8 -Root $root `
            -Arguments @(
                'diff',
                '--no-renames',
                '--name-only',
                '-z',
                '--diff-filter=ACMRTUXBD',
                "$BaseRef...$HeadRef") `
            -FailureMessage "Unable to calculate changed files for $BaseRef...${HeadRef}:" `
            -NullDelimited)
    }
}
$changed = @($ChangedFiles |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    ForEach-Object { ConvertTo-RepositoryPath ([string]$_) } |
    Sort-Object -Unique)

$allProjects = [Collections.Generic.List[object]]::new()
foreach ($projectFile in @(Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' -File -Recurse |
        Sort-Object FullName)) {
    [xml]$projectXml = Get-Content $projectFile.FullName -Raw
    $path = Get-RepositoryRelativePath -Root $root -Path $projectFile.FullName
    $allProjects.Add((Get-ProjectDefinition -Root $root -Path $path -ProjectXml $projectXml))
}

$projectsByPath = @{}
foreach ($project in $allProjects) {
    if ($project.IsTest) {
        $project.Category = Get-CiCategory `
            -TestKind $project.TestKind `
            -Concern $project.Concern
        if ($project.Category -notin @(
                'Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')) {
            throw "Edge test project has an invalid CI category: project=$($project.Path) category=$($project.Category)"
        }
    }
    $projectsByPath[$project.Path] = $project
}
foreach ($project in $allProjects) {
    $project.References = @(Get-ProjectReferences -Root $root -Project $project)
}

$testProjects = @($allProjects | Where-Object IsTest | Sort-Object Path)
$selected = @{}
foreach ($project in $testProjects) {
    if ($Mode -ne 'Deployment' -and $project.Category -ceq 'Architecture') {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Architecture -Reason 'mandatory-architecture'
    }
    if ($Mode -ne 'Deployment' -and $project.Category -ceq 'Security') {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Security -Reason 'mandatory-security'
    }
}

$deploymentAffected = $false
$unclassified = [Collections.Generic.List[string]]::new()
$deferredFiles = [Collections.Generic.List[string]]::new()
$requiredExplicitMode = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$retiredBusinessProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

if ($Mode -eq 'Quality') {
    foreach ($project in @($testProjects | Where-Object Category -ceq 'Quality')) {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Quality -Reason 'manual-quality'
    }
} elseif ($Mode -eq 'Full') {
    foreach ($project in $testProjects) {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category $project.Category -Reason 'manual-full'
    }
    $deploymentAffected = $true
} elseif ($Mode -eq 'CrossProject') {
    $selected.Clear()
    foreach ($projectName in @(
            'IIoT.Edge.Cloud.ContractTests',
            'IIoT.Edge.Cloud.ContractFilesystemTests')) {
        $matches = @($testProjects | Where-Object Name -eq $projectName)
        if ($matches.Count -ne 1) {
            throw "CrossProject mode requires exactly one $projectName project."
        }
        Add-SelectedProject -Selected $selected -Project $matches[0] `
            -Category CrossProject -Reason 'manual-cross-project'
    }
} else {
    $projectClosures = @{}
    foreach ($testProject in $testProjects) {
        $projectClosures[$testProject.Path] = @(Get-ReferenceClosure `
            -ProjectPath $testProject.Path `
            -ProjectsByPath $projectsByPath)
    }
    $projectDirectories = @($allProjects |
        Sort-Object @{ Expression = { $_.RelativeDirectory.Length }; Descending = $true })
    $requiresBaselineState = $changed -contains 'IIoT.EdgeClient.slnx'
    if (-not $requiresBaselineState) {
        foreach ($candidateFile in @($changed | Where-Object {
                    $_.StartsWith('src/', [StringComparison]::Ordinal)
                })) {
            $currentOwner = @($projectDirectories | Where-Object {
                    $candidateFile -ceq $_.Path -or
                    $candidateFile.StartsWith("$($_.RelativeDirectory)/", [StringComparison]::Ordinal)
                } | Select-Object -First 1)
            if ($currentOwner.Count -eq 0) {
                $requiresBaselineState = $true
                break
            }
        }
    }
    $baselineState = $null
    if ($requiresBaselineState -and
        -not [string]::IsNullOrWhiteSpace($BaseRef) -and
        $BaseRef -notmatch '^0+$') {
        $baselineState = Get-BaselineState -Root $root -Revision $BaseRef
    }

    $controlledSdkPackageSetPaths = $null
    foreach ($file in $changed) {
        if ($file -match '^(?:docs/|AGENTS\.md$|README(?:\.[^/]+)?$|LICENSE(?:\.[^/]+)?$)') {
            continue
        }
        if ($file -match '^(?:\.github/workflows/|scripts/tests/)') {
            continue
        }
        if ($file -ceq 'Directory.Packages.props' -or
            $file.StartsWith('eng/local-package-feed/', [StringComparison]::Ordinal)) {
            if ($null -eq $controlledSdkPackageSetPaths) {
                $controlledSdkPackageSetPaths = Get-ControlledSdkPackageSetPaths `
                    -Root $root `
                    -BaselineRevision $BaseRef
            }
            if (-not $controlledSdkPackageSetPaths.Contains($file)) {
                $unclassified.Add($file)
                [void]$requiredExplicitMode.Add('Full')
                continue
            }

            $deploymentAffected = $true
            Add-AutomaticReleaseLaneImpact `
                -Selected $selected `
                -TestProjects $testProjects `
                -Mode $Mode `
                -Reason "affected-sdk-package-set:$file" `
                -DeferredFiles $deferredFiles
            continue
        }
        if ($file -match '^(?:global\.json$|Directory\.Build\.(?:props|targets)$|Directory\.Packages\.targets$)') {
            $unclassified.Add($file)
            [void]$requiredExplicitMode.Add('Full')
            continue
        }
        if ($file -ceq 'IIoT.EdgeClient.slnx') {
            if ($null -eq $baselineState) {
                $unclassified.Add($file)
                continue
            }

            [xml]$currentSolution = Get-Content (Join-Path $root 'IIoT.EdgeClient.slnx') -Raw
            $currentSolutionPaths = @(Get-SolutionProjectPaths -Solution $currentSolution)
            $baselineSolutionPaths = @($baselineState.SolutionProjectPaths)
            $solutionDelta = @(
                @($currentSolutionPaths | Where-Object { $baselineSolutionPaths -notcontains $_ })
                @($baselineSolutionPaths | Where-Object { $currentSolutionPaths -notcontains $_ })
            ) | Sort-Object -Unique
            $solutionSafelyAttributed = $true

            foreach ($projectPath in $solutionDelta) {
                $currentProject = if ($projectsByPath.ContainsKey($projectPath)) {
                    $projectsByPath[$projectPath]
                } else {
                    $null
                }
                $baselineProject = if ($baselineState.ProjectsByPath.ContainsKey($projectPath)) {
                    $baselineState.ProjectsByPath[$projectPath]
                } else {
                    $null
                }
                $classificationProject = if ($null -ne $currentProject) {
                    $currentProject
                } else {
                    $baselineProject
                }
                if ($null -eq $classificationProject -or -not $classificationProject.IsTest) {
                    $solutionSafelyAttributed = $false
                    continue
                }

                switch ($classificationProject.Category) {
                    'Business' {
                        if ($null -ne $currentProject) {
                            if ($Mode -eq 'Deployment') {
                                $deferredFiles.Add("Business:$projectPath")
                            } else {
                                Add-SelectedProject -Selected $selected -Project $currentProject `
                                    -Category Business -Reason "affected-solution:$projectPath"
                            }
                        } elseif (-not (Add-BaselineBusinessImpact `
                                    -Selected $selected `
                                    -BaselineProject $baselineProject `
                                    -CurrentTestProjects $testProjects `
                                    -CurrentProjectClosures $projectClosures `
                                    -Mode $Mode `
                                    -Reason $projectPath `
                                    -DeferredFiles $deferredFiles `
                                    -RetiredBusinessProjects $retiredBusinessProjects)) {
                            $solutionSafelyAttributed = $false
                        }
                    }
                    'Architecture' {
                        if ($null -eq $currentProject) {
                            $solutionSafelyAttributed = $false
                        } else {
                            Add-SelectedProject -Selected $selected -Project $currentProject `
                                -Category Architecture -Reason "affected-solution:$projectPath"
                        }
                    }
                    'Security' {
                        if ($null -eq $currentProject) {
                            $solutionSafelyAttributed = $false
                        } else {
                            Add-SelectedProject -Selected $selected -Project $currentProject `
                                -Category Security -Reason "affected-solution:$projectPath"
                        }
                    }
                    'DeploymentContract' {
                        $deploymentAffected = $true
                        if ($null -eq $currentProject) {
                            $solutionSafelyAttributed = $false
                        } else {
                            Add-SelectedProject -Selected $selected -Project $currentProject `
                                -Category DeploymentContract -Reason "affected-solution:$projectPath"
                        }
                    }
                    'Quality' {
                        $deferredFiles.Add("Quality:$projectPath")
                        [void]$requiredExplicitMode.Add('Quality')
                    }
                    'CrossProject' {
                        $deferredFiles.Add("CrossProject:$projectPath")
                        [void]$requiredExplicitMode.Add('CrossProject')
                    }
                    default {
                        $solutionSafelyAttributed = $false
                    }
                }
            }
            if (-not $solutionSafelyAttributed) {
                $unclassified.Add($file)
            }
            continue
        }
        if ($file.StartsWith('deploy/', [StringComparison]::Ordinal) -or
            ($file.StartsWith('scripts/', [StringComparison]::Ordinal) -and
                -not $file.StartsWith('scripts/tests/', [StringComparison]::Ordinal))) {
            $deploymentAffected = $true
            $deploymentProjects = @($testProjects | Where-Object Category -ceq 'DeploymentContract')
            if ($deploymentProjects.Count -eq 0) {
                $unclassified.Add($file)
            } else {
                foreach ($deploymentProject in $deploymentProjects) {
                    Add-SelectedProject -Selected $selected -Project $deploymentProject `
                        -Category DeploymentContract -Reason "affected:$file"
                }
            }
            continue
        }

        $owner = @($projectDirectories | Where-Object {
                $file -ceq $_.Path -or
                $file.StartsWith("$($_.RelativeDirectory)/", [StringComparison]::Ordinal)
            } | Select-Object -First 1)
        if ($owner.Count -eq 1) {
            if ($owner[0].IsTest) {
                switch ($owner[0].Category) {
                    'Architecture' {
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category Architecture -Reason "affected-test:$file"
                    }
                    'Security' {
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category Security -Reason "affected-test:$file"
                    }
                    'Business' {
                        if ($Mode -eq 'Deployment') {
                            $deferredFiles.Add("Business:$file")
                        } else {
                            Add-SelectedProject -Selected $selected -Project $owner[0] `
                                -Category Business -Reason "affected-test:$file"
                        }
                    }
                    'DeploymentContract' {
                        $deploymentAffected = $true
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category DeploymentContract -Reason "affected-test:$file"
                    }
                    'Quality' {
                        $deferredFiles.Add("Quality:$file")
                        [void]$requiredExplicitMode.Add('Quality')
                    }
                    'CrossProject' {
                        $deferredFiles.Add("CrossProject:$file")
                        [void]$requiredExplicitMode.Add('CrossProject')
                    }
                }
                continue
            }

            $dependents = @($testProjects | Where-Object {
                    $projectClosures[$_.Path] -contains $owner[0].Path
                })
            $businessDependents = @($dependents | Where-Object Category -ceq 'Business')
            $mandatoryDependents = @($dependents | Where-Object {
                    $_.Category -in @('Architecture', 'Security')
                })
            if ($Mode -eq 'Deployment') {
                foreach ($dependent in @($mandatoryDependents | Where-Object {
                            $_.Category -ceq 'Architecture'
                        })) {
                    Add-SelectedProject -Selected $selected -Project $dependent `
                        -Category Architecture -Reason "affected-architecture:$($owner[0].Path)"
                }
                foreach ($dependent in @($mandatoryDependents | Where-Object {
                            $_.Category -ceq 'Security'
                        })) {
                    Add-SelectedProject -Selected $selected -Project $dependent `
                        -Category Security -Reason "affected-security:$($owner[0].Path)"
                }
                continue
            }
            if ($businessDependents.Count -eq 0 -and $mandatoryDependents.Count -eq 0) {
                $qualityOnly = @($dependents | Where-Object Category -ceq 'Quality').Count -gt 0
                $crossOnly = @($dependents | Where-Object Category -ceq 'CrossProject').Count -gt 0
                if ($qualityOnly) {
                    $deferredFiles.Add("Quality:$file")
                    [void]$requiredExplicitMode.Add('Quality')
                }
                if ($crossOnly) {
                    $deferredFiles.Add("CrossProject:$file")
                    [void]$requiredExplicitMode.Add('CrossProject')
                }
                if (-not $qualityOnly -and -not $crossOnly) {
                    $unclassified.Add($file)
                }
                continue
            }
            foreach ($dependent in $businessDependents) {
                Add-SelectedProject -Selected $selected -Project $dependent `
                    -Category Business -Reason "affected:$($owner[0].Path)"
            }
            continue
        }

        if ($null -ne $baselineState) {
            $baselineOwner = @($baselineState.ProjectDirectories | Where-Object {
                    $file -ceq $_.Path -or
                    $file.StartsWith("$($_.RelativeDirectory)/", [StringComparison]::Ordinal)
                } | Select-Object -First 1)
            if ($baselineOwner.Count -eq 1 -and
                $baselineOwner[0].IsTest -and
                $baselineOwner[0].Category -ceq 'Business') {
                if (Add-BaselineBusinessImpact `
                        -Selected $selected `
                        -BaselineProject $baselineOwner[0] `
                        -CurrentTestProjects $testProjects `
                        -CurrentProjectClosures $projectClosures `
                        -Mode $Mode `
                        -Reason $file `
                        -DeferredFiles $deferredFiles `
                        -RetiredBusinessProjects $retiredBusinessProjects) {
                    continue
                }
            }
        }

        $unclassified.Add($file)
    }
}

$selectedProjects = @($selected.Values | Sort-Object path | ForEach-Object {
        [ordered]@{
            path = [string]$_.path
            projectName = [string]$_.projectName
            runtime = [string]$_.runtime
            categories = @($_.categories | Sort-Object)
            testFilter = [string]$_.testFilter
            reasons = @($_.reasons | Sort-Object)
        }
    })
$requiresDocker = @($selectedProjects | Where-Object {
        $_.runtime -in @('Aspire', 'Postgres', 'Redis', 'RabbitMQ', 'Docker')
    }).Count -gt 0

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
}
[void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
$document = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    mode = $Mode
    baseRef = $BaseRef
    headRef = $HeadRef
    changedFiles = $changed
    discoveredTestProjects = $testProjects.Count
    selectedDotNetProjects = $selectedProjects
    selectedCategories = @($selectedProjects |
        ForEach-Object { @($_.categories) } |
        Sort-Object -Unique)
    deploymentAffected = $deploymentAffected
    requiresDocker = $requiresDocker
    deferredExplicitFiles = @($deferredFiles | Sort-Object -Unique)
    retiredBusinessTestProjects = @($retiredBusinessProjects | Sort-Object)
    unclassifiedFiles = @($unclassified | Sort-Object -Unique)
    requiredExplicitModes = @($requiredExplicitMode | Sort-Object)
}
$document | ConvertTo-Json -Depth 10 | Set-Content $resolvedOutput -Encoding utf8

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    @(
        "selection_path=$OutputPath"
        "mode=$($Mode.ToLowerInvariant())"
        "deployment_affected=$($deploymentAffected.ToString().ToLowerInvariant())"
        "requires_docker=$($requiresDocker.ToString().ToLowerInvariant())"
    ) | Add-Content $GitHubOutputPath -Encoding utf8
}

if ($unclassified.Count -gt 0) {
    $nextStep = if ($requiredExplicitMode.Count -gt 0) {
        "An explicitly authorized mode is required after review: $(@($requiredExplicitMode | Sort-Object) -join ',')."
    } else {
        'Add or correct the source/test owner mapping before retrying.'
    }
    throw "Edge CI cannot safely attribute these files; selection stopped and no full-suite fallback was used. $nextStep`n$(@($unclassified | Sort-Object -Unique) -join "`n")"
}

Write-Host "EDGE_CI_SELECTION_OK mode=$Mode tests=$($selectedProjects.Count) deployment=$deploymentAffected output=$resolvedOutput"
