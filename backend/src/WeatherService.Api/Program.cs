using Microsoft.EntityFrameworkCore;
using Serilog;
using WeatherService.Api.Diagnostics;
using WeatherService.Api.RateLimiting;
using WeatherService.Infrastructure;
using WeatherService.Infrastructure.Configuration;
using WeatherService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

builder.Services.AddWeatherInfrastructure(builder.Configuration);

builder.Services.AddWeatherRateLimiting(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ProblemDetails for everything, including the framework's own 4xx responses.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<WeatherExceptionHandler>();

var allowedOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // The front end reads provenance off the response, and a browser
            // hides every header that is not explicitly exposed.
            .WithExposedHeaders(
                "X-Weather-Data-Source",
                "X-Weather-Degraded",
                "X-Weather-Retrieved-At")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

// Always on in Development; elsewhere only when asked for. The compose stack
// asks for it so the API can be explored without rebuilding in another
// environment — a real deployment would leave it off.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Diagnostics");

await ApplyMigrationsAsync(app);

app.Run();

/// <summary>
/// Brings the schema up to date on boot.
///
/// Convenient for a reviewer running `docker compose up`; not a habit to carry
/// into production, where migrations belong in a deployment step rather than in
/// however many instances happen to start at once.
///
/// The retry loop is not superstition: compose can report Postgres as up before
/// it is accepting connections, and a container that dies on the first refused
/// socket makes the whole stack look broken.
/// </summary>
static async Task ApplyMigrationsAsync(WebApplication app)
{
    var options =
        app.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
        ?? new DatabaseOptions();

    if (!options.MigrateOnStartup)
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            await database.Database.MigrateAsync();
            logger.LogInformation("Database schema is up to date");
            return;
        }
        catch (Exception failure) when (attempt < 10)
        {
            logger.LogWarning(
                failure,
                "Database not ready (attempt {Attempt}/10); retrying in 2s",
                attempt);

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

/// <summary>Exposed so WebApplicationFactory can boot this exact application in tests.</summary>
public partial class Program;
