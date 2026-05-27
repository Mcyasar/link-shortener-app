using MediatR;

namespace LinkShortener.Application.Features.ShortenedLinks.Commands.CreateShortLink;

// MediatR'a bunun bir istek olduğunu ve geriye string (kısa kod) döneceğini söylüyoruz
public sealed record CreateShortLinkCommand(
    string OriginalUrl,
    Guid? UserId,
    DateTime? ExpiresAt = null) : IRequest<string>;