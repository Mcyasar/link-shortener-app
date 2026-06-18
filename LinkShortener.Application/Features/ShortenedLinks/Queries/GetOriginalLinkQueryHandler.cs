using MediatR;
using LinkShortener.Application.Interfaces;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetOriginalLink;

public sealed class GetOriginalLinkQueryHandler : IRequestHandler<GetOriginalLinkQuery, string>
{
    private readonly IShortenedLinkRepository _linkRepository;
    private readonly ICacheService _cacheService;

    public GetOriginalLinkQueryHandler(
        IShortenedLinkRepository linkRepository,
        ICacheService cacheService)
    {
        _linkRepository = linkRepository;
        _cacheService = cacheService;
    }

    public async Task<string> Handle(GetOriginalLinkQuery request, CancellationToken cancellationToken)
    {

        string cacheKey = $"link:{request.ShortCode}";

        // 1. Önce Cache (Redis) kontrol edilir
        var cachedUrl = await _cacheService.GetAsync<string>(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedUrl))
        {
            // Burası önemli bir mimari karar alanı: Cache'ten okurken tıklama sayısını 
            // asenkron olarak arka planda (Background Job / MQ) artırmak prod ortamında daha sağlıklı olacaktır.
            // Şimdilik doğrudan cache'ten yönlendirip geçiyoruz.
            return cachedUrl;
        }

        // 2. Cache'te yoksa Veritabanından (DynamoDB) sorgulanır
        var shortenedLink = await _linkRepository.GetByCodeAsync(request.ShortCode, cancellationToken);

        if (shortenedLink == null)
            throw new KeyNotFoundException("Kısaltılmış link bulunamadı.");

        if (shortenedLink.IsExpired())
            throw new InvalidOperationException("Bu linkin kullanım süresi dolmuş.");

        // 3. Domain iş kuralı işletilir: Tıklama sayısı artırılır
        shortenedLink.RecordClick();

        // 4. Güncel durum veritabanına yansıtılır
        // (Not: Repository arayüzümüze Update eklememiz gerekecek, bunu birazdan yapalım)
        await _linkRepository.UpdateAsync(shortenedLink.ShortCode, cancellationToken);

        // 5. Bir sonraki istekler için veri Cache'e yazılır (Örn: 1 saatlik ömürle)
        await _cacheService.SetAsync(
            cacheKey,
            shortenedLink.OriginalUrl.Value,
            TimeSpan.FromHours(1),
            cancellationToken);

        return shortenedLink.OriginalUrl.Value;

    }
}
