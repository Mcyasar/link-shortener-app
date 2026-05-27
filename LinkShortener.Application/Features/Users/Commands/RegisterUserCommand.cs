using MediatR;

namespace LinkShortener.Application.Features.Users.Commands.RegisterUser;

/// <summary>
/// Yeni bir kullanıcı kayıt isteğini taşıyan immutable (değiştirilemez) komut nesnesi.
/// </summary>
public record RegisterUserCommand(
    string Email,
    string Password
) : IRequest<Guid>;