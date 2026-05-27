using LinkShortener.Domain.Entities;

namespace LinkShortener.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
}