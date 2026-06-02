$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sharedRoot = Join-Path $repoRoot "src/Shared/IIoT.Edge.UI.Shared"

if (-not (Test-Path $sharedRoot)) {
    Write-Error "Shared UI project was not found. Run this script from the IIoT.EdgeClient scripts directory."
}

$controlsDir = Join-Path $sharedRoot "Avalonia/Controls"
$edgeActionButton = Join-Path $controlsDir "Actions/EdgeActionButton.cs"
$edgeControls = Join-Path $sharedRoot "Avalonia/Styles/EdgeControls.axaml"
$edgeTheme = Join-Path $sharedRoot "Avalonia/Styles/EdgeTheme.axaml"
$edgeIcons = Join-Path $sharedRoot "Avalonia/Resources/EdgeIcons.axaml"
$edgeConverters = Join-Path $sharedRoot "Avalonia/Resources/EdgeConverters.axaml"
$convertersDir = Join-Path $sharedRoot "Avalonia/Converters"
$srcDir = Join-Path $repoRoot "src"

$controlFiles = @(Get-ChildItem -Path $controlsDir -Filter "*.cs" -Recurse -File)
if ($controlFiles.Count -ne 36) {
    Write-Error "Expected 36 shared control .cs files, found $($controlFiles.Count)."
}

$publicEdgeClasses = @(Select-String -Path $controlFiles.FullName -Pattern "^public .*class Edge")
if ($publicEdgeClasses.Count -ne 36) {
    Write-Error "Expected 36 public Edge* classes, found $($publicEdgeClasses.Count)."
}

$expectedPublicEdgeClasses = @(
    "EdgeAccountChip",
    "EdgeActionButton",
    "EdgeActionColumn",
    "EdgeBarLineChart",
    "EdgeCard",
    "EdgeChartPoint",
    "EdgeChartSeries",
    "EdgeCheckBox",
    "EdgeCheckColumn",
    "EdgeDataGrid",
    "EdgeDialogChrome",
    "EdgeFieldRow",
    "EdgeFilterComboBox",
    "EdgeFilterDatePicker",
    "EdgeHeaderBrand",
    "EdgeHeaderDivider",
    "EdgeListBox",
    "EdgeLogList",
    "EdgeLogListItem",
    "EdgeMetricCard",
    "EdgeNoticeBar",
    "EdgeScrollHost",
    "EdgeSectionHeader",
    "EdgeSegmentedNav",
    "EdgeSegmentedNavItem",
    "EdgeStatusChip",
    "EdgeStatusDot",
    "EdgeStatusListItem",
    "EdgeStatusPill",
    "EdgeSummaryItem",
    "EdgeTabControl",
    "EdgeTablePanel",
    "EdgeTemplateColumn",
    "EdgeTextBox",
    "EdgeTextColumn",
    "EdgeWindowButton"
)

$publicEdgeClassNames = @(
    $publicEdgeClasses | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w+)\b") {
            $Matches[1]
        }
    }
)

$unexpectedPublicEdgeClasses = @($publicEdgeClassNames | Where-Object { $_ -notin $expectedPublicEdgeClasses })
if ($unexpectedPublicEdgeClasses.Count -ne 0) {
    Write-Error "Unexpected public Edge classes found: $($unexpectedPublicEdgeClasses -join ', ')."
}

$missingPublicEdgeClasses = @($expectedPublicEdgeClasses | Where-Object { $_ -notin $publicEdgeClassNames })
if ($missingPublicEdgeClasses.Count -ne 0) {
    Write-Error "Expected public Edge classes missing: $($missingPublicEdgeClasses -join ', ')."
}

$expectedPublicEdgeEnums = @(
    "EdgeActionButtonIconPlacement",
    "EdgeActionButtonKind",
    "EdgeActionButtonRole",
    "EdgeActionButtonSize",
    "EdgeCardElevation",
    "EdgeCardPaddingMode",
    "EdgeCardSurface",
    "EdgeChartAxis",
    "EdgeChartSeriesKind",
    "EdgeDataGridDensity",
    "EdgeScrollHostVariant",
    "EdgeTablePanelDensity",
    "EdgeTablePanelSurface",
    "EdgeVisualStatus",
    "EdgeVisualVariant",
    "EdgeWindowButtonAction",
    "EdgeWindowButtonKind"
)

