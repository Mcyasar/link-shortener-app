namespace LinkShortener.Application.Interfaces;

public interface IPasswordHasher
{
    /// <summary>
    /// Dışarıdan gelen düz metin şifreyi güvenli ve geri döndürülemez bir şekilde hash'ler.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// İstemcinin girdiği şifre ile veritabanındaki hash'lenmiş şifrenin eşleşip eşleşmediğini doğrular.
    /// </summary>
    bool Verify(string password, string passwordHash);
}