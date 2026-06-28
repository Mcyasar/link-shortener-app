using MediatR;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;
using Microsoft.Extensions.Logging; 

namespace LinkShortener.Application.Features.Users.Queries.LoginUser;

public sealed class LoginUserQueryHandler : IRequestHandler<LoginUserCommandQuery, LoginCommandResponseDto>
{
    private readonly IUserRepository _userRepository; // Bağımlılık artık soyut
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<LoginUserQueryHandler> _logger; 

    public LoginUserQueryHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenService refreshTokenService,
        ILogger<LoginUserQueryHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<LoginCommandResponseDto> Handle(LoginUserCommandQuery request, CancellationToken cancellationToken)
    {
        ///TODO: result pattern ile hata yönetimi yapılabilir. Şu an UnauthorizedAccessException fırlatılıyor.
        
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

       _logger.LogInformation("LoginUserQueryHandler received request for email: {Email}", request.Email);

        if (user is null)
            throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");

        bool isPasswordValid = _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
            throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");

        // 1. JWT Access Token oluştur
        var token = _jwtTokenGenerator.GenerateToken(user);

        // 2. Refresh Token oluştur ve kaydet
        var refreshToken = new UserRefreshToken
        {
            UserId = user.Id.ToString(),
            Token = Guid.NewGuid().ToString("N"), // Kriptografik olarak güçlü, benzersiz bir token
            CreatedByIp = request.ClientIpAddress,
            ExpiresAt = DateTime.UtcNow.AddDays(7) // Refresh token 7 gün geçerli olsun
        };
        await _refreshTokenService.SaveRefreshTokenAsync(refreshToken, cancellationToken);

        return new LoginCommandResponseDto(user.Id, user.Email, token, refreshToken.Token);
    }
}
