using System.Collections.Generic;
using System.Linq;

namespace IIoT.Edge.Application.Features.Production.Planning;

public sealed record ProductionPlanOption(
    string Id,
    string MainPlanCode,
    string WorkOrderCode,
    string ErpOrderCode,
    string ProductCode,
    string ProductName,
    string PlanStatus,
    string ProcessCode,
    string ProcessName,
    string LineCode,
    string LineName,
    string PlannedQuantity,
    string CompletedQuantity,
    string Unit,
    string ProductModel,
    string StartTime,
    string EndTime,
    IReadOnlyDictionary<string, string> Fields)
{
    public string DisplayPlanCode => FirstNonEmpty(MainPlanCode, WorkOrderCode, ErpOrderCode, Id);

    public string DisplayWorkOrder => FirstNonEmpty(WorkOrderCode, ErpOrderCode);

    public string DisplayProduct => FirstNonEmpty(ProductName, ProductCode, ProductModel);

    public string DisplayQuantity
    {
        get
        {
            var quantity = FirstNonEmpty(PlannedQuantity);
            if (string.IsNullOrWhiteSpace(quantity))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(Unit) ? quantity : $"{quantity} {Unit}";
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
