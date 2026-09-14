using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;

namespace LinkShortener.IntegrationTests;

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    // Testcontainers tanımları
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly IContainer _dynamoDbContainer = new ContainerBuilder("amazon/dynamodb-local:latest")
        .WithPortBinding(8000, true) // Rastgele boş bir porta eşle (Çakışmayı önler)
        .WithCommand("-jar", "DynamoDBLocal.jar", "-sharedDb")
        .Build();

    public IAmazonDynamoDB DynamoDbClient { get; private set; } = null!;
    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Konteynerleri asenkron olarak arka arkaya ayağa kaldırıyoruz
        await Task.WhenAll(_redisContainer.StartAsync(), _dynamoDbContainer.StartAsync());

        // Host ve Port'u Testcontainers üzerinden dinamik alıyoruz
        var dynamoHost = _dynamoDbContainer.Hostname;
        var dynamoPort = _dynamoDbContainer.GetMappedPublicPort(8000);

        // DynamoDB için dinamik üretilen bağlantı adresini alıyoruz
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = $"http://{dynamoHost}:{dynamoPort}",
            AuthenticationRegion = "eu-central-1"
        };

        // AWS SDK'nın zincir hatası vermesini engellemek için dummy credentials geçiyoruz
        var credentials = new BasicAWSCredentials("test-key", "test-secret");
    
        DynamoDbClient = new AmazonDynamoDBClient(credentials, config);

        // DynamoDB şemasız olsa da "ShortenedLinks" tablosunun var olması şarttır.
        // Test başlamadan önce tabloyu otomatik oluşturuyoruz.
        await CreateShortenedLinksTableAsync();
    }

    public async Task DisposeAsync()
    {
        // Testler bittiğinde konteynerleri kapat ve Docker'ı temizle
        await Task.WhenAll(_redisContainer.DisposeAsync().AsTask(), _dynamoDbContainer.DisposeAsync().AsTask());
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