$publicEdgeEnumHits = @(Select-String -Path $controlFiles.FullName -Pattern "^public enum Edge")
$publicEdgeEnumNames = @(
    $publicEdgeEnumHits | ForEach-Object {
        if ($_.Line -match "\benum\s+(Edge\w+)\b") {
            $Matches[1]
        }
    }
)

$unexpectedPublicEdgeEnums = @($publicEdgeEnumNames | Where-Object { $_ -notin $expectedPublicEdgeEnums })
if ($unexpectedPublicEdgeEnums.Count -ne 0) {
    Write-Error "Unexpected public Edge enums found: $($unexpectedPublicEdgeEnums -join ', ')."
}

$missingPublicEdgeEnums = @($expectedPublicEdgeEnums | Where-Object { $_ -notin $publicEdgeEnumNames })
if ($missingPublicEdgeEnums.Count -ne 0) {
    Write-Error "Expected public Edge enums missing: $($missingPublicEdgeEnums -join ', ')."
}

$allowedButtonClasses = @("EdgeActionButton", "EdgeWindowButton")
$buttonClassHits = @(Select-String -Path $controlFiles.FullName -Pattern "^public .*class Edge.*Button\b")
$buttonClassNames = @(
    $buttonClassHits | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w*Button)\b") {
            $Matches[1]
        }
    }
)

$unexpectedButtonClasses = @($buttonClassNames | Where-Object { $_ -notin $allowedButtonClasses })
if ($unexpectedButtonClasses.Count -ne 0) {
    Write-Error "Unexpected Edge button controls found: $($unexpectedButtonClasses -join ', '). Use EdgeActionButton or EdgeWindowButton."
}

$missingButtonClasses = @($allowedButtonClasses | Where-Object { $_ -notin $buttonClassNames })
if ($missingButtonClasses.Count -ne 0) {
    Write-Error "Required Edge button controls missing: $($missingButtonClasses -join ', ')."
}

$allowedCardClasses = @("EdgeCard", "EdgeMetricCard")
$cardClassHits = @(Select-String -Path $controlFiles.FullName -Pattern "^public .*class Edge.*Card\b")
$cardClassNames = @(
    $cardClassHits | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w*Card)\b") {
            $Matches[1]
        }
    }
)

$unexpectedCardClasses = @($cardClassNames | Where-Object { $_ -notin $allowedCardClasses })
if ($unexpectedCardClasses.Count -ne 0) {
    Write-Error "Unexpected Edge card controls found: $($unexpectedCardClasses -join ', '). Use EdgeCard, EdgeMetricCard, and shared classes."
}

$missingCardClasses = @($allowedCardClasses | Where-Object { $_ -notin $cardClassNames })
if ($missingCardClasses.Count -ne 0) {
    Write-Error "Required Edge card controls missing: $($missingCardClasses -join ', ')."
}

$allowedMetricClasses = @("EdgeMetricCard", "EdgeSummaryItem")
$metricClassHits = @(Select-String -Path $controlFiles.FullName -Pattern "^public .*class Edge.*(Metric|Summary)")
$metricClassNames = @(
    $metricClassHits | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w*(Metric|Summary)\w*)\b") {
            $Matches[1]
        }
    }
)

$unexpectedMetricClasses = @($metricClassNames | Where-Object { $_ -notin $allowedMetricClasses })
if ($unexpectedMetricClasses.Count -ne 0) {
    Write-Error "Unexpected Edge metric or summary classes found: $($unexpectedMetricClasses -join ', '). Use EdgeMetricCard or EdgeSummaryItem."
}

$allowedChartClasses = @("EdgeBarLineChart", "EdgeChartSeries", "EdgeChartPoint")
$chartClassHits = @(Select-String -Path $controlFiles.FullName -Pattern "^public .*class Edge.*Chart|^public .*class EdgeChart")
$chartClassNames = @(
    $chartClassHits | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w*Chart\w*|EdgeChart\w*)\b") {
            $Matches[1]
        }
    }
)

