using IIoT.Edge.Application.Modules.Hardware;
using System.Runtime.CompilerServices;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Sdk.Signals;

public sealed class ProductionContextSignalBindingStore : IProductionContextSignalBindingStore
{
    private sealed class BindingHolder(IReadOnlyList<ModuleIoSnapshot> bindings)
    {
        public IReadOnlyList<ModuleIoSnapshot> Bindings { get; } = bindings;
    }

    private readonly ConditionalWeakTable<ProductionContext, BindingHolder> _bindingsByContext = new();

    public void Set(ProductionContext context, IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bindings);

        var normalized = bindings
            .Select(static binding => new ModuleIoSnapshot(
                binding.SignalKey,
                binding.PlcAddress,
                binding.AddressCount,
                binding.DataType,
                binding.Direction,
                binding.SortOrder,
                binding.Category,
                binding.BusinessGroup))
            .ToArray();

        _bindingsByContext.Remove(context);
        _bindingsByContext.Add(context, new BindingHolder(normalized));
    }

    public IReadOnlyList<ModuleIoSnapshot> Get(ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _bindingsByContext.TryGetValue(context, out var holder)
            ? holder.Bindings
            : [];
    }
}
