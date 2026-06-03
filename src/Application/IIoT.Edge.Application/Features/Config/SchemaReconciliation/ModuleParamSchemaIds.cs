using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public static class ModuleParamSchemaIds
{
    public const string Mes = "param-mes";
    public const string Cloud = "param-cloud";
    public const string Business = "param-business";

    public static string ForCategory(ModuleParamCategory category)
        => category switch
        {
            ModuleParamCategory.Mes => Mes,
            ModuleParamCategory.Cloud => Cloud,
            ModuleParamCategory.Business => Business,
            _ => category.ToString()
        };
}
