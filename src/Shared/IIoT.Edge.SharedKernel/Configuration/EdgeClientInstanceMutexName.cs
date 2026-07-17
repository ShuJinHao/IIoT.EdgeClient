using System.Security.Cryptography;
using System.Text;

namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientInstanceMutexName
{
    private const string Prefix = "Global\\IIoT.EdgeClient_";
    private const string DefaultInstanceId = "IIoT-Edge-Default";
    private const int MaxInstanceSegmentLength = 96;

    public static string Create(string? instanceId)
        => Prefix + NormalizeInstanceId(instanceId);

    public static string NormalizeInstanceId(string? instanceId)
    {
        var original = string.IsNullOrWhiteSpace(instanceId)
            ? DefaultInstanceId
            : instanceId.Trim();
        var sanitized = new string(original
            .Select(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray())
            .Replace("..", "__", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            sanitized = DefaultInstanceId;

        if (string.Equals(sanitized, original, StringComparison.Ordinal)
            && sanitized.Length <= MaxInstanceSegmentLength)
        {
            return sanitized;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..12];
        var prefixLength = Math.Max(1, MaxInstanceSegmentLength - hash.Length - 1);
        if (sanitized.Length > prefixLength)
            sanitized = sanitized[..prefixLength];

        return $"{sanitized}_{hash}";
    }
}
