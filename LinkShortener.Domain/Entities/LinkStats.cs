namespace LinkShortener.Domain.Entities;

public sealed class LinkStats
{
    public string ShortCode { get; private set; }
    public int ClickCount { get; private set; }
    public DateTime LastUpdated { get; private set; }

    // ORM / Serileştirme araçları için boş constructor (Shadow Constructor)
    private LinkStats() { }

    public LinkStats(string shortCode, int clickCount, DateTime lastUpdated)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
            throw new ArgumentException("Kısa kod boş olamaz.", nameof(shortCode));

        ShortCode = shortCode;
        ClickCount = clickCount;
        LastUpdated = lastUpdated;
    }
}