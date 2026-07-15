namespace IIoT.Edge.Testing;

public sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
