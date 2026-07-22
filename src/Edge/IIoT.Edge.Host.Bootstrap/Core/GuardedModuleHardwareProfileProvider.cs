using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Shell.Core;

internal sealed class GuardedModuleHardwareProfileProvider : IModuleHardwareProfileProvider
{
    private readonly IModuleHardwareProfileProvider _inner;
    private readonly Exception? _identityFailure;

    public GuardedModuleHardwareProfileProvider(
        string expectedModuleId,
        IModuleHardwareProfileProvider inner)
    {
        ModuleId = expectedModuleId;
        _inner = inner;
        try
        {
            var actualModuleId = inner.ModuleId;
            if (!string.Equals(actualModuleId, expectedModuleId, StringComparison.OrdinalIgnoreCase))
            {
                _identityFailure = new InvalidOperationException(
                    $"硬件 profile ModuleId“{actualModuleId}”与插件“{expectedModuleId}”不一致。");
            }
        }
        catch (Exception ex)
        {
            _identityFailure = ex;
        }
    }

    public string ModuleId { get; }

    internal string? IdentityFailureMessage => _identityFailure?.Message;

    public ModulePlcDefaults GetDefaultPlcSettings()
    {
        EnsureIdentity();
        return _inner.GetDefaultPlcSettings();
    }

    public PlcIoRuntimePolicy GetIoRuntimePolicy()
    {
        EnsureIdentity();
        return _inner.GetIoRuntimePolicy();
    }

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
    {
        EnsureIdentity();
        return _inner.GetDefaultIoTemplate();
    }

    public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
    {
        EnsureIdentity();
        return _inner.GetIoMappingCandidates();
    }

    public ModuleIoTemplateEntry ResolveIoTemplateForDevice(
        string deviceName,
        ModuleIoTemplateEntry template)
    {
        EnsureIdentity();
        return _inner.ResolveIoTemplateForDevice(deviceName, template);
    }

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
    {
        EnsureIdentity();
        return _inner.ValidatePlcConfiguration(deviceName, deviceModel, mappings);
    }

    private void EnsureIdentity()
    {
        if (_identityFailure is not null)
        {
            throw new InvalidOperationException(
                $"插件“{ModuleId}”的硬件 profile 身份无效。",
                _identityFailure);
        }
    }
}
