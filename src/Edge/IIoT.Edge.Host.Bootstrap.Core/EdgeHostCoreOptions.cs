using IIoT.Edge.Application.Abstractions.Config;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap.Core;

public sealed record EdgeHostCoreOptions(
    IConfiguration Configuration,
    EdgeRuntimePaths RuntimePaths,
    string EnvironmentName);
