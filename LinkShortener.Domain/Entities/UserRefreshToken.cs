namespace LinkShortener.Domain.Entities;

public sealed class UserRefreshToken
{
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string CreatedByIp { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Altyapı katmanında TTL attribute ismi olarak "ExpiresAtTimestamp" eşlemesi yapacağız
    public long ExpiresAtTimestamp => new DateTimeOffset(ExpiresAt).ToUnixTimeSeconds();

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}