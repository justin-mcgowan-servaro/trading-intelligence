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
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("TradingIntelligence.Infrastructure")));

// ── Redis ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

// ── HttpClients ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("Reddit");
builder.Services.AddHttpClient("StockTwits");
builder.Services.AddHttpClient("News");
builder.Services.AddHttpClient("Volume");

// ── Collectors ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<RedditCollector>();
builder.Services.AddScoped<StockTwitsCollector>();
builder.Services.AddScoped<NewsCollector>();
builder.Services.AddScoped<VolumeCollector>();

// ── Background Services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<SignalAggregatorService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<SignalAggregatorService>());

builder.Services.AddSingleton<MomentumScoringService>(sp =>
    new MomentumScoringService(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<SignalAggregatorService>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<MomentumScoringService>>()));

// ← This is the line that was missing — registers it as a hosted service
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MomentumScoringService>());

// ── Quartz Scheduler ──────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    // Reddit collector — hourly
    var redditJobKey = new JobKey("RedditCollectorJob");
    q.AddJob<RedditCollectorJob>(opts => opts.WithIdentity(redditJobKey));
    q.AddTrigger(opts => opts
        .ForJob(redditJobKey)
        .WithIdentity("RedditCollectorTrigger")
        .WithCronSchedule("0 0 * * * ?")
        .StartNow());

    // StockTwits collector — every 30 minutes
    var stockTwitsJobKey = new JobKey("StockTwitsCollectorJob");
    q.AddJob<StockTwitsCollectorJob>(opts => opts.WithIdentity(stockTwitsJobKey));
    q.AddTrigger(opts => opts
        .ForJob(stockTwitsJobKey)
        .WithIdentity("StockTwitsTrigger")
        //.WithCronSchedule("0 0/30 * * * ?")
        .WithCronSchedule("0 23 13 * * ?")
        .StartAt(DateBuilder.FutureDate(30, IntervalUnit.Second)));

    // News collector — every hour at :15
    var newsJobKey = new JobKey("NewsCollectorJob");
    q.AddJob<NewsCollectorJob>(opts => opts.WithIdentity(newsJobKey));
    q.AddTrigger(opts => opts
        .ForJob(newsJobKey)
        .WithIdentity("NewsCollectorTrigger")
        .WithCronSchedule("0 15 * * * ?")
        .StartAt(DateBuilder.FutureDate(45, IntervalUnit.Second)));

    // Volume collector — every 4 hours
    var volumeJobKey = new JobKey("VolumeCollectorJob");
    q.AddJob<VolumeCollectorJob>(opts => opts.WithIdentity(volumeJobKey));
    q.AddTrigger(opts => opts
        .ForJob(volumeJobKey)
        .WithIdentity("VolumeCollectorTrigger")
        .WithCronSchedule("0 0 */4 * * ?")
        .StartAt(DateBuilder.FutureDate(60, IntervalUnit.Second)));
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS ──────────────────────────────────────────────────────────────────────
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

// ── Auto-migrate + load tickers ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Log.Information("Database migration applied");

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