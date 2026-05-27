using MediatR;
using Microsoft.AspNetCore.Mvc;
using LinkShortener.Application.Features.Users.Commands.RegisterUser;
using LinkShortener.Application.Features.Users.Queries.LoginUser;

namespace LinkShortener.API.Controllers;

[ApiController]
[Route("api/auth")]
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
    public async Task<IActionResult> Login([FromBody] LoginUserQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(query, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Geçersiz e-posta veya şifre hatalarında 401 Unauthorized fırlatıyoruz
            return Unauthorized(new { Message = ex.Message });
        }
    }
}