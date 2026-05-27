namespace LinkShortener.Application.Features.ShortenedLinks.Events;

public record LinkClickedEvent(string ShortCode, DateTime ClickedAt);