using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using StockAnalysis.Api.Analysis;
using StockAnalysis.Api.Data;
using StockAnalysis.Api.Hubs;
using StockAnalysis.Api.Services;
using StockAnalysis.Api.Models.Strategy;

var builder = WebApplication.CreateBuilder(args);

// ── Forwarded headers (trust Nginx reverse proxy) ────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks();

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ── Database ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Authentication ───────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "stock_auth";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    options.CallbackPath = "/signin-google";
});

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<PolygonRestClient>();
builder.Services.AddHttpClient<BacktestDataService>();
builder.Services.AddSingleton<MarketStateService>();
builder.Services.AddSingleton<BacktestSessionService>();
builder.Services.AddScoped<IctAnalysisEngine>();
builder.Services.AddScoped<BacktestEngine>();
builder.Services.AddScoped<BacktestJobService>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddSingleton<LiveSignalEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveSignalEngine>());
builder.Services.AddHostedService<LiveBarService>();

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ── Migrate DB on startup ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var jobService = scope.ServiceProvider.GetRequiredService<BacktestJobService>();
    await jobService.RecoverStaleJobsAsync();

    var strategyService = scope.ServiceProvider.GetRequiredService<StrategyService>();
    await strategyService.ExpireStaleSignalsAsync(TimeSpan.FromHours(24));
}

app.UseForwardedHeaders();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<AnalysisHub>("/hubs/analysis");

app.Run();
