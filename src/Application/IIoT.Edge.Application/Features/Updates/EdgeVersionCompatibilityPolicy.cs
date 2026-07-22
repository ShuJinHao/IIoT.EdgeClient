using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Application.Features.Updates;

public sealed class EdgeVersionCompatibilityPolicy : IEdgeVersionCompatibilityPolicy
{
    public bool IsReleaseCompatible(
        EdgePluginVersionRelease release,
        string hostVersion,
        string hostApiVersion,
        out string? issue)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!string.Equals(release.HostApiVersion, hostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"插件 {release.ModuleId} 要求 HostApiVersion={release.HostApiVersion}，当前宿主为 {hostApiVersion}。";
            return false;
        }

        if (!EdgeClientHostRuntime.TryParseVersion(hostVersion, out var host)
            || !EdgeClientHostRuntime.TryParseVersion(release.MinHostVersion, out var min)
            || !EdgeClientHostRuntime.TryParseVersion(release.MaxHostVersion, out var max)
            || host.CompareTo(min) < 0
            || host.CompareTo(max) > 0)
        {
            issue = $"插件 {release.ModuleId} 兼容宿主版本范围为 [{release.MinHostVersion}, {release.MaxHostVersion}]，当前宿主为 {hostVersion}。";
            return false;
        }

        issue = null;
        return true;
    }
}
