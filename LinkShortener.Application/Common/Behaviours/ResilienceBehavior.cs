using MediatR;
using Polly;

namespace Application.Common.Behaviors;

// 💡 ÇÖZÜM: IPipelineBehavior arayüzünü MediatR.Contracts paketinden kalıtım alıyoruz
public class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _resiliencePipeline;

    public ResilienceBehavior(ResiliencePipeline resiliencePipeline)
    {
        _resiliencePipeline = resiliencePipeline;
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        return await _resiliencePipeline.ExecuteAsync(async state => 
        {
            return await next();
        }, cancellationToken);
    }
}