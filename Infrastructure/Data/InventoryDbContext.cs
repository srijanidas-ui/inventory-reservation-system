namespace InventoryReservationSystem.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using InventoryReservationSystem.Domain.Entities;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options) { }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // InventoryItem configuration
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.ProductId);
            
            entity.Property(e => e.ProductId)
                .HasMaxLength(50);
            
            entity.Property(e => e.ProductName)
                .HasMaxLength(255)
                .IsRequired();
            
            entity.Property(e => e.Price)
                .HasPrecision(18, 2);
            
            entity.Property(e => e.RowVersion)
                .IsRowVersion();
            
            // Constraint: Total = Available + Reserved
            entity.HasCheckConstraint(
                "CK_InventoryItem_Conservation",
                "[TotalQuantity] = [AvailableQuantity] + [ReservedQuantity]");
            
            entity.HasIndex(e => e.UpdatedAt);
        });

        // Reservation configuration
        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWID()");
            
            entity.Property(e => e.ProductId)
                .HasMaxLength(50)
                .IsRequired();
            
            entity.Property(e => e.PricePerUnit)
                .HasPrecision(18, 2);
            
            entity.Property(e => e.Status)
                .HasConversion<int>();
            
            entity.Property(e => e.RowVersion)
                .IsRowVersion();
            
            entity.Property(e => e.CorrelationId)
                .HasMaxLength(36);
            
            entity.Property(e => e.SagaId)
                .HasMaxLength(36);
            
            // Indexes for common queries
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });
            entity.HasIndex(e => e.CorrelationId);
            
            // Foreign key constraint (soft reference to inventory)
            entity.HasOne<InventoryItem>()
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}