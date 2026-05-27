using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Interfaces;
using LinkShortener.Infrastructure.Caching;
using LinkShortener.Infrastructure.Persistence;
using LinkShortener.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace LinkShortener.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // appsettings.json içerisindeki JwtSettings alanını okuyoruz
        var jwtSecret = configuration["JwtSettings:Secret"] ?? "EnAz32KarakterUzunlugundaCokGizliBirAnahtar!!!";
        var issuer = configuration["JwtSettings:Issuer"] ?? "LinkShortenerAPI";
        var audience = configuration["JwtSettings:Audience"] ?? "LinkShortenerAPI";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true, // Süresi dolmuş token'ları otomatik reddet
                ValidateIssuerSigningKey = true, // Dijital imza kontrolü yap

                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),

                ClockSkew = TimeSpan.Zero // Sunucular arası zaman farkı payını sıfırlıyoruz (Anında düşsün)
            };
        });


        // =========================================================================
        // AWS DYNAMODB ALTYAPI KAYITLARI (Lokal / Bulut Dinamik Ayrımı)
        // =========================================================================
        var awsOptions = configuration.GetAWSOptions();
        services.AddDefaultAWSOptions(awsOptions);

        // appsettings.Development.json içindeki yerel endpoint adresini kontrol ediyoruz
        string? localDynamoUrl = configuration["AWS:LocalDynamoDBUrl"];

        if (!string.IsNullOrEmpty(localDynamoUrl))
        {
            // Eğer lokal URL tanımlıysa, istekleri Docker'daki yerel DynamoDB'ye yönlendiriyoruz
            services.AddSingleton<IAmazonDynamoDB>(sp =>
            {
                var clientConfig = new AmazonDynamoDBConfig
                {
                    ServiceURL = localDynamoUrl // http://localhost:8000
                };
                return new AmazonDynamoDBClient(clientConfig);
            });
        }
        else
        {
            // Eğer lokal URL yoksa, gerçek AWS bulut ortamındaki IAM yetkileriyle ayağa kalkar
            services.AddAWSService<IAmazonDynamoDB>();
        }

        // Interface Domain'den geliyor, Implementasyon buradan!
        services.AddScoped<IShortCodeGenerator, Base62ShortCodeGenerator>();

        // PostgreSQL DbContext Kaydı
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgreSQL")));
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddScoped<IShortenedLinkRepository, DynamoDbShortenedLinkRepository>();

        // =========================================================================
        // REDIS & CACHING ALTYAPI KAYITLARI
        // =========================================================================
        // .NET'in Redis ile konuşmasını sağlayan yerleşik Distributed Cache servisi
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });

        // Bizim Application katmanına sunduğumuz önbellek arayüzünün (kontratının) eşlenmesi
        services.AddScoped<ICacheService, RedisCacheService>();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddCustomDistributedRateLimiter(configuration);

        return services;
    }
}

public static class DynamoDbInitializer
{
    public static async Task EnsureTablesCreatedAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();

        const string tableName = "ShortenedLinks";

        try
        {
            // 1. Tablo var mı diye kontrol etmeyi deniyoruz
            await client.DescribeTableAsync(tableName);
        }
        catch (ResourceNotFoundException)
        {
            // 2. Tablo bulunamadı hatası alırsak, hemen pürüzsüzce oluşturuyoruz
            var request = new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new() { AttributeName = "ShortCode", AttributeType = ScalarAttributeType.S }
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new() { AttributeName = "ShortCode", KeyType = KeyType.HASH } // Partition Key
                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 5,
                    WriteCapacityUnits = 5
                }
            };

            await client.CreateTableAsync(request);

            // Tablonun tamamen aktifleşmesi için local ortamda 1-2 saniye bekletiyoruz
            await Task.Delay(2000);
        }
    }
}
