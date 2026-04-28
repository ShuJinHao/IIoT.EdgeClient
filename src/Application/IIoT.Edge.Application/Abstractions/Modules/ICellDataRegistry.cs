using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface ICellDataRegistry
{
    void Register<TCellData>(string processType) where TCellData : CellDataBase;

    void Register(string processType, Type cellDataType);

    bool IsRegistered(string processType);

    IReadOnlyDictionary<string, Type> GetRegistrations();
}
