using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.ConformanceTests;

public sealed class CellDataSerializationContractTests
{
    [Fact]
    public void CellDataTypeRegistry_ShouldResolveRegisteredTypesAndReturnNullForUnknownProcess()
    {
        var registry = new CellDataTypeRegistry();

        Assert.Null(registry.Resolve(TestProcessCellData.ProcessTypeKey));
        Assert.Null(registry.Deserialize("MissingProcess", "{}"));

        registry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

        var cellData = Assert.IsType<TestProcessCellData>(registry.Deserialize(
            TestProcessCellData.ProcessTypeKey,
            """
            {"ProcessType":"TestProcess","Barcode":"BC-REG","WorkOrderNo":"WO-REG"}
            """));

        Assert.Equal("BC-REG", cellData.Barcode);
        Assert.Equal("WO-REG", cellData.WorkOrderNo);
    }

    [Fact]
    public void CellDataJsonSerializer_ShouldPreserveCamelCaseShapeAndUseInjectedRegistry()
    {
        var registry = new CellDataTypeRegistry();
        registry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);
        var serializer = new CellDataJsonSerializer(registry);

        var json = serializer.Serialize(new TestProcessCellData
        {
            Barcode = "BC-JSON",
            WorkOrderNo = "WO-JSON"
        });

        Assert.Contains("\"processType\":\"TestProcess\"", json, StringComparison.Ordinal);
        Assert.Contains("\"workOrderNo\":\"WO-JSON\"", json, StringComparison.Ordinal);

        var restored = Assert.IsType<TestProcessCellData>(
            serializer.Deserialize(TestProcessCellData.ProcessTypeKey, json));
        Assert.Equal("BC-JSON", restored.Barcode);
        Assert.Equal("WO-JSON", restored.WorkOrderNo);
    }
}
