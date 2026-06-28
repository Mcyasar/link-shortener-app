using MediatR;

namespace LinkShortener.Application.Features.Users.Queries.LoginUser;

// Giriş işlemi geriye Token ve kullanıcı detaylarını taşıyan bir DTO dönecek
public record LoginUserQuery(string Email, string Password);

public record LoginUserCommandQuery(string Email, string Password, string ClientIpAddress) : IRequest<LoginCommandResponseDto>;

public record LoginCommandResponseDto(Guid UserId, string Email, string Token, string RefreshToken);

public record LoginResponseDto(Guid UserId, string Email, string Token);