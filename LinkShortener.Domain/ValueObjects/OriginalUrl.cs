namespace LinkShortener.Domain.ValueObjects;

public sealed class OriginalUrl
{
    public string Value { get; }

    private OriginalUrl(string value)
    {
        Value = value;
    }

    public static OriginalUrl Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("URL boş olamaz.", nameof(value));

        // Basit bir URL format doğrulaması (SOLID - Single Responsibility)
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uriResult) ||
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Geçersiz URL formatı.", nameof(value));
        }

        return new OriginalUrl(value);
    }

    // Değer bazlı karşılaştırma için eşitlik kontrolleri
    public override bool Equals(object? obj) => obj is OriginalUrl other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}