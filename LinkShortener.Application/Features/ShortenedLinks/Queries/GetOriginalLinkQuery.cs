using MediatR;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetOriginalLink;

public sealed record GetOriginalLinkQuery(string ShortCode) : IRequest<string>;