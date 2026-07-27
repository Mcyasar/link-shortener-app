using LinkShortener.Domain.ValueObjects;

namespace LinkShortener.Domain.Entities;

public sealed class ShortenedLink
{
    public Guid Id { get; private set; }
    public string ShortCode { get; private set; }
    public OriginalUrl OriginalUrl { get; private set; }
    public Guid? UserId { get; private set; } // PostgreSQL'deki kullanıcı ile gevşek bağ (Loose Coupling)
    public int ClickCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    // ORM / Serileştirme araçları için boş constructor (Shadow Constructor)
    private ShortenedLink() { }

    public ShortenedLink(Guid id, string shortCode, OriginalUrl originalUrl, Guid? userId, DateTime? expiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
            throw new ArgumentException("Kısa kod boş olamaz.", nameof(shortCode));

        Id = id;
        ShortCode = shortCode;
        OriginalUrl = originalUrl;
        UserId = userId;
        ClickCount = 0;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// TODO bu method silinecek
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    // İş Mantığı Metodu: Her tıklamada sayacı artırır
    public void RecordClick()
    {
        if (IsExpired())
            throw new InvalidOperationException("Bu linkin süresi dolmuş.");

        ClickCount++;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    }
}