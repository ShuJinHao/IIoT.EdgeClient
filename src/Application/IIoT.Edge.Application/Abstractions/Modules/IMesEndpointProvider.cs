namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IMesEndpointProvider
{
    bool IsConfigured { get; }

    string BuildUrl(string relativeOrAbsoluteUrl);

    IReadOnlyDictionary<string, string> GetDefaultHeaders();
}