$unexpectedChartClasses = @($chartClassNames | Where-Object { $_ -notin $allowedChartClasses })
if ($unexpectedChartClasses.Count -ne 0) {
    Write-Error "Unexpected Edge chart classes found: $($unexpectedChartClasses -join ', '). Use EdgeBarLineChart and its shared data objects."
}

$allowedShellClasses = @("EdgeAccountChip", "EdgeHeaderBrand", "EdgeHeaderDivider")
$shellControlDir = Join-Path $controlsDir "Shell"
$shellControlFiles = @(Get-ChildItem -Path $shellControlDir -Filter "*.cs" -Recurse -File -ErrorAction SilentlyContinue)
$shellClassHits = @(Select-String -Path $shellControlFiles.FullName -Pattern "^public .*class Edge")
$shellClassNames = @(
    $shellClassHits | ForEach-Object {
        if ($_.Line -match "\bclass\s+(Edge\w+)\b") {
            $Matches[1]
        }
    }
)

$unexpectedShellClasses = @($shellClassNames | Where-Object { $_ -notin $allowedShellClasses })
if ($unexpectedShellClasses.Count -ne 0) {
    Write-Error "Unexpected Edge shell header controls found: $($unexpectedShellClasses -join ', '). Use EdgeAccountChip, EdgeHeaderBrand, or EdgeHeaderDivider."
}

$deprecatedDir = Join-Path $controlsDir "Deprecated"
$deprecatedFiles = @(Get-ChildItem -Path $deprecatedDir -Filter "*.cs" -Recurse -File -ErrorAction SilentlyContinue)
if ($deprecatedFiles.Count -ne 0) {
    Write-Error "Deprecated shared controls remain: $($deprecatedFiles.FullName -join ', ')"
}

$expectedFontFile = [System.IO.Path]::GetFullPath((Join-Path $sharedRoot "Assets/fonts/iconfont.ttf"))
$fontFiles = @(
    Get-ChildItem -Path $srcDir -Recurse -File |
        Where-Object {
            $_.Extension -in @(".ttf", ".otf", ".woff", ".woff2") -and
            $_.FullName -notmatch "[/\\](bin|obj)[/\\]"
        }
)

if ($fontFiles.Count -ne 1) {
    Write-Error "Expected one shared font asset, found $($fontFiles.Count)."
}

$actualFontFile = [System.IO.Path]::GetFullPath($fontFiles[0].FullName)
if ($actualFontFile -ne $expectedFontFile) {
    Write-Error "Unexpected font asset found: $actualFontFile. Use $expectedFontFile."
}

$requiredThemeIncludes = @(
    'Source="avares://IIoT.Edge.UI.Shared/Avalonia/Resources/EdgeIcons.axaml"',
    'Source="avares://IIoT.Edge.UI.Shared/Avalonia/Resources/EdgeConverters.axaml"',
    'Source="avares://IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeControls.axaml"'
)

foreach ($include in $requiredThemeIncludes) {
    $includeHits = @(Select-String -Path $edgeTheme -SimpleMatch $include)
    if ($includeHits.Count -eq 0) {
        Write-Error "Required EdgeTheme include missing: $include"
    }
}

$sharedAxamlFiles = @(
    Get-ChildItem -Path (Join-Path $sharedRoot "Avalonia") -Recurse -File -Filter "*.axaml" |
        Where-Object { $_.FullName -notmatch "[/\\](bin|obj)[/\\]" }
)

$edgeThemePath = [System.IO.Path]::GetFullPath($edgeTheme)
$hexOutsideThemeHits = @(
    $sharedAxamlFiles |
        Where-Object { [System.IO.Path]::GetFullPath($_.FullName) -ne $edgeThemePath } |
        Select-String -Pattern '#[0-9A-Fa-f]{6,8}'
)

if ($hexOutsideThemeHits.Count -ne 0) {
    Write-Error 'Shared UI hex colors must be declared in EdgeTheme.axaml only.'
}

