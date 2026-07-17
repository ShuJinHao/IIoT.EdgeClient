using IIoT.Edge.Shell.Core;
using IIoT.Edge.SharedKernel.Configuration;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.FilesystemTests;

public sealed class SingleInstanceMutexHandleBehaviorTests
{
    [Fact]
    public void TryAcquire_WhenExistingMutexIsNotOwnedByCaller_ShouldDisposeHandleImmediately()
    {
        var mutexName = $"IIoT.Edge.Shell.FilesystemTests.{Guid.NewGuid():N}";

        using var first = new SingleInstanceMutexHandle();
        using var second = new SingleInstanceMutexHandle();
        using var third = new SingleInstanceMutexHandle();

        Assert.True(first.TryAcquire(mutexName));
        Assert.False(second.TryAcquire(mutexName));

        second.Release();
        first.Release();

        Assert.True(third.TryAcquire(mutexName));
    }

    [Fact]
    public void Release_WhenCalledMultipleTimes_ShouldRemainSafe()
    {
        var mutexName = $"IIoT.Edge.Shell.FilesystemTests.{Guid.NewGuid():N}";

        using var handle = new SingleInstanceMutexHandle();

        Assert.True(handle.TryAcquire(mutexName));

        handle.Release();
        handle.Release();
    }

    [Fact]
    public void TryAcquireNonBlocking_WhenMutexNameIsInvalid_ShouldReturnUnavailableWithoutThrowing()
    {
        using var handle = new SingleInstanceMutexHandle();

        var result = handle.TryAcquireNonBlocking("invalid\0mutex", out var failure);

        Assert.Equal(SingleInstanceMutexAcquireResult.Unavailable, result);
        Assert.NotNull(failure);
        Assert.False(handle.OwnsMutex);
    }

    [Theory]
    [InlineData("../../Line-A")]
    [InlineData("Line-A\\Other")]
    [InlineData("Line-A\nInjected")]
    public void InstanceMutexName_WhenInstanceIdContainsUnsafeCharacters_ShouldBeSafeAndStable(string instanceId)
    {
        var first = EdgeClientInstanceMutexName.Create(instanceId);
        var second = EdgeClientInstanceMutexName.Create(instanceId);

        Assert.Equal(first, second);
        Assert.StartsWith("Global\\IIoT.EdgeClient_", first, StringComparison.Ordinal);
        var instanceSegment = first["Global\\IIoT.EdgeClient_".Length..];
        Assert.DoesNotContain("..", instanceSegment, StringComparison.Ordinal);
        Assert.DoesNotContain('/', instanceSegment);
        Assert.DoesNotContain('\\', instanceSegment);
        Assert.DoesNotContain('\n', instanceSegment);
        Assert.True(instanceSegment.Length <= 96);
    }
}

public sealed class CrashLogWriterBehaviorTests
{
    [Fact]
    public void Write_WhenPrimaryPathFails_ShouldFallbackToSecondaryPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var diagnosticMessages = new List<string>();
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                (path, entry) =>
            {
                if (string.Equals(path, primaryPath, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("primary blocked");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry, Encoding.UTF8);
            },
                diagnosticMessages.Add);

            writer.Write("fatal-source", new InvalidOperationException("boom"), "details");

            Assert.Empty(diagnosticMessages);
            var content = File.ReadAllText(fallbackPath);
            Assert.Contains("fatal-source", content, StringComparison.Ordinal);
            Assert.Contains("primary_result=failed", content, StringComparison.Ordinal);
            Assert.Contains("fallback_result=succeeded", content, StringComparison.Ordinal);
            Assert.Contains("primary blocked", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Write_WhenPrimaryAndFallbackPathsFail_ShouldEmitDiagnosticSignal()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var diagnosticMessages = new List<string>();
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                (path, _) =>
            {
                if (string.Equals(path, primaryPath, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("primary blocked");
                }

                throw new IOException("fallback blocked");
            },
                diagnosticMessages.Add);

            writer.Write("fatal-source", new InvalidOperationException("boom"), "details");

            var message = Assert.Single(diagnosticMessages);
            Assert.Contains("primary_result=failed", message, StringComparison.Ordinal);
            Assert.Contains("fallback_result=failed", message, StringComparison.Ordinal);
            Assert.Contains("primary blocked", message, StringComparison.Ordinal);
            Assert.Contains("fallback blocked", message, StringComparison.Ordinal);
            Assert.Contains("fatal-source", message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Write_WhenAllCrashLogSinksFail_ShouldSurfaceDiagnosticFailure()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                static (_, _) => throw new IOException("file sinks blocked"),
                static _ => throw new InvalidOperationException("diagnostic sink blocked"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                writer.Write("fatal-source", new InvalidOperationException("boom"), "details"));

            Assert.Equal("diagnostic sink blocked", error.Message);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-shell-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
