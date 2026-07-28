using System.Net;
using System.Net.Http;
using System.Net.Sockets;

public sealed class FactAttribute : Attribute { }

public sealed class PureNetworkTest
{
    [Fact]
    public void OpensRealNetworkResources()
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        using var tcpClient = new TcpClient();
        _ = new TcpListener(IPAddress.Loopback, 0);
        using var udpClient = new UdpClient();
        using var httpClient = new HttpClient();
    }
}
