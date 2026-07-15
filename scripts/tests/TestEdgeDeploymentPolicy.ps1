Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ruleId = 'EDGE-DEPLOY-SECURITY-001'
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

$privateAddressPattern = '\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b'
$certificateBypassPattern = 'DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback|TrustAllCertificates|SkipCertificateValidation'
$deploymentFiles = @(
    Get-ChildItem (Join-Path $root 'scripts') -File -Filter '*.ps1'
    Get-ChildItem (Join-Path $root 'src') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[/\\](?:Tests|Testing|bin|obj)[/\\]' }
) | Sort-Object FullName -Unique
$findings = [Collections.Generic.List[string]]::new()
foreach ($file in $deploymentFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $relativePath = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    if ($text -match $privateAddressPattern) {
        $findings.Add("$relativePath hard-codes a private production address.")
    }
    if ($text -match $certificateBypassPattern) {
        $findings.Add("$relativePath contains a certificate-validation bypass.")
    }
}
if ($findings.Count -gt 0) {
    throw "$ruleId deployment security policy failed:`n - $($findings -join "`n - ")"
}

Write-Host "$ruleId Edge deployment policy architecture test passed."
