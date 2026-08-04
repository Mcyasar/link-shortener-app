using MediatR;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetOriginalLink;

public sealed class GetOriginalLinkQueryHandler(
    IShortenedLinkRepository linkRepository,
    ICacheService cacheService,
    ILinkClickOutboxRepository linkClickOutboxRepository,
    IUnitOfWork unitOfWork,
    ILogger<GetOriginalLinkQueryHandler> logger) : IRequestHandler<GetOriginalLinkQuery, string>
{
    private readonly IShortenedLinkRepository _linkRepository = linkRepository;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILinkClickOutboxRepository _linkClickOutboxRepository = linkClickOutboxRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ILogger<GetOriginalLinkQueryHandler> _logger = logger;

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

        // Outbox pattern: LinkClickedEvent'i doğrudan RabbitMQ'ya göndermek yerine PostgreSQL outbox tablosuna kaydediyoruz.
        var outboxEntry = new LinkClickOutbox(request.ShortCode, DateTime.UtcNow);
        await _linkClickOutboxRepository.AddAsync(outboxEntry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // Outbox kaydını veritabanına kaydet

        _logger.LogInformation("LinkClickedEvent for ShortCode: {ShortCode} saved to outbox.", request.ShortCode);

        return cachedUrl;
    }
}