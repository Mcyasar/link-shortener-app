using MediatR;
using LinkShortener.Application.Features.Users.Queries.LoginUser;
using LinkShortener.Application.Common.Results;

namespace LinkShortener.Application.Features.Users.Commands.RefreshToken;

// Refresh token yenileme işlemi, yeni bir Access Token ve Refresh Token dönecek
public record RefreshTokenCommand(Guid UserId, string RefreshToken, string ClientIpAddress) : IRequest<Result<LoginResponseDto>>;