$indTokenHits = @($sharedAxamlFiles | Select-String -Pattern '\bInd\.')
if ($indTokenHits.Count -ne 0) {
    Write-Error 'Ind.* token usage remains in shared UI. Use Edge.* tokens.'
}

$sourceFiles = @(
    Get-ChildItem -Path $srcDir -Recurse -File |
        Where-Object {
            $_.Extension -in @(".cs", ".axaml", ".xaml") -and
            $_.FullName -notmatch "[/\\](bin|obj)[/\\]"
        }
)

$removedControlNames = @(
    "EdgeDataPanel",
    "EdgeFeatureHeroCard",
    "EdgeInfoTile",
    "EdgeKpiCard",
    "EdgeLoginInput",
    "EdgeMetricStrip",
    "EdgeParameterPanel",
    "EdgeProcessCard",
    "EdgeReportFilterBar",
    "EdgeSummaryCard",
    "EdgeStatusSummaryCard",
    "EdgeToolButton"
)

foreach ($controlName in $removedControlNames) {
    $hits = @($sourceFiles | Select-String -Pattern "\b$controlName\b")
    if ($hits.Count -ne 0) {
        Write-Error "Removed shared control reference remains for $controlName."
    }
}

$manualGhostClassHits = @($sourceFiles | Select-String -Pattern 'Classes="[^"]*\bghost\b')
if ($manualGhostClassHits.Count -ne 0) {
    Write-Error 'Manual Classes="ghost" usage remains. Use EdgeActionButton Kind="Ghost" instead.'
}

$legacyButtonKindHits = @($sourceFiles | Select-String -Pattern 'Kind="(Soft|Cell|IconOnly|Language|Nav)"')
if ($legacyButtonKindHits.Count -ne 0) {
    Write-Error 'Legacy EdgeActionButton Kind usage remains. Use Kind plus Role instead.'
}

$legacyButtonKindCodeHits = @($sourceFiles | Select-String -Pattern 'EdgeActionButtonKind\.(Soft|Cell|IconOnly|Language|Nav)')
if ($legacyButtonKindCodeHits.Count -ne 0) {
    Write-Error 'Legacy EdgeActionButtonKind code usage remains. Use semantic Kind plus Role instead.'
}

$legacyActionButtonClassHits = @($sourceFiles | Select-String -Pattern 'Classes="cell"|Classes="right-rail( |")')
if ($legacyActionButtonClassHits.Count -ne 0) {
    Write-Error 'Legacy EdgeActionButton scene class usage remains. Use EdgeActionButton Role instead.'
}

$legacyActionButtonSelectorHits = @(Select-String -Path $edgeControls -Pattern 'EdgeActionButton\.(soft|cell|icononly|language|nav|right-rail)\b')
if ($legacyActionButtonSelectorHits.Count -ne 0) {
    Write-Error 'Legacy EdgeActionButton style selectors remain. Use semantic Kind and role-* selectors.'
}

$legacyTableSelectorHits = @(Select-String -Path $edgeControls -Pattern 'DataGrid\.(edge-grid|production-grid)\b')
if ($legacyTableSelectorHits.Count -ne 0) {
    Write-Error 'Legacy DataGrid style selectors remain. Use EdgeDataGrid and its density classes.'
}

$legacyTableClassHits = @($sourceFiles | Select-String -Pattern '\b(edge-grid|production-grid)\b')
if ($legacyTableClassHits.Count -ne 0) {
    Write-Error 'Legacy DataGrid class usage remains. Use EdgeDataGrid instead.'
}

$businessUiRoots = @(
    (Join-Path $repoRoot "src/Presentation"),
    (Join-Path $repoRoot "src/Modules"),
    (Join-Path $repoRoot "src/Edge")
) | Where-Object { Test-Path $_ }

$businessAxamlFiles = @(
    foreach ($root in $businessUiRoots) {
        Get-ChildItem -Path $root -Recurse -File -Filter "*.axaml" |
            Where-Object { $_.FullName -notmatch "[/\\](bin|obj)[/\\]" }
    }
)

