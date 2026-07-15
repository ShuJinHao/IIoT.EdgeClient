using System.Net;

public sealed class FactAttribute : Attribute { }
public static class File { public static string ReadAllText(string path) => path; }
public static class Directory { public static string[] GetFiles(string path) => [path]; }
public static class Process { public static object Start(string name) => name; }

public sealed class ResourceBoundaryTest
{
    [Fact]
    public void FakeResourcesAndInjectedHttpArePure()
    {
        _ = File.ReadAllText(Path.Combine("root", "value"));
        _ = Directory.GetFiles("root");
        _ = Process.Start("fake");
        using var client = new HttpClient(new FakeHandler());
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
