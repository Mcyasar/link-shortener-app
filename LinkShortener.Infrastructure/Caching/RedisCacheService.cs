using LinkShortener.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StackExchange.Redis;
using System.Text.Json;

namespace LinkShortener.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    //private readonly ResiliencePipeline _resiliencePipeline;

    public RedisCacheService(IDistributedCache cache/*, ResiliencePipeline resiliencePipeline*/)
    {
        _cache = cache;
        //_resiliencePipeline = resiliencePipeline;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var cachedData = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(cachedData)) return default;

            return JsonSerializer.Deserialize<T>(cachedData);


            // Redis operasyonunu kalkanın koruması altında çalıştır
            // return await _resiliencePipeline.ExecuteAsync(async token =>
            // {
            //     var cachedData = await _cache.GetStringAsync(key, cancellationToken);
            //     if (string.IsNullOrEmpty(cachedData)) return default;

            //     return JsonSerializer.Deserialize<T>(cachedData);
            // }, cancellationToken);
        }
        catch (Exception ex) when (ex is BrokenCircuitException or TimeoutRejectedException or RedisException)
        {
            // 🚨 Hata durumunda veya devre AÇIK (Open) olduğunda yukarıya hata fırlatma!
            // Sessizce 'null' dön ki, Handler otomatik olarak DynamoDB'ye sapsın.
            Console.WriteLine($"⚠️ [Redis Bypass] Kalkan devreye girdi, veri DynamoDB'den yields edecek. Detay: {ex.Message}");
            return default;
        }
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