using Microsoft.Extensions.DependencyInjection;

namespace LinkShortener.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Bu assembly içindeki tüm MediatR Handler'larını otomatik tarar ve kaydeder
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}