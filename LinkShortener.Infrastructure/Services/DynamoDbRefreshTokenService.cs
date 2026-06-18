using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Infrastructure.Services;

internal sealed class DynamoDbRefreshTokenService : IRefreshTokenService
{
    private readonly Table _tokenTable;

    public DynamoDbRefreshTokenService(IAmazonDynamoDB dynamoDbClient)
    {
        // Projenin genel yaklaşımına uygun olarak tabloyu doğrudan low-level istemci üzerinden yüklüyoruz.
        // Bu yapı şemaya ihtiyaç duymaz, sadece verdiğimiz PK ve SK isimleriyle eşleşir.
        _tokenTable = new TableBuilder(dynamoDbClient, "UserRefreshTokens")
            .Build();
    }

    // ✍️ 1. KAYDETME: Sıfır mapping, tamamen dinamik Document ataması
    public async Task SaveRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken cancellationToken)
    {
        var doc = new Document
        {
            ["UserId"] = refreshToken.UserId, // Veritabanındaki HashKey ile tam eşleşme
            ["Token"] = refreshToken.Token,   // Veritabanındaki RangeKey ile tam eşleşme
            ["CreatedByIp"] = refreshToken.CreatedByIp,
            ["CreatedAt"] = refreshToken.CreatedAt.ToString("o"), // ISO 8601 formatı
            ["ExpiresAt"] = refreshToken.ExpiresAt.ToString("o"),
            ["ExpiresAtTimestamp"] = refreshToken.ExpiresAtTimestamp // TTL sütunumuz
        };

        if (refreshToken.RevokedAt.HasValue)
        {
            doc["RevokedAt"] = refreshToken.RevokedAt.Value.ToString("o");
        }

        await _tokenTable.PutItemAsync(doc, cancellationToken);
    }

    // 🔍 2. TEKİL OKUMA: Nokta atışı Point-Read
    public async Task<UserRefreshToken?> GetTokenAsync(string userId, string token, CancellationToken cancellationToken)
    {
        // PK ve SK değerlerini vererek doğrudan dökümanı çekiyoruz
        Document doc = await _tokenTable.GetItemAsync(userId, token, cancellationToken);
        
        if (doc == null) return null;

        return new UserRefreshToken
        {
            UserId = doc["UserId"].AsString(),
            Token = doc["Token"].AsString(),
            CreatedByIp = doc["CreatedByIp"].AsString(),
            CreatedAt = DateTime.Parse(doc["CreatedAt"].AsString()),
            ExpiresAt = DateTime.Parse(doc["ExpiresAt"].AsString()),
            RevokedAt = doc.ContainsKey("RevokedAt") ? DateTime.Parse(doc["RevokedAt"].AsString()) : null
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
        // Kullanıcıya ait tüm token'ları Partition Key (UserId) üzerinden Query filtresiyle buluyoruz
        var queryFilter = new QueryFilter("UserId", QueryOperator.Equal, userId);
        var search = _tokenTable.Query(queryFilter);
        
        List<Document> documents = await search.GetRemainingAsync(cancellationToken);

        if (documents.Count == 0) return;

        // AWS SDK Document modelinin toplu silme (Batch) motorunu tetikliyoruz
        var batchWrite = _tokenTable.CreateBatchWrite();

        foreach (var doc in documents)
        {
            // Eğer token'ın revizyon zamanı yoksa ve süresi dolmadıysa silme kuyruğuna ekle
            bool isRevoked = doc.ContainsKey("RevokedAt");
            DateTime expiresAt = DateTime.Parse(doc["ExpiresAt"].AsString());
            
            if (!isRevoked && DateTime.UtcNow < expiresAt)
            {
                // Sadece PK ve SK vererek batch'e ekliyoruz
                batchWrite.AddKeyToDelete(doc["UserId"].AsPrimitive(), doc["Token"].AsPrimitive());
            }
        }

        // Tek bir network çağrısıyla kullanıcının tüm cihazlardaki oturumlarını uçuruyoruz
        await batchWrite.ExecuteAsync(cancellationToken);
    }
}