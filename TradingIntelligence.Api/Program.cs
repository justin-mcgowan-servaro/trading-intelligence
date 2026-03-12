using Microsoft.EntityFrameworkCore;
using Quartz;
using StackExchange.Redis;
using Serilog;
using TradingIntelligence.Infrastructure.Collectors;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;
using TradingIntelligence.Infrastructure.Jobs;
using TradingIntelligence.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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

// ── HttpClient for Reddit ────────────────────────────────────────────────────
builder.Services.AddHttpClient("Reddit");

// ── Collectors ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<RedditCollector>();

// ── Background Services ──────────────────────────────────────────────────────
builder.Services.AddSingleton<SignalAggregatorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalAggregatorService>());

// ── Quartz Scheduler ─────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    // Reddit collector — runs every 30 minutes
    var redditJobKey = new JobKey("RedditCollectorJob");

    q.AddJob<RedditCollectorJob>(opts => opts.WithIdentity(redditJobKey));

    q.AddTrigger(opts => opts
        .ForJob(redditJobKey)
        .WithIdentity("RedditCollectorTrigger")
        //.WithCronSchedule("0 0/30 * * * ?")  // Every 30 minutes
        .WithCronSchedule("0 0 * * * ?")  // Every hour on the hour
        .StartNow());
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// ── Controllers + Swagger ────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

var app = builder.Build();

// ── Auto-migrate + seed ticker list on startup ───────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Log.Information("Database migration applied");

    // Load valid tickers into the extractor
    var tickers = await db.Tickers
        .Where(t => t.IsActive)
        .Select(t => t.Symbol)
        .ToListAsync();

    TickerExtractor.LoadValidTickers(tickers);
    Log.Information("Loaded {Count} valid tickers into extractor", tickers.Count);
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseSerilogRequestLogging();
app.UseAuthorization();
app.MapControllers();

// ── Health endpoint ───────────────────────────────────────────────────────────
app.MapGet("/health", (SignalAggregatorService aggregator) => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    timestampSast = MarketSessionHelper.ToSast(DateTime.UtcNow),
    session = MarketSessionHelper.SessionDisplayName(
        MarketSessionHelper.CurrentSession()),
    signalBuffer = aggregator.GetBufferSummary(),
    version = "1.0.0"
}));

app.Run();