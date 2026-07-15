[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$LedgerPath,
    [string]$DiscoveredInventoryPath,
    [string]$InventoryPath,
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$baselineCommit = 'e92846eec7a4d4a3cf9b9d9f843d06244d2b40ff'
$baselineTree = 'd55eaadadda7fb2ce50110f42da67649e3c7f505'
$baselineCaseCount = 1091
$baselineDeclarationCount = 964

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

function Get-Sha256([string[]]$Lines) {
    $payload = [string]::Join("`n", @($Lines | Sort-Object))
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($payload)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $RepositoryRoot @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-REGRESSION-LEDGER-001 git command failed: git $($Arguments -join ' ')`n$($output -join "`n")"
    }
    return [string[]]$output
}

function Get-BaselineDeclarations {
    [void](Invoke-Git @('cat-file', '-e', "$baselineCommit`^{commit}"))
    $actualTree = @(Invoke-Git @('rev-parse', "$baselineCommit`^{tree}"))
    if ($actualTree.Count -ne 1 -or $actualTree[0] -cne $baselineTree) {
        throw "EDGE-REGRESSION-LEDGER-001 fixed source tree drifted: expected=$baselineTree actual=$($actualTree -join ',')."
    }
    $sourcePaths = @(Invoke-Git @('ls-tree', '-r', '--name-only', $baselineCommit, '--', 'src/Tests') |
        Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object)
    $declarations = [Collections.Generic.List[object]]::new()
    $attributePattern = '^\s*\[(?:(?:Xunit\.)?(?:Fact|Theory)|AvaloniaFact|AvaloniaTheory)(?:Attribute)?(?:\(|\])'

    foreach ($sourcePath in $sourcePaths) {
        $lines = @(Invoke-Git @('show', "$baselineCommit`:$sourcePath"))
        $namespaceName = ''
        $className = ''
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            if ($line -match '^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]') {
                $namespaceName = $Matches[1]
            }
            if ($line -match '\bclass\s+([A-Za-z_][A-Za-z0-9_]*)(\s*<([^>{]+)>)?') {
                $className = $Matches[1]
                if (-not [string]::IsNullOrWhiteSpace($Matches[3])) {
                    $arity = @($Matches[3].Split(',')).Count
                    $className = "$className``$arity"
                }
            }
            if ($line -notmatch $attributePattern) { continue }
            if ([string]::IsNullOrWhiteSpace($namespaceName) -or [string]::IsNullOrWhiteSpace($className)) {
                throw "EDGE-REGRESSION-LEDGER-001 cannot resolve namespace/class for ${sourcePath}:$($index + 1)."
            }

            $cursor = $index + 1
            while ($cursor -lt $lines.Count -and $lines[$cursor].TrimStart().StartsWith('[', [StringComparison]::Ordinal)) {
                $cursor++
            }
            $signature = ''
            while ($cursor -lt $lines.Count -and -not $signature.Contains('(', [StringComparison]::Ordinal)) {
                $signature += " $($lines[$cursor].Trim())"
                $cursor++
            }
            if ($signature -notmatch '([A-Za-z_][A-Za-z0-9_]*)\s*\(') {
                throw "EDGE-REGRESSION-LEDGER-001 cannot parse test method for ${sourcePath}:$($index + 1)."
            }
            $methodName = $Matches[1]
            $oldClass = "$namespaceName.$className"
            $oldKey = "$oldClass.$methodName"
            $declarations.Add([pscustomobject][ordered]@{
                oldKey = $oldKey
                oldSourcePath = $sourcePath
                oldClass = $oldClass
                oldMethod = $methodName
            })
        }
    }

    $duplicateKeys = @($declarations | Group-Object oldKey | Where-Object Count -gt 1)
    if ($declarations.Count -ne $baselineDeclarationCount -or $duplicateKeys.Count -gt 0) {
        throw "EDGE-REGRESSION-LEDGER-001 fixed-commit extraction mismatch: expected=$baselineDeclarationCount actual=$($declarations.Count) duplicates=$($duplicateKeys.Count)."
    }
    return [object[]]@($declarations | Sort-Object oldKey)
}

function Get-CurrentDeclarationKey([string]$Identity) {
    $argumentIndex = $Identity.IndexOf('(', [StringComparison]::Ordinal)
    if ($argumentIndex -ge 0) { return $Identity.Substring(0, $argumentIndex) }
    return $Identity
}

function Get-SimpleDeclarationKey([string]$DeclarationKey) {
    $parts = $DeclarationKey.Split('.')
    if ($parts.Count -lt 2) {
        throw "EDGE-REGRESSION-LEDGER-001 invalid declaration key '$DeclarationKey'."
    }
    return "$($parts[-2]).$($parts[-1])"
}

$LedgerPath = if ([string]::IsNullOrWhiteSpace($LedgerPath)) {
    Join-Path $PSScriptRoot 'baselines/edge-regression-ledger.json'
} else { Resolve-RepositoryPath $LedgerPath }
$DiscoveredInventoryPath = if ([string]::IsNullOrWhiteSpace($DiscoveredInventoryPath)) {
    Join-Path $PSScriptRoot 'discovered-test-inventory.json'
} else { Resolve-RepositoryPath $DiscoveredInventoryPath }
$InventoryPath = if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    Join-Path $PSScriptRoot 'edge-test-inventory.json'
} else { Resolve-RepositoryPath $InventoryPath }

$baselineDeclarations = @(Get-BaselineDeclarations)
$baselineKeys = @($baselineDeclarations | ForEach-Object { [string]$_.oldKey })
$baselineSha256 = Get-Sha256 $baselineKeys

$discovered = Get-Content $DiscoveredInventoryPath -Raw | ConvertFrom-Json -Depth 40
$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 40
$currentDeclarations = @($discovered.cases |
    ForEach-Object { Get-CurrentDeclarationKey ([string]$_.identity) } |
    Sort-Object -Unique)
$currentSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$currentBySimpleKey = @{}
$currentByMethod = @{}
foreach ($declaration in $currentDeclarations) {
    [void]$currentSet.Add($declaration)
    $simpleKey = Get-SimpleDeclarationKey $declaration
    if (-not $currentBySimpleKey.ContainsKey($simpleKey)) {
        $currentBySimpleKey[$simpleKey] = [Collections.Generic.List[string]]::new()
    }
    $currentBySimpleKey[$simpleKey].Add($declaration)
    $methodName = $declaration.Split('.')[-1]
    if (-not $currentByMethod.ContainsKey($methodName)) {
        $currentByMethod[$methodName] = [Collections.Generic.List[string]]::new()
    }
    $currentByMethod[$methodName].Add($declaration)
}

$renamedMap = @{
    'IIoT.Edge.NonUiRegressionTests.EdgeMemoryCacheServiceBehaviorTests.RemoveByPrefix_WhenEntryCreatedByGetOrCreate_ShouldRemoveIt' = 'IIoT.Edge.Caching.UnitTests.EdgeMemoryCacheServiceBehaviorTests.CacheOperations_ShouldPreserveTypedExpirationAndFailureContracts'
    'IIoT.Edge.NonUiRegressionTests.CloudConsumerBehaviorTests.ProcessBatchAsync_WhenProductionAndDeviceStatusSharePlc_ShouldUploadOnlyProductionToPassStation' = 'IIoT.Edge.Runtime.WorkflowTests.CloudConsumerBehaviorTests.ProcessBatchAsync_WhenProductionAndDeviceStatusSharePlc_ShouldBlockWholeDirectCallWithoutPartialSuccess'
    'IIoT.Edge.Launcher.Tests.LauncherProfileCatalogTests.SourceProfileCatalog_ShouldLoadHomogenizationProfile' = 'IIoT.Edge.Launcher.FilesystemTests.LauncherProfileCatalogTests.LoadProfiles_ShouldLoadNeutralTestPluginProfile'
    'IIoT.Edge.Installer.Tests.SelfExtractorTests.InstallerOptions_ShouldParseVelopackInstallDirectoryAndSilentMode' = 'IIoT.Edge.Installer.UnitTests.InstallerOptionsTests.Parse_ShouldReadVelopackInstallDirectoryAndSilentMode'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.DiscoverDirectoryPlugins_ShouldFindProductModules' = 'IIoT.Edge.Module.ConformanceTests.ModuleDiscoveryContractTests.DiscoverDirectoryPlugins_ShouldFindTestPluginFixture'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.CreateAllModules_ShouldInstantiateAllDiscoveredPluginsWithoutDuplicateIdentity' = 'IIoT.Edge.Module.ConformanceTests.ModuleDiscoveryContractTests.CreateEnabledModules_ShouldInstantiateConfiguredDiscoveredPluginsWithoutDuplicateIdentity'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.RegisterAllDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts' = 'IIoT.Edge.Module.ConformanceTests.ModuleDiscoveryContractTests.RegisterEnabledDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.PluginBundles_ShouldContainHomogenizationSingleLineBundle' = 'IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationPackagingContractTests.PluginBundle_ShouldSelectHomogenizationLineOnly'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.ProductModules_ShouldUseStandardProductionAndSampleDirectories' = 'IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationPackagingContractTests.SourceLayout_ShouldSeparateProductionRuntimeFromDevelopmentSamples'
    'IIoT.Edge.Module.ContractTests.ModuleDiscoveryContractTests.RegisterMockNewModule_ShouldRequireZeroHostChanges' = 'IIoT.Edge.Module.ConformanceTests.ModuleDiscoveryContractTests.RegisterAdditionalTestModule_ShouldRequireZeroHostChanges'
    'IIoT.Edge.NonUiRegressionTests.RetryTaskCloudMesBehaviorTests.MesRetry_WhenHomogenizationOutboundRecordIsRetried_ShouldKeepFullPayload' = 'IIoT.Edge.Runtime.WorkflowTests.RetryTaskCloudMesBehaviorTests.MesRetry_WhenProcessRecordIsRetried_ShouldKeepFullPayload'
    'IIoT.Edge.NonUiRegressionTests.McPlcServiceBehaviorTests.Disconnect_WhenReadIsInFlight_ShouldWaitForReadOperationGate' = 'IIoT.Edge.Plc.ContractNetworkTests.McPlcServiceNetworkBehaviorTests.Disconnect_WhenReadIsInFlight_ShouldWaitForReadOperationGate'
    'IIoT.Edge.NonUiRegressionTests.McPlcServiceBehaviorTests.ReadDataAsync_WhenFrameTypeIsE4_ShouldUseMcpX4ERequestHeader' = 'IIoT.Edge.Plc.ContractNetworkTests.McPlcServiceNetworkBehaviorTests.ReadDataAsync_WhenFrameTypeIsE4_ShouldUseMcpX4ERequestHeader'
    'IIoT.Edge.NonUiRegressionTests.McPlcServiceBehaviorTests.ReadDataAsync_WhenReadingBits_ShouldUseMcpX3EProtocol' = 'IIoT.Edge.Plc.ContractNetworkTests.McPlcServiceNetworkBehaviorTests.ReadDataAsync_WhenReadingBits_ShouldUseMcpX3EProtocol'
    'IIoT.Edge.NonUiRegressionTests.McPlcServiceBehaviorTests.ReadDataAsync_WhenReadingWords_ShouldUseMcpX3EProtocol' = 'IIoT.Edge.Plc.ContractNetworkTests.McPlcServiceNetworkBehaviorTests.ReadDataAsync_WhenReadingWords_ShouldUseMcpX3EProtocol'
    'IIoT.Edge.NonUiRegressionTests.McPlcServiceBehaviorTests.WriteDataAsync_WhenWritingWords_ShouldUseMcpX3EProtocol' = 'IIoT.Edge.Plc.ContractNetworkTests.McPlcServiceNetworkBehaviorTests.WriteDataAsync_WhenWritingWords_ShouldUseMcpX3EProtocol'
    'IIoT.Edge.NonUiRegressionTests.SqliteConnectionFactoryBehaviorTests.CreateAsync_ShouldApplySharedPragmas' = 'IIoT.Edge.Persistence.Tests.SqliteConnectionFactoryBehaviorTests.Create_ShouldApplySharedPragmasOnProductionPath'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.AppLifecycleManager_WhenEnabledTaskSignalIsMissing_ShouldMarkRuntimeFaultAndSkipTaskRegistration' = 'IIoT.Edge.Module.Homogenization.WorkflowTests.PlcTaskBindingBehaviorTests.GetEnabledTaskKeys_WhenCandidateDefaultEnabledButIoMissing_ShouldKeepDisabled'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.AppLifecycleManager_WhenOnlyHomogenizationIsEnabled_ShouldReportPluginLifecycleStates' = 'IIoT.Edge.Startup.IntegrationTests.ModuleRuntimeRegistrationTests.AppLifecycleManager_WhenOnlyTestPluginIsEnabled_ShouldRunNeutralPluginLifecycleExactlyOnce'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.AppLifecycleManager_WhenProcessUploadPathIsMissing_ShouldStartWithDiagnosticIssue' = 'IIoT.Edge.Startup.IntegrationTests.ModuleRuntimeRegistrationTests.AppLifecycleManager_WhenCloudApiPathIsMissing_ShouldStartWithDiagnosticIssue'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.ConfiguredCatalog_WhenHomogenizationIsEnabled_ShouldLoadModule' = 'IIoT.Edge.Startup.IntegrationTests.ModuleRuntimeRegistrationTests.ConfiguredCatalog_WhenTestPluginIsEnabled_ShouldLoadModule'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.DiscoverDirectoryPlugins_ShouldFindProductModules' = 'IIoT.Edge.Startup.IntegrationTests.ModuleRuntimeRegistrationTests.DiscoverDirectoryPlugins_ShouldFindTestPluginFixture'
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.ValidationCatalog_ShouldRegisterProductModulesWithoutConflicts' = 'IIoT.Edge.Startup.IntegrationTests.ModuleRuntimeRegistrationTests.EnabledCatalog_ShouldRegisterTestPluginWithoutConflicts'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.HostRuntime_ShouldNotContainProductionDataBusinessSchemaFallback' = 'IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.HostAssemblies_ShouldNotDefineRemovedProductionDataFallbackTypes'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.HostDependencyInjection_ShouldNotRegisterProductionDataFallbackFacade' = 'IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.NavigationDependencyInjection_ShouldNotRegisterProductionDataFallback'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.HostNavigation_ShouldOnlyUsePluginProvidedDataViewRoutes' = 'IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.NavigationDependencyInjection_ShouldNotRegisterProductionDataFallback'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.VisualTestData_ShouldNotProvideProductionDataFacadeReplacement' = 'IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.VisualTestDataDependencyInjection_ShouldOnlyReplaceGenericPresentationFacades'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.HostBootstrap_ReleaseBuild_ShouldNotRegisterVisualTestDataPresentation' = 'IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.HostBootstrap_ShouldReferenceVisualTestDataOnlyInDebugBuild'
    'IIoT.Edge.Shell.Tests.ProductionDataViewBehaviorTests.HomogenizationDataView_ShouldNotInjectVisualTestRowsFromUiConfig' = 'IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationPackagingContractTests.DataView_ShouldNotInjectVisualTestRowsFromUiConfig'
    'IIoT.Edge.Shell.Tests.ShellRuntimePathResolverBehaviorTests.HomogenizationMachineProfile_ShouldStoreRuntimeDataUnderPublishRoot' = 'IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationPackagingContractTests.HomogenizationMachineProfile_ShouldStoreRuntimeDataUnderPublishRoot'
}
$analyzerMap = @{
    'IIoT.Edge.Shell.Tests.ModuleRuntimeRegistrationTests.CloudApiProductionCode_ShouldNotContainApiRouteDefaults' = 'EDGECLOUDCFG001'
}
$repositoryHygieneMap = @{
    'SharedProjects_ShouldNotReferenceUpperLayers' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#WSARCH004'
    'CoreLayerProjectReferences_ShouldPreserveDependencyDirection' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#WSARCH004'
    'CoreLayerSource_ShouldNotReferenceForbiddenUpperLayerNamespaces' = 'analyzer:DDD001'
    'MainSolution_ShouldContainOnlyApprovedRuntimeToolAndNoSdkSamples' = 'test:IIoT.Edge.Architecture.Tests.ProjectRegistryArchitectureTests.MainSolution_ShouldRegisterEveryPhysicalProjectExactlyOnce'
    'HostProductionDataFallback_ShouldNotExistInRuntimeSource' = 'test:IIoT.Edge.Shell.UiTests.ProductionDataViewBehaviorTests.HostAssemblies_ShouldNotDefineRemovedProductionDataFallbackTypes'
    'DashboardPreviewPlcStatusTable_ShouldKeepLongErrorsOutOfMainColumns' = 'test:IIoT.Edge.Shell.UiTests.DashboardPreviewRuntimeViewModelTests.PlcStatusTableItems_WhenLastErrorIsLong_ShouldExposeSummaryAndDetail'
    'SourceTree_ShouldNotContainGeneratedOrDuplicateArtifacts' = 'static:scripts/tests/Test-EdgeDuplicationBaseline.ps1#TEST-GOV-006'
    'RoundedWindowRegion_ShouldLiveInSharedUiAndBeReusedByShellLauncherAndPanels' = 'test:IIoT.Edge.Shell.UiTests.PresentationWindowChromeHeadlessTests.ProductionPlanWindow_ShouldMatchNativeRegionToVisibleRootCornerRadius'
    'ShellAppSettings_ShouldNotContainCommittedLicenseOrJwtSecrets' = 'static:scripts/tests/Test-EdgeSourceQualityPolicy.ps1#EDGE-SOURCE-QUALITY-001'
    'LocalAccounts_ShouldNotCommitDefaultPasswordsOrSha256LoginCompatibility' = 'test:IIoT.Edge.Cloud.ContractTests.AuthServiceBehaviorTests.ResetLocalAdminPasswordAsync_WhenStoredHashIsLegacySha256_ShouldPersistPbkdf2AndCreateSession'
    'ClientRules_ShouldDocumentLocalPasswordResetContract' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'ShellLoginDialog_ShouldExposeLocalEmergencyInitializeAndResetFlows' = 'test:IIoT.Edge.Shell.UiTests.ShellLoginDialogHeadlessTests.LocalEmergencyMode_ShouldMatchCredentialStatus'
    'CloudJwtAuthorization_ShouldValidateTokensBeforeReadingClaims' = 'test:IIoT.Edge.Cloud.ContractTests.AuthServiceBehaviorTests.LoginCloudAsync_WhenJwtSignatureDoesNotMatch_ShouldRejectSession'
    'MesSigning_ShouldUseConfiguredHmacWithoutFixedToken' = 'test:IIoT.Edge.Module.Homogenization.WorkflowTests.HomogenizationMesIntegrationTests.UploadInboundAsync_ShouldBuildTrayBasedRequest'
    'ProductionSource_ShouldNotUseDebugWriteLine' = 'static:scripts/tests/Test-EdgeSourceQualityPolicy.ps1#EDGE-SOURCE-QUALITY-001'
    'CapacitySyncTask_ShouldKeepBoundedConcurrency' = 'test:IIoT.Edge.Runtime.WorkflowTests.CapacitySyncTaskBehaviorTests.RetryBuffer_WhenCalledConcurrently_ShouldSerializeCloudPosts'
    'ClientArchitecture_ShouldUseSharedUploadAndRetryHelpers' = 'analyzer:EDGEOUT001'
    'OversizedViewModelsAndServices_ShouldStayOnExplicitGovernanceList' = 'static:scripts/tests/Test-EdgeDuplicationBaseline.ps1#TEST-GOV-006'
    'DeploymentScriptsAndDocs_ShouldNotHardcodeProductionIpOrBypassCertificates' = 'static:scripts/tests/TestEdgeDeploymentPolicy.ps1#EDGE-DEPLOY-SECURITY-001'
    'ClientRules_ShouldDocumentHttpAsSupportedFieldPath' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'LauncherDevelopmentLayout_ShouldUseCrossPlatformProfileAndDotnetSyncTool' = 'test:IIoT.Edge.Launcher.FilesystemTests.LauncherProfileCatalogTests.LoadProfiles_ShouldResolveSiblingHostExecutable'
    'RuntimeLayoutSync_ShouldRemoveStaleShellAssembliesFromLauncherRoot' = 'test:IIoT.Edge.Deployment.Tests.RuntimeLayoutSyncExternalPluginBehaviorTests.Run_WhenShellSourceIsHostDirectory_ShouldNotCleanHostOutput'
    'RuntimeLayoutSync_ShouldPublishSingleHostAndConfiguredPluginsRoot' = 'test:IIoT.Edge.Deployment.Tests.RuntimeLayoutSyncExternalPluginBehaviorTests.Run_WhenRuntimeLayoutIsRefreshed_ShouldPublishSingleHostAndPreserveDataDirectory'
    'IntegrationDependencyInjection_ShouldNotCacheTypedHttpClientsAsSingletons' = 'test:IIoT.Edge.Cloud.ContractTests.CloudApiResilienceBehaviorTests.CloudApiClient_WhenGetReturnsTransientFailure_ShouldRetry'
    'EfCoreSqliteConnection_ShouldEnableWalMode' = 'test:IIoT.Edge.Persistence.Tests.EdgeSqliteConnectionBehaviorTests.EnsureRuntimePragmas_ShouldCreateDirectoryAndEnableWalMode'
    'SourceTree_ShouldNotReferenceOldContractProjects' = 'static:scripts/tests/Test-EdgeCompatibilityInventory.ps1#TEST-COMPAT-001'
    'SourceTree_ShouldNotReferenceDeletedSdkArtifacts' = 'static:scripts/tests/Test-EdgeCompatibilityInventory.ps1#TEST-COMPAT-001'
    'SourceTree_ShouldNotReferenceDeletedOverWrappedApis' = 'analyzer:EDGECOMP001'
    'EdgeDocs_ShouldNotDocumentLegacyLauncherUpdateJsonPascalCaseKeys' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'EdgePackWorkflow_ShouldBuildOnWindowsAndPublishFromIntranetRunner' = 'static:.github/workflows/edge-pack-modules.yml#runs-on: windows-latest'
    'EdgeInstallUpdateDocs_ShouldDocumentStandardArtifactPublishPath' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'EdgeDocs_ShouldPreserveChangeClosureAndPlcSelectionContracts' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'ClientRules_ShouldDocumentStablePlcRuntimeSnapshotContract' = 'decision:EDGE-DOC-WORDING-RETIRE-001'
    'SourceTree_ShouldNotReferenceRemovedMapperOrUnusedCentralPackages' = 'static:scripts/tests/Test-EdgeCompatibilityInventory.ps1#TEST-COMPAT-001'
    'Application_ShouldNotReintroducePresentationModelsOrObservableBase' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#WSARCH004'
    'PresentationRecipeView_ShouldNotKeepDuplicateMediatRUseCases' = 'analyzer:EDGEPRES001'
    'PresentationViewModels_ShouldNotDependOnMediatRSender' = 'analyzer:EDGEPRES001'
    'Presentation_ShouldNotDefineMediatRRequestUseCases' = 'analyzer:EDGEPRES001'
    'DeviceSelection_ShouldOnlyBePublishedByEquipmentPanel' = 'test:IIoT.Edge.Shell.UiTests.IoViewViewModelBehaviorTests.SelectedDevice_WhenSetInsideIoPage_ShouldNotWriteSharedSelection'
    'EquipmentPanel_CurrentProcess_ShouldPreferBusinessProcessName' = 'test:IIoT.Edge.Shell.UiTests.EquipmentViewModelBehaviorTests.CurrentProcessDisplayName_WhenBusinessProcessNameExists_ShouldOverrideGenericFallback'
    'EquipmentPanel_CurrentProcessSlot_ShouldStayVisible' = 'test:IIoT.Edge.Shell.UiTests.EquipmentViewModelBehaviorTests.EquipmentView_WhenCurrentProcessIsAvailable_ShouldRenderVisibleBusinessProcessSlot'
    'HardwareCrudPages_ShouldUseSharedTableToolbarContract' = 'test:IIoT.Edge.Shell.UiTests.HardwareConfigViewModelBehaviorTests.AddNetworkDevice_WhenConfirmed_ShouldSaveImmediately'
    'NetworkDevicePage_ShouldUseResponsiveColumnsAndGroupedSharedDialog' = 'test:IIoT.Edge.Shell.UiTests.HardwareConfigViewModelBehaviorTests.AddNetworkDevice_WhenSaveFails_ShouldReloadPersistedSnapshot'
    'ShellWindowRegion_ShouldMatchStageCornerRadiusToken' = 'test:IIoT.Edge.Shell.UiTests.PresentationWindowChromeHeadlessTests.ProductionPlanWindow_ShouldMatchNativeRegionToVisibleRootCornerRadius'
    'IoMappingPage_ShouldUseTemplateEditToolbarContract' = 'test:IIoT.Edge.Shell.UiTests.HardwareConfigViewModelBehaviorTests.EditIoMapping_WhenConfirmed_ShouldUpdateSelectedMapping'
    'ConfigAndIoTables_ShouldAvoidUnboundedScrollHostStackPanelLayout' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.DataGrid_WhenViewportAndDensityChange_ShouldApplySharedBehaviorWithoutPageOverrides'
    'PlcTaskBindingPage_ShouldUseContentDrivenBoundedTable' = 'test:IIoT.Edge.Shell.UiTests.PlcTaskBindingViewModelBehaviorTests.OnActivatedAsync_WhenDeviceSelected_ShouldExposeCurrentDeviceTextWithoutSelectPrompt'
    'CapacityView_ShouldUseFillTableLayoutAndSharedToolbar' = 'test:IIoT.Edge.Shell.UiTests.CapacityViewModelBehaviorTests.CapacityViewPage_ShouldBindLoadingAndErrorStatesToSharedTablePanel'
    'MonitorView_ShouldUseFillTablesInsteadOfFixedSmallHeights' = 'test:IIoT.Edge.Shell.UiTests.MonitorViewModelBehaviorTests.OnActivatedAsync_WhenSharedSelectionMatchesDevice_ShouldShowSelectedDeviceSnapshot'
    'RecipePage_ShouldKeepRecipeSpecificLayout' = 'test:IIoT.Edge.Shell.UiTests.RecipeViewPageHeadlessTests.EmergencyEditor_ShouldFollowLocalAdminVisibility'
    'DeviceSelection_ShouldNotExposeSecondActionableDeviceSelector' = 'test:IIoT.Edge.Shell.UiTests.MonitorViewModelBehaviorTests.SelectedDevice_WhenSetInsideMonitorPage_ShouldNotWriteSharedSelection'
    'DeviceSelection_ShouldStayOutOfRuntimeAndIntegrationLayers' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#WSARCH004'
    'HardwareIoMappingPage_ShouldNotExposeSecondActionableDeviceSelector' = 'test:IIoT.Edge.Shell.UiTests.HardwareConfigViewModelBehaviorTests.LoadAll_WhenSelectionIsAll_ShouldExposePlcsWithoutAutoSelectingFirstDevice'
    'HardwareConfigPage_ShouldNotExposeGlobalSaveButton' = 'test:IIoT.Edge.Shell.UiTests.HardwareConfigViewModelBehaviorTests.AddNetworkDevice_WhenConfirmed_ShouldSaveImmediately'
    'IoInteractionPage_ShouldFollowIoMappingFiveCategories' = 'test:IIoT.Edge.Shell.UiTests.IoViewViewModelBehaviorTests.LoadMappingsAsync_WhenMappingsUseFiveCategories_ShouldExposeSameFiveIoBuckets'
    'Shell_ShouldUseRealIconAndAutoSizedEquipmentRail' = 'test:IIoT.Edge.Shell.UiTests.EquipmentViewModelBehaviorTests.EquipmentView_WhenCurrentProcessIsAvailable_ShouldRenderVisibleBusinessProcessSlot'
    'ModuleProjects_ShouldDeclareExplicitPluginOrSharedRole' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#PLUG004'
    'RuntimeLogs_ShouldNotUseLegacyEnglishVisiblePrefixes' = 'test:IIoT.Edge.Shell.UiTests.LocalizationBehaviorTests.ShellFooterView_ShouldNotRenderHardcodedCloudOrMesPrefixes'
    'SourceTree_ShouldNotContainMojibakeMarkers' = 'static:scripts/tests/Test-EdgeSourceQualityPolicy.ps1#EDGE-SOURCE-QUALITY-001'
    'ApplicationAbstractions_ShouldNotContainImplementationHelpers' = 'static:scripts/tests/Test-EdgeSourceQualityPolicy.ps1#EDGE-SOURCE-QUALITY-001'
    'NavigationLanguageDictionaries_ShouldHaveSameResourceKeys' = 'test:IIoT.Edge.Shell.UiTests.NavigationResourceContractTests.LanguageDictionaries_ShouldExposeTheSameResourceKeys'
    'NavigationLanguageDictionaries_ShouldNotKeepHostProcessDisplayKeys' = 'test:IIoT.Edge.Shell.UiTests.NavigationResourceContractTests.LanguageDictionaries_ShouldNotKeepHostProcessDisplayKeys'
    'NavigationFeatureResourceLookups_ShouldExistInLanguageDictionaries' = 'test:IIoT.Edge.Shell.UiTests.NavigationResourceContractTests.FeatureResourceLookups_ShouldExistInLanguageDictionaries'
    'NavigationFeatures_ShouldNotCreateVisibleChineseValidationIssuesDirectly' = 'analyzer:EDGEPRES002'
    'Tests_ShouldNotUseLongFixedTaskDelaysForSynchronization' = 'static:scripts/tests/Test-EdgeSourceQualityPolicy.ps1#EDGE-SOURCE-QUALITY-001'
    'ShellVisibleXaml_ShouldUseDynamicResourcesForChineseText' = 'test:IIoT.Edge.Shell.UiTests.LocalizationBehaviorTests.AppLanguageService_Change_LoadsVisibleUiResourceDictionaries'
    'BusinessXaml_ShouldUseSharedVisibleControlsInsteadOfNativeControls' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.SummaryAndTimelineControls_WhenPropertiesChange_ShouldExposeDerivedRuntimeState'
    'ReworkedBusinessXaml_ShouldNotUsePageLevelVisualProperties' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.DataGrid_WhenViewportAndDensityChange_ShouldApplySharedBehaviorWithoutPageOverrides'
    'SharedUi_ShouldProvidePropertyDrivenSummaryAndTimelineControls' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.SummaryAndTimelineControls_WhenPropertiesChange_ShouldExposeDerivedRuntimeState'
    'SharedUi_Scrollbars_ShouldUseUnifiedHitAreaAndThumbVisibility' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.DataGrid_WhenViewportAndDensityChange_ShouldApplySharedBehaviorWithoutPageOverrides'
    'SharedUi_StatusSegment_ShouldUseExistingCornerRadiusToken' = 'test:IIoT.Edge.UI.Shared.Tests.SharedControlBehaviorTests.StatusSegmentBar_WhenGeometryIsConfigured_ShouldKeepPropertyDrivenDimensions'
    'DiagnosticsDeadLetterRequeue_ShouldUseRetrySemanticIcon' = 'test:IIoT.Edge.Shell.UiTests.DiagnosticsViewModelBehaviorTests.RequeueDeadLetterCommand_WhenConfirmedAndSuccessful_ShouldCallOperatorAndRefresh'
}
$architectureBoundaryMap = @{
    'HostAndCommonProjects_ShouldNotReferenceConcreteModuleNamespaces' = 'analyzer:PLUG003'
    'ConcreteModuleNamespaces_ShouldOnlyAppearInModulesAndTests' = 'analyzer:PLUG003'
    'ProcessModules_ShouldOnlyReferenceApprovedHostContracts' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#PLUG001'
    'ModuleSdk_ShouldNotReferenceDataPipelineRuntime' = 'static:scripts/tests/Test-EdgeArchitectureProjectGraph.ps1#WSARCH004'
    'ProcessModules_ShouldNotReferenceDataPipelineRuntime' = 'analyzer:PLUG001'
    'PluginCloudUploaders_ShouldDependOnApplicationCloudChannelAbstraction' = 'test:IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationModuleContractTests.RegisterServices_ShouldRegisterOptionalCloudUploaderAndHardwareProfile'
    'ApplicationUploadChannels_ShouldNotKeepObsoleteUploaderBaseLayers' = 'analyzer:EDGECOMP001'
    'PluginProductionTasks_ShouldOnlyEmitDataPipelineRecordsForExternalUploads' = 'test:IIoT.Edge.Module.ConformanceTests.TestPluginRuntimeContractTests.ExecuteOnce_ShouldInvokeCallbackOnlyAfterPipelineAcceptanceReturns'
    'PluginProductionTasks_ShouldHandleDataPipelineEnqueueExceptionsInsideTask' = 'analyzer:EDGEOUT002'
    'Header_ShouldNotReferenceCompanyLogoResource' = 'test:IIoT.Edge.Module.ConformanceTests.HostResourceConformanceTests.Header_ShouldNotReferenceRemovedCompanyLogoResource'
    'PluginHardwareAndSampleRegistration_ShouldUseModuleBuilder' = 'test:IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationModuleContractTests.RegisterServices_ShouldRegisterDevelopmentSampleContributor'
    'Runtime_ShouldNotKeepOldIoScanContractName' = 'analyzer:EDGECOMP001'
    'PluginRuntime_ShouldNotUseStaticPlcProfileOrStringSignalAccessor' = 'test:IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationHardwareProfileBehaviorTests.HomogenizationPlcSignalsSource_ShouldOnlyContainSignalEnums'
}

