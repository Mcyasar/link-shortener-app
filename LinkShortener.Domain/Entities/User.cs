namespace LinkShortener.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = "User"; // "User" veya "Admin"
    public DateTime CreatedAt { get; private set; }

    // EF Core için parametresiz gizli constructor
    private User() { }

    public User(Guid id, string email, string passwordHash, string role = "User")
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("E-posta adresi boş olamaz.", nameof(email));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Şifre hash değeri boş olamaz.", nameof(passwordHash));

        Id = id;
        Email = email.ToLowerInvariant();
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = DateTime.UtcNow;
    }
}