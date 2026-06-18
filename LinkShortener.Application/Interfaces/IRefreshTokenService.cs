using LinkShortener.Domain.Entities;

namespace LinkShortener.Application.Interfaces;

public interface IRefreshTokenService
{
    Task SaveRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken cancellationToken);
    Task<UserRefreshToken?> GetTokenAsync(string userId, string token, CancellationToken cancellationToken);
    Task UpdateRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken cancellationToken);
    Task RevokeAllUserSessionsAsync(string userId, CancellationToken cancellationToken);
}