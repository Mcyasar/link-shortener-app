using MediatR;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using LinkShortener.Application.Common.Configurations;
using LinkShortener.Application.Features.ShortenedLinks.Commands.CreateShortLink;

namespace LinkShortener.Application.Common.Behaviors;

public class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ActivitySource _tracingSource;

    // 🚀 IOptions<TelemetrySettings> ile API'den gelen değeri constructor üzerinden içeri alıyoruz
    public TracingBehavior(IOptions<TelemetrySettings> telemetryOptions)
    {
        var serviceName = telemetryOptions.Value.ServiceName;
        
        // Dinamik olarak API'nin appsettings'indeki servis adı ile ActivitySource başlatılıyor
        _tracingSource = new ActivitySource(serviceName);
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;
        
        // Artık tamamen dinamik olan kaynağımız üzerinden span başlatıyoruz
        using Activity? activity = _tracingSource.StartActivity($"MediatR-{requestName}");

        activity?.SetTag("mediatr.request_type", typeof(TRequest).FullName);

        EnrichActivityWithCustomTags(activity, request);

        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }

    private void EnrichActivityWithCustomTags(Activity? activity, TRequest request)
    {
        if (activity == null) return;

        switch (request)
        {
            case CreateShortLinkCommand createCommand:
                activity.SetTag("link.long_url", createCommand.OriginalUrl);
                break;
        }
    }
}