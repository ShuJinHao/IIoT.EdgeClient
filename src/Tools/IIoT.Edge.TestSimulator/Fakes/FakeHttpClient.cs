using IIoT.Edge.Application.Abstractions.Device;
using System.Text.Json;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// Fake HTTP client for simulator.
/// Tracks URL and payload history.
/// </summary>
public sealed class FakeHttpClient : ICloudHttpClient
{
    private int _callCount;
    private readonly List<string> _urlHistory = new();
    private readonly List<string> _payloadHistory = new();
    private readonly object _lock = new();

    public bool IsOnline { get; set; } = true;

    public int CallCount => _callCount;

    public IReadOnlyList<string> UrlHistory
    {
        get { lock (_lock) return _urlHistory.ToList(); }
    }

    public IReadOnlyList<string> PayloadHistory
    {
        get { lock (_lock) return _payloadHistory.ToList(); }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
        lock (_lock)
        {
            _urlHistory.Clear();
            _payloadHistory.Clear();
        }
    }

    public Task<CloudCallResult> PostAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            _urlHistory.Add(url);
            _payloadHistory.Add(SafeSerialize(payload));
        }

        return Task.FromResult(
            IsOnline
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.NetworkFailure, "simulator_offline"));
    }

    public Task<CloudCallResult<string>> PostWithResponseAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            _urlHistory.Add(url);
            _payloadHistory.Add(SafeSerialize(payload));
        }

        return Task.FromResult(
            IsOnline
                ? CloudCallResult<string>.Success("{\"success\":true}")
                : CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "simulator_offline"));
    }

    public Task<CloudCallResult<string>> GetAsync(string url, CloudRequestOptions? options = null)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock) _urlHistory.Add(url);
        return Task.FromResult(
            IsOnline
                ? CloudCallResult<string>.Success("{}")
                : CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "simulator_offline"));
    }

    private static string SafeSerialize(object payload)
    {
        try
        {
            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return "<serialize-failed>";
        }
    }
}
