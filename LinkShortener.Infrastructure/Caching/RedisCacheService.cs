using System.Text.Json;
using LinkShortener.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace LinkShortener.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public RedisCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var cachedData = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(cachedData)) return default;

        return JsonSerializer.Deserialize<T>(cachedData);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken cancellationToken)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        var serializedData = JsonSerializer.Serialize(value);
        await _cache.SetStringAsync(key, serializedData, options, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(key, cancellationToken);
    }
}