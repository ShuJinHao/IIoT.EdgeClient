using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Shell.Core;

public sealed class CellDataRegistry : ICellDataRegistry
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICellDataTypeRegistry _cellDataTypeRegistry;

    public CellDataRegistry(ICellDataTypeRegistry cellDataTypeRegistry)
    {
        _cellDataTypeRegistry = cellDataTypeRegistry ?? throw new ArgumentNullException(nameof(cellDataTypeRegistry));
    }

    public void Register<TCellData>(string processType) where TCellData : CellDataBase
        => Register(processType, typeof(TCellData));

    public void Register(string processType, Type cellDataType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            throw new InvalidOperationException("CellData 的 processType 不能为空。");
        }

        if (!typeof(CellDataBase).IsAssignableFrom(cellDataType))
        {
            throw new InvalidOperationException(
                $"CellData 类型“{cellDataType.FullName}”必须继承 {nameof(CellDataBase)}。");
        }

        if (_registrations.TryGetValue(processType, out var existingType))
        {
            if (existingType == cellDataType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"ProcessType“{processType}”已绑定到“{existingType.Name}”。");
        }

        _registrations[processType] = cellDataType;
        _cellDataTypeRegistry.Register(processType, cellDataType);
    }

    public bool IsRegistered(string processType) => _registrations.ContainsKey(processType);

    public IReadOnlyDictionary<string, Type> GetRegistrations() => _registrations;
}
