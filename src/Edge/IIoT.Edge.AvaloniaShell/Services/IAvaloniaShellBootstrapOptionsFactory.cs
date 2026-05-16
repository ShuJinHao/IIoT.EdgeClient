using IIoT.Edge.Host.Bootstrap;

namespace IIoT.Edge.AvaloniaShell.Services;

public interface IAvaloniaShellBootstrapOptionsFactory
{
    AvaloniaHostBootstrapOptions Create(string baseDirectory);
}
