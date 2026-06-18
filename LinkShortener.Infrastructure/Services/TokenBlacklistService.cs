using LinkShortener.Application.Interfaces;
using StackExchange.Redis; // Lokal Docker'da ayağa kaldırdığımız Redis bağlantısı

namespace LinkShortener.Infrastructure.Services;

internal sealed class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IDatabase _redisDb;
    private static readonly string KeyPrefix = "blacklist:token:";

    public TokenBlacklistService(IConnectionMultiplexer redis)
    {
        _redisDb = redis.GetDatabase();
    }

    public async Task BlacklistTokenAsync(string tokenJti, TimeSpan expiryTime, CancellationToken cancellationToken)
    {
        if (expiryTime <= TimeSpan.Zero) return;
        
        string key = $"{KeyPrefix}{tokenJti}";
        // Token'ın kalan süresi kadar Redis'te kilitliyoruz, süre bitince otomatik siliniyor.
        await _redisDb.StringSetAsync(key, "revoked", expiryTime);
    }

    public async Task<bool> IsTokenBlacklistedAsync(string tokenJti, CancellationToken cancellationToken)
    {
        string key = $"{KeyPrefix}{tokenJti}";
        return await _redisDb.KeyExistsAsync(key);
    }
}