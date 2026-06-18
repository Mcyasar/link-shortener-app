using MediatR;
using LinkShortener.Application.Interfaces;
using LinkShortener.Domain.Entities;

namespace LinkShortener.Application.Features.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Guid>
{
    private readonly IUserRepository _userRepository; // Bağımlılık artık soyut
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        bool isEmailTaken = await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken);
        if (isEmailTaken)
            throw new InvalidOperationException("Bu e-posta adresi zaten kullanımda.");

        string hashedPassword = _passwordHasher.Hash(request.Password);

        var user = new User(
            id: Guid.CreateVersion7(),
            email: request.Email,
            passwordHash: hashedPassword
        );

        await _userRepository.AddAsync(user, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
