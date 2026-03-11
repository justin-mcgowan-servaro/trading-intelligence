using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Serilog;
using TradingIntelligence.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ─────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

//builder.Services.AddDbContext<AppDbContext>(options =>
//    options.UseNpgsql(
//        builder.Configuration.GetConnectionString("DefaultConnection"),
//        b => b.MigrationsAssembly("TradingIntelligence.Infrastructure")));

// ── Redis ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

// ── Controllers + Swagger ────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS (for Angular dev) ───────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

var app = builder.Build();

// ── Auto-migrate on startup ──────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    Log.Information("Database migration applied successfully");
}

// ── Middleware ───────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseSerilogRequestLogging();
app.UseAuthorization();
app.MapControllers();

// ── Health endpoint ──────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    timestampSast = TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"))
        .ToString("yyyy-MM-dd HH:mm:ss SAST"),
    session = TradingIntelligence.Infrastructure.Helpers.MarketSessionHelper.CurrentSession().ToString(),
    version = "1.0.0"
}));

app.Run();