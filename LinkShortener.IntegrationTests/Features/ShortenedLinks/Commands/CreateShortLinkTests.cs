using LinkShortener.Application.Features.ShortenedLinks.Commands.CreateShortLink;
using LinkShortener.Infrastructure.Persistence;

namespace LinkShortener.IntegrationTests.Features.ShortenedLinks.Commands;

[Collection("TestDatabaseCollection")]
public sealed class CreateShortLinkTests
{
    private readonly TestDatabaseFixture _fixture;

    public CreateShortLinkTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_GivenValidCommand_ShouldSaveToDynamoDbAndReturnShortCode()
    {
        // --- 1. ARRANGE (Hazırlık) ---
        // Altyapı katmanındaki gerçek somut sınıfları Testcontainers bağlantılarıyla oluşturuyoruz
        var repository = new DynamoDbShortenedLinkRepository(_fixture.DynamoDbClient);
        var codeGenerator = new Base62ShortCodeGenerator();

        var handler = new CreateShortLinkCommandHandler(codeGenerator, repository);

        var command = new CreateShortLinkCommand(
            OriginalUrl: "https://www.google.com",
            UserId: Guid.NewGuid(),
            ExpiresAt: DateTime.UtcNow.AddDays(30)
        );

        // --- 2. ACT (Eylem) ---
        var shortCode = await handler.Handle(command, CancellationToken.None);

        // --- 3. ASSERT (Doğrulama) ---
        // Geriye dönen kısa kodun boş olmadığını doğrula
        Assert.False(string.IsNullOrWhiteSpace(shortCode));
        Assert.Equal(7, shortCode.Length); // generator 7 karakter üretiyor demiştik

        // Gerçekten Testcontainers üzerindeki DynamoDB'ye gidip veriyi oku
        var savedLink = await repository.GetByCodeAsync(shortCode, CancellationToken.None);

        Assert.NotNull(savedLink);
        Assert.Equal(command.OriginalUrl, savedLink.OriginalUrl.Value);
        Assert.Equal(command.UserId, savedLink.UserId);
        Assert.Equal(0, savedLink.ClickCount); // İlk oluşturulduğunda sıfır olmalı
    }
}