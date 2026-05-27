namespace LinkShortener.Application.Interfaces;

public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// O ana kadar bellek üzerinde (EF Tracker) birikmiş tüm ekleme, silme ve güncelleme
    /// işlemlerini tek bir veritabanı transaction'ı ile diske yazar.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}