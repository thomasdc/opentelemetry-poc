using System.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryPoc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AuditContext>();

builder.Services.AddSingleton(new DiagnosticsConfig());

// https://github.com/open-telemetry/opentelemetry-dotnet
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("OpenTelemetryPoc")
        .AddAttributes(new List<KeyValuePair<string, object>>
        {
            new("Startup", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            new("AppVersion", "0.1.42")
        }))
    .WithTracing(tracing => tracing
        .AddSource(DiagnosticsConfig.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithLogging(logging => logging
        .AddOtlpExporter());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.Use((context, next) =>
{
    try
    {
        return next(context);
    }
    catch (Exception e)
    {
        Activity.Current?.AddException(e);
        throw;
    }
});

app.MapGet("/weatherforecast", async (
        [FromServices] ILogger<Program> logger, 
        [FromServices] AuditContext auditContext, 
        [FromServices] DiagnosticsConfig diagnosticsConfig,
        HttpContext httpContext,
        bool shouldError = false) =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        Activity.Current?.AddEvent(new ActivityEvent("Weather forecast requested (as span/activity event)"));
        logger.WeatherForecastRequested(shouldError);
        if (shouldError)
        {
            var x = 1 / int.Parse("0");
        }

        using (var span = diagnosticsConfig.Source.StartActivity("Audit logging"))
        {
            logger.LogInformation("Saving to audit log...");
            var auditEntry = new AuditEntry
            {
                RawUrl = httpContext.Request.GetEncodedPathAndQuery(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                Method = httpContext.Request.Method
            };
            await auditContext.AddAsync(auditEntry);
            await auditContext.SaveChangesAsync();
            span?.SetTag(DiagnosticsNames.AuditEntryId, auditEntry.Id);
        }
        
        Activity.Current?.SetStatus(ActivityStatusCode.Ok);
        return TypedResults.Ok(forecast);
    })
    .WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

internal static partial class LoggerExtensions
{
    [LoggerMessage(LogLevel.Information, "Weather forecast requested (as log event) should error: `{ShouldError}`")]
    public static partial void WeatherForecastRequested(this ILogger logger, bool shouldError);
}

public class DiagnosticsConfig
{
    public const string SourceName = "audit-source";
    public ActivitySource Source = new(SourceName);
}

public static class DiagnosticsNames
{
    public const string AuditEntryId = "audit.entry.id";
}