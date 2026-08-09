using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherStartupCoordinator
{
    void PrepareLocalization();

    void Initialize();

    bool TryCompleteUpdateStartup();
}

public sealed class LauncherStartupBlockedException(
    string reasonCode,
    Exception? innerException = null)
    : InvalidOperationException(reasonCode, innerException)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class LauncherStartupCoordinator(
    IAppLanguageService languageService,
    ILauncherAccountCatalogInitializer accountCatalogInitializer,
    IEdgeUpdateConfigInitializer updateConfigInitializer,
    ILauncherUpdateOperationGate updateOperationGate,
    IEdgeUpdateTransactionRecovery updateTransactionRecovery,
    ILauncherPluginActivationReconciler activationReconciler,
    ILauncherDeviceBindingImporter deviceBindingImporter,
    ILauncherStartupDiagnosticWriter diagnostics,
    ILauncherRuntimePreflight? runtimePreflight = null,
    ILauncherLegacyCredentialMigrator? legacyCredentialMigrator = null)
    : ILauncherStartupCoordinator
{
    public void PrepareLocalization()
        => RunLocalStep(
            LauncherStartupDiagnosticAreas.Language,
            "LAUNCHER_LANGUAGE_INITIALIZATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.LauncherConfiguration,
            languageService.Initialize);

    public void Initialize()
    {
        if (!TryCompleteUpdateStartup())
        {
            throw new LauncherStartupBlockedException(
                "LAUNCHER_UPDATE_RECOVERY_NOT_READY");
        }

        RunCriticalStep(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            "LAUNCHER_PRODUCTION_IDENTITY_PREFLIGHT_FAILED",
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            () => runtimePreflight?.ValidateIdentityBeforeWrites());
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.AccountCatalog,
            "LAUNCHER_ACCOUNT_CATALOG_INITIALIZATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.LocalAccount,
            accountCatalogInitializer.EnsureCatalogExists);
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.PluginActivationMaterialization,
            "LAUNCHER_LEGACY_PLUGIN_ACTIVATION_RECONCILIATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.PluginActivation,
            activationReconciler.Reconcile,
            clearOnSuccess: false);
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            "LAUNCHER_DEVICE_BINDING_IMPORT_FAILED",
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            deviceBindingImporter.ApplyPendingBindingsOrThrow,
            clearOnSuccess: false);
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            "LAUNCHER_LEGACY_CREDENTIAL_MIGRATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            () => legacyCredentialMigrator?.Migrate(),
            clearOnSuccess: false);
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            "LAUNCHER_BINDING_V3_PREFLIGHT_FAILED",
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            () => runtimePreflight?.ValidateCompleteRuntime(),
            clearOnSuccess: false);
        RunCriticalStep(
            LauncherStartupDiagnosticAreas.UpdateConfiguration,
            "LAUNCHER_UPDATE_CONFIGURATION_INITIALIZATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.LauncherConfiguration,
            updateConfigInitializer.EnsureConfigExists);
    }

    public bool TryCompleteUpdateStartup()
    {
        IDisposable? recoveryLease;
        try
        {
            recoveryLease = updateOperationGate.TryAcquireUpdate();
        }
        catch (Exception ex) when (IsRecoverableLocalFailure(ex))
        {
            ReplaceFailure(
                LauncherStartupDiagnosticAreas.UpdateRecovery,
                "LAUNCHER_UPDATE_RECOVERY_GATE_FAILED",
                LauncherStartupDiagnosticRepairTargets.UpdateRecovery,
                ex);
            return false;
        }

        using (recoveryLease)
        {
            if (recoveryLease is null)
            {
                ReplaceFailure(
                    LauncherStartupDiagnosticAreas.UpdateRecovery,
                    "LAUNCHER_UPDATE_RECOVERY_BUSY",
                    LauncherStartupDiagnosticRepairTargets.UpdateRecovery);
                return false;
            }

            EdgeUpdateTransactionRecoveryResult recovery;
            try
            {
                recovery = updateTransactionRecovery.RecoverPendingTransaction();
            }
            catch (Exception ex) when (IsRecoverableLocalFailure(ex))
            {
                ReplaceFailure(
                    LauncherStartupDiagnosticAreas.UpdateRecovery,
                    "LAUNCHER_UPDATE_RECOVERY_FAILED",
                    LauncherStartupDiagnosticRepairTargets.UpdateRecovery,
                    ex);
                return false;
            }

            if (!recovery.Success || recovery.Blocked)
            {
                ReplaceFailure(
                    LauncherStartupDiagnosticAreas.UpdateRecovery,
                    recovery.Blocked
                        ? "LAUNCHER_UPDATE_RECOVERY_BLOCKED"
                        : "LAUNCHER_UPDATE_RECOVERY_FAILED",
                    LauncherStartupDiagnosticRepairTargets.UpdateRecovery);
                return false;
            }

            diagnostics.ReplaceArea(LauncherStartupDiagnosticAreas.UpdateRecovery, []);
            return true;
        }
    }

    private void RunCriticalStep(
        string area,
        string reasonCode,
        string repairTarget,
        Action action,
        bool clearOnSuccess = true)
    {
        try
        {
            action();
            if (clearOnSuccess)
            {
                diagnostics.ReplaceArea(area, []);
            }
        }
        catch (LauncherStartupBlockedException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableLocalFailure(ex))
        {
            ReplaceFailure(area, reasonCode, repairTarget, ex);
            throw new LauncherStartupBlockedException(reasonCode, ex);
        }
    }

    private void RunLocalStep(
        string area,
        string reasonCode,
        string repairTarget,
        Action action,
        bool clearOnSuccess = true)
    {
        try
        {
            action();
            if (clearOnSuccess)
            {
                diagnostics.ReplaceArea(area, []);
            }
        }
        catch (Exception ex) when (IsRecoverableLocalFailure(ex))
        {
            ReplaceFailure(area, reasonCode, repairTarget, ex);
        }
    }

    private static bool IsRecoverableLocalFailure(Exception exception)
        => exception is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidDataException
            or InvalidOperationException
            or JsonException
            or Win32Exception
            or KeyNotFoundException;

    private void ReplaceFailure(
        string area,
        string reasonCode,
        string repairTarget,
        Exception? exception = null)
    {
        diagnostics.ReplaceArea(
            area,
            [
                new LauncherStartupDiagnostic(
                    area,
                    reasonCode,
                    repairTarget,
                    ExceptionType: exception?.GetType().Name)
            ]);
        Trace.TraceWarning(
            "Launcher 局部初始化失败：{0} ({1})",
            reasonCode,
            exception?.GetType().Name ?? "None");
    }
}