$requiredRegressionIds = @($inventory.projects |
    ForEach-Object { @($_.overrides) } |
    Where-Object { $null -ne $_ } |
    ForEach-Object { [string]$_.regressionId } |
    Sort-Object -Unique)
$discoveredRegressionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($regressionId in @($discovered.cases | ForEach-Object { [string]$_.regressionId })) {
    [void]$discoveredRegressionIds.Add($regressionId)
}
foreach ($requiredRegressionId in $requiredRegressionIds) {
    if (-not $discoveredRegressionIds.Contains($requiredRegressionId)) {
        throw "EDGE-REGRESSION-LEDGER-001 classified RegressionId disappeared: $requiredRegressionId."
    }
}

if ($Update) {
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($old in $baselineDeclarations) {
        $disposition = ''
        $replacement = ''
        $reason = ''
        if ([string]$old.oldClass -match 'DieCutting' -or
            [string]$old.oldMethod -match 'DieCutting|PolaritySpecific|SharedDieCutting') {
            $disposition = 'retired-diecut'
            $replacement = 'decision:EDGE-DIECUT-RETIRE-001'
            $reason = 'This declaration covered only the die-cutting feature that the user explicitly approved for complete physical deletion; no production implementation remains to exercise.'
        } elseif ([string]$old.oldSourcePath -like '*/RepositoryHygieneTests.cs' -and
                  $repositoryHygieneMap.ContainsKey([string]$old.oldMethod)) {
            $replacement = [string]$repositoryHygieneMap[[string]$old.oldMethod]
            if ($replacement -ceq 'decision:EDGE-DOC-WORDING-RETIRE-001') {
                $disposition = 'retired-doc-lock'
                $reason = 'The declaration locked documentation wording rather than executable behavior. Current rules remain reviewed as documents, while regression evidence no longer treats prose as a test API.'
            } else {
                $disposition = 'replaced'
                $reason = 'The broad repository source-text assertion was replaced by the named Analyzer, project/static gate, behavior test, deployment test, or real headless UI owner.'
            }
        } elseif ([string]$old.oldSourcePath -like '*/ArchitectureBoundaryContractTests.cs' -and
                  $architectureBoundaryMap.ContainsKey([string]$old.oldMethod)) {
            $disposition = 'replaced'
            $replacement = [string]$architectureBoundaryMap[[string]$old.oldMethod]
            $reason = 'The source-regex architecture bucket was replaced by the named compile-time Analyzer/project gate or executable plugin conformance/workflow behavior owner.'
        } elseif ($currentSet.Contains([string]$old.oldKey)) {
            $disposition = 'retained-exact'
            $replacement = "test:$($old.oldKey)"
            $reason = 'The exact fully qualified test declaration remains discovered.'
        } elseif ($analyzerMap.ContainsKey([string]$old.oldKey)) {
            $disposition = 'replaced'
            $replacement = "analyzer:$($analyzerMap[[string]$old.oldKey])"
            $reason = 'The source-string guard is now enforced at production compilation by the named diagnostic and its AnalyzerTests positive/negative fixtures.'
        } elseif ($renamedMap.ContainsKey([string]$old.oldKey)) {
            $disposition = 'replaced'
            $replacement = "test:$($renamedMap[[string]$old.oldKey])"
            $reason = 'The same reviewed semantic contract moved to the named physical runner and was intentionally renamed for the neutral TestPlugin or current process terminology.'
        } elseif ([string]$old.oldClass -ceq 'IIoT.Edge.Module.ContractTests.ModuleContractTestBase`1') {
            $survivingModuleContract = "IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests.HomogenizationModuleContractTests.$($old.oldMethod)"
            if (-not $currentSet.Contains($survivingModuleContract)) {
                throw "EDGE-REGRESSION-LEDGER-001 surviving module contract is not discovered: $survivingModuleContract."
            }
            $disposition = 'moved-exact'
            $replacement = "test:$survivingModuleContract"
            $reason = 'The generic module contract is now discovered through the surviving Homogenization module in its dedicated conformance filesystem runner; die-cutting implementations were separately retired.'
        } else {
            $simpleKey = "$(([string]$old.oldClass).Split('.')[-1]).$($old.oldMethod)"
            $candidates = @(if ($currentBySimpleKey.ContainsKey($simpleKey)) { @($currentBySimpleKey[$simpleKey]) })
            if ($candidates.Count -eq 0 -and $currentByMethod.ContainsKey([string]$old.oldMethod)) {
                $candidates = @($currentByMethod[[string]$old.oldMethod])
            }
            if ($candidates.Count -ne 1) {
                throw "EDGE-REGRESSION-LEDGER-001 old declaration needs an exact reviewed mapping: $($old.oldKey); candidates=[$($candidates -join ', ')]."
            }
            $disposition = 'moved-exact'
            $replacement = "test:$($candidates[0])"
            $reason = 'The identical class.method declaration remains discovered after physical runner and namespace migration.'
        }
        $entries.Add([pscustomobject][ordered]@{
            oldKey = [string]$old.oldKey
            oldSourcePath = [string]$old.oldSourcePath
            oldClass = [string]$old.oldClass
            oldMethod = [string]$old.oldMethod
            disposition = $disposition
            replacement = $replacement
            reason = $reason
        })
    }

    $document = [pscustomobject][ordered]@{
        schemaVersion = 2
        ruleId = 'EDGE-REGRESSION-LEDGER-001'
        baselineCommit = $baselineCommit
        baselineTree = $baselineTree
        baselineCaseCount = $baselineCaseCount
        baselineDeclarationCount = $baselineDeclarationCount
        baselineDeclarationSha256 = $baselineSha256
        extraction = [pscustomobject][ordered]@{
            sourceRoot = 'src/Tests'
            sourcePattern = '*.cs'
            attributes = @('Fact', 'Theory', 'AvaloniaFact', 'AvaloniaTheory')
        }
        requiredRegressionIds = $requiredRegressionIds
        entries = [object[]]$entries
    }
    [void](New-Item (Split-Path $LedgerPath -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText($LedgerPath, (($document | ConvertTo-Json -Depth 16) + "`n"), [Text.UTF8Encoding]::new($false))
}

if (-not (Test-Path $LedgerPath -PathType Leaf)) {
    throw "EDGE-REGRESSION-LEDGER-001 ledger does not exist: $LedgerPath"
}
$ledger = Get-Content $LedgerPath -Raw | ConvertFrom-Json -Depth 40
$entries = @($ledger.entries)
if ([int]$ledger.schemaVersion -ne 2 -or [string]$ledger.ruleId -cne 'EDGE-REGRESSION-LEDGER-001' -or
    [string]$ledger.baselineCommit -cne $baselineCommit -or [string]$ledger.baselineTree -cne $baselineTree -or
    [int]$ledger.baselineCaseCount -ne $baselineCaseCount -or
    [int]$ledger.baselineDeclarationCount -ne $baselineDeclarationCount -or $entries.Count -ne $baselineDeclarationCount -or
    [string]$ledger.baselineDeclarationSha256 -cne $baselineSha256) {
    throw 'EDGE-REGRESSION-LEDGER-001 schema, fixed-commit extraction, or declaration hash drifted.'
}
if ((@($ledger.requiredRegressionIds | ForEach-Object { [string]$_ } | Sort-Object) -join '|') -cne ($requiredRegressionIds -join '|')) {
    throw 'EDGE-REGRESSION-LEDGER-001 required RegressionId ledger drifted.'
}

$ledgerByKey = @{}
foreach ($entry in $entries) {
    $key = [string]$entry.oldKey
    if ($ledgerByKey.ContainsKey($key)) { throw "EDGE-REGRESSION-LEDGER-001 duplicate old declaration: $key" }
    $ledgerByKey[$key] = $entry
}
foreach ($old in $baselineDeclarations) {
    if (-not $ledgerByKey.ContainsKey([string]$old.oldKey) -or
        [string]$ledgerByKey[[string]$old.oldKey].oldSourcePath -cne [string]$old.oldSourcePath -or
        [string]$ledgerByKey[[string]$old.oldKey].oldClass -cne [string]$old.oldClass -or
        [string]$ledgerByKey[[string]$old.oldKey].oldMethod -cne [string]$old.oldMethod) {
        throw "EDGE-REGRESSION-LEDGER-001 fixed-commit declaration is missing or changed: $($old.oldKey)."
    }
}

$counts = @{}
$analyzerText = @(Get-ChildItem (Join-Path $RepositoryRoot 'src/Analyzers') -Recurse -File -Filter '*.cs' |
    ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$analyzerTestText = @(Get-ChildItem (Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.Architecture.AnalyzerTests') -File -Filter '*.cs' |
    ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
foreach ($entry in $entries) {
    $disposition = [string]$entry.disposition
    if ($disposition -notin @('retained-exact', 'moved-exact', 'replaced', 'retired-diecut', 'retired-doc-lock')) {
        throw "EDGE-REGRESSION-LEDGER-001 unsupported disposition '$disposition' for $($entry.oldKey)."
    }
    $counts[$disposition] = 1 + [int]($counts[$disposition] ?? 0)
    $replacement = [string]$entry.replacement
    if ($replacement.StartsWith('test:', [StringComparison]::Ordinal)) {
        $currentKey = $replacement.Substring('test:'.Length)
        if (-not $currentSet.Contains($currentKey)) {
            throw "EDGE-REGRESSION-LEDGER-001 exact replacement is not discovered: $($entry.oldKey) -> $currentKey."
        }
    } elseif ($replacement.StartsWith('analyzer:', [StringComparison]::Ordinal)) {
        $diagnosticId = $replacement.Substring('analyzer:'.Length)
        if (-not $analyzerText.Contains("`"$diagnosticId`"", [StringComparison]::Ordinal) -or
            -not $analyzerTestText.Contains("`"$diagnosticId`"", [StringComparison]::Ordinal)) {
            throw "EDGE-REGRESSION-LEDGER-001 Analyzer replacement lacks implementation or AnalyzerTests evidence: $diagnosticId."
        }
    } elseif ($replacement.StartsWith('static:', [StringComparison]::Ordinal)) {
        $staticEvidence = $replacement.Substring('static:'.Length)
        $markerSeparator = $staticEvidence.LastIndexOf('#', [StringComparison]::Ordinal)
        if ($markerSeparator -le 0 -or $markerSeparator -eq $staticEvidence.Length - 1) {
            throw "EDGE-REGRESSION-LEDGER-001 invalid static evidence '$replacement'."
        }
        $staticPath = Resolve-RepositoryPath $staticEvidence.Substring(0, $markerSeparator)
        $staticMarker = $staticEvidence.Substring($markerSeparator + 1)
        if (-not (Test-Path $staticPath -PathType Leaf) -or
            -not [IO.File]::ReadAllText($staticPath).Contains($staticMarker, [StringComparison]::Ordinal)) {
            throw "EDGE-REGRESSION-LEDGER-001 static replacement lacks file or marker evidence: $replacement."
        }
    } elseif ($replacement -ceq 'decision:EDGE-DIECUT-RETIRE-001') {
        if ($disposition -cne 'retired-diecut' -or
            ([string]$entry.oldClass -notmatch 'DieCutting' -and [string]$entry.oldMethod -notmatch 'DieCutting|PolaritySpecific|SharedDieCutting')) {
            throw "EDGE-REGRESSION-LEDGER-001 non-die-cutting declaration cannot use the retirement decision: $($entry.oldKey)."
        }
    } elseif ($replacement -ceq 'decision:EDGE-DOC-WORDING-RETIRE-001') {
        if ($disposition -cne 'retired-doc-lock' -or
            -not $repositoryHygieneMap.ContainsKey([string]$entry.oldMethod) -or
            [string]$repositoryHygieneMap[[string]$entry.oldMethod] -cne $replacement) {
            throw "EDGE-REGRESSION-LEDGER-001 only documentation wording declarations can use the doc-lock retirement decision: $($entry.oldKey)."
        }
    } else {
        throw "EDGE-REGRESSION-LEDGER-001 unsupported replacement evidence '$replacement'."
    }
    if ([string]::IsNullOrWhiteSpace([string]$entry.reason)) {
        throw "EDGE-REGRESSION-LEDGER-001 declaration has no reviewed reason: $($entry.oldKey)."
    }
}

$retained = [int]($counts['retained-exact'] ?? 0)
$moved = [int]($counts['moved-exact'] ?? 0)
$replaced = [int]($counts['replaced'] ?? 0)
$retired = [int]($counts['retired-diecut'] ?? 0)
$retiredDocLock = [int]($counts['retired-doc-lock'] ?? 0)
if ($retained + $moved + $replaced + $retired + $retiredDocLock -ne $baselineDeclarationCount -or
    $retained -eq 0 -or $moved -eq 0 -or $replaced -eq 0 -or $retired -eq 0) {
    throw 'EDGE-REGRESSION-LEDGER-001 disposition counts are incomplete.'
}

Write-Host "Edge regression ledger passed: baselineCases=$baselineCaseCount, declarations=$baselineDeclarationCount, retainedExact=$retained, movedExact=$moved, replaced=$replaced, retiredDieCut=$retired, retiredDocLock=$retiredDocLock, requiredRegressionIds=$($requiredRegressionIds.Count), unknown=0."
