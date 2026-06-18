using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StackExchange.Redis;
using System.Threading.RateLimiting;

namespace LinkShortener.Infrastructure.Resilience;

internal static class RateLimiterSetup
{
    //global rate limiter artık kullanılmıyor ama örnek olması açısından silinmedi.
    public static IServiceCollection AddCustomDistributedGlobalRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.SyncTimeout = 300;
        redisOptions.ConnectTimeout = 2000;

        var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
        var redisDb = connectionMultiplexer.GetDatabase();

        var localBackupLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (path.Contains("/scalar", StringComparison.OrdinalIgnoreCase) || 
                path.Contains("/openapi", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter("scalar-bypass");
            }

            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10, // Pod başına saniyede 10 istek
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 0
                });
        });

        // 3. ADIM: Polly Resilience Pipeline (Sigorta Kutusu)
        var resiliencePipeline = new ResiliencePipelineBuilder()
            // 1. KATMAN: TIMEOUT (Zaman Aşımı Koruması)
            // Komut 300 ms içinde dönmezsa Polly işlemi zorla iptal eder (Hata fırlatır)
            .AddTimeout(TimeSpan.FromMilliseconds(300))
            // 2. KATMAN: CIRCUIT BREAKER (Devre Kesici)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 8,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisException>()
                    .Handle<TimeoutRejectedException>(),

                OnOpened = _ => {
                    Console.WriteLine("🚨 [RATE LIMITER CIRCUIT OPEN] Redis çöktü! In-Memory korumaya geçildi.");
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ => {
                    Console.WriteLine("💚 [RATE LIMITER CIRCUIT CLOSED] Redis düzeldi. Global sınırlamaya geri dönüldü.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = arguments =>
                {
                    Console.WriteLine($"⏳ [CIRCUIT HALF-OPEN] Deneme istekleri gönderiliyor, Redis test ediliyor...");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        services.AddSingleton(resiliencePipeline);

        // 3. ADIM: .NET RateLimiter Middleware Entegrasyonu
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global bir policy oluşturuyoruz
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var redisKey = $"ratelimit:{clientIp}";

                var path = context.Request.Path.Value ?? string.Empty;

                if (path.Contains("/scalar", StringComparison.OrdinalIgnoreCase) || 
                    path.Contains("/openapi", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("scalar-bypass");
                }

                // Polly kalkanını çalıştırıyoruz
                try
                {
                    return RateLimitPartition.Get(clientIp, _ =>
                    {
                        // Polly'nin yönettiği akış
                        bool isAllowed = resiliencePipeline.Execute(() =>
                        {
                            // Sizin önceki static kurgudaki Redis Lua Script veya Increment mantığınız:
                            // Örn: Redis üzerinde saniyelik counter artırımı
                            var currentRequests = redisDb.StringIncrement(redisKey);
                            if (currentRequests == 1)
                            {
                                redisDb.KeyExpire(redisKey, TimeSpan.FromSeconds(1));
                            }
                            return currentRequests <= 100; // Global limit: Saniyede 100 istek
                        });

                        // Eğer limit aşıldıysa Custom bir limiter dönerek engelle
                        return isAllowed
                            ? new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(1) }) // Geçiş izni
                            : new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 0, Window = TimeSpan.FromSeconds(1) });// Engelleme
                    });
                }
                catch (Exception ex) when (ex is BrokenCircuitException or TimeoutRejectedException or RedisException)
                {
                    // 🚨 FAIL-OVER / BACKUP MODU: Redis patladıysa veya devre açıksa buraya düşer.
                    // İstek doğrudan yukarıda tanımladığımız yerel (In-Memory) limiter'a paslanır!

                    return RateLimitPartition.Get(clientIp, _ =>
                    {
                        // localBackupLimiter'dan bu istek için bir bilet (lease) almaya çalışıyoruz
                        using var lease = localBackupLimiter.AttemptAcquire(context);

                        // Eğer lokal limit aşılmadıysa geçiş veren, aşıldıysa engelleyen standart limiter dönüyoruz
                        return lease.IsAcquired
                            ? new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(1) })
                            : new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 0, Window = TimeSpan.FromSeconds(1) });
                    });
                }
            });
        });

        return services;
    }

    internal static IServiceCollection AddCustomDistributedRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // 💡 DİNAMİK TEK POLİTİKA: Tüm parametrik istekleri bu isim altında toplayacağız
            options.AddPolicy("dynamic-parametric-policy", context =>
            {
                // 1. Adım: Tetiklenen action metodun üzerindeki Custom Attribute'u cımbızla çekiyoruz
                var endpoint = context.GetEndpoint();
                var customRateLimitMeta = endpoint?.Metadata.GetMetadata<CustomRateLimitAttribute>();

                // 2. Adım: Eğer metotta bizim custom attribute'umuz YOKSA, sınırsız geçiş ver (veya default kural uygula)
                if (customRateLimitMeta == null)
                {
                    return RateLimitPartition.GetNoLimiter("no-limit");
                }

                // 3. Adım: Eğer attribute VARSA, içerisindeki parametreleri (15, 100 vb.) dinamik olarak oku!
                int limit = customRateLimitMeta.PermitLimit;
                int window = customRateLimitMeta.WindowInSeconds;
                string clientKey = context.Request.Headers.Host.ToString();

                // 4. Adım: Değerleri anlık olarak besleyerek limiter'ı inşa et ve döndür
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"{clientKey}_{context.Request.Path}",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = limit, // 🔥 İşte metottan gelen dinamik 15 değeri!
                        Window = TimeSpan.FromSeconds(window)
                    });
            });
        });

        return services;
    }
}