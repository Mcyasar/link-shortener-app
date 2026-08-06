using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedConsumer : IConsumer<DebeziumMessage>
{
    private readonly ILogger<LinkClickedConsumer> _logger;

    public LinkClickedConsumer(ILogger<LinkClickedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DebeziumMessage> context)
    {
        var message = context.Message;

        // 1. Zarf veya Payload kontrolü
        if (message?.Payload == null)
        {
            _logger.LogWarning("Debezium mesaj gövdesi (Payload) boş geldi, mesaj atlanıyor.");

            _logger.LogWarning("Debezium Payload null geldi! Gelen Raw Obje: {RawMessage}", 
                    System.Text.Json.JsonSerializer.Serialize(message));

            return;
        }

        var payload = message.Payload;

        // 2. Operasyon tipi ve After kontrolü
        // Debezium: 'c' (create/insert), 'r' (read/snapshot), 'u' (update), 'd' (delete)
        if (payload.After == null)
        {
            _logger.LogWarning(
                "Debezium mesajında 'After' verisi bulunamadı. İşlem Tipi: {Op}. Silme veya Tombstone mesajı olabilir.", 
                payload.Operation ?? "Bilinmiyor"
            );
            return;
        }

        // 3. Veriyi güvenle alıp işleme
        DebeziumLinkClickOutboxAfter clickData = payload.After;

        _logger.LogInformation(
            "Link tıklama CDC mesajı başarıyla alındı. ShortCode: {ShortCode}, ClickedAt: {ClickedAt}",
            clickData.ShortCode,
            clickData.ClickedAt
        );

        // ... DynamoDB / Redis kayıt mantığınız ...
        
        await Task.CompletedTask;
    }
}