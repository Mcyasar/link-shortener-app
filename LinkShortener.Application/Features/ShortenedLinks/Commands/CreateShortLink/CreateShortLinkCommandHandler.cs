using MediatR;
using LinkShortener.Domain.Entities;
using LinkShortener.Domain.Interfaces;
using LinkShortener.Domain.ValueObjects;
using LinkShortener.Application.Interfaces; // Bu klasörde reposu tanımlayacağız

namespace LinkShortener.Application.Features.ShortenedLinks.Commands.CreateShortLink;

public sealed class CreateShortLinkCommandHandler : IRequestHandler<CreateShortLinkCommand, string>
{
    private readonly IShortCodeGenerator _codeGenerator;
    private readonly IShortenedLinkRepository _linkRepository;

    // Bağımlılıklar soyutlamalar üzerinden enjekte ediliyor (Dependency Inversion)
    public CreateShortLinkCommandHandler(
        IShortCodeGenerator codeGenerator,
        IShortenedLinkRepository linkRepository)
    {
        _codeGenerator = codeGenerator;
        _linkRepository = linkRepository;
    }

    public async Task<string> Handle(CreateShortLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Kriptografik benzersiz kısa kod üretilir
        string shortCode = _codeGenerator.Generate();

        // 2. Domain Value Object ve Entity yaratılır (Doğrulamalar domain içinde patlar)
        var originalUrl = OriginalUrl.Create(request.OriginalUrl);

        var shortenedLink = new ShortenedLink(
            id: Guid.NewGuid(),
            shortCode: shortCode,
            originalUrl: originalUrl,
            userId: request.UserId,
            expiresAt: request.ExpiresAt
        );

        // 3. Altyapı bağımlılığı olan repo üzerinden veritabanına kaydedilir
        await _linkRepository.AddAsync(shortenedLink, cancellationToken);

        // 4. Üretilen kısa kod geriye dönülür
        return shortenedLink.ShortCode;
    }
}