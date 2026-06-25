using InventoryReservationSystem.Infrastructure.Data;
using InventoryReservationSystem.Infrastructure.Locking;
using InventoryReservationSystem.Infrastructure.ResiliencePolicies;
using InventoryReservationSystem.Application.Services;
using InventoryReservationSystem.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Serilog;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Serilog setup
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/inventory-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.WithProperty("Application", "InventoryReservationSystem")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis connection
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.ConnectRetry = 5;
redisOptions.ConnectTimeout = 5000;
redisOptions.SyncTimeout = 5000;
redisOptions.AbortOnConnectFail = false;

try
{
    var redis = ConnectionMultiplexer.Connect(redisOptions);
    if (!redis.IsConnected)
    {
        throw new InvalidOperationException("Could not connect to Redis");
    }
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    Log.Information("Connected to Redis: {RedisConnection}", redisConnection);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to initialize Redis connection");
    throw;
}

// EF Core
builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' not found.");
    
    options.UseSqlServer(connectionString, opt =>
    {
        opt.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(200), null);
    });
});

// Domain services
builder.Services.AddScoped<IDistributedLockService, RedisDistributedLockService>();
builder.Services.AddScoped<IResiliencePolicyProvider, ResiliencePolicyProvider>();
builder.Services.AddScoped<IInventoryReservationService, InventoryReservationService>();

// MassTransit configuration
builder.Services.AddMassTransit(x =>
{
    // Configure saga
    x.AddSagaStateMachine<ReservationSagaDefinition, ReservationSagaState>()
        .InMemoryRepository();

    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error applying migrations");
        throw;
    }
}

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("Health")
    .WithOpenApi();

app.MapControllers();

app.Run();