$rawBusinessControlHits = @($businessAxamlFiles | Select-String -Pattern '<(Button|DataGrid|ScrollViewer|ListBox|TextBox|ComboBox|CalendarDatePicker|DatePicker|CheckBox|TabControl)\b')
if ($rawBusinessControlHits.Count -ne 0) {
    Write-Error 'Raw interactive control usage remains in business XAML. Use IIoT.Edge.UI.Shared controls.'
}

$localStyleHits = @($businessAxamlFiles | Select-String -Pattern '<Style\b|<Style\.Resources\b')
if ($localStyleHits.Count -ne 0) {
    Write-Error 'Local style resources remain in business XAML. Move visible control styling to IIoT.Edge.UI.Shared.'
}

$privateSurfaceValueHits = @($businessAxamlFiles | Select-String -Pattern 'Background="#[0-9A-Fa-f]{6,8}"|BorderBrush="#[0-9A-Fa-f]{6,8}"|Foreground="#[0-9A-Fa-f]{6,8}"|BoxShadow="[^"]*#[0-9A-Fa-f]{6,8}"|CornerRadius="[0-9][^"{]*"')
if ($privateSurfaceValueHits.Count -ne 0) {
    Write-Error 'Private surface visual values remain in business XAML. Use EdgeCard, EdgeDialogChrome, shared classes, and Edge.* tokens.'
}

$hardcodedFontSizeHits = @($businessAxamlFiles | Select-String -Pattern 'FontSize="[0-9][^"]*"')
if ($hardcodedFontSizeHits.Count -ne 0) {
    Write-Error 'Hardcoded FontSize remains in business XAML. Use Edge.FontSize.* tokens or shared control classes.'
}

$confirmationDialogFiles = @($businessAxamlFiles | Where-Object { $_.Name -like "*ConfirmationDialog.axaml" })
foreach ($dialogFile in $confirmationDialogFiles) {
    $usesSharedDialogChrome = @(Select-String -Path $dialogFile.FullName -Pattern '<[^>]*EdgeDialogChrome\b')
    if ($usesSharedDialogChrome.Count -eq 0) {
        Write-Error "Confirmation dialog must use EdgeDialogChrome: $($dialogFile.FullName)"
    }
}

$rawShapeHits = @($businessAxamlFiles | Select-String -Pattern '<(Ellipse|Rectangle)\b')
if ($rawShapeHits.Count -ne 0) {
    Write-Error 'Raw shape usage remains in business XAML. Use shared Edge status, icon, card, or divider styles instead.'
}

$rawChartPrimitiveHits = @($businessAxamlFiles | Select-String -Pattern '<(Canvas|Line|Path|Polygon|Polyline)\b')
if ($rawChartPrimitiveHits.Count -ne 0) {
    Write-Error 'Raw drawing primitive usage remains in business XAML. Use EdgeBarLineChart or shared Edge.Icon resources instead.'
}

$tabItemFiles = @($businessAxamlFiles | Select-String -Pattern '<TabItem\b' | Select-Object -ExpandProperty Path -Unique)
foreach ($tabItemFile in $tabItemFiles) {
    $hasSharedTabControl = @(Select-String -Path $tabItemFile -Pattern '<[^>]*EdgeTabControl\b')
    if ($hasSharedTabControl.Count -eq 0) {
        Write-Error "TabItem is only allowed as an EdgeTabControl child: $tabItemFile"
    }
}

$privateGeometryHits = @($businessAxamlFiles | Select-String -Pattern '<StreamGeometry\b|Data="\s*[Mm]\s*[0-9]')
if ($privateGeometryHits.Count -ne 0) {
    Write-Error 'Private icon geometry remains in business XAML. Declare reusable icons as Edge.Icon.* in UI Shared.'
}

