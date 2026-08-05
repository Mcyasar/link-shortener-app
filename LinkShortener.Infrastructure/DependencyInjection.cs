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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LinkShortener.Application.Services;
using StackExchange.Redis;
using LinkShortener.Infrastructure.Resilience;
using LinkShortener.Infrastructure.Services;
using MassTransit;
using LinkShortener.Application.Configurations;
using LinkShortener.Infrastructure.Consumers;
using LinkShortener.Domain.Entities;

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
        string? localDynamoUrl = configuration["AWS:ServiceURL"];

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

        services.AddScoped<ILinkClickOutboxRepository, EfLinkClickOutboxRepository>();
        services.AddScoped<IShortenedLinkRepository, DynamoDbShortenedLinkRepository>();

        // =========================================================================
        // REDIS & CACHING ALTYAPI KAYITLARI
        // =========================================================================
        // .NET'in Redis ile konuşmasını sağlayan yerleşik Distributed Cache servisi
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });

        // Redis bağlantısını IConnectionMultiplexer olarak da kaydediyoruz
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")));

        services.AddResilienceStrategy();
        services.AddCustomDistributedRateLimiter();

        // Bizim Application katmanına sunduğumuz önbellek arayüzünün (kontratının) eşlenmesi
        services.AddScoped<ICacheService, RedisCacheService>();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IRefreshTokenService, DynamoDbRefreshTokenService>();

        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();


        // Pod'un Worker olarak mı yoksa API olarak mı çalıştığını kontrol ediyoruz
        bool isWorkerEnabled = configuration.GetValue<bool>("MassTransitWorkerEnabled", true);

        // var rabbitMQOptions = configuration
        //     .GetSection(RabbitMQOptions.SectionName)
        //     .Get<RabbitMQOptions>() ?? new RabbitMQOptions();

        services.AddMassTransit(x =>
        {
            if (isWorkerEnabled)
            {
                x.AddConsumer<LinkClickedConsumer>();
            }

            // İleride yazacağımız Consumer sınıflarını otomatik tarayıp bulur
            x.SetKebabCaseEndpointNameFormatter();

            // 🔥 Kafka bir Rider olarak eklenmelidir
            x.AddRider(rider =>
            {
                // 1. Consumer'ı Rider'a ekleyin
                //rider.AddConsumer<LinkClickKafkaConsumer>();

                // 2. UsingKafka metodunu 'rider' üzerinden çağırın
                rider.UsingKafka((context, cfg) =>
                {
                    cfg.Host("local-docker-host:9092"); // Docker Compose içindeki Kafka servisinizin adresi

                    if (isWorkerEnabled)
                    {
                        // Debezium'un oluşturduğu topic'i dinliyoruz
                        // Topic adı: database.server.name.schema.table (örn: linkshortener-postgres.public.LinkClickOutbox)
                        cfg.TopicEndpoint<string, DebeziumMessage>("debezium.public.LinkClickOutbox", "link-shortener-group", e =>
                        {
                            // Debezium'un oluşturduğu topic'i dinliyoruz
                            // Topic adı: database.server.name.schema.table (örn: linkshortener-postgres.public.LinkClickOutbox)
                            // Otomatik retry ve hatalı mesaj (DLQ) yönetimi
                            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));

                            // DynamoDB 1.000 WCU sınırını tek pod üzerinde %100 garantiye alan Rate Limiter:
                            e.UseRateLimit(800, TimeSpan.FromSeconds(1));

                            // Debezium mesajları JSON formatında ve şemasız geldiği için RawJsonDeserializer kullanıyoruz.
                            // Bu, MassTransit'in gelen ham JSON'u doğrudan DebeziumMessage DTO'muza dönüştürmesini sağlar.
                            e.UseRawJsonDeserializer();
                            e.ConfigureConsumer<LinkClickedConsumer>(context);
                        });
                    }
                });
            });

            // RabbitMQ Konfigürasyonu
            // x.UsingRabbitMq(...) // Bu blok artık kullanılmadığı için kaldırıldı
        });

        return services;
    }

    public static IServiceCollection AddBackgroundWorkerServices(this IServiceCollection services)
    {
        // 1. CHANNEL: Bellekte tek bir kuyruk olması için kesinlikle Singleton!
        services.AddSingleton<ILinkClickChannel, LinkClickChannel>();

        // 2. WORKER: Arka planda sürekli çalışacak olan Hosted Service/BackgroundService kaydı.
        // .NET mimarisinde arka plan işçileri AddHostedService metoduyla tescillenir.
        // Bu metot arka planda o sınıfı otomatik olarak Singleton olarak yönetir.
        // services.AddHostedService<LinkClickProcessorWorker>();

        return services;
    }    
}

public static class DynamoDbInitializer
{
    public static async Task EnsureShortenedLinksTableCreatedAsync(this IApplicationBuilder app, CancellationToken cancellationToken = default)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DynamoDbInitializer");

        // 🚨 GÜVENLİK DUVARI: Sadece yerel geliştirme (Local Docker) ortamında çalışsın!
        if (!environment.IsDevelopment()) return;

