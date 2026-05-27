using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedisRateLimiting;
using System.Security.Claims;

namespace LinkShortener.Infrastructure;

public static class RateLimiterSetup
{
    public static IServiceCollection AddCustomDistributedRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddRateLimiter(options =>
        {
            // Sınır aşımında dönecek özel JSON yanıtı
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\": \"Çok fazla istek attınız. Lütfen biraz bekleyin.\"}",
                    cancellationToken: token);
            };

            // Kullanıcı ve IP bazlı dağıtık politikamız
            options.AddPolicy("UserBasedPolicy", httpContext =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // GİRİŞ YAPMIŞ KULLANICI
                if (!string.IsNullOrEmpty(userId))
                {
                    return RedisRateLimitPartition.GetFixedWindowRateLimiter(
                        partitionKey: $"ratelimit:auth:{userId}",
                        partitioner => new RedisFixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            ConnectionMultiplexerFactory = () => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString)
                        });
                }

                // ANONİM KULLANICI
                var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return RedisRateLimitPartition.GetFixedWindowRateLimiter(
                    partitionKey: $"ratelimit:anon:{ipAddress}",
                    partitioner => new RedisFixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        ConnectionMultiplexerFactory = () => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString)
                    });
            });
        });

        return services;
    }
}