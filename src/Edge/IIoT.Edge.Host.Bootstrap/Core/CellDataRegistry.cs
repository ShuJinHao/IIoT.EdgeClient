using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Shell.Core;

public sealed class CellDataRegistry : ICellDataRegistry
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public void Register<TCellData>(string processType) where TCellData : CellDataBase
        => Register(processType, typeof(TCellData));

    public void Register(string processType, Type cellDataType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            throw new InvalidOperationException("CellData processType cannot be empty.");
        }

        if (!typeof(CellDataBase).IsAssignableFrom(cellDataType))
        {
            throw new InvalidOperationException(
                $"CellData type '{cellDataType.FullName}' must inherit from {nameof(CellDataBase)}.");
        }

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
        CellDataTypeRegistry.Register(processType, cellDataType);
    }

    public bool IsRegistered(string processType) => _registrations.ContainsKey(processType);

    public IReadOnlyDictionary<string, Type> GetRegistrations() => _registrations;
}
