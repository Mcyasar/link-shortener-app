using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Threading.RateLimiting;

namespace LinkShortener.Infrastructure.Resilience;

internal static class RateLimiterSetup
{
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