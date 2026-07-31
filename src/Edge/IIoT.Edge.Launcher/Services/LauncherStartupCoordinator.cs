using System.Diagnostics;
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

public sealed class LauncherStartupCoordinator(
    IAppLanguageService languageService,
    ILauncherAccountCatalogInitializer accountCatalogInitializer,
    IEdgeUpdateConfigInitializer updateConfigInitializer,
    ILauncherUpdateOperationGate updateOperationGate,
    IEdgeUpdateTransactionRecovery updateTransactionRecovery,
    ILauncherPluginActivationReconciler activationReconciler,
    ILauncherDeviceBindingImporter deviceBindingImporter,
    ILauncherStartupDiagnosticWriter diagnostics)
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
        RunLocalStep(
            LauncherStartupDiagnosticAreas.AccountCatalog,
            "LAUNCHER_ACCOUNT_CATALOG_INITIALIZATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.LocalAccount,
            accountCatalogInitializer.EnsureCatalogExists);
        RunLocalStep(
            LauncherStartupDiagnosticAreas.UpdateConfiguration,
            "LAUNCHER_UPDATE_CONFIGURATION_INITIALIZATION_FAILED",
            LauncherStartupDiagnosticRepairTargets.LauncherConfiguration,
            updateConfigInitializer.EnsureConfigExists);
        _ = TryCompleteUpdateStartup();
    }

    public bool TryCompleteUpdateStartup()
    {
        IDisposable? recoveryLease;
        try
        {
            recoveryLease = updateOperationGate.TryAcquireUpdate();
        }
        catch (Exception ex)
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
            catch (Exception ex)
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
            RunLocalStep(
                LauncherStartupDiagnosticAreas.PluginActivationMaterialization,
                "LAUNCHER_PLUGIN_ACTIVATION_RECONCILIATION_FAILED",
                LauncherStartupDiagnosticRepairTargets.PluginActivation,
                activationReconciler.Reconcile,
                clearOnSuccess: false);
            RunLocalStep(
                LauncherStartupDiagnosticAreas.DeviceBinding,
                "LAUNCHER_DEVICE_BINDING_IMPORT_FAILED",
                LauncherStartupDiagnosticRepairTargets.DeviceBinding,
                deviceBindingImporter.ApplyPendingBindings,
                clearOnSuccess: false);
            return true;
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
        catch (Exception ex)
        {
            ReplaceFailure(area, reasonCode, repairTarget, ex);
        }
    }

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
