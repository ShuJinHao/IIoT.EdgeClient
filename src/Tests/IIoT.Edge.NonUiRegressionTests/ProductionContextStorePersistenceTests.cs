using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ProductionContextStorePersistenceTests
{
    [Fact]
    public void SaveAndLoad_ShouldRestoreCoreRuntimeState()
    {
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var source = new ProductionContextStore(logger, tempDir);

            var ctx = source.GetOrCreate("PLC-A");
            ctx.SetStep("Scan", 3);
            ctx.Set("CurrentRecipeId", 42);
            ctx.AddCell("BC-1001", new InjectionCellData
            {
                Barcode = "BC-1001",
                WorkOrderNo = "WO-001",
                ScanTime = DateTime.UtcNow,
                InjectionVolume = 1.25
            });
            ctx.TodayCapacity.Increment(
                new DateTime(2026, 1, 2, 9, 0, 0),
                isOk: true,
                dayStart: new TimeSpan(8, 0, 0),
                dayEnd: new TimeSpan(20, 0, 0));

            source.SaveToFile();

            var restored = new ProductionContextStore(logger, tempDir);
            restored.LoadFromFile();

            var reloaded = restored.GetOrCreate("PLC-A");
            Assert.Equal(3, reloaded.GetStep("Scan"));
            Assert.Equal(42, reloaded.Get<int>("CurrentRecipeId"));
            Assert.True(reloaded.HasCell("BC-1001"));
            Assert.Equal(1, reloaded.TodayCapacity.TotalAll);
            Assert.Equal(1, reloaded.TodayCapacity.OkAll);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ProductionContext_WhenDisplayLabelsOverlap_ShouldTrackExplicitBarcodesOnly()
    {
        var context = new ProductionContext
        {
            DeviceName = "PLC-A"
        };

        context.AddCell("BC-2001", new NamedCellData { Label = "Shared-Label" });
        context.AddCell("BC-2002", new NamedCellData { Label = "Shared-Label" });

        Assert.True(context.HasCell("BC-2001"));
        Assert.True(context.HasCell("BC-2002"));
        Assert.False(context.HasCell("Shared-Label"));
        Assert.Equal(["BC-2001", "BC-2002"], context.GetAllBarcodes().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void SaveAndLoad_WhenDisplayLabelDiffers_ShouldPreserveOriginalBarcodeKeys()
    {
        CellDataTypeRegistry.Register<NamedCellData>("Named");
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var source = new ProductionContextStore(logger, tempDir);

            var ctx = source.GetOrCreate("PLC-A");
            ctx.AddCell("BC-3001", new NamedCellData { Label = "Display-Only" });
            source.SaveToFile();

            var restored = new ProductionContextStore(logger, tempDir);
            restored.LoadFromFile();

            var reloaded = restored.GetOrCreate("PLC-A");
            Assert.True(reloaded.HasCell("BC-3001"));
            Assert.False(reloaded.HasCell("Display-Only"));
            Assert.Equal("Display-Only", Assert.IsType<NamedCellData>(reloaded.GetCell("BC-3001")).DisplayLabel);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadFromFile_WhenNoFile_ShouldKeepEmptyState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var store = new ProductionContextStore(logger, tempDir);

            store.LoadFromFile();

            Assert.Empty(store.GetAll());
            Assert.Contains(logger.Entries, x => x.Message.Contains("No persisted file", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadFromFile_WhenPersistedFileIsCorrupt_ShouldQuarantineAndStartEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var persistPath = Path.Combine(tempDir, "production_context.json");
            File.WriteAllText(persistPath, "{ bad json");

            var store = new ProductionContextStore(logger, tempDir);
            store.LoadFromFile();

            Assert.Empty(store.GetAll());
            Assert.False(File.Exists(persistPath));

            var quarantinedFile = Directory.GetFiles(tempDir, "production_context.corrupt-*.json").SingleOrDefault();
            Assert.NotNull(quarantinedFile);
            Assert.Contains("{ bad json", File.ReadAllText(quarantinedFile!));
            Assert.Contains(logger.Entries, x => x.Message.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadFromFile_WhenMultipleCorruptFilesExist_ShouldRefreshPersistenceDiagnostics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            File.WriteAllText(
                Path.Combine(tempDir, "production_context.corrupt-20260418010101001.json"),
                "{ bad json 1");
            File.WriteAllText(
                Path.Combine(tempDir, "production_context.corrupt-20260418153045002.json"),
                "{ bad json 2");

            var store = new ProductionContextStore(logger, tempDir);
            store.LoadFromFile();

            var diagnostics = store.GetPersistenceDiagnostics();
            Assert.Equal(2, diagnostics.CorruptFileCount);
            var expectedLastCorruptDetectedAt = new DateTime(2026, 4, 18, 15, 30, 45, 2, DateTimeKind.Utc);
            Assert.Equal(expectedLastCorruptDetectedAt, diagnostics.LastCorruptDetectedAt);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetOrCreate_WhenModuleFactoryRegistered_ShouldCreateModuleSpecificContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        var logger = new FakeLogService();

        try
        {
            var store = new ProductionContextStore(logger, [new TestProductionContextFactory()], tempDir);

            var context = store.GetOrCreate("PLC-H", TestProductionContextFactory.TestModuleId);

            var typedContext = Assert.IsType<TestProductionContext>(context);
            Assert.Equal("PLC-H", typedContext.DeviceName);
            Assert.Equal("CreatedByFactory", typedContext.ModuleMarker);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetOrCreate_WhenExistingBaseContextNeedsModuleContext_ShouldPreserveRuntimeState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        var logger = new FakeLogService();

        try
        {
            var store = new ProductionContextStore(logger, [new TestProductionContextFactory()], tempDir);

            var baseContext = store.GetOrCreate("PLC-H");
            baseContext.DeviceId = 9;
            baseContext.SetStep("Homogenization.Inbound", 30);
            baseContext.Set("BatchNo", "B-1001");
            baseContext.AddCell("BC-1001", new NamedCellData { Label = "Display-Only" });

            var upgraded = store.GetOrCreate("PLC-H", TestProductionContextFactory.TestModuleId);

            var typedContext = Assert.IsType<TestProductionContext>(upgraded);
            Assert.Equal(9, typedContext.DeviceId);
            Assert.Equal(30, typedContext.GetStep("Homogenization.Inbound"));
            Assert.Equal("B-1001", typedContext.Get<string>("BatchNo"));
            Assert.True(typedContext.HasCell("BC-1001"));
            Assert.Equal("Display-Only", Assert.IsType<NamedCellData>(typedContext.GetCell("BC-1001")).DisplayLabel);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveToFile_WhenTempWriteFails_ShouldDeleteResidualTmpFileAndLogCleanupResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var fileSystem = new FaultingPersistenceFileSystem
            {
                FailOnWrite = true,
                LeaveTempFileOnWriteFailure = true
            };
            var store = new ProductionContextStore(logger, fileSystem, tempDir);
            store.GetOrCreate("PLC-A").Set("WorkOrder", "WO-001");

            store.SaveToFile();

            var tempPath = Path.Combine(tempDir, "production_context.json.tmp");
            Assert.False(File.Exists(tempPath));
            Assert.Contains(
                logger.Entries,
                x => x.Message.Contains("writing temp file", StringComparison.OrdinalIgnoreCase)
                    && x.Message.Contains("Temp cleanup: deleted residual .tmp file.", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveToFile_WhenReplaceFails_ShouldDeleteTmpFileAndKeepExistingPersistedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var fileSystem = new FaultingPersistenceFileSystem
            {
                FailOnReplace = true
            };
            var persistPath = Path.Combine(tempDir, "production_context.json");
            File.WriteAllText(persistPath, "seed");

            var store = new ProductionContextStore(logger, fileSystem, tempDir);
            store.GetOrCreate("PLC-A").Set("WorkOrder", "WO-002");

            store.SaveToFile();

            var tempPath = Path.Combine(tempDir, "production_context.json.tmp");
            Assert.False(File.Exists(tempPath));
            Assert.Equal("seed", File.ReadAllText(persistPath));
            Assert.Contains(
                logger.Entries,
                x => x.Message.Contains("replacing persisted file", StringComparison.OrdinalIgnoreCase)
                    && x.Message.Contains("Temp cleanup: deleted residual .tmp file.", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class NamedCellData : CellDataBase
    {
        public override string ProcessType => "Named";

        public override string DisplayLabel => Label;

        public string Label { get; set; } = string.Empty;
    }

    private sealed class TestProductionContext : ProductionContext
    {
        public string ModuleMarker { get; init; } = string.Empty;
    }

    private sealed class TestProductionContextFactory : IProductionContextFactory
    {
        public const string TestModuleId = "Homogenization";

        public string ModuleId => TestModuleId;

        public Type ContextType => typeof(TestProductionContext);

        public ProductionContext Create(string deviceName)
            => new TestProductionContext
            {
                DeviceName = deviceName,
                ModuleMarker = "CreatedByFactory"
            };
    }

    private sealed class FaultingPersistenceFileSystem : IProductionContextPersistenceFileSystem
    {
        public bool FailOnWrite { get; init; }

        public bool LeaveTempFileOnWriteFailure { get; init; }

        public bool FailOnReplace { get; init; }

        public void WriteAllText(string path, string content)
        {
            if (!FailOnWrite)
            {
                File.WriteAllText(path, content);
                return;
            }

            if (LeaveTempFileOnWriteFailure)
            {
                File.WriteAllText(path, content);
            }

            throw new IOException("disk full");
        }

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            if (FailOnReplace)
            {
                throw new UnauthorizedAccessException("replace blocked");
            }

            File.Move(sourcePath, destinationPath, overwrite: true);
        }

        public bool FileExists(string path) => File.Exists(path);

        public void DeleteFile(string path) => File.Delete(path);
    }
}