        var client = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
        const string tableName = "ShortenedLinks"; // Repository ile eşitlendi!

        try
        {
            logger.LogInformation("⏳ [DynamoDB Local] '{TableName}' tablosu kontrol ediliyor...", tableName);
            await client.DescribeTableAsync(tableName);
            logger.LogInformation("✅ [DynamoDB Local] '{TableName}' tablosu zaten mevcut ve aktif.", tableName);
        }
        catch (ResourceNotFoundException)
        {
            logger.LogWarning("🛠️ [DynamoDB Local] '{TableName}' tablosu bulunamadı, mevcut Repository şeması ve GSI indeksiyle birlikte oluşturuluyor...", tableName);

            var request = new CreateTableRequest
            {
                TableName = tableName,
                // 1. Şema Alan Tanımları (Repository'deki ShortCode + Gelecekteki Listeleme İndeksi için UserId ve CreatedAt)
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new() { AttributeName = "ShortCode", AttributeType = ScalarAttributeType.S }, // Repository ile eşitlendi!
                    new() { AttributeName = "UserId", AttributeType = ScalarAttributeType.S },    // GSI için gerekli
                    new() { AttributeName = "CreatedAt", AttributeType = ScalarAttributeType.S } // GSI için gerekli
                },
                // 2. Primary Key (Partition Key = ShortCode)
                KeySchema = new List<KeySchemaElement>
                {
                    new() { AttributeName = "ShortCode", KeyType = KeyType.HASH } // Repository ile eşitlendi!
                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 5,
                    WriteCapacityUnits = 5
                },
                // 🔥 GELECEĞE YATIRIM: Kullanıcı bazlı "en yeniden eskiye" link listeleme indeksi (GSI)
                GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
                {
                    new()
                    {
                        IndexName = "UserLinksIndex",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new() { AttributeName = "UserId", KeyType = KeyType.HASH },     // GSI Partition Key
                            new() { AttributeName = "CreatedAt", KeyType = KeyType.RANGE } // GSI Sort Key (Sıralama için)
                        },
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                        ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 5 }
                    }
                }
            };

            await client.CreateTableAsync(request);
            logger.LogInformation("🎉 [DynamoDB Local] '{TableName}' tablosu, ShortCode anahtarı ve UserLinksIndex ile başarıyla oluşturuldu.", tableName);

            // Tablonun local container içinde tamamen hazır hale gelmesi için küçük bir nefes payı
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ [DynamoDB Local] Tablo oluşturma aşamasında beklenmeyen bir hata oluştu!");
        }
    }

    public static async Task EnsureUserRefreshTokensTableCreatedAsync(this IApplicationBuilder app, CancellationToken cancellationToken = default)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DynamoDbInitializer");

        // 🚨 GÜVENLİK DUVARI: Sadece yerel geliştirme (Local Docker) ortamında çalışsın!
        if (!environment.IsDevelopment()) return;

        var dynamoDbClient = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();

        const string tableName = "UserRefreshTokens";

        try
        {
            // 1. Tablo var mı kontrol et
            await dynamoDbClient.DescribeTableAsync(tableName, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            // 2. Tablo yoksa sıfırdan oluşturma isteği hazırla
            var createTableRequest = new CreateTableRequest
            {
                TableName = tableName,
                //BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new() { AttributeName = "UserId", AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = "Token", AttributeType = ScalarAttributeType.S }
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new() { AttributeName = "UserId", KeyType = KeyType.HASH }, // Partition Key
                    new() { AttributeName = "Token", KeyType = KeyType.RANGE } // Sort Key
                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 5, WriteCapacityUnits = 5
                }
            };

            await dynamoDbClient.CreateTableAsync(createTableRequest, cancellationToken);
            logger.LogInformation("🎉 [DynamoDB Local] '{TableName}' tablosu başarıyla oluşturuldu.", tableName);
            
            // Local DynamoDB'nin tabloyu tamamen hazır hale getirmesi için çok kısa bir an bekleme (Opsiyonel)
            await Task.Delay(1000, cancellationToken);

            // 3. 🔥 EN KRİTİK DETAY: TTL Özelliğini Otomatik Aktifleştir
            var updateTimeToLiveRequest = new UpdateTimeToLiveRequest
            {
                TableName = tableName,
                TimeToLiveSpecification = new TimeToLiveSpecification
                {
                    Enabled = true,
                    AttributeName = "ExpiresAtTimestamp" // Bizim entity'deki long değerimizle eşleşiyor
                }
            };

            await dynamoDbClient.UpdateTimeToLiveAsync(updateTimeToLiveRequest, cancellationToken);
            //Console.WriteLine($"[INFO] DynamoDB '{tableName}' tablosu ve TTL aktivasyonu başarıyla kuruldu.");
            logger.LogInformation("[INFO]🎉 [DynamoDB Local] '{TableName}' tablosu ve TTL aktivasyonu başarıyla kuruldu.", tableName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ [DynamoDB Local] Tablo oluşturma aşamasında beklenmeyen bir hata oluştu!");
        }
    }
}
