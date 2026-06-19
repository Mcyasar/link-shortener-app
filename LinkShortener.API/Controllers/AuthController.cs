using MediatR;
using Microsoft.AspNetCore.Mvc;
using LinkShortener.Application.Features.Users.Commands.RegisterUser;
using LinkShortener.Application.Features.Users.Queries.LoginUser;
using Microsoft.AspNetCore.RateLimiting;
using LinkShortener.Infrastructure.Resilience;
using LinkShortener.Application.Features.Users.Commands.LogoutUser;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LinkShortener.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("dynamic-parametric-policy")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Yeni bir kullanıcı hesabı oluşturur.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
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
            var response = await _mediator.Send(query with { ClientIpAddress = clientIp }, cancellationToken);
            return Ok(response);
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
    [Authorize] // Bu endpoint sadece yetkili kullanıcılar tarafından çağrılabilir
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
        return result.IsSuccess ? Ok(new { Message = "Başarıyla çıkış yapıldı." }) : Unauthorized(new { Message = result.Error?.Message });
    }
}