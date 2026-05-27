using LinkShortener.Application.Interfaces;

namespace LinkShortener.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    // BCrypt.Net-Next kütüphanesinin en güncel ve güvenli Enhanced metodunu kullanıyoruz
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, workFactor: 11);

    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
}