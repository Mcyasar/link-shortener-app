using LinkShortener.Application.Features.ShortenedLinks.Commands.CreateShortLink;
using LinkShortener.Application.Features.ShortenedLinks.Events;
using LinkShortener.Application.Features.ShortenedLinks.Queries.GetOriginalLink;
using LinkShortener.Application.Interfaces;
using LinkShortener.Application.Features.ShortenedLinks.Queries.GetUserLinks;
using LinkShortener.Infrastructure.Resilience;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace LinkShortener.API.Controllers;

[ApiController]
[Route("api/links")]
[Authorize] // Güvenlik Çemberi: Bu controller altındaki tüm işlemler varsayılan olarak JWT Token gerektirir.
//[EnableRateLimiting("FixedWindowPolicy")]
[EnableRateLimiting("dynamic-parametric-policy")]
public sealed class ShortenedLinksController : ControllerBase
{
    private readonly IMediator _mediator;

    public ShortenedLinksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Giriş yapmış kullanıcı için yeni bir kısa link oluşturur.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    //[CustomRateLimit(permitLimit: 15, windowInSeconds: 1)]
    public async Task<IActionResult> Create([FromBody] CreateShortLinkRequest request, CancellationToken cancellationToken)
    {
        // JWT içinden giriş yapan kullanıcının ID'sini (UUID v7) güvenle okuyoruz
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { Message = "Geçersiz veya eksik kullanıcı kimliği." });
        }

        // Application katmanındaki Command nesnemizi besliyoruz
        var command = new CreateShortLinkCommand(
            OriginalUrl: request.OriginalUrl,
            UserId: userId, // Linki oluşturan kullanıcı kimliğini buraya bağlıyoruz
            ExpiresAt: request.ExpiresAt
        );

        var shortCode = await _mediator.Send(command, cancellationToken);

        // İstemciye hem üretilen kodu hem de tam yönlendirme URL'ini dönebiliriz
        var fullShortUrl = $"{Request.Scheme}://{Request.Host}/api/links/{shortCode}";

        return Ok(new { ShortCode = shortCode, ShortUrl = fullShortUrl });
    }

    /// <summary>
    /// Kısa kod ile eşleşen orijinal URL'e HTTP 302 ile yönlendirme (Redirect) yapar.
    /// </summary>
    [HttpGet("{shortCode}")]
    [AllowAnonymous] // KRİTİK İSTİSNA: Linke tıklayan dış ziyaretçilerin JWT Token'a ihtiyacı yoktur!
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    //[CustomRateLimit(permitLimit: 100, windowInSeconds: 1)]
    public async Task<IActionResult> RedirectToOriginal(string shortCode, 
        [FromServices] ILinkClickChannel clickChannel, // Kanalı inject ediyoruz
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new GetOriginalLinkQuery(shortCode);
            var originalUrl = await _mediator.Send(query, cancellationToken);

            if (string.IsNullOrEmpty(originalUrl))
            {
                return NotFound(new { Message = "Kısa kod bulunamadı veya süresi dolmuş." });
            }

            // await kullanmıyoruz, çünkü arka plan kuyruğuna yazma işlemi milisaniyeden kısa sürer ve HTTP isteğini bekletmez
            _ = clickChannel.WriteAsync(new LinkClickedEvent(shortCode, DateTime.UtcNow), cancellationToken);

            // Ziyaretçiyi orijinal web sitesine (HTTP 302 - Geçici Yönlendirme) fırlatıyoruz
            return Redirect(originalUrl);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Message = "Aradığınız kısa kod sistemde mevcut değil." });
        }
    }

    /// <summary>
    /// Giriş yapmış kullanıcının oluşturduğu tüm kısa linkleri listeler.
    /// </summary>
    [HttpGet("shortenedLinks")]
    [ProducesResponseType(typeof(List<ShortenedLinkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    //[CustomRateLimit(permitLimit: 10, windowInSeconds: 1)]
    public async Task<IActionResult> GetUserLinks(CancellationToken cancellationToken)
    {
        // JWT içinden giriş yapan kullanıcının ID'sini (UUID v7) güvenle okuyoruz
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { Message = "Geçersiz veya eksik kullanıcı kimliği." });
        }

        // Application katmanındaki Query nesnemizi besliyoruz
        var query = new GetUserLinksQuery(userId);

        var userLinks = await _mediator.Send(query, cancellationToken);

        return Ok(userLinks);
    }
}

/// <summary>
/// Dış dünyadan (HTTP Body) gelecek olan istek şeması
/// </summary>
public record CreateShortLinkRequest(string OriginalUrl, DateTime? ExpiresAt = null);