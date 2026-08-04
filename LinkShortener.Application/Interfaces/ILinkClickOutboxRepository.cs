// e:\Projeler\LinkShortener\link-shortener-app\LinkShortener.Application\Interfaces\ILinkClickOutboxRepository.cs
using LinkShortener.Domain.Entities;

namespace LinkShortener.Application.Interfaces;

public interface ILinkClickOutboxRepository
{
    Task AddAsync(LinkClickOutbox entry, CancellationToken cancellationToken = default);
    Task<List<LinkClickOutbox>> GetPendingEntriesAsync(int batchSize, CancellationToken cancellationToken = default);
    Task UpdateAsync(LinkClickOutbox entry, CancellationToken cancellationToken = default);
    Task UpdateBatchStatusAsync(IEnumerable<Guid> ids, LinkClickOutboxStatus status, CancellationToken cancellationToken = default);
    Task UpdateBatchStatusAndIncrementRetryAsync(IEnumerable<Guid> ids, LinkClickOutboxStatus status, string? errorMessage, CancellationToken cancellationToken = default);
}
