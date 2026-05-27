using Microsoft.EntityFrameworkCore;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

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
    }
}