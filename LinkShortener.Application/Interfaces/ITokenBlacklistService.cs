namespace LinkShortener.Application.Interfaces;

public interface ITokenBlacklistService
{
    Task BlacklistTokenAsync(string tokenJti, TimeSpan expiryTime, CancellationToken cancellationToken);
    Task<bool> IsTokenBlacklistedAsync(string tokenJti, CancellationToken cancellationToken);
}