foreach ($axamlFile in $businessAxamlFiles) {
    $content = Get-Content -Path $axamlFile.FullName -Raw
    $pathIconMatches = [regex]::Matches($content, '(?s)<PathIcon\b.*?/>')

    foreach ($match in $pathIconMatches) {
        $block = $match.Value
        $usesSharedIcon = $block -match 'Data="\{StaticResource Edge\.Icon\.[^"]+\}"'
        $usesSharedConverter = $block -match 'Converter=\{StaticResource Edge\.Converter\.[^}]+\}'

        if (-not ($usesSharedIcon -or $usesSharedConverter)) {
            Write-Error "PathIcon without shared Edge icon resource or converter found in $($axamlFile.FullName)."
        }
    }
}

$legacyButtonKindClassHits = @(Select-String -Path $edgeActionButton -Pattern '"soft"|"icononly"')
if ($legacyButtonKindClassHits.Count -ne 0) {
    Write-Error 'Legacy EdgeActionButton Kind class names remain in EdgeActionButton.cs.'
}

$converterRoot = [System.IO.Path]::GetFullPath($convertersDir)
$converterHits = @(
    Get-ChildItem -Path $srcDir -Recurse -File -Filter "*.cs" |
        Where-Object { $_.FullName -notmatch "[/\\](bin|obj)[/\\]" } |
        Select-String -Pattern "\bIValueConverter\b|\bIMultiValueConverter\b"
)

foreach ($hit in $converterHits) {
    $hitPath = [System.IO.Path]::GetFullPath($hit.Path)
    if (-not $hitPath.StartsWith($converterRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Error "Converter implementation outside shared UI converters directory: $hitPath"
    }
}

$axamlFiles = @(
    Get-ChildItem -Path $srcDir -Recurse -File |
        Where-Object {
            $_.Extension -eq ".axaml" -and
            $_.FullName -notmatch "[/\\](bin|obj)[/\\]"
        }
)

$edgeConvertersPath = [System.IO.Path]::GetFullPath($edgeConverters)
$converterResourceHits = @($axamlFiles | Select-String -Pattern 'x:Key="Edge\.Converter\.')
foreach ($hit in $converterResourceHits) {
    $hitPath = [System.IO.Path]::GetFullPath($hit.Path)
    if ($hitPath -ne $edgeConvertersPath) {
        Write-Error "Edge converter resource key declared outside EdgeConverters.axaml: $hitPath"
    }
}

$edgeIconsPath = [System.IO.Path]::GetFullPath($edgeIcons)
$iconGeometryHits = @($axamlFiles | Select-String -Pattern '<StreamGeometry\b')
foreach ($hit in $iconGeometryHits) {
    $hitPath = [System.IO.Path]::GetFullPath($hit.Path)
    if ($hitPath -ne $edgeIconsPath) {
        Write-Error "StreamGeometry declared outside EdgeIcons.axaml: $hitPath"
    }
}

$inlinePathDataHits = @($axamlFiles | Select-String -Pattern 'Data="\s*[Mm]\s*[0-9]')
foreach ($hit in $inlinePathDataHits) {
    $hitPath = [System.IO.Path]::GetFullPath($hit.Path)
    if ($hitPath -ne $edgeIconsPath) {
        Write-Error "Inline path geometry declared outside EdgeIcons.axaml: $hitPath"
    }
}

$requiredConverterKeys = @(
    "Edge.Converter.LogLevelToVisualStatus",
    "Edge.Converter.ProfileIconPath"
)

foreach ($key in $requiredConverterKeys) {
    $keyHits = @(Select-String -Path $edgeConverters -SimpleMatch "x:Key=""$key""")
    if ($keyHits.Count -eq 0) {
        Write-Error "Required converter resource key missing: $key"
    }
}

$styleIncludeHits = @(Select-String -Path $edgeControls -Pattern "Styles/Controls/")
if ($styleIncludeHits.Count -ne 0) {
    Write-Error "Split style include remains in EdgeControls.axaml."
}

$scrollbarRollbackHits = @(Select-String -Path $edgeControls -Pattern 'Value="\{TemplateBinding Value\}')
if ($scrollbarRollbackHits.Count -ne 0) {
    Write-Error 'Scrollbar Value="{TemplateBinding Value}" rollback detected.'
}

Write-Host "Edge UI shared baseline check passed."
