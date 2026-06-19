namespace LinkShortener.API.Models;

public record RefreshTokenRequest(Guid UserId, string RefreshToken);
