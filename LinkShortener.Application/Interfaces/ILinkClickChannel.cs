using LinkShortener.Application.Features.ShortenedLinks.Events;

namespace LinkShortener.Application.Interfaces;

public interface ILinkClickChannel
{
    ValueTask WriteAsync(LinkClickedEvent @event, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LinkClickedEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}