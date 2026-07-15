using IIoT.Edge.Application.Abstractions.Cache;
using System.Collections.Concurrent;

namespace IIoT.Edge.Application.Common.Caching.Memory;

public class EdgeMemoryCacheService : IEdgeCacheService
{
    private static readonly TimeSpan DefaultNullValueExpiration = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _keyVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mutationLock = new();
    private readonly Action<string>? _expiredEntryObserved;
    private long _globalVersion;

    public EdgeMemoryCacheService()
    {
    }

    internal EdgeMemoryCacheService(Action<string> expiredEntryObserved)
    {
        _expiredEntryObserved = expiredEntryObserved ?? throw new ArgumentNullException(nameof(expiredEntryObserved));
    }

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

        lock (_mutationLock)
        {
            IncrementKeyVersion(key);
            _cache[key] = CacheEntry.FromValue(value, expiresAtUtc: null);
        }
    }

    public void Remove(string key)
    {
        lock (_mutationLock)
        {
            IncrementKeyVersion(key);
            _cache.TryRemove(key, out _);
        }
    }

    public void RemoveByPrefix(string prefix)
    {
        lock (_mutationLock)
        {
            var keys = _cache.Keys
                .Concat(_loadLocks.Keys)
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var key in keys)
            {
                IncrementKeyVersion(key);
                _cache.TryRemove(key, out _);
            }
        }
    }

    public void Clear()
    {
        lock (_mutationLock)
        {
            _globalVersion++;
            _cache.Clear();
        }
    }

    public bool Contains(string key)
    {
        if (!_cache.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value.IsExpired)
        {
            RemoveExpiredEntryIfUnchanged(key, value);
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
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetValue<T>(key, out var cached))
        {
            return cached;
        }

        var gate = _loadLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetValue<T>(key, out cached))
            {
                return cached;
            }

            long globalVersion;
            long keyVersion;
            lock (_mutationLock)
            {
                globalVersion = _globalVersion;
                keyVersion = GetKeyVersion(key);
            }

            var created = await factory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var expiration = created is null
                ? DateTimeOffset.UtcNow.Add(nullValueExpirationRelativeToNow ?? DefaultNullValueExpiration)
                : absoluteExpirationRelativeToNow is null
                    ? (DateTimeOffset?)null
                    : DateTimeOffset.UtcNow.Add(absoluteExpirationRelativeToNow.Value);

            lock (_mutationLock)
            {
                if (globalVersion == _globalVersion && keyVersion == GetKeyVersion(key))
                    _cache[key] = CacheEntry.FromValue(created, expiration);
            }
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    private long GetKeyVersion(string key)
        => _keyVersions.TryGetValue(key, out var version) ? version : 0;

    private void IncrementKeyVersion(string key)
        => _keyVersions.AddOrUpdate(key, 1, static (_, version) => version + 1);

    private bool TryGetValue<T>(string key, out T? typed)
    {
        typed = default;
        if (!_cache.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value.IsExpired)
        {
            RemoveExpiredEntryIfUnchanged(key, value);
            return false;
        }

        if (value.IsNull)
        {
            return true;
        }

        if (value.Value is T entryValue)
        {
            typed = entryValue;
            return true;
        }

        return false;
    }

    private void RemoveExpiredEntryIfUnchanged(string key, CacheEntry expiredEntry)
    {
        _expiredEntryObserved?.Invoke(key);
        lock (_mutationLock)
        {
            if (_cache.TryGetValue(key, out var current) && ReferenceEquals(current, expiredEntry))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private sealed class CacheEntry(object? value, DateTimeOffset? expiresAtUtc)
    {
        public object? Value { get; } = value;

        public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;

        public bool IsNull => Value is null;

        public bool IsExpired => ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;

        public static CacheEntry FromValue(object? value, DateTimeOffset? expiresAtUtc)
            => new(value, expiresAtUtc);
    }
}
