using System.Diagnostics;
using Hangfire;
using Hangfire.PostgreSql;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryPoc;
using Refit;
using Serilog;
using Serilog.Events;
using SolrNet;
using ILogger = Microsoft.Extensions.Logging.ILogger;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .Filter.ByExcluding("RequestPath like '/hangfire%'")
    .Filter.ByExcluding("RequestPath like '/health%'")
    .WriteTo.Console()
    .WriteTo.OpenTelemetry(options =>
    {
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "OpenTelemetryPoc"
        };
    })
    .CreateLogger();

try
{
    Log.Information("Starting web application");

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AuditContext>();

    builder.Services.AddOpenApi();

    builder.Services.AddHangfire(config =>
    {
        config.UseRecommendedSerializerSettings();
        config.UsePostgreSqlStorage(c =>
        {
            c.UseNpgsqlConnection(
                "host=localhost;port=5433;user id=postgres;password=secret;database=OpenTelemetryPoc");
        });
    });

    builder.Services.AddHangfireServer();

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<SomeMessageConsumer>();
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context);
            cfg.Host("localhost", 5673, "/", h =>
            { 
                h.Username("guest");
                h.Password("guest");
            });
        });
    });

    builder.Services.AddSolrNet("http://localhost:8988/solr/gettingstarted");

    builder.Services.AddDbContext<AuditContext>(opt =>
        opt.UseNpgsql("host=localhost;port=5433;user id=postgres;password=secret;database=OpenTelemetryPoc"));

    builder.Services.AddRefitClient<ICodexApi>()
        .ConfigureHttpClient(x => x.BaseAddress = new Uri("https://codex.opendata.api.vlaanderen.be:443/api"));

    builder.Services.AddSingleton(new DiagnosticsConfig());
    
    var appVersion = typeof(Program).Assembly.GetName().Version!;

    // https://github.com/open-telemetry/opentelemetry-dotnet
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService("OpenTelemetryPoc")
            .AddAttributes(new List<KeyValuePair<string, object>>
            {
                new("Startup", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                new("AppVersion", $"{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}")
            }))
        .WithTracing(tracing => tracing
            .AddSource(DiagnosticsConfig.SourceName)
            .AddSource(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context => 
                    !context.Request.Path.StartsWithSegments("/hangfire") && 
                    !context.Request.Path.StartsWithSegments("/health");
            })
            .AddHangfireInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter());

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<AuditContext>().Database;
        database.EnsureDeleted();
        database.EnsureCreated();    
    }

    var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManagerV2>();
    recurringJobManager.AddOrUpdate<SomeRecurringJob>("some-recurring-job", x => x.Execute(), Cron.Minutely);

    // Configure the HTTP request pipeline.
    app.UseSerilogRequestLogging();
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    
    app.UseHttpsRedirection();

    app.MapHealthChecks("/health");
    
    app.UseHangfireDashboard();

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
            [FromServices] IPublishEndpoint publishEndpoint,
            [FromServices] ISolrBasicReadOnlyOperations<Dictionary<string, object>> solr,
            HttpContext httpContext,
            bool shouldError = false) =>
        {
            var summaries = new[]
            {
                "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
            };
            
            var forecast = Enumerable.Range(1, 5).Select(index =>
                    new WeatherForecast
                    (
                        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        summaries[Random.Shared.Next(summaries.Length)]
                    ))
                .ToArray();
            Log.ForContext<Program>().Information("This log line is from {Source}", "Serilog");
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

            await publishEndpoint.Publish<SomeMessage>(new()
            {
                MaxTemperatureDate = forecast.MaxBy(_ => _.TemperatureC)!.Date,
                MaxTemperature = forecast.Max(_ => _.TemperatureC)
            });

            var solrResponse = await solr.PingAsync();
            logger.LogInformation("Got a response from Solr in {ResponseTime}ms", solrResponse.QTime);
            
            Activity.Current?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok(forecast);
        })
        .WithName("GetWeatherForecast");

    app.MapGet("/thema/{id:int}", async (
        [FromRoute] int id, 
        [FromServices] ICodexApi codexApi) =>
    {
        var thema = await codexApi.GetThema(id);
        return TypedResults.Ok(thema);
    }).WithName("GetThemaById");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

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