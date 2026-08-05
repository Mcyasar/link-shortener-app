using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkShortener.Infrastructure.Consumers;

// Represents the 'after' part of the Debezium message for LinkClickOutbox
// Debezium genellikle sütun adlarını 'after' payload'ında küçük harfle verir.
public class DebeziumLinkClickOutboxAfter
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("shortcode")]
    public string ShortCode { get; set; } = string.Empty;

    [JsonPropertyName("clickedat")]
    public DateTime ClickedAt { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdat")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("retrycount")]
    public int RetryCount { get; set; }

    [JsonPropertyName("errormessage")]
    public string? ErrorMessage { get; set; }
}

// Represents the 'payload' part of the Debezium message
public class DebeziumPayload
{
    [JsonPropertyName("before")]
    public DebeziumLinkClickOutboxAfter? Before { get; set; } // Güncelleme/silme işlemleri için

    [JsonPropertyName("after")]
    public DebeziumLinkClickOutboxAfter? After { get; set; } // Ekleme/güncelleme işlemleri için

    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; } // Kaynak meta verileri

    [JsonPropertyName("op")]
    public string? Operation { get; set; } // İşlem türü: 'c' (create), 'u' (update), 'd' (delete), 'r' (read/snapshot)

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; set; } // İşlemin zaman damgası
}

// Represents the full Debezium message structure
public class DebeziumMessage
{
    [JsonPropertyName("payload")]
    public DebeziumPayload? Payload { get; set; }
}
