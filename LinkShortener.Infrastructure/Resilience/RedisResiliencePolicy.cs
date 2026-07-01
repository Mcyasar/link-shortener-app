using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace LinkShortener.Infrastructure.Resilience;

internal static class ResilienceSetup
{
    public static IServiceCollection AddResilienceStrategy(this IServiceCollection services)
    {
        var defaultResiliencePipeline = new ResiliencePipelineBuilder()
            // 1. STRATEJİ: Genel Yeniden Deneme (Retry)
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential // 200ms, 400ms, 800ms olarak esne
            })            

            // 2. KATMAN: CIRCUIT BREAKER (Devre Kesici)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5, // İsteklerin %50'si hata verirse devreyi aç
                SamplingDuration = TimeSpan.FromSeconds(10), // Son 10 saniyeyi analiz et
                MinimumThroughput = 20, // En az 20 istek geldikten sonra analize baş (Dev ortamı için ideal)
                BreakDuration = TimeSpan.FromSeconds(15), // Devre açılırsa 15 saniye boyunca tüm işlemleri iptal et

                // Tüm exception'lar için devreyi aç
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),

                OnOpened = arguments =>
                {
                    Console.WriteLine($"🚨 [CIRCUIT OPENED] Redis çöktü veya çok yavaş! Devre açıldı. Trafik kesildi. Süre: 30sn.");
                    return ValueTask.CompletedTask;
                },
                OnClosed = arguments =>
                {
                    Console.WriteLine($"💚 [CIRCUIT CLOSED] Redis kendine geldi. Devre kapandı, trafik normale döndü.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = arguments =>
                {
                    Console.WriteLine($"⏳ [CIRCUIT HALF-OPEN] Deneme istekleri gönderiliyor, Redis test ediliyor...");
                    return ValueTask.CompletedTask;
                }
            })

            // 3. KATMAN: TIMEOUT (Zaman Aşımı Koruması)
            // Komut 2000 ms içinde dönmezsa Polly işlemi zorla iptal eder (Hata fırlatır)
            .AddTimeout(TimeSpan.FromSeconds(5))

            .Build();

        services.AddSingleton(defaultResiliencePipeline);

        return services;
    }
}