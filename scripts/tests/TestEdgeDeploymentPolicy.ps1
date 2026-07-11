Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}
function Forbid-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -match $Pattern) { throw $Message }
}

Require-Text 'scripts/LocalPublishAndDeploy.ps1' 'Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost' 'Edge host publication must require the workspace dispatch gate.'
Require-Text 'scripts/LocalPublishAndDeploy.ps1' "Formal stable Edge host releases only support Cloud Human HTTP publication" 'Edge host publication must remain a Mac-build-to-Cloud-HTTP workflow.'
Require-Text 'scripts/PublishEdgePluginRelease.ps1' 'Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin' 'Edge plugin publication must remain independent from the host release.'
Require-Text 'scripts/TestEdgeDeploymentPreflight.ps1' 'RequirePushedHead' 'Edge production publication must require a pushed Git HEAD.'
Require-Text 'scripts/GetEdgePublishedState.ps1' 'hostSourceCommit' 'Edge incremental planning must inspect the published Windows-download baseline.'
Require-Text 'docs/客户端规则.md' 'Windows.*下载|下载.*Windows' 'Edge deployment definition must say that the server exposes artifacts for Windows download.'
Forbid-Text 'scripts/LocalPublishAndDeploy.ps1' "'scp'|'rsync'" 'The formal Edge host implementation must not restore scp/rsync transport.'
Write-Host 'Edge deployment policy architecture test passed.'
