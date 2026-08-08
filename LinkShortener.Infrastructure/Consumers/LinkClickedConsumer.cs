using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedConsumer(IShortenedLinkRepository shortenedLinkRepository, ILogger<LinkClickedConsumer> logger) : IConsumer<Batch<DebeziumMessage>>
{
    private readonly ILogger<LinkClickedConsumer> _logger = logger;
    private readonly IShortenedLinkRepository _shortenedLinkRepository = shortenedLinkRepository;

    private const string TableName = "LinkStats";

    public async Task Consume(ConsumeContext<Batch<DebeziumMessage>> context)
    {
        var messages = context.Message;

        if (messages == null || !messages.Any())
        {
            _logger.LogWarning("Debezium batch mesajları bulunamadı veya boş geldi.");
            return;
        }

        // 1. Gelen batch içerisinden geçerli CDC 'Create' (c) ve 'Snapshot' (r) mesajlarını süz
        var validMessages = context.Message
            .Select(x => x.Message)
            .Where(m => m != null && m.After != null && (m.Operation == "c" || m.Operation == "r"))
            .ToList();

        if (!validMessages.Any())
        {
            _logger.LogWarning("Batch içerisinde geçerli 'Create' veya 'Snapshot' mesajı bulunamadı.");
            return;
        }

        // 2. ShortCode bazında grupla ve 5 saniyelik penceredeki toplam tıklama artışını (sum/count) hesapla
        var groupedClicks = validMessages
            .GroupBy(m => m.After!.ShortCode)
            .Select(g => new
            {
                ShortCode = g.Key,
                IncrementAmount = g.Count() // Bu gruptaki tıklama sayısı
            })
            .ToList();

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