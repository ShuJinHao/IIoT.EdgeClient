using System.Net.Sockets;

public sealed class FactAttribute : Attribute { }

public sealed class PureNetworkTest
{
    [Fact]
    public void OpensARealSocket()
    {
        using var client = new TcpClient();
    }
}
