using MediatR;
using LinkShortener.Application.Interfaces;

namespace LinkShortener.Application.Features.Users.Queries.LoginUser;

public sealed class LoginUserQueryHandler : IRequestHandler<LoginUserQuery, LoginResponseDto>
{
    private readonly IUserRepository _userRepository; // Bağımlılık artık soyut
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginUserQueryHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<LoginResponseDto> Handle(LoginUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
            throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");

        bool isPasswordValid = _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
            throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");

        var token = _jwtTokenGenerator.GenerateToken(user);

        return new LoginResponseDto(user.Id, user.Email, token);
    }
}
