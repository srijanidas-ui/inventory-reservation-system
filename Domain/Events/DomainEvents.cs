namespace InventoryReservationSystem.Domain.Events;

/// <summary>
/// Event published when inventory is successfully reserved.
/// This event allows Order service to react without direct coupling.
/// </summary>
public interface IInventoryReservedEvent
{
    Guid ReservationId { get; }
    Guid OrderId { get; }
    string ProductId { get; }
    int Quantity { get; }
    decimal PricePerUnit { get; }
    DateTime ReservedAt { get; }
    string CorrelationId { get; }
}

/// <summary>
/// Event published when inventory reservation fails and is rolled back.
/// </summary>
public interface IInventoryReservationFailedEvent
{
    Guid ReservationId { get; }
    Guid OrderId { get; }
    string ProductId { get; }
    string Reason { get; }
    DateTime FailedAt { get; }
    string CorrelationId { get; }
}

/// <summary>
/// Event published when a reservation is confirmed and committed.
/// </summary>
public interface IInventoryReservationConfirmedEvent
{
    Guid ReservationId { get; }
    Guid OrderId { get; }
    DateTime ConfirmedAt { get; }
    string CorrelationId { get; }
}

/// <summary>
/// Event published when inventory is released (reservation cancelled/expired).
/// </summary>
public interface IInventoryReleasedEvent
{
    Guid ReservationId { get; }
    Guid OrderId { get; }
    string ProductId { get; }
    int Quantity { get; }
    string Reason { get; }
    DateTime ReleasedAt { get; }
    string CorrelationId { get; }
}