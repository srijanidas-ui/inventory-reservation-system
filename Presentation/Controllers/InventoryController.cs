namespace InventoryReservationSystem.Presentation.Controllers;

using InventoryReservationSystem.Infrastructure.Data;
using InventoryReservationSystem.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class InventoryController : ControllerBase
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        InventoryDbContext db,
        ILogger<InventoryController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInventory(
        string productId,
        CancellationToken ct)
    {
        var item = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductId == productId, ct);

        if (item == null)
        {
            return NotFound(new { error = "Product not found" });
        }

        return Ok(MapToDto(item));
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInventory(
        CancellationToken ct)
    {
        var items = await _db.InventoryItems
            .OrderBy(i => i.ProductId)
            .ToListAsync(ct);

        return Ok(new
        {
            count = items.Count,
            items = items.Select(MapToDto)
        });
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateInventory(
        [FromBody] CreateInventoryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductId))
        {
            return BadRequest(new { error = "ProductId is required" });
        }

        var existing = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);

        if (existing != null)
        {
            return BadRequest(new { error = "Product already exists" });
        }

        var item = new InventoryItem
        {
            ProductId = request.ProductId,
            ProductName = request.ProductName,
            TotalQuantity = request.TotalQuantity,
            AvailableQuantity = request.TotalQuantity,
            ReservedQuantity = 0,
            Price = request.Price
        };

        _db.InventoryItems.Add(item);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Inventory created: {ProductId}, Quantity: {Quantity}",
            item.ProductId, item.TotalQuantity);

        return CreatedAtAction(nameof(GetInventory),
            new { productId = item.ProductId },
            MapToDto(item));
    }

    [HttpPost("init-sample")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeSampleData(CancellationToken ct)
    {
        var sampleItems = new[]
        {
            new InventoryItem
            {
                ProductId = "PROD-001",
                ProductName = "Laptop",
                TotalQuantity = 50,
                AvailableQuantity = 50,
                ReservedQuantity = 0,
                Price = 999.99m
            },
            new InventoryItem
            {
                ProductId = "PROD-002",
                ProductName = "Mouse",
                TotalQuantity = 200,
                AvailableQuantity = 200,
                ReservedQuantity = 0,
                Price = 29.99m
            },
            new InventoryItem
            {
                ProductId = "PROD-003",
                ProductName = "Keyboard",
                TotalQuantity = 150,
                AvailableQuantity = 150,
                ReservedQuantity = 0,
                Price = 79.99m
            }
        };

        foreach (var item in sampleItems)
        {
            var existing = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductId == item.ProductId, ct);

            if (existing == null)
            {
                _db.InventoryItems.Add(item);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Sample data initialized" });
    }

    private static InventoryDto MapToDto(InventoryItem item) => new()
    {
        ProductId = item.ProductId,
        ProductName = item.ProductName,
        TotalQuantity = item.TotalQuantity,
        AvailableQuantity = item.AvailableQuantity,
        ReservedQuantity = item.ReservedQuantity,
        Price = item.Price,
        UpdatedAt = item.UpdatedAt
    };
}

// DTOs
public class CreateInventoryRequest
{
    public string ProductId { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public int TotalQuantity { get; set; }
    public decimal Price { get; set; }
}

public class InventoryDto
{
    public string ProductId { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public decimal Price { get; set; }
    public DateTime UpdatedAt { get; set; }
}