namespace InventoryReservationSystem.Presentation.Controllers;

using InventoryReservationSystem.Application.Services;
using InventoryReservationSystem.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ReservationsController : ControllerBase
{
    private readonly IInventoryReservationService _reservationService;
    private readonly ILogger<ReservationsController> _logger;

    public ReservationsController(
        IInventoryReservationService reservationService,
        ILogger<ReservationsController> logger)
    {
        _reservationService = reservationService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateReservation(
        [FromBody] CreateReservationRequest request,
        CancellationToken ct)
    {
        try
        {
            // Validate input
            if (request.Quantity <= 0 || request.Quantity > 1000)
            {
                return BadRequest(new { error = "Quantity must be between 1 and 1000" });
            }

            if (string.IsNullOrWhiteSpace(request.ProductId))
            {
                return BadRequest(new { error = "ProductId is required" });
            }

            var correlationId = HttpContext.TraceIdentifier;

            var reservation = await _reservationService.CreateReservationAsync(
                request.OrderId,
                request.ProductId,
                request.Quantity,
                request.PricePerUnit,
                correlationId,
                ct);

            return CreatedAtAction(nameof(GetReservation),
                new { id = reservation.Id },
                MapToDto(reservation));
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning("Lock timeout: {Exception}", ex.Message);
            return StatusCode(StatusCodes.Status409Conflict,
                new { error = "Could not acquire inventory lock. Please retry." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Reservation validation failed: {Exception}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating reservation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while creating the reservation" });
        }
    }

    [HttpPost("{id}/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmReservation(
        Guid id,
        CancellationToken ct)
    {
        try
        {
            var reservation = await _reservationService.ConfirmReservationAsync(id, ct);
            return Ok(MapToDto(reservation));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming reservation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while confirming the reservation" });
        }
    }

    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelReservation(
        Guid id,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        try
        {
            var success = await _reservationService.CancelReservationAsync(
                id,
                reason ?? "Customer cancelled",
                ct);

            if (!success)
            {
                return NotFound(new { error = "Reservation not found" });
            }

            return Ok(new { message = "Reservation cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling reservation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while cancelling the reservation" });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReservation(
        Guid id,
        CancellationToken ct)
    {
        var reservation = await _reservationService.GetReservationAsync(id, ct);

        if (reservation == null)
        {
            return NotFound(new { error = "Reservation not found" });
        }

        return Ok(MapToDto(reservation));
    }

    private static ReservationDto MapToDto(Reservation reservation) => new()
    {
        Id = reservation.Id,
        OrderId = reservation.OrderId,
        ProductId = reservation.ProductId,
        Quantity = reservation.Quantity,
        PricePerUnit = reservation.PricePerUnit,
        Status = reservation.Status.ToString(),
        CreatedAt = reservation.CreatedAt,
        ConfirmedAt = reservation.ConfirmedAt,
        ExpiresAt = reservation.ExpiresAt,
        CorrelationId = reservation.CorrelationId
    };
}

// DTOs
public class CreateReservationRequest
{
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
}

public class ReservationDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ProductId { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? CorrelationId { get; set; }
}