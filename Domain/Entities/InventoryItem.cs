namespace InventoryReservationSystem.Domain.Entities;

/// <summary>
/// Inventory item aggregate root.
/// Uses optimistic concurrency (RowVersion) for consistency.
/// </summary>
public class InventoryItem
{
    public string ProductId { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public decimal Price { get; set; }
    
    /// <summary>
    /// Optimistic concurrency token - incremented on every update.
    /// Prevents lost updates in concurrent scenarios.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Validates inventory conservation law: Total = Available + Reserved.
    /// </summary>
    public bool IsValid => TotalQuantity == (AvailableQuantity + ReservedQuantity);

    /// <summary>
    /// Attempts to reserve quantity. Returns success and updated state.
    /// Does NOT persist - caller must save.
    /// </summary>
    public bool TryReserve(int quantity)
    {
        if (quantity <= 0 || quantity > AvailableQuantity)
            return false;

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedAt = DateTime.UtcNow;
        return IsValid;
    }

    /// <summary>
    /// Releases reserved quantity (failed or cancelled reservation).
    /// Does NOT persist - caller must save.
    /// </summary>
    public bool TryRelease(int quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity)
            return false;

        AvailableQuantity += quantity;
        ReservedQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
        return IsValid;
    }
}

/// <summary>
/// Reservation aggregate - represents a customer's hold on inventory.
/// Status machine: Pending → Reserved → Confirmed or Cancelled.
/// </summary>
public class Reservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token for saga safety.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public string? CorrelationId { get; set; }
    public string? SagaId { get; set; }

    /// <summary>
    /// Transitions to Reserved state after successful inventory lock.
    /// </summary>
    public bool TryReserve()
    {
        if (Status != ReservationStatus.Pending)
            return false;

        Status = ReservationStatus.Reserved;
        ExpiresAt = DateTime.UtcNow.AddMinutes(15);
        return true;
    }

    /// <summary>
    /// Transitions to Confirmed state (no time limit).
    /// </summary>
    public bool TryConfirm()
    {
        if (Status != ReservationStatus.Reserved)
            return false;

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
        ExpiresAt = null; // No expiration once confirmed
        return true;
    }

    /// <summary>
    /// Cancels reservation, releasing inventory.
    /// </summary>
    public bool TryCancel(string reason = "Customer cancelled")
    {
        if (Status != ReservationStatus.Pending && Status != ReservationStatus.Reserved)
            return false;

        Status = ReservationStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Marks as expired (used by background job).
    /// </summary>
    public bool TryExpire()
    {
        if (Status != ReservationStatus.Pending && Status != ReservationStatus.Reserved)
            return false;

        if (ExpiresAt == null || DateTime.UtcNow < ExpiresAt)
            return false;

        Status = ReservationStatus.Expired;
        CancelledAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Marks as failed (saga compensation).
    /// </summary>
    public bool TryFail(string reason = "Saga compensation")
    {
        if (Status == ReservationStatus.Confirmed || Status == ReservationStatus.Failed)
            return false;

        Status = ReservationStatus.Failed;
        CancelledAt = DateTime.UtcNow;
        return true;
    }
}

public enum ReservationStatus
{
    Pending = 0,      // Initial state, waiting for inventory lock
    Reserved = 1,     // Inventory locked, awaiting confirmation
    Confirmed = 2,    // Confirmed and committed
    Cancelled = 3,    // Cancelled by customer
    Expired = 4,      // Auto-expired after 15 minutes
    Failed = 5        // Saga compensation failure
}