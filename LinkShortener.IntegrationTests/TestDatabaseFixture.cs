using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Testcontainers.Redis;

namespace LinkShortener.IntegrationTests;

public sealed class TestDatabaseFixture: WebApplicationFactory<Program>, IAsyncLifetime
{
    // Testcontainers tanımları
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly IContainer _dynamoDbContainer = new ContainerBuilder("amazon/dynamodb-local:latest")
        .WithPortBinding(8000, true) // Rastgele boş bir porta eşle (Çakışmayı önler)
        .WithCommand("-jar", "DynamoDBLocal.jar", "-sharedDb", "-inMemory")
        .Build();

    public IAmazonDynamoDB DynamoDbClient { get; private set; } = null!;
    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public async Task InitializeAsync()
{
    await Task.WhenAll(_redisContainer.StartAsync(), _dynamoDbContainer.StartAsync());

    var dynamoHost = _dynamoDbContainer.Hostname;
    var dynamoPort = _dynamoDbContainer.GetMappedPublicPort(8000);

    var config = new AmazonDynamoDBConfig
    {
        // 1. ServiceURL'i net olarak verin
        ServiceURL = $"http://{dynamoHost}:{dynamoPort}",
        
        // 2. RegionEndpoint ve AuthenticationRegion'ı açıkça eşleyin
        RegionEndpoint = RegionEndpoint.EUCentral1,
        AuthenticationRegion = "eu-central-1",
        
        // 3. Yerel HTTP bağlantısı için SSL doğrulamalarını devre dışı bırakın
        UseHttp = true
    };

    // AWS SDK SigV4 imzalayıcısı için standart dummy anahtarlar
    var credentials = new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey");

    DynamoDbClient = new AmazonDynamoDBClient(credentials, config);

    await CreateShortenedLinksTableAsync();
}

    public new async Task DisposeAsync()
    {
        // Testler bittiğinde konteynerleri kapat ve Docker'ı temizle
        await Task.WhenAll(_redisContainer.DisposeAsync().AsTask(), _dynamoDbContainer.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // 4. KRİTİK ADIM: Program.cs'teki gerçek DynamoDB kaydını DI konteynerinden sil
            services.RemoveAll<IAmazonDynamoDB>();

            // 5. Testcontainers ile ayağa kalkan istemciyi ekle
            services.AddSingleton<IAmazonDynamoDB>(DynamoDbClient);
        });
    }

    private async Task CreateShortenedLinksTableAsync()
    {
        var request = new CreateTableRequest
        {
            TableName = "ShortenedLinks",
            KeySchema = [new KeySchemaElement("ShortCode", KeyType.HASH)],
            AttributeDefinitions = [new AttributeDefinition("ShortCode", ScalarAttributeType.S)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        };

        await DynamoDbClient.CreateTableAsync(request);
    }
}

// xUnit'e bu fixture'ı tüm test sınıflarında ortak kullanabileceğini söylüyoruz
[CollectionDefinition("TestDatabaseCollection")]
public class TestDatabaseCollection : ICollectionFixture<TestDatabaseFixture> { }