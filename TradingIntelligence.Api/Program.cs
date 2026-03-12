using Microsoft.EntityFrameworkCore;
using Quartz;
using StackExchange.Redis;
using Serilog;
using TradingIntelligence.Infrastructure.Collectors;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;
using TradingIntelligence.Infrastructure.Jobs;
using TradingIntelligence.Infrastructure.Services;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TradingIntelligence.Api.Hubs;
using TradingIntelligence.Api.Services;
using TradingIntelligence.Core.Interfaces;

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

// ── JWT Authentication ────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["Jwt:SecretKey"]
                    ?? throw new InvalidOperationException("JWT secret not configured")))
        };

        // Allow SignalR to authenticate via query string token
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
// ── HttpClients ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("Reddit");
builder.Services.AddHttpClient("StockTwits");
builder.Services.AddHttpClient("News");
builder.Services.AddHttpClient("Volume");

builder.Services.AddHttpClient("NewsApi");
builder.Services.AddHttpClient("Polygon");
builder.Services.AddHttpClient("FearGreed");
builder.Services.AddHttpClient("GoogleTrends");
builder.Services.AddHttpClient("Telegram");

// ── Collectors ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<RedditCollector>();
builder.Services.AddScoped<StockTwitsCollector>();
builder.Services.AddScoped<NewsCollector>();
builder.Services.AddScoped<VolumeCollector>();
builder.Services.AddScoped<NewsApiCollector>();       // ← add
builder.Services.AddScoped<PolygonCollector>();       // ← add
builder.Services.AddScoped<FearGreedCollector>();     // ← add
builder.Services.AddScoped<GoogleTrendsCollector>();  // ← add

builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();

// ── Background Services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<SignalAggregatorService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<SignalAggregatorService>());

// Alert service — singleton so it can be injected into MomentumScoringService
builder.Services.AddSingleton<TelegramAlertService>(sp =>
    new TelegramAlertService(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<ILogger<TelegramAlertService>>(),
        sp.GetRequiredService<IHttpClientFactory>()));

builder.Services.AddSingleton<MomentumScoringService>(sp =>
    new MomentumScoringService(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<SignalAggregatorService>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<MomentumScoringService>>(),
        sp.GetRequiredService<IRealtimeNotifier>(),
        sp.GetRequiredService<TelegramAlertService>()));

// ← This is the line that was missing — registers it as a hosted service
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MomentumScoringService>());

// ── Quartz Scheduler ──────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    // StockTwits — startup + every 30 mins
    var stockTwitsJobKey = new JobKey("StockTwitsCollectorJob");
    q.AddJob<StockTwitsCollectorJob>(opts => opts
        .WithIdentity(stockTwitsJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(stockTwitsJobKey)
        .WithIdentity("StockTwitsStartupTrigger")
        .StartAt(DateBuilder.FutureDate(30, IntervalUnit.Second)));
    q.AddTrigger(opts => opts
        .ForJob(stockTwitsJobKey)
        .WithIdentity("StockTwitsRecurringTrigger")
        .WithCronSchedule("0 0/30 * * * ?"));

    // NewsAPI — startup + every hour at :30
    var newsApiJobKey = new JobKey("NewsApiCollectorJob");
    q.AddJob<NewsApiCollectorJob>(opts => opts
        .WithIdentity(newsApiJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(newsApiJobKey)
        .WithIdentity("NewsApiStartupTrigger")
        .StartAt(DateBuilder.FutureDate(60, IntervalUnit.Second)));
    q.AddTrigger(opts => opts
        .ForJob(newsApiJobKey)
        .WithIdentity("NewsApiRecurringTrigger")
        .WithCronSchedule("0 30 * * * ?"));

    // Fear & Greed — startup + every hour at :45
    var fearGreedJobKey = new JobKey("FearGreedCollectorJob");
    q.AddJob<FearGreedCollectorJob>(opts => opts
        .WithIdentity(fearGreedJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(fearGreedJobKey)
        .WithIdentity("FearGreedStartupTrigger")
        .StartAt(DateBuilder.FutureDate(15, IntervalUnit.Second)));
    q.AddTrigger(opts => opts
        .ForJob(fearGreedJobKey)
        .WithIdentity("FearGreedRecurringTrigger")
        .WithCronSchedule("0 45 * * * ?"));

    // Polygon — startup + every 4 hours
    var polygonJobKey = new JobKey("PolygonCollectorJob");
    q.AddJob<PolygonCollectorJob>(opts => opts
        .WithIdentity(polygonJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(polygonJobKey)
        .WithIdentity("PolygonStartupTrigger")
        .StartAt(DateBuilder.FutureDate(90, IntervalUnit.Second)));
    q.AddTrigger(opts => opts
        .ForJob(polygonJobKey)
        .WithIdentity("PolygonRecurringTrigger")
        .WithCronSchedule("0 0 */4 * * ?"));

    // Google Trends — startup + every 2 hours
    var googleTrendsJobKey = new JobKey("GoogleTrendsCollectorJob");
    q.AddJob<GoogleTrendsCollectorJob>(opts => opts
        .WithIdentity(googleTrendsJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(googleTrendsJobKey)
        .WithIdentity("GoogleTrendsStartupTrigger")
        .StartAt(DateBuilder.FutureDate(120, IntervalUnit.Second)));
    q.AddTrigger(opts => opts
        .ForJob(googleTrendsJobKey)
        .WithIdentity("GoogleTrendsRecurringTrigger")
        .WithCronSchedule("0 0 */2 * * ?"));

    // Reddit — hourly (no startup trigger — blocked from VPS anyway)
    var redditJobKey = new JobKey("RedditCollectorJob");
    q.AddJob<RedditCollectorJob>(opts => opts.WithIdentity(redditJobKey));
    q.AddTrigger(opts => opts
        .ForJob(redditJobKey)
        .WithIdentity("RedditCollectorTrigger")
        .WithCronSchedule("0 0 * * * ?"));

    // RSS News — every hour at :15
    var newsJobKey = new JobKey("NewsCollectorJob");
    q.AddJob<NewsCollectorJob>(opts => opts.WithIdentity(newsJobKey));
    q.AddTrigger(opts => opts
        .ForJob(newsJobKey)
        .WithIdentity("NewsCollectorTrigger")
        .WithCronSchedule("0 15 * * * ?"));

    // Volume — every 4 hours (kept for fallback)
    var volumeJobKey = new JobKey("VolumeCollectorJob");
    q.AddJob<VolumeCollectorJob>(opts => opts.WithIdentity(volumeJobKey));
    q.AddTrigger(opts => opts
        .ForJob(volumeJobKey)
        .WithIdentity("VolumeCollectorTrigger")
        .WithCronSchedule("0 0 */4 * * ?"));

    // Morning briefing — 04:00 UTC = 06:00 SAST
    var morningBriefingJobKey = new JobKey("MorningBriefingJob");
    q.AddJob<MorningBriefingJob>(opts => opts.WithIdentity(morningBriefingJobKey));
    q.AddTrigger(opts => opts
        .ForJob(morningBriefingJobKey)
        .WithIdentity("MorningBriefingTrigger")
        .WithCronSchedule("0 0 4 * * ?"));
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
        policy.WithOrigins(
            "http://localhost:4200",
            "https://servaro.co.za",
            "https://www.servaro.co.za")
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<MomentumHub>("/hubs/momentum");

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