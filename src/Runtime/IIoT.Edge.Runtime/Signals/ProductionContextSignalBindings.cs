using IIoT.Edge.Application.Modules.Hardware;
using System.Runtime.CompilerServices;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Signals;

public static class ProductionContextSignalBindings
{
    private sealed class BindingHolder(IReadOnlyList<ModuleIoSnapshot> bindings)
    {
        public IReadOnlyList<ModuleIoSnapshot> Bindings { get; } = bindings;
    }

    private static readonly ConditionalWeakTable<ProductionContext, BindingHolder> BindingsByContext = new();

    public static void Set(ProductionContext context, IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bindings);

        var normalized = bindings
            .Select(static binding => new ModuleIoSnapshot(
                binding.Label,
                binding.PlcAddress,
                binding.AddressCount,
                binding.DataType,
                binding.Direction,
                binding.SortOrder,
                binding.Category,
                binding.GroupName,
                binding.DisplayRole))
            .ToArray();

        BindingsByContext.Remove(context);
        BindingsByContext.Add(context, new BindingHolder(normalized));
    }

    public static IReadOnlyList<ModuleIoSnapshot> Get(ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingsByContext.TryGetValue(context, out var holder)
            ? holder.Bindings
            : [];
    }
}
