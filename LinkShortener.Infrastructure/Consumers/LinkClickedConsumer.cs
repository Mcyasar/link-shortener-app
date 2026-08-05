using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedConsumer : IConsumer<DebeziumMessage>
{
    private readonly IShortenedLinkRepository _dynamoDbRepository;
    private readonly ILogger<LinkClickedConsumer> _logger;

    public LinkClickedConsumer(IShortenedLinkRepository dynamoDbRepository, ILogger<LinkClickedConsumer> logger)
    {
        _dynamoDbRepository = dynamoDbRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DebeziumMessage> context)
    {
        var debeziumMessage = context.Message;

        // Sadece 'create' (c) operasyonlarını ve 'after' payload'ı olan mesajları işliyoruz
        if (debeziumMessage?.Payload?.Operation == "c" && debeziumMessage.Payload.After != null)
        {
            var afterData = debeziumMessage.Payload.After;
            
            // Debezium'dan gelen veriyi kullanarak DynamoDB'yi güncelliyoruz
            if (!string.IsNullOrEmpty(afterData.ShortCode))
            {
                _logger.LogInformation("Processing click count for ShortCode: {ShortCode} from Debezium message.", afterData.ShortCode);
                await _dynamoDbRepository.UpdateAsync(afterData.ShortCode, context.CancellationToken);
            } else {
                _logger.LogWarning("Debezium message for 'create' operation has missing ShortCode in 'after' data.");
            }
        }
        else
        {
            _logger.LogWarning("Received Debezium message is not a 'create' operation or 'after' data is missing. Operation: {Operation}", debeziumMessage?.Payload?.Operation);
        }
    }
}