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
    public DebeziumLinkClickOutboxAfter? Before { get; set; }

    [JsonPropertyName("after")]
    public DebeziumLinkClickOutboxAfter? After { get; set; }

    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; }

    [JsonPropertyName("op")]
    public string? Operation { get; set; } // Debezium tarafındaki 'op' alanı

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; set; }
}

// Represents the full Debezium message structure
public class DebeziumMessage
{
    [JsonPropertyName("before")]
    public DebeziumLinkClickOutboxAfter? Before { get; set; }

    [JsonPropertyName("after")]
    public DebeziumLinkClickOutboxAfter? After { get; set; }

    [JsonPropertyName("source")]
    public JsonElement? Source { get; set; }

    [JsonPropertyName("op")]
    public string? Operation { get; set; } // 'c', 'u', 'd', 'r'

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; set; }
}
