namespace LinkShortener.Domain.Entities;

public enum LinkClickOutboxStatus
{
    Pending,
    InProgress,
    Processed,
    Failed // Hata durumları için eklenebilir
}

public sealed class LinkClickOutbox
{
    public Guid Id { get; private set; }
    public string ShortCode { get; private set; }
    public DateTime ClickedAt { get; private set; }
    public LinkClickOutboxStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    // EF Core için parametresiz gizli constructor
    private LinkClickOutbox() { }

    public LinkClickOutbox(string shortCode, DateTime clickedAt)
    {
        Id = Guid.NewGuid(); // Her outbox kaydı için benzersiz bir ID
        ShortCode = shortCode;
        ClickedAt = clickedAt;
        Status = LinkClickOutboxStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        RetryCount = 0;
    }

    public void MarkInProgress()
    {
        Status = LinkClickOutboxStatus.InProgress;
        RetryCount++;
    }

    public void MarkProcessed()
    {
        Status = LinkClickOutboxStatus.Processed;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = LinkClickOutboxStatus.Failed;
        ErrorMessage = errorMessage;
    }

    public void ResetToPending()
    {
        Status = LinkClickOutboxStatus.Pending;
    }
}
