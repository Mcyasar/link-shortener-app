using MediatR;
using LinkShortener.Application.Interfaces;
using MassTransit;
using LinkShortener.Application.Features.ShortenedLinks.Events;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetOriginalLink;

public sealed class GetOriginalLinkQueryHandler : IRequestHandler<GetOriginalLinkQuery, string>
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IShortenedLinkRepository _linkRepository;
    private readonly ICacheService _cacheService;

    public GetOriginalLinkQueryHandler(
        IPublishEndpoint publishEndpoint,
        IShortenedLinkRepository linkRepository,
        ICacheService cacheService)
    {
        _publishEndpoint = publishEndpoint;
        _linkRepository = linkRepository;
        _cacheService = cacheService;
    }

    public async Task<string> Handle(GetOriginalLinkQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = $"link:{request.ShortCode}";

        // Önce Cache (Redis) kontrol edilir
        var cachedUrl = await _cacheService.GetAsync<string>(cacheKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(cachedUrl))
        {
            var shortenedLink = await _linkRepository.GetByCodeAsync(request.ShortCode, cancellationToken);

            if (shortenedLink == null)
                throw new KeyNotFoundException("Kısaltılmış link bulunamadı.");

            if (shortenedLink.IsExpired())
                throw new InvalidOperationException("Bu linkin kullanım süresi dolmuş.");

            cachedUrl = shortenedLink.OriginalUrl.Value;

            // Bir sonraki istekler için veri Cache'e yazılır (Örn: 1 saatlik ömürle)
            await _cacheService.SetAsync(
                cacheKey,
                shortenedLink.OriginalUrl.Value,
                TimeSpan.FromHours(1),
                cancellationToken);
        }        

        await _publishEndpoint.Publish(new LinkClickedEvent(request.ShortCode, DateTime.UtcNow), cancellationToken); // Tıklama sayısını artırmak için event yayınlıyoruz

        return cachedUrl;
    }
}
