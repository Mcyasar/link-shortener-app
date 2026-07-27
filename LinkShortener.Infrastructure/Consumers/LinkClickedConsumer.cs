using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;
using LinkShortener.Application.Features.ShortenedLinks.Events;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedConsumer : IConsumer<LinkClickedEvent>
{
    private readonly IShortenedLinkRepository _dynamoDbRepository;
    private readonly ILogger<LinkClickedConsumer> _logger;

    public LinkClickedConsumer(IShortenedLinkRepository dynamoDbRepository, ILogger<LinkClickedConsumer> logger)
    {
        _dynamoDbRepository = dynamoDbRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LinkClickedEvent> context)
    {
        var message = context.Message;
        
        _logger.LogInformation("Processing click count for ShortCode: {ShortCode}", message.ShortCode);

        // DynamoDB atomik artırım metodu
        await _dynamoDbRepository.UpdateAsync(message.ShortCode, context.CancellationToken);
    }
}