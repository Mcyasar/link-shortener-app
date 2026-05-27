using MediatR;

namespace LinkShortener.Application.Features.Users.Queries.LoginUser;

// Giriş işlemi geriye Token ve kullanıcı detaylarını taşıyan bir DTO dönecek
public record LoginUserQuery(string Email, string Password) : IRequest<LoginResponseDto>;

public record LoginResponseDto(Guid UserId, string Email, string Token);