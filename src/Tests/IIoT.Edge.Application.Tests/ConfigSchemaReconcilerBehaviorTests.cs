using IIoT.Edge.Application.Features.Config.SchemaReconciliation;

namespace IIoT.Edge.Application.Tests;

public sealed class ConfigSchemaReconcilerBehaviorTests
{
    [Fact]
    public async Task ReconcileAsync_WhenSchemaAndStoreDiffer_ShouldInsertMissingDeleteExtraAndPreserveShared()
    {
        var source = new FakeSchemaSource(
            "param-cloud",
            [
                new ConfigSchemaItem("A", "default-a"),
                new ConfigSchemaItem("B", "default-b"),
                new ConfigSchemaItem("C", "default-c")
            ]);
        var store = new FakeValueStore("param-cloud", ["A", "B", "D"]);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            store.Inserted,
            item =>
            {
                Assert.Equal("C", item.Key);
                Assert.Equal("default-c", item.DefaultValue);
            });
        Assert.Equal(["D"], store.Deleted);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSchemaIdDoesNotMatchStore_ShouldSkip()
    {
        var source = new FakeSchemaSource("param-cloud", [new ConfigSchemaItem("A", "default-a")]);
        var store = new FakeValueStore("param-mes", ["D"]);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Empty(store.Inserted);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSourceIsEmpty_ShouldNotThrow()
    {
        var source = new FakeSchemaSource("param-cloud", []);
        var store = new FakeValueStore("param-cloud", []);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Empty(store.Inserted);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public async Task ReconcileAsync_WhenStoreAlreadyMatchesSchema_ShouldNotMutate()
    {
        var source = new FakeSchemaSource(
            "param-cloud",
            [
                new ConfigSchemaItem("A", "default-a"),
                new ConfigSchemaItem("B", "default-b")
            ]);
        var store = new FakeValueStore("param-cloud", ["A", "B"]);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Empty(store.Inserted);
        Assert.Empty(store.Deleted);
    }

    private sealed class FakeSchemaSource(
        string schemaId,
        IReadOnlyCollection<ConfigSchemaItem> items) : IConfigSchemaSource
    {
        public string SchemaId { get; } = schemaId;

        public Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(items);
    }

    private sealed class FakeValueStore(
        string schemaId,
        IReadOnlyCollection<string> existingKeys) : IConfigValueStore
    {
        public string SchemaId { get; } = schemaId;

        public List<ConfigSchemaItem> Inserted { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<IReadOnlyCollection<string>> GetExistingKeysAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(existingKeys);

        public Task InsertAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default)
        {
            Inserted.Add(item);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Deleted.Add(key);
            return Task.CompletedTask;
        }
    }
}
