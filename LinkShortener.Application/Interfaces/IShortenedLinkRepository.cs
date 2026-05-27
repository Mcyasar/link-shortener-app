using LinkShortener.Domain.Entities;

namespace LinkShortener.Application.Interfaces;

public interface IShortenedLinkRepository
{
    Task AddAsync(ShortenedLink link, CancellationToken cancellationToken);
    Task<ShortenedLink?> GetByCodeAsync(string shortCode, CancellationToken cancellationToken);
    Task UpdateAsync(string shortCode, CancellationToken cancellationToken);
}