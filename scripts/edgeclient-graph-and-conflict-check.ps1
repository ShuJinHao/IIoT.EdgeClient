param(
    [string]$RootPath = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath,
    [switch]$NoEmitToConsole
)

$ErrorActionPreference = 'Stop'

$edgeClientRoot = Join-Path -Path $RootPath -ChildPath 'IIoT.EdgeClient'
if (-not (Test-Path $edgeClientRoot)) {
    $edgeClientRoot = $RootPath
}

$sourceRoot = Join-Path -Path $edgeClientRoot -ChildPath 'src'
if (-not (Test-Path $sourceRoot)) {
    throw "无法找到源代码目录: $sourceRoot"
}

function Write-OutputBlock {
    param(
        [string]$Title,
        [System.Collections.Generic.List[string]]$Output
    )
    $Output.Add('')
    $Output.Add("## $Title")
}

$output = [System.Collections.Generic.List[string]]::new()
$output.Add('# EdgeClient Graph and Conflict Check')
$output.Add("检查时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")

$csprojFiles = Get-ChildItem -Path $sourceRoot -Recurse -Filter *.csproj -File
$output.Add('')
$output.Add("项目总数: $($csprojFiles.Count)")

$projects = foreach ($project in $csprojFiles) {
    [xml]$doc = Get-Content -Path $project.FullName

    $tfms = @()
    $tfmNodes = $doc.SelectNodes('/Project/PropertyGroup/TargetFramework | /Project/PropertyGroup/TargetFrameworks')
    foreach ($node in $tfmNodes) {
        if ($null -ne $node.InnerText -and $node.InnerText.Trim()) {
            $tfms += $node.InnerText.Trim()
        }
    }

    $useWpfNode = $doc.SelectSingleNode('/Project/PropertyGroup/UseWPF[normalize-space()]')
    $useWpfValue = if ($null -ne $useWpfNode) { $useWpfNode.InnerText.Trim() } else { '' }

    $refNodes = $doc.SelectNodes('/Project/ItemGroup/ProjectReference')
    $references = @()
    if ($refNodes) {
        foreach ($ref in $refNodes) {
            $references += $ref.Include
        }
    }

    [PSCustomObject]@{
        Project = $project.Name
        Path = $project.FullName
        TargetFramework = ($tfms -join ';')
        UseWPF = $useWpfValue
        References = $references
    }
}

Write-OutputBlock -Title '项目引用图' -Output $output
foreach ($p in $projects) {
    $output.Add("- $($p.Project)")
    $output.Add("  - Path: $($p.Path.Replace($edgeClientRoot + '\\', ''))")
    $output.Add("  - TargetFramework: $($p.TargetFramework)")
    $output.Add("  - UseWPF: $(if ([string]::IsNullOrWhiteSpace($p.UseWPF)) { '未设置' } else { $p.UseWPF })")
    if ($p.References.Count -gt 0) {
        $output.Add('  - ProjectReference:')
        foreach ($r in $p.References) {
            $output.Add("    - $r")
        }
    }
}

$wpfProjects = $projects | Where-Object { $_.UseWPF -eq 'true' }
Write-OutputBlock -Title 'WPF 工程' -Output $output
if ($wpfProjects.Count -eq 0) {
    $output.Add('- 未检测到 UseWPF=true 的项目')
} else {
    foreach ($p in $wpfProjects) {
        $output.Add("- $($p.Project)")
    }
}

$sourceFiles = Get-ChildItem -Path $edgeClientRoot -Recurse -File -Include *.cs,*.xaml,*.xaml.cs
$conflictLines = $sourceFiles | Select-String -Pattern '<<<<<<<', '=======', '>>>>>>>' -SimpleMatch
Write-OutputBlock -Title '合并冲突标记扫描' -Output $output
if ($null -eq $conflictLines) {
    $output.Add('- 未发现 `<<<<<<<`, `=======`, `>>>>>>>` 标记')
} else {
    $grouped = $conflictLines | Group-Object Path
    foreach ($group in $grouped) {
        $relative = $group.Name.Replace($edgeClientRoot + '\\', '')
        $output.Add("- $relative")
        foreach ($line in $group.Group) {
            $output.Add("  - 第 $($line.LineNumber) 行: $($line.Line.Trim())")
        }
    }
}

Write-OutputBlock -Title '同名符号扫描(可能的命名冲突风险)' -Output $output
$classCandidates = foreach ($f in (Get-ChildItem -Path $sourceRoot -Recurse -File -Filter *.cs)) {
    $raw = Get-Content -Path $f.FullName -Raw
    $namespaceMatch = [regex]::Match($raw, '(?m)^\\s*namespace\\s+([A-Za-z_][A-Za-z0-9_.]*)')
    $ns = if ($namespaceMatch.Success) { $namespaceMatch.Groups[1].Value } else { 'global' }

    [regex]::Matches($raw, '(?m)^\\s*(?:public|internal|private|protected|partial|static|abstract|sealed|async|readonly\\s+)*\\b(class|interface|struct|record|enum)\\s+([A-Za-z_][A-Za-z0-9_]*)') |
        ForEach-Object {
            [PSCustomObject]@{
                Symbol = "$ns.$($_.Groups[2].Value)"
                File = $f.FullName.Replace($edgeClientRoot + '\\', '')
            }
        }
}

$dupSymbols = $classCandidates | Group-Object Symbol | Where-Object { $_.Count -gt 1 }
if ($dupSymbols.Count -eq 0) {
    $output.Add('- 未发现明显重复符号（按简单扫描结果）')
} else {
    foreach ($dup in $dupSymbols) {
        $output.Add("- $($dup.Name):")
        foreach ($it in $dup.Group) {
            $output.Add("  - $($it.File)")
        }
    }
}

$reportText = $output -join "`r`n"

if ($OutputPath) {
    $outDir = Split-Path -Path $OutputPath -Parent
    if ([string]::IsNullOrWhiteSpace($outDir)) {
        $outDir = (Get-Location).Path
    }
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    Set-Content -Path $OutputPath -Value $reportText -Encoding UTF8
}

if (-not $NoEmitToConsole) {
    Write-Output $reportText
}
