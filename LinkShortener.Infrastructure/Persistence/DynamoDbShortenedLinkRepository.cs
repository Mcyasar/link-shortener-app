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

    public async Task UpdateLinkStatsBySumAsync(string shortCode, dynamic item, CancellationToken cancellationToken)
    {
        var request = new UpdateItemRequest
        {
            TableName = "LinkStats",
            Key = new Dictionary<string, AttributeValue>
            {
                { "ShortCode", new AttributeValue { S = shortCode } }
            },
            // UpdateExpression Püf Noktası:
            // 'ADD clickCount :inc' -> Varolan sayıya ekler. Kıymetli kısmı: Kayıt yoksa 0 kabul edip ekler!
            // 'SET lastUpdated = :now' -> En son ne zaman güncellendiğini yazar.
            UpdateExpression = "ADD clickCount :inc SET lastUpdated = :now",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":inc", new AttributeValue { N = item.IncrementAmount.ToString() } },
                { ":now", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
            }
        };

        await _dynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task<List<ShortenedLink>> GetLinksByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            IndexName = "UserLinksIndex", // Kullanıcıya özel GSI'ımızı kullanıyoruz
            KeyConditionExpression = "UserId = :v_userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":v_userId", new AttributeValue { S = userId.ToString() } }
            },
            ScanIndexForward = false, // En yeni linkleri önce getir (CreatedAt Sort Key olduğu için)
            ProjectionExpression = "Id, ShortCode, OriginalUrl, ClickCount, CreatedAt, ExpiresAt, UserId" // Sadece gerekli alanları çekiyoruz
        };

        var response = await _dynamoDb.QueryAsync(queryRequest, cancellationToken);

        var links = new List<ShortenedLink>();
        foreach (var item in response.Items)
        {
            // MapFromAttributes metodu zaten Dictionary<string, AttributeValue> alıyor
            links.Add(MapFromAttributes(item));
        }

        return links;
    }

    public async Task<List<LinkStats>> GetShortCodeStatsAsync(string shortCode, CancellationToken cancellationToken)
    {
        var queryRequest = new QueryRequest
        {
            TableName = "LinkStats",
            IndexName = "ShortCode", // Kullanıcıya özel GSI'ımızı kullanıyoruz
            KeyConditionExpression = "ShortCode = :v_shortCode",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":v_shortCode", new AttributeValue { S = shortCode } }
            },
            ScanIndexForward = false, // En yeni linkleri önce getir (CreatedAt Sort Key olduğu için)
            ProjectionExpression = "ShortCode, ClickCount, LastUpdated" // Sadece gerekli alanları çekiyoruz
        };

        var response = await _dynamoDb.QueryAsync(queryRequest, cancellationToken);

        var links = new List<LinkStats>();
        foreach (var item in response.Items)
        {
            links.Add(new LinkStats(
                shortCode: item["ShortCode"].S,
                clickCount: int.Parse(item["ClickCount"].N),
                lastUpdated: DateTime.Parse(item["LastUpdated"].S)
            ));
        }

        return links;
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