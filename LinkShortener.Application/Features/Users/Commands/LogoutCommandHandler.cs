using MediatR;
using LinkShortener.Application.Common.Results;
using LinkShortener.Application.Interfaces;

namespace LinkShortener.Application.Features.Users.Commands.LogoutUser;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenService _refreshTokenService;

    public LogoutCommandHandler(ITokenBlacklistService tokenBlacklistService, IRefreshTokenService refreshTokenService)
    {
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenService = refreshTokenService;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // 1. Access Token'ı blacklist'e ekle
        // Token'ın kalan ömrü boyunca blacklist'te kalmasını sağlıyoruz.
        await _tokenBlacklistService.BlacklistTokenAsync(request.JwtId, request.TokenRemainingLifetime, cancellationToken);

        // 2. Kullanıcının tüm refresh token'larını iptal et (isteğe bağlı ama güvenlik için önerilir)
        // Bu, kullanıcının tüm cihazlardaki oturumlarını sonlandırır.
        await _refreshTokenService.RevokeAllUserSessionsAsync(request.UserId.ToString(), cancellationToken);

        return Result.Success();
    }
}