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

    public Task<bool> PostAsync(string url, object payload)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            _urlHistory.Add(url);
            _payloadHistory.Add(SafeSerialize(payload));
        }

        return Task.FromResult(IsOnline);
    }

    public Task<string?> PostWithResponseAsync(string url, object payload)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            _urlHistory.Add(url);
            _payloadHistory.Add(SafeSerialize(payload));
        }

        return Task.FromResult(IsOnline ? "{\"success\":true}" : (string?)null);
    }

    public Task<string?> GetAsync(string url)
    {
        Interlocked.Increment(ref _callCount);
        lock (_lock) _urlHistory.Add(url);
        return Task.FromResult(IsOnline ? "{}" : (string?)null);
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
