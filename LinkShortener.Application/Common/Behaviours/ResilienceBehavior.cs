using MediatR;
using Polly;
using Microsoft.Extensions.Logging; // Add this

namespace Application.Common.Behaviors;

// 💡 ÇÖZÜM: IPipelineBehavior arayüzünü MediatR.Contracts paketinden kalıtım alıyoruz
public class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<ResilienceBehavior<TRequest, TResponse>> _logger; // Add this

    public ResilienceBehavior(ResiliencePipeline resiliencePipeline, ILogger<ResilienceBehavior<TRequest, TResponse>> logger) // Add this
    {
        _resiliencePipeline = resiliencePipeline;
        _logger = logger; // Assign this
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {   
        _logger.LogInformation("ResilienceBehavior executing for request type: {RequestType}", typeof(TRequest).Name);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async state => 
            {
                _logger.LogInformation("ResiliencePipeline executing next() for request type: {RequestType}", typeof(TRequest).Name);
                return await next();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResilienceBehavior caught exception for request type: {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }
}