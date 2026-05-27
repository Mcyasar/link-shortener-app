using LinkShortener.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LinkShortener.Infrastructure.BackgroundWorkers;

public sealed class LinkClickProcessorWorker : BackgroundService
{
    private readonly ILinkClickChannel _clickChannel;
    private readonly ILogger<LinkClickProcessorWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public LinkClickProcessorWorker(
        ILinkClickChannel clickChannel,
        IServiceScopeFactory scopeFactory,
        ILogger<LinkClickProcessorWorker> logger)
    {
        _clickChannel = clickChannel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kısa link tıklanma işleyicisi (Background Worker) hazır.");

        await foreach (var @event in _clickChannel.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var linkRepository = scope.ServiceProvider.GetRequiredService<IShortenedLinkRepository>();
                                
                await linkRepository.UpdateAsync(@event.ShortCode, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arka plan tıklanma güncellemesinde hata.");
            }
        }
    }
}