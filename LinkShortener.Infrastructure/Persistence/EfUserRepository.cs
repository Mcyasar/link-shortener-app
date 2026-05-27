using Microsoft.EntityFrameworkCore;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Infrastructure.Persistence;

public sealed class EfUserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public EfUserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Users.FindAsync([id], cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var emailLower = email.ToLowerInvariant();
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == emailLower, cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var emailLower = email.ToLowerInvariant();
        return await _context.Users.AnyAsync(u => u.Email == emailLower, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        await _context.Users.AddAsync(user, cancellationToken);
    }
}