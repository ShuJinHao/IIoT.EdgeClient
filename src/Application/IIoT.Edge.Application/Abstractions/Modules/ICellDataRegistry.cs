using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface ICellDataRegistry
{
    void Register<TCellData>(string processType) where TCellData : CellDataBase;

    bool IsRegistered(string processType);

    IReadOnlyDictionary<string, Type> GetRegistrations();
}
