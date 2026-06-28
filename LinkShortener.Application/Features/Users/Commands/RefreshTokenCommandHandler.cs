using MediatR;
using LinkShortener.Application.Features.Users.Queries.LoginUser;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;
using LinkShortener.Application.Common.Results;

namespace LinkShortener.Application.Features.Users.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<LoginCommandResponseDto>>
{
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RefreshTokenCommandHandler(
        IRefreshTokenService refreshTokenService,
        IUserRepository userRepository,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _refreshTokenService = refreshTokenService;
        _userRepository = userRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<LoginCommandResponseDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // 1. Refresh Token'ı veritabanından getir
        // DynamoDbRefreshTokenService'deki GetTokenAsync metodu hem UserId hem de Token gerektirdiğinden,
        // Command'e UserId'yi de dahil ettik. Bu UserId, genellikle mevcut (belki süresi dolmuş)
        // access token'dan veya refresh token'ın kendisinden (eğer JWT ise) alınır.
        var existingRefreshToken = await _refreshTokenService.GetTokenAsync(request.UserId.ToString(), request.RefreshToken, cancellationToken);
        if (existingRefreshToken is null)
            return Result<LoginCommandResponseDto>.Failure(Error.Unauthorized("Geçersiz refresh token."));

        // 2. Refresh Token'ı doğrula
        if (existingRefreshToken.IsRevoked)
            return Result<LoginCommandResponseDto>.Failure(Error.Unauthorized("Refresh token iptal edilmiş."));

        // Use IsExpired property from UserRefreshToken entity
        if (existingRefreshToken.IsExpired)
            return Result<LoginCommandResponseDto>.Failure(Error.Unauthorized("Refresh token süresi dolmuş."));

        // 3. Kullanıcıyı getir
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result<LoginCommandResponseDto>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        // 4. Eski refresh token'ı iptal et (Rotation)
        existingRefreshToken.Revoke(request.ClientIpAddress);
        await _refreshTokenService.UpdateRefreshTokenAsync(existingRefreshToken, cancellationToken);

        // 5. Yeni JWT Access Token oluştur
        var newAccessToken = _jwtTokenGenerator.GenerateToken(user);

        // 6. Yeni Refresh Token oluştur ve kaydet
        var newRefreshToken = new UserRefreshToken
        {
            UserId = user.Id.ToString(),
            Token = Guid.NewGuid().ToString("N"), // Kriptografik olarak güçlü, benzersiz bir token
            CreatedByIp = request.ClientIpAddress,
            ExpiresAt = DateTime.UtcNow.AddDays(7) // Refresh token 7 gün geçerli olsun
        };
        await _refreshTokenService.SaveRefreshTokenAsync(newRefreshToken, cancellationToken);

        return Result<LoginCommandResponseDto>.Success(new LoginCommandResponseDto(user.Id, user.Email, newAccessToken, newRefreshToken.Token));
    }
}