using System.Threading.Channels;
using LinkShortener.Application.Interfaces;
using LinkShortener.Application.Features.ShortenedLinks.Events;

namespace LinkShortener.Application.Services;

public sealed class LinkClickChannel : ILinkClickChannel
{
    // BoundedChannel: Bellek patlamasını önlemek için maksimum 10.000 event kapasitesi koyuyoruz.
    // Kuyruk dolarsa, arkadaki worker eriyene kadar yazmayı hafifçe bloklar (Backpressure).
    private readonly Channel<LinkClickedEvent> _channel = Channel.CreateBounded<LinkClickedEvent>(new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true, // Sadece tek bir Background Worker dinleyeceği için optimize ediyoruz
        SingleWriter = false // Birden fazla HTTP isteği aynı anda event yazabilir
    });

    public ValueTask WriteAsync(LinkClickedEvent @event, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public IAsyncEnumerable<LinkClickedEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}