using Microsoft.EntityFrameworkCore;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
     public DbSet<LinkClickOutbox> LinkClickOutbox => Set<LinkClickOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Fluent API ile User tablosunun kurallarını belirliyoruz
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(150);

            // E-posta adresinin benzersiz (Unique) olmasını sağlıyoruz
            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.Property(e => e.PasswordHash)
                .IsRequired();

            entity.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(20);
        });

         // LinkClickOutbox tablosunun kurallarını belirliyoruz
        modelBuilder.Entity<LinkClickOutbox>(entity =>
        {
            entity.ToTable("LinkClickOutbox");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ShortCode)
                .IsRequired()
                .HasMaxLength(10); // Kısa kod uzunluğuna göre ayarlayın

            entity.Property(e => e.Status)
                .HasConversion<string>() // Enum'ı string ("Pending", "InProcess", "Processed") olarak kaydeder
                .IsRequired()
                .HasMaxLength(20); // PostgreSQL tarafında sınırsız text oluşmasını engellemek için

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            // 🔥 KRİTİK PERFORMANS İNDEKSİ: Partial (Filtreli) Index
            // PostgreSQL tarafında SADECE işlenmeyi bekleyen (Status = 'Pending') satırları indeksler.
            // Tabloda milyonlarca 'Processed' kayıt olsa bile indeks boyutu küçücük kalır 
            // ve Background Worker 'Pending' kayıtları O(1) hızında (milisaniyeler içinde) bulur.
            entity.HasIndex(e => e.Status)
                .HasFilter("\"Status\" = 'Pending'")
                .HasDatabaseName("IX_LinkClickOutbox_PendingStatus");
        });
    }
}