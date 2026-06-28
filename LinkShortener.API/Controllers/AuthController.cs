using MediatR;
using Microsoft.AspNetCore.Mvc;
using LinkShortener.Application.Features.Users.Commands.RegisterUser;
using LinkShortener.Application.Features.Users.Queries.LoginUser;
using Microsoft.AspNetCore.RateLimiting;
using LinkShortener.Infrastructure.Resilience;
using LinkShortener.Application.Features.Users.Commands.LogoutUser;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LinkShortener.Application.Features.Users.Commands.RefreshToken;
using System.IdentityModel.Tokens.Jwt; // For JwtSecurityTokenHandler

namespace LinkShortener.API.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize] // Varsayılan olarak tüm endpoint'ler yetkilendirme gerektirsin
[EnableRateLimiting("dynamic-parametric-policy")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public AuthController(IMediator mediator, IConfiguration configuration, IHostEnvironment environment)
    {
        _mediator = mediator;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Yeni bir kullanıcı hesabı oluşturur.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [AllowAnonymous] // Kayıt işlemi için yetkilendirme gerekmez
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [CustomRateLimit(permitLimit: 15, windowInSeconds: 1)]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var userId = await _mediator.Send(command, cancellationToken);

            // REST standartlarına göre yeni kaynak oluştuğunda 201 Created ve kaynağın Id'si dönülür
            return CreatedAtAction(nameof(Register), new { id = userId }, new { Id = userId });
        }
        catch (InvalidOperationException ex)
        {
            // "E-posta adresi zaten kullanımda" gibi kontrollü iş mantığı hatalarını yakalıyoruz
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Kullanıcı girişi yapar ve geçerli bir JWT Token üretir.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous] // Giriş işlemi için yetkilendirme gerekmez
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [CustomRateLimit(permitLimit: 100, windowInSeconds: 1)]
    public async Task<IActionResult> Login([FromBody] LoginUserQuery query, CancellationToken cancellationToken)
    {
        // Kullanıcının IP adresini alıyoruz
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try
        {
            // Query'ye IP adresini ekleyerek gönderiyoruz
            var loginResponse = 
               await _mediator.Send(new LoginUserCommandQuery(query.Email, query.Password, clientIp), cancellationToken);

            // Refresh token'ı HTTP-only cookie olarak ayarla
            Response.Cookies.Append("refreshToken", loginResponse.RefreshToken, new CookieOptions
            {
                HttpOnly = !_environment.IsDevelopment(),
                Secure = !_environment.IsDevelopment(), // Sadece üretimde HTTPS zorunlu
                SameSite = _environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict, // Geliştirmede Lax, üretimde Strict
                Expires = DateTimeOffset.UtcNow.AddDays(7) // Refresh token ömrüyle eşleşmeli
            });

            // Access token'ı ve diğer bilgileri response body'de dön
            var loginResponseDto = new LoginResponseDto(loginResponse.UserId, loginResponse.Email, loginResponse.Token); // RefreshToken'ı body'den kaldırıyoruz
            return Ok(loginResponseDto);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Geçersiz e-posta veya şifre hatalarında 401 Unauthorized fırlatıyoruz
            return Unauthorized(new { Message = ex.Message });
        }
    }
    
    /// <summary>
    /// Kullanıcının mevcut oturumunu sonlandırır ve Access Token'ı blacklist'e ekler.
    /// Ayrıca, kullanıcının tüm Refresh Token'larını iptal eder.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [CustomRateLimit(permitLimit: 5, windowInSeconds: 1)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // JWT içinden giriş yapan kullanıcının ID'sini ve JWT ID'sini (jti) güvenle okuyoruz
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var jwtIdClaim = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expirationClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { Message = "Geçersiz veya eksik kullanıcı kimliği." });
        }
        if (string.IsNullOrEmpty(jwtIdClaim))
        {
            return Unauthorized(new { Message = "JWT ID (jti) bulunamadı." });
        }
        if (string.IsNullOrEmpty(expirationClaim) || !long.TryParse(expirationClaim, out var expirationUnixTimestamp))
        {
            return Unauthorized(new { Message = "Token son kullanma tarihi bulunamadı." });
        }

        var expirationDateTime = DateTimeOffset.FromUnixTimeSeconds(expirationUnixTimestamp).UtcDateTime;
        var remainingLifetime = expirationDateTime - DateTime.UtcNow;

        // Eğer token zaten süresi dolmuşsa, en az 1 saniye blacklist'te kalmasını sağla
        var command = new LogoutCommand(jwtIdClaim, userId, remainingLifetime > TimeSpan.Zero ? remainingLifetime : TimeSpan.FromSeconds(1));
        var result = await _mediator.Send(command, cancellationToken);

        // Refresh token cookie'sini temizle
        Response.Cookies.Delete("refreshToken");
        return result.IsSuccess ? Ok(new { Message = "Başarıyla çıkış yapıldı." }) : Unauthorized(new { Message = result.Error?.Message });
    }

    /// <summary>
    /// Geçerli bir Refresh Token kullanarak yeni bir Access Token ve Refresh Token çifti üretir.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [AllowAnonymous] // Refresh endpoint does not require an active access token
    [CustomRateLimit(permitLimit: 5, windowInSeconds: 1)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // 1. Refresh token'ı HTTP-only cookie'den oku
        var refreshToken = Request.Headers["refreshToken"].ToString() ?? Request.Cookies["refreshToken"]?.ToString();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { Message = "Refresh token cookie'de bulunamadı." });
        }

        // 2. UserId'yi Authorization header'daki (muhtemelen süresi dolmuş) Access Token'dan al
        // Bu token'ın süresi dolmuş olsa bile, claim'lerini okumak için manuel olarak parse ediyoruz.
        var accessToken = HttpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        if (string.IsNullOrEmpty(accessToken))
        {
            return Unauthorized(new { Message = "Access token Authorization header'da bulunamadı." });
        }

        Guid userId;
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(accessToken);

            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
            {
                return Unauthorized(new { Message = "Access token'dan kullanıcı kimliği alınamadı." });
            }
        }
        catch (Exception ex)
        {
            // Token parsing hatası (örneğin, token formatı bozuksa)
            return Unauthorized(new { Message = $"Access token ayrıştırma hatası: {ex.Message}" });
        }

        // 3. Application katmanındaki RefreshTokenCommand'ı oluşturuyoruz
        var command = new RefreshTokenCommand(
            userId, // Access Token'dan alınan UserId
            refreshToken, // Cookie'den alınan Refresh Token
            clientIp
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (result?.Value is not null && result.IsSuccess)
        {
            // Yeni refresh token'ı HTTP-only cookie olarak ayarla
            Response.Cookies.Append("refreshToken", result.Value.RefreshToken, new CookieOptions { 
                HttpOnly = !_environment.IsDevelopment(),
                Secure = !_environment.IsDevelopment(), // Sadece üretimde HTTPS zorunlu
                SameSite = _environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict, // Geliştirmede Lax, üretimde Strict
                Expires = DateTimeOffset.UtcNow.AddDays(7) // Refresh token ömrüyle eşleşmeli
            });

            // Yeni access token'ı ve diğer bilgileri response body'de dön
            return Ok(new LoginResponseDto(result.Value.UserId, result.Value.Email, result.Value.Token)); // RefreshToken'ı body'den kaldırıyoruz
        }
        return Unauthorized(new { Message = result?.Error?.Message });
    }
}