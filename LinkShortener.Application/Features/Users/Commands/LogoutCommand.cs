using MediatR;
using LinkShortener.Application.Common.Results;

namespace LinkShortener.Application.Features.Users.Commands.LogoutUser;

// Logout işlemi için komut. Blacklist edilecek JWT'nin JTI'sını ve kullanıcının ID'sini taşır.
public record LogoutCommand(string JwtId, Guid UserId, TimeSpan TokenRemainingLifetime) : IRequest<Result>;