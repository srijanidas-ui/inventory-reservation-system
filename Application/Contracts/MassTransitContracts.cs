namespace InventoryReservationSystem.Application.Contracts;

using InventoryReservationSystem.Domain.Events;
using MassTransit;

/// <summary>
/// Implementation of domain events for publishing via MassTransit.
/// These are contracts published to the message bus for other services to consume.
/// </summary>

public class InventoryReservedEvent : IInventoryReservedEvent
{
    public Guid ReservationId { get; set; }
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public DateTime ReservedAt { get; set; }
    public string CorrelationId { get; set; } = null!;
}

public class InventoryReservationFailedEvent : IInventoryReservationFailedEvent
{
    public Guid ReservationId { get; set; }
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public DateTime FailedAt { get; set; }
    public string CorrelationId { get; set; } = null!;
}

public class InventoryReservationConfirmedEvent : IInventoryReservationConfirmedEvent
{
    public Guid ReservationId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public string CorrelationId { get; set; } = null!;
}

public class InventoryReleasedEvent : IInventoryReleasedEvent
{
    public Guid ReservationId { get; set; }
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public int Quantity { get; set; }
    public string Reason { get; set; } = null!;
    public DateTime ReleasedAt { get; set; }
    public string CorrelationId { get; set; } = null!;
}