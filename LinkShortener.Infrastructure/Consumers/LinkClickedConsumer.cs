using MassTransit;
using LinkShortener.Application.Interfaces; // IDynamoDbRepository arayüzünüzün bulunduğu namespace
using Microsoft.Extensions.Logging;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;

namespace LinkShortener.Infrastructure.Consumers;

public class LinkClickedConsumer : IConsumer<Batch<DebeziumMessage>>
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ILogger<LinkClickedConsumer> _logger;

    private const string TableName = "LinkStats";

    public LinkClickedConsumer(IAmazonDynamoDB dynamoDb, ILogger<LinkClickedConsumer> logger)
    {
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

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

        _logger.LogInformation(
            "Batch içerisindeki ilk mesaj: {message}",
            JsonSerializer.Serialize(groupedClicks.First())
        );

        // 3. Her bir ShortCode için DynamoDB Atomic Update çalıştır
        foreach (var item in groupedClicks)
        {
            try
            {
                var updateRequest = new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "ShortCode", new AttributeValue { S = item.ShortCode } }
                    },
                    // UpdateExpression Püf Noktası:
                    // 'ADD clickCount :inc' -> Varolan sayıya ekler. Kıymetli kısmı: Kayıt yoksa 0 kabul edip ekler!
                    // 'SET lastUpdated = :now' -> En son ne zaman güncellendiğini yazar.
                    UpdateExpression = "ADD clickCount :inc SET lastUpdated = :now",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":inc", new AttributeValue { N = item.IncrementAmount.ToString() } },
                        { ":now", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                    }
                };

                await _dynamoDb.UpdateItemAsync(updateRequest, context.CancellationToken);

                _logger.LogDebug(
                    "DynamoDB güncellendi: ShortCode = {ShortCode}, Eklenen = +{Inc}",
                    item.ShortCode,
                    item.IncrementAmount
                );
            }
            catch (Exception ex)
            {
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