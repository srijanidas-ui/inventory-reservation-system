namespace InventoryReservationSystem.Infrastructure.ResiliencePolicies;

using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using System.Threading.RateLimiting;

/// <summary>
/// Factory for creating Polly v8 resilience policies.
/// AR-02: Polly v8 ResiliencePipeline pattern (not Policy<T>).
/// </summary>
public interface IResiliencePolicyProvider
{
    ResiliencePipeline<T> GetInventoryRetryPolicy<T>();
    ResiliencePipeline<T> GetDatabaseRetryPolicy<T>();
    ResiliencePipeline GetAsyncPolicy();
}

public class ResiliencePolicyProvider : IResiliencePolicyProvider
{
    private readonly ILogger<ResiliencePolicyProvider> _logger;

    public ResiliencePolicyProvider(ILogger<ResiliencePolicyProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Retry policy for inventory operations (Redis).
    /// Exponential backoff: 100ms, 200ms, 400ms
    /// </summary>
    public ResiliencePipeline<T> GetInventoryRetryPolicy<T>()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = BackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>()
                    .Build(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Inventory operation retry {AttemptNumber}: {Exception}",
                        args.AttemptNumber, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>()
                    .Build(),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Inventory circuit breaker opened: {Exception}",
                        args.Outcome.Exception?.Message);
                    return default;
                }
            });

        return pipelineBuilder.Build();
    }

    /// <summary>
    /// Retry policy for database operations.
    /// Handles transient SQL errors, optimistic concurrency failures.
    /// </summary>
    public ResiliencePipeline<T> GetDatabaseRetryPolicy<T>()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = BackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(
                        ex => ex.Message.Contains("concurrency", StringComparison.OrdinalIgnoreCase))
                    .Handle<Exception>(
                        ex => ex.GetType().Name == "DbUpdateConcurrencyException")
                    .Build(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Database operation retry {AttemptNumber}: {Exception}",
                        args.AttemptNumber, args.Outcome.Exception?.Message);
                    return default;
                }
            });

        return pipelineBuilder.Build();
    }

    /// <summary>
    /// General async policy for fire-and-forget operations.
    /// </summary>
    public ResiliencePipeline GetAsyncPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = BackoffType.Exponential,
                UseJitter = true
            })
            .Build();
    }
}