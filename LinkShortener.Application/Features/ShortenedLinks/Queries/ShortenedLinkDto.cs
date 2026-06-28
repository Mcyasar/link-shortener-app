using System;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetUserLinks;

public record ShortenedLinkDto(Guid Id, string ShortCode, string OriginalUrl, int ClickCount, DateTime CreatedAt, DateTime? ExpiresAt);