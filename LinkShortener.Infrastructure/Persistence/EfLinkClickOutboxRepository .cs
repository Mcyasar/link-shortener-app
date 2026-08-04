// e:\Projeler\LinkShortener\link-shortener-app\LinkShortener.Infrastructure\Persistence\EfLinkClickOutboxRepository.cs
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LinkShortener.Infrastructure.Persistence;

public sealed class EfLinkClickOutboxRepository : ILinkClickOutboxRepository
{
    private readonly ApplicationDbContext _context;

    public EfLinkClickOutboxRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(LinkClickOutbox entry, CancellationToken cancellationToken = default)
    {
        await _context.LinkClickOutbox.AddAsync(entry, cancellationToken);
    }

    public async Task<List<LinkClickOutbox>> GetPendingEntriesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return await _context.LinkClickOutbox
            .Where(e => e.Status == LinkClickOutboxStatus.Pending || (e.Status == LinkClickOutboxStatus.InProgress && e.CreatedAt.AddMinutes(5) < DateTime.UtcNow)) // 5 dakikadan uzun süredir InProgress olanları da tekrar dene
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(LinkClickOutbox entry, CancellationToken cancellationToken = default)
    {
        _context.LinkClickOutbox.Update(entry);
    }

    public async Task UpdateBatchStatusAsync(IEnumerable<Guid> ids, LinkClickOutboxStatus status, CancellationToken cancellationToken = default)
    {
        await _context.LinkClickOutbox
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, status), cancellationToken);
    }

    public async Task UpdateBatchStatusAndIncrementRetryAsync(IEnumerable<Guid> ids, LinkClickOutboxStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        await _context.LinkClickOutbox
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, status)
                .SetProperty(e => e.RetryCount, e => e.RetryCount + 1)
                .SetProperty(e => e.ErrorMessage, errorMessage),
                cancellationToken);
    }
}
