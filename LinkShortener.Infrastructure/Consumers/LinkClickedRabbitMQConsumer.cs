using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;
using System.Text.Json;
using LinkShortener.Application.Features.ShortenedLinks.Events;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedRabbitMQConsumer(IShortenedLinkRepository shortenedLinkRepository, ILogger<LinkClickedConsumer> logger) : IConsumer<Batch<LinkClickedEvent>>
{
    private readonly ILogger<LinkClickedConsumer> _logger = logger;
    private readonly IShortenedLinkRepository _shortenedLinkRepository = shortenedLinkRepository;

    private const string TableName = "LinkStats";

    public async Task Consume(ConsumeContext<Batch<LinkClickedEvent>> context)
    {
        var validMessages = context.Message.Select(x => x.Message).ToList();

        if (validMessages == null || !validMessages.Any())
        {
            _logger.LogWarning("LinkClickedEvent batch mesajları bulunamadı veya boş geldi.");
            return;
        }

        // 1. Gelen batch içerisinden geçerli CDC 'Create' (c) ve 'Snapshot' (r) mesajlarını süz
        var groupedClicks = validMessages
            .GroupBy(m => m.ShortCode)
            .Select(g => new { ShortCode = g.Key, IncrementAmount = g.Count() })
            .ToList();

        if (!groupedClicks.Any())
        {
            _logger.LogWarning("Batch içerisinde LinkClickedEvent mesajı bulunamadı.");
            return;
        }       

        _logger.LogInformation(
            "Batch işleniyor: {GroupCount} farklı ShortCode için toplam {TotalEvents} tıklama olayı güncellenecek.",
            groupedClicks.Count,
            validMessages.Count
        );
        

        // 3. Her bir ShortCode için DynamoDB Atomic Update çalıştır
        foreach (var item in groupedClicks)
        {
            try
            {
                _logger.LogInformation(
                    "Batch içerisindeki mesaj: {message}",
                     JsonSerializer.Serialize(item.ShortCode) + " / " + item.IncrementAmount
                );

                await _shortenedLinkRepository.UpdateLinkStatsBySumAsync(item.ShortCode, item, context.CancellationToken);

                _logger.LogInformation(
                    "DynamoDB güncellendi: ShortCode = {ShortCode}, Eklenen = +{Inc}",
                    item.ShortCode,
                    item.IncrementAmount
                );

                _logger.LogDebug(
                    "DynamoDB güncellendi: ShortCode = {ShortCode}, Eklenen = +{Inc}",
                    item.ShortCode,
                    item.IncrementAmount
                );
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                    "DynamoDB 'LinkStats' güncellenirken hata oluştu! ShortCode: {ShortCode}",
                    item.ShortCode
                );

                _logger.LogError(
                    ex,
                    "DynamoDB 'LinkStats' güncellenirken hata oluştu! ShortCode: {ShortCode}",
                    item.ShortCode
                );

                // Not: Hata durumunda MassTransit'in Kafka offset'i commit etmeyip
                // batch'i tekrar denemesi için exception fırlatılır.
                throw;
            }
        }
    }
}