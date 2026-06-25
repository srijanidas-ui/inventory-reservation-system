namespace InventoryReservationSystem.Infrastructure.Locking;

using StackExchange.Redis;
using System.Diagnostics;

/// <summary>
/// Distributed lock implementation using Redis.
/// Implements Redlock algorithm: SET key value NX EX timeout
/// Ensures atomicity and handles ownership verification.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string LockKey { get; }
    string LockToken { get; }
    bool IsHeld { get; }
}

/// <summary>
/// Redis-backed distributed lock with Lua script for safe release.
/// </summary>
internal class RedisDistributedLock : IDistributedLock
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisDistributedLock> _logger;
    private bool _disposed;

    public string LockKey { get; }
    public string LockToken { get; }
    public bool IsHeld { get; private set; }

    internal RedisDistributedLock(IDatabase db, string lockKey, string lockToken, 
        ILogger<RedisDistributedLock> logger)
    {
        _db = db;
        LockKey = lockKey;
        LockToken = lockToken;
        _logger = logger;
        IsHeld = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        if (!IsHeld)
            return;

        try
        {
            // Lua script: Only delete if token matches (atomic, safe release)
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            var result = await _db.ExecuteScriptAsync(script,
                new RedisKey[] { LockKey },
                new RedisValue[] { LockToken });

            IsHeld = false;
            _logger.LogDebug("Lock released: {LockKey}", LockKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock: {LockKey}", LockKey);
            // Don't throw - lock will expire naturally
        }
    }
}

/// <summary>
/// Service for acquiring and managing distributed locks.
/// Uses Redis SETNX EX for atomic lock acquisition.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Acquires a distributed lock with exponential backoff retry.
    /// </summary>
    Task<IDistributedLock?> AcquireLockAsync(
        string lockKey,
        TimeSpan? expiration = null,
        int maxRetries = 3,
        CancellationToken ct = default);

    /// <summary>
    /// Executes action with automatic lock acquisition and release.
    /// Throws TimeoutException if lock cannot be acquired.
    /// </summary>
    Task<T> ExecuteWithLockAsync<T>(
        string lockKey,
        Func<Task<T>> action,
        TimeSpan? lockExpiration = null,
        TimeSpan? actionTimeout = null,
        CancellationToken ct = default);
}

public class RedisDistributedLockService : IDistributedLockService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDistributedLockService> _logger;
    private const string LockKeyPrefix = "dist-lock:";

    public RedisDistributedLockService(
        IConnectionMultiplexer redis,
        ILogger<RedisDistributedLockService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IDistributedLock?> AcquireLockAsync(
        string lockKey,
        TimeSpan? expiration = null,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        var fullLockKey = $"{LockKeyPrefix}{lockKey}";
        expiration ??= TimeSpan.FromSeconds(10);

        var lockToken = Guid.NewGuid().ToString("N");
        var retryCount = 0;
        var delay = 100; // Start with 100ms

        while (retryCount < maxRetries)
        {
            try
            {
                var db = _redis.GetDatabase();

                // AR-01 Compliance: Atomic SETNX EX operation
                // SET key value NX EX expiration_seconds
                var acquired = await db.StringSetAsync(
                    fullLockKey,
                    lockToken,
                    expiration,
                    When.NotExists);

                if (acquired)
                {
                    _logger.LogDebug("Lock acquired: {LockKey}", lockKey);
                    return new RedisDistributedLock(db, fullLockKey, lockToken, _logger);
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    var backoffDelay = delay * (int)Math.Pow(2, retryCount - 1);
                    await Task.Delay(backoffDelay, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error acquiring lock: {LockKey}", lockKey);
                retryCount++;
            }
        }

        _logger.LogWarning("Failed to acquire lock after {MaxRetries} attempts: {LockKey}",
            maxRetries, lockKey);
        return null;
    }

    public async Task<T> ExecuteWithLockAsync<T>(
        string lockKey,
        Func<Task<T>> action,
        TimeSpan? lockExpiration = null,
        TimeSpan? actionTimeout = null,
        CancellationToken ct = default)
    {
        actionTimeout ??= TimeSpan.FromSeconds(30);
        lockExpiration ??= TimeSpan.FromSeconds(10);

        var @lock = await AcquireLockAsync(lockKey, lockExpiration, ct: ct);
        if (@lock == null)
        {
            throw new TimeoutException(
                $"Could not acquire distributed lock: {lockKey}");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(actionTimeout.Value);

            try
            {
                return await action();
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(
                    "Action execution timeout ({ActionTimeout}s) for lock: {LockKey}",
                    actionTimeout.Value.TotalSeconds, lockKey);
                throw new TimeoutException(
                    $"Action execution timeout: {actionTimeout.Value.TotalSeconds}s",
                    ex);
            }
        }
        finally
        {
            await @lock.DisposeAsync();
        }
    }
}