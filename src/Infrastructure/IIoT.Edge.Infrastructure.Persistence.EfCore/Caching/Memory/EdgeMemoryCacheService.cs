using IIoT.Edge.Application.Abstractions.Cache;
using System.Collections.Concurrent;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Caching.Memory;

public class EdgeMemoryCacheService : IEdgeCacheService
{
    private static readonly TimeSpan DefaultNullValueExpiration = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new(StringComparer.OrdinalIgnoreCase);

    public T? Get<T>(string key)
    {
        if (TryGetValue<T>(key, out var typed))
        {
            return typed;
        }

        return default;
    }

    public void Set<T>(string key, T value)
    {
        if (value is null)
        {
            return;
        }

        _cache[key] = CacheEntry.FromValue(value, expiresAtUtc: null);
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        var keys = _cache.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public bool Contains(string key)
    {
        if (!_cache.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value is CacheEntry entry && entry.IsExpired)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        TimeSpan? nullValueExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGetValue<T>(key, out var cached))
        {
            return cached;
        }

        var gate = _loadLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetValue<T>(key, out cached))
            {
                return cached;
            }

            var created = await factory(cancellationToken).ConfigureAwait(false);
            var expiration = created is null
                ? DateTimeOffset.UtcNow.Add(nullValueExpirationRelativeToNow ?? DefaultNullValueExpiration)
                : absoluteExpirationRelativeToNow is null
                    ? (DateTimeOffset?)null
                    : DateTimeOffset.UtcNow.Add(absoluteExpirationRelativeToNow.Value);

            _cache[key] = CacheEntry.FromValue(created, expiration);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetValue<T>(string key, out T? typed)
    {
        typed = default;
        if (!_cache.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value is CacheEntry entry)
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                return false;
            }

            if (entry.IsNull)
            {
                return true;
            }

            if (entry.Value is T entryValue)
            {
                typed = entryValue;
                return true;
            }

            return false;
        }

        if (value is T directValue)
        {
            typed = directValue;
            return true;
        }

        return false;
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset? ExpiresAtUtc)
    {
        public bool IsNull => Value is null;

        public bool IsExpired => ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;

        public static CacheEntry FromValue(object? value, DateTimeOffset? expiresAtUtc)
            => new(value, expiresAtUtc);
    }
}
