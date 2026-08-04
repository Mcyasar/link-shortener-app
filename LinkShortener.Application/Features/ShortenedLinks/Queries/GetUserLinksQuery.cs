using MediatR;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetUserLinks;

public sealed record GetUserLinksQuery(Guid UserId) : IRequest<List<ShortenedLinkDto>>;