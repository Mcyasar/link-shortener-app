namespace LinkShortener.Infrastructure.Resilience;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class CustomRateLimitAttribute : Attribute
{
    public int PermitLimit { get; }
    public int WindowInSeconds { get; }

    public CustomRateLimitAttribute(int permitLimit, int windowInSeconds = 1)
    {
        PermitLimit = permitLimit;
        WindowInSeconds = windowInSeconds;
    }
}