using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.SharedKernel.Enums;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.DieCutting.Config.Io;

/// <summary>
/// 模切硬件模板提供者，声明 MELSEC/MC 默认端口和只读数据扫描策略。
/// </summary>
public sealed class DieCuttingHardwareProfileProvider
    : StandardModuleHardwareProfileProviderBase<
        DieCuttingPlcSignals.Interaction,
        DieCuttingPlcSignals.SingleRead,
        DieCuttingPlcSignals.ContinuousRead,
        DieCuttingPlcSignals.SingleWrite,
        DieCuttingPlcSignals.ContinuousWrite>
{
    private readonly DieCuttingModuleDefinition _definition;
    private readonly DieCuttingModuleOptions _moduleOptions;

    public DieCuttingHardwareProfileProvider(
        DieCuttingModuleDefinition definition,
        IModulePlcSignalProfile<DieCuttingPlcSignals.Interaction> interactionProfile,
        IModulePlcSignalProfile<DieCuttingPlcSignals.SingleRead> singleReadProfile,
        IModulePlcSignalProfile<DieCuttingPlcSignals.ContinuousRead> continuousReadProfile,
        IModulePlcSignalProfile<DieCuttingPlcSignals.SingleWrite> singleWriteProfile,
        IModulePlcSignalProfile<DieCuttingPlcSignals.ContinuousWrite> continuousWriteProfile,
        IOptions<DieCuttingModuleOptions> moduleOptions)
        : base(
            interactionProfile,
            singleReadProfile,
            continuousReadProfile,
            singleWriteProfile,
            continuousWriteProfile)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _moduleOptions = moduleOptions.Value;
    }

    /// <summary>
    /// 模切模板备注使用的中文模块名。
    /// </summary>
    protected override string ModuleDisplayName => $"{_definition.DisplayName}只读采集模块";

    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 65530, "E4");

    public override PlcIoRuntimePolicy GetIoRuntimePolicy()
        => new(
            SignalLoopIntervalMs: 50,
            MaxSignalBlockWordCount: 100,
            WriteGapPolicy: PlcIoWriteGapPolicy.Split,
            DataReadLoopIntervalMs: Math.Max(500, _moduleOptions.Runtime.DataReadLoopIntervalMs));

    public override ModuleIoTemplateEntry ResolveIoTemplateForDevice(
        string deviceName,
        ModuleIoTemplateEntry template)
    {
        if (!string.Equals(template.SignalKey, "DieCutting.BatchNumber", StringComparison.OrdinalIgnoreCase))
        {
            return template;
        }

        return IsFirstDevice(deviceName)
            ? template with { PlcAddress = "R9660" }
            : template with { PlcAddress = "R9600" };
    }

    private static bool IsFirstDevice(string deviceName)
        => !string.IsNullOrWhiteSpace(deviceName)
           && deviceName.Trim().EndsWith("01", StringComparison.OrdinalIgnoreCase);
}
