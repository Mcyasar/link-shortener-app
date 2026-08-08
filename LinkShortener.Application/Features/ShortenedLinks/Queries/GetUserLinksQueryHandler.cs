using MediatR;
using LinkShortener.Application.Interfaces;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetUserLinks;

public sealed class GetUserLinksQueryHandler : IRequestHandler<GetUserLinksQuery, List<ShortenedLinkDto>>
{
    private readonly IShortenedLinkRepository _linkRepository;

    public GetUserLinksQueryHandler(IShortenedLinkRepository linkRepository)
    {
        _linkRepository = linkRepository;
    }

    public async Task<List<ShortenedLinkDto>> Handle(GetUserLinksQuery request, CancellationToken cancellationToken)
    {
        var links = await _linkRepository.GetLinksByUserIdAsync(request.UserId, cancellationToken);

        foreach(var link in links)
        {
            var linkStats = await _linkRepository.GetShortCodeStatsAsync(link.ShortCode, cancellationToken);
            link.ClickCount = linkStats.FirstOrDefault()?.ClickCount ?? 0;
        }

        return links.Select(link => new ShortenedLinkDto(
            link.Id,
            link.ShortCode,
            link.OriginalUrl.Value,
            link.ClickCount,
            link.CreatedAt,
            link.ExpiresAt
        )).ToList();
    }
}