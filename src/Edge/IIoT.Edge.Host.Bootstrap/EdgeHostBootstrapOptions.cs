using IIoT.Edge.Application.Abstractions.Config;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap;

public sealed record EdgeHostBootstrapOptions(
    IConfiguration Configuration,
    EdgeRuntimePaths RuntimePaths,
    string EnvironmentName);
