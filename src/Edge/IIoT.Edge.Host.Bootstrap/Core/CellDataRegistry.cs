using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Shell.Core;

public sealed class CellDataRegistry : ICellDataRegistry
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public void Register<TCellData>(string processType) where TCellData : CellDataBase
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            throw new InvalidOperationException("CellData processType cannot be empty.");
        }

        var cellDataType = typeof(TCellData);
        if (_registrations.TryGetValue(processType, out var existingType))
        {
            if (existingType == cellDataType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"ProcessType '{processType}' is already bound to '{existingType.Name}'.");
        }

        _registrations[processType] = cellDataType;
        CellDataTypeRegistry.Register<TCellData>(processType);
    }

    public bool IsRegistered(string processType) => _registrations.ContainsKey(processType);

    public IReadOnlyDictionary<string, Type> GetRegistrations() => _registrations;
}
