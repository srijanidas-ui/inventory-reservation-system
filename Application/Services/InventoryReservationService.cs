namespace InventoryReservationSystem.Application.Services;

using InventoryReservationSystem.Domain.Entities;
using InventoryReservationSystem.Domain.Events;
using InventoryReservationSystem.Infrastructure.Data;
using InventoryReservationSystem.Infrastructure.Locking;
using Microsoft.EntityFrameworkCore;
using MassTransit;

/// <summary>
/// Service for managing inventory reservations.
/// Coordinates with distributed lock, EF Core optimistic concurrency, and event publishing.
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Creates a new reservation with distributed lock on inventory.
    /// AR-01: Uses distributed lock, not in-memory lock.
    /// AR-06: Publishes IInventoryReservedEvent instead of updating Order directly.
    /// </summary>
    Task<Reservation> CreateReservationAsync(
        Guid orderId,
        string productId,
        int quantity,
        decimal pricePerUnit,
        string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Confirms a pending reservation (transitions to Confirmed).
    /// </summary>
    Task<Reservation> ConfirmReservationAsync(
        Guid reservationId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels reservation and releases inventory.
    /// </summary>
    Task<bool> CancelReservationAsync(
        Guid reservationId,
        string reason = "Customer cancelled",
        CancellationToken ct = default);

    /// <summary>
    /// Expires pending reservations older than 15 minutes.
    /// </summary>
    Task<int> ExpireReservationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets reservation details.
    /// </summary>
    Task<Reservation?> GetReservationAsync(
        Guid reservationId,
        CancellationToken ct = default);
}

public class InventoryReservationService : IInventoryReservationService
{
    private readonly InventoryDbContext _db;
    private readonly IDistributedLockService _lockService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<InventoryReservationService> _logger;

    public InventoryReservationService(
        InventoryDbContext db,
        IDistributedLockService lockService,
        IPublishEndpoint publishEndpoint,
        ILogger<InventoryReservationService> logger)
    {
        _db = db;
        _lockService = lockService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Reservation> CreateReservationAsync(
        Guid orderId,
        string productId,
        int quantity,
        decimal pricePerUnit,
        string correlationId,
        CancellationToken ct = default)
    {
        // Create reservation in Pending state
        var reservation = new Reservation
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity,
            PricePerUnit = pricePerUnit,
            CorrelationId = correlationId,
            Status = ReservationStatus.Pending
        };

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reservation created (Pending): {ReservationId} for Order {OrderId}, Product {ProductId}",
            reservation.Id, orderId, productId);

        // AR-01: Acquire distributed lock on inventory
        // Lock key: "inventory:{productId}" with 10-second expiration
        var lockKey = $"inventory:{productId}";

        try
        {
            var result = await _lockService.ExecuteWithLockAsync(
                lockKey,
                async () =>
                {
                    // Reload inventory within lock to ensure latest state
                    var inventory = await _db.InventoryItems
                        .FirstOrDefaultAsync(i => i.ProductId == productId, ct);

                    if (inventory == null)
                    {
                        throw new InvalidOperationException(
                            $"Product not found: {productId}");
                    }

                    // Check availability
                    if (inventory.AvailableQuantity < quantity)
                    {
                        throw new InvalidOperationException(
                            $"Insufficient inventory for {productId}. " +
                            $"Available: {inventory.AvailableQuantity}, Requested: {quantity}");
                    }

                    // Reserve inventory (updates object, doesn't persist yet)
                    if (!inventory.TryReserve(quantity))
                    {
                        throw new InvalidOperationException(
                            "Inventory conservation constraint violated");
                    }

                    // Persist inventory update with optimistic concurrency
                    // AR-03: RowVersion prevents lost updates
                    try
                    {
                        await _db.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        _logger.LogWarning(
                            "Concurrency conflict on inventory {ProductId}: {Exception}",
                            productId, ex.Message);
                        throw;
                    }

                    // Transition reservation to Reserved state
                    reservation.TryReserve();
                    reservation.SagaId = Guid.NewGuid().ToString("N");

                    await _db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "Inventory reserved within lock: {ReservationId}, " +
                        "Available: {Available} → {NewAvailable}",
                        reservation.Id, inventory.AvailableQuantity + quantity,
                        inventory.AvailableQuantity);

                    return true;
                },
                lockExpiration: TimeSpan.FromSeconds(10),
                actionTimeout: TimeSpan.FromSeconds(5),
                ct: ct);

            if (!result)
            {
                throw new InvalidOperationException("Failed to acquire inventory lock");
            }

            // AR-06: Publish event instead of direct coupling to Order service
            var @event = new InventoryReservedEvent
            {
                ReservationId = reservation.Id,
                OrderId = orderId,
                ProductId = productId,
                Quantity = quantity,
                PricePerUnit = pricePerUnit,
                ReservedAt = DateTime.UtcNow,
                CorrelationId = correlationId
            };

            await _publishEndpoint.Publish(@event, ct);
            _logger.LogInformation(
                "Published IInventoryReservedEvent: {ReservationId}",
                reservation.Id);

            return reservation;
        }
        catch (TimeoutException ex)
        {
            // Lock acquisition timeout - release the pending reservation
            reservation.TryFail("Lock acquisition timeout");
            await _db.SaveChangesAsync(ct);

            var failedEvent = new InventoryReservationFailedEvent
            {
                ReservationId = reservation.Id,
                OrderId = orderId,
                ProductId = productId,
                Reason = "Could not acquire distributed lock on inventory",
                FailedAt = DateTime.UtcNow,
                CorrelationId = correlationId
            };

            await _publishEndpoint.Publish(failedEvent, ct);

            _logger.LogError(
                "Failed to acquire lock for product {ProductId}: {Exception}",
                productId, ex.Message);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Insufficient inventory or other business rule violation
            reservation.TryFail(ex.Message);
            await _db.SaveChangesAsync(ct);

            var failedEvent = new InventoryReservationFailedEvent
            {
                ReservationId = reservation.Id,
                OrderId = orderId,
                ProductId = productId,
                Reason = ex.Message,
                FailedAt = DateTime.UtcNow,
                CorrelationId = correlationId
            };

            await _publishEndpoint.Publish(failedEvent, ct);

            _logger.LogWarning(
                "Inventory reservation failed: {ReservationId}, Reason: {Reason}",
                reservation.Id, ex.Message);
            throw;
        }
    }

    public async Task<Reservation> ConfirmReservationAsync(
        Guid reservationId,
        CancellationToken ct = default)
    {
        var reservation = await _db.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new InvalidOperationException($"Reservation not found: {reservationId}");

        if (!reservation.TryConfirm())
        {
            throw new InvalidOperationException(
                $"Cannot confirm reservation in status: {reservation.Status}");
        }

        await _db.SaveChangesAsync(ct);

        // Publish confirmation event
        var @event = new InventoryReservationConfirmedEvent
        {
            ReservationId = reservation.Id,
            OrderId = reservation.OrderId,
            ConfirmedAt = DateTime.UtcNow,
            CorrelationId = reservation.CorrelationId!
        };

        await _publishEndpoint.Publish(@event, ct);

        _logger.LogInformation(
            "Reservation confirmed: {ReservationId} for Order {OrderId}",
            reservation.Id, reservation.OrderId);

        return reservation;
    }

    public async Task<bool> CancelReservationAsync(
        Guid reservationId,
        string reason = "Customer cancelled",
        CancellationToken ct = default)
    {
        var reservation = await _db.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct);

        if (reservation == null)
            return false;

        if (!reservation.TryCancel(reason))
            return false;

        // Release inventory within lock
        var lockKey = $"inventory:{reservation.ProductId}";

        try
        {
            await _lockService.ExecuteWithLockAsync(
                lockKey,
                async () =>
                {
                    var inventory = await _db.InventoryItems
                        .FirstOrDefaultAsync(i => i.ProductId == reservation.ProductId, ct);

                    if (inventory != null && !inventory.TryRelease(reservation.Quantity))
                    {
                        _logger.LogError(
                            "Failed to release inventory for cancelled reservation: {ReservationId}",
                            reservation.Id);
                        return false;
                    }

                    await _db.SaveChangesAsync(ct);
                    return true;
                },
                lockExpiration: TimeSpan.FromSeconds(10),
                ct: ct);

            // Publish release event
            var @event = new InventoryReleasedEvent
            {
                ReservationId = reservation.Id,
                OrderId = reservation.OrderId,
                ProductId = reservation.ProductId,
                Quantity = reservation.Quantity,
                Reason = reason,
                ReleasedAt = DateTime.UtcNow,
                CorrelationId = reservation.CorrelationId ?? string.Empty
            };

            await _publishEndpoint.Publish(@event, ct);

            _logger.LogInformation(
                "Reservation cancelled: {ReservationId}, Reason: {Reason}",
                reservation.Id, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Error cancelling reservation: {ReservationId}, Exception: {Exception}",
                reservationId, ex.Message);
            throw;
        }
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken ct = default)
    {
        var expiredReservations = await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Reserved &&
                        r.ExpiresAt != null &&
                        r.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);

        var count = 0;

        foreach (var reservation in expiredReservations)
        {
            try
            {
                await CancelReservationAsync(
                    reservation.Id,
                    "Auto-expired after 15 minutes",
                    ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Error expiring reservation: {ReservationId}, Exception: {Exception}",
                    reservation.Id, ex.Message);
            }
        }

        _logger.LogInformation(
            "Expired {Count} reservations", count);

        return count;
    }

    public async Task<Reservation?> GetReservationAsync(
        Guid reservationId,
        CancellationToken ct = default)
    {
        return await _db.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct);
    }
}