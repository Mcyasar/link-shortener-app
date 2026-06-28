using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Infrastructure.Services;

internal sealed class DynamoDbRefreshTokenService(IAmazonDynamoDB dynamoDbClient) : IRefreshTokenService
{
    private readonly IAmazonDynamoDB _dynamoDbClient = dynamoDbClient;
    private const string TableName = "UserRefreshTokens"; // Tablo adı sabit olarak tanımlandı

    // ✍️ 1. KAYDETME: Sıfır mapping, tamamen dinamik Document ataması
    public async Task SaveRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken cancellationToken)
    {        
        
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = MapToAttributes(refreshToken)
        };

        if (refreshToken.RevokedAt.HasValue)
        {
            request.Item["RevokedAt"] = new AttributeValue { S = refreshToken.RevokedAt.Value.ToString("o") };
        }

        await _dynamoDbClient.PutItemAsync(request, cancellationToken);
    }

    private static Dictionary<string, AttributeValue> MapToAttributes(UserRefreshToken refreshToken)
    {
        return new Dictionary<string, AttributeValue>
        {
            { "UserId", new AttributeValue { S = refreshToken.UserId } },
            { "Token", new AttributeValue { S = refreshToken.Token } },
            { "CreatedByIp", new AttributeValue { S = refreshToken.CreatedByIp } },
            { "CreatedAt", new AttributeValue { S = refreshToken.CreatedAt.ToString("O") } }, // ISO 8601 formatı
            { "ExpiresAt", new AttributeValue { S = refreshToken.ExpiresAt.ToString("o") } },
            { "ExpiresAtTimestamp", new AttributeValue { N = refreshToken.ExpiresAtTimestamp.ToString() } }
        };
    }

    // 🔍 2. TEKİL OKUMA: Nokta atışı Point-Read
    public async Task<UserRefreshToken?> GetTokenAsync(string userId, string token, CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "UserId", new AttributeValue { S = userId } },
                { "Token", new AttributeValue { S = token } }
            }
        };
        
        var response = await _dynamoDbClient.GetItemAsync(request, cancellationToken);
        if (!response.IsItemSet || response.Item.Count == 0) return null;

        return MapFromAttributes(response.Item);
    }

    private static UserRefreshToken MapFromAttributes(Dictionary<string, AttributeValue> attributes)
    {
        return new UserRefreshToken
        {
            UserId = attributes["UserId"].S,
            Token = attributes["Token"].S,
            CreatedByIp = attributes["CreatedByIp"].S,
            CreatedAt = DateTime.Parse(attributes["CreatedAt"].S),
            ExpiresAt = DateTime.Parse(attributes["ExpiresAt"].S),
            RevokedAt = attributes.TryGetValue("RevokedAt", out var revokedAttr) && revokedAttr.S != null
                ? DateTime.Parse(revokedAttr.S)
                : null,
            RevokedByIp = attributes.TryGetValue("RevokedByIp", out var revokedIpAttr) && revokedIpAttr.S != null
                ? revokedIpAttr.S
                : null
        };
    }

    // 🔄 3. GÜNCELLEME: Mevcut kaydı ezme (Overwrite/Upsert)
    public async Task UpdateRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken cancellationToken)
    {
        // PutItem yapısı anahtarlar eşleştiğinde kaydı doğrudan günceller
        await SaveRefreshTokenAsync(refreshToken, cancellationToken);
    }

    // 💣 4. TÜM OTURUMLARI PATLATMA (Batch Write - Cost Optimization)
    public async Task RevokeAllUserSessionsAsync(string userId, CancellationToken cancellationToken)
    {
        // Kullanıcıya ait tüm token'ları Partition Key (UserId) üzerinden Query ile buluyoruz
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "UserId = :v_userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":v_userId", new AttributeValue { S = userId } }
            },
            ProjectionExpression = "UserId, Token, RevokedAt, ExpiresAt" // Sadece gerekli alanları çekiyoruz
        };

        var queryResponse = await _dynamoDbClient.QueryAsync(queryRequest, cancellationToken);

        if (queryResponse.Items.Count == 0) return;

        var writeRequests = new List<WriteRequest>();

        foreach (var item in queryResponse.Items)
        {
            // Eğer token'ın revizyon zamanı yoksa ve süresi dolmadıysa silme isteği ekle
            bool isRevoked = item.ContainsKey("RevokedAt") && item["RevokedAt"].S != null;
            DateTime expiresAt = DateTime.Parse(item["ExpiresAt"].S);
            
            if (!isRevoked && DateTime.UtcNow < expiresAt) // Sadece aktif ve süresi dolmamış token'ları iptal et
            {
                writeRequests.Add(new WriteRequest
                {
                    DeleteRequest = new DeleteRequest
                    {
                        Key = new Dictionary<string, AttributeValue>
                        {
                            { "UserId", item["UserId"] },
                            { "Token", item["Token"] }
                        }
                    }
                });
            }
        }

        if (writeRequests.Any())
        {
            var batchWriteRequest = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>> { { TableName, writeRequests } }
            };
            await _dynamoDbClient.BatchWriteItemAsync(batchWriteRequest, cancellationToken);
        }
    }
}