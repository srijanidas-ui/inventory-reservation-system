# Inventory Reservation System

A production-ready .NET 8 inventory reservation system with distributed locking, event-driven architecture, and saga pattern orchestration.

## Architecture Rules Compliance

| Rule | Implementation | Status |
|------|---|---|
| **AR-01** | Redis SETNX EX atomic distributed locking | ✅ |
| **AR-02** | Single responsibility per service | ✅ |
| **AR-03** | All I/O async (no .Result/.Wait) | ✅ |
| **AR-04** | Configuration externalization | ✅ |
| **AR-05** | Dependency injection | ✅ |
| **AR-06** | Saga compensation idempotent | ✅ |
| **AR-07** | Event-driven (IInventoryReservedEvent) | ✅ |
| **AR-08** | Telemetry instrumentation | ✅ |

## Key Technologies

- **.NET 8** with async/await
- **Entity Framework Core 8** with optimistic concurrency (RowVersion)
- **Redis** with Redlock algorithm
- **MassTransit 8.1** saga pattern
- **Polly 8.2** resilience pipelines
- **SQL Server** with database constraints
- **Serilog** structured logging

## Quick Start

### Prerequisites
```bash
docker run -e ACCEPT_EULA=Y -e SA_PASSWORD=Password123! \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

docker run -p 6379:6379 -d redis:latest
```

### Setup
```bash
dotnet restore
dotnet ef database update
dotnet run
```

Navigate to `https://localhost:5001/swagger`

## API Examples

### Create Reservation
```bash
curl -X POST https://localhost:5001/api/reservations \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "550e8400-e29b-41d4-a716-446655440000",
    "productId": "PROD-001",
    "quantity": 5,
    "pricePerUnit": 99.99
  }'
```

### Confirm Reservation
```bash
curl -X POST https://localhost:5001/api/reservations/{id}/confirm
```

### Cancel Reservation
```bash
curl -X POST "https://localhost:5001/api/reservations/{id}/cancel?reason=OutOfStock"
```

## Architecture

See [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md) for detailed architecture documentation.

## Testing

```bash
dotnet test
```

Tests validate:
- Distributed lock prevents race conditions
- Inventory conservation law enforced
- Concurrent reservations handled correctly
- Optimistic concurrency conflicts retry
- Saga compensation idempotent

## License

MIT