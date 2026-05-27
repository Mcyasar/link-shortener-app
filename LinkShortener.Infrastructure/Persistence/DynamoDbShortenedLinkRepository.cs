using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;
using LinkShortener.Domain.ValueObjects;

namespace LinkShortener.Infrastructure.Persistence;

public sealed class DynamoDbShortenedLinkRepository : IShortenedLinkRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "ShortenedLinks";

    public DynamoDbShortenedLinkRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task AddAsync(ShortenedLink link, CancellationToken cancellationToken)
    {
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = MapToAttributes(link)
        };

        await _dynamoDb.PutItemAsync(request, cancellationToken);
    }

    public async Task<ShortenedLink?> GetByCodeAsync(string shortCode, CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "ShortCode", new AttributeValue { S = shortCode } }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request, cancellationToken);
        if (!response.IsItemSet) return null;

        return MapFromAttributes(response.Item);
    }

    public async Task UpdateAsync(string shortCode, CancellationToken cancellationToken)
    {
        var request = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "ShortCode", new AttributeValue { S = shortCode } }
            },
            AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
            {
                {
                    "ClickCount",
                    new AttributeValueUpdate
                    { 
                        // CRITICAL: Buraya link.ClickCount yazmıyoruz! Sabit "1" yazıyoruz.
                        // Çünkü DynamoDB'ye "mevcut değerin üzerine 1 ekle" emri veriyoruz.
                        // Bu yaklaşım hem thread safe hem de concurrent işlemlerde veri kaybını önler.
                        Value = new AttributeValue { N = "1" },
                        Action = AttributeAction.ADD // <-- PUT YERİNE ADD!
                    }
                }
            }
        };

        await _dynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    // --- DDD Entity <-> DynamoDB Mapping Mantığı ---
    private static Dictionary<string, AttributeValue> MapToAttributes(ShortenedLink link)
    {
        return new Dictionary<string, AttributeValue>
        {
            { "ShortCode", new AttributeValue { S = link.ShortCode } },
            { "Id", new AttributeValue { S = link.Id.ToString() } },
            { "OriginalUrl", new AttributeValue { S = link.OriginalUrl.Value } },
            { "ClickCount", new AttributeValue { N = link.ClickCount.ToString() } },
            { "CreatedAt", new AttributeValue { S = link.CreatedAt.ToString("O") } },
            { "UserId", link.UserId.HasValue ? new AttributeValue { S = link.UserId.Value.ToString() } : new AttributeValue { NULL = true } },
            { "ExpiresAt", link.ExpiresAt.HasValue ? new AttributeValue { S = link.ExpiresAt.Value.ToString("O") } : new AttributeValue { NULL = true } }
        };
    }

    private static ShortenedLink MapFromAttributes(Dictionary<string, AttributeValue> attributes)
    {
        var originalUrl = OriginalUrl.Create(attributes["OriginalUrl"].S);

        Guid? userId = attributes.TryGetValue("UserId", out var userAttr) && userAttr.NULL != true
        ? Guid.Parse(userAttr.S)
        : null;

        DateTime? expiresAt = attributes.TryGetValue("ExpiresAt", out var expAttr) && expAttr.NULL != true
        ? DateTime.Parse(expAttr.S)
        : null;

        var link = new ShortenedLink(
            id: Guid.Parse(attributes["Id"].S),
            shortCode: attributes["ShortCode"].S,
            originalUrl: originalUrl,
            userId: userId,
            expiresAt: expiresAt
        );

        int clickCount = int.Parse(attributes["ClickCount"].N);
        for (int i = 0; i < clickCount; i++) link.RecordClick();

        return link;
    }
}