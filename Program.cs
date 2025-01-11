using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
        .AddAspNetCoreInstrumentation()
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

app.MapGet("/weatherforecast", (bool shouldError = false) =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        Activity.Current?.AddEvent(new ActivityEvent("Weather forecast requested"));
        if (shouldError)
        {
            var x = 1 / int.Parse("0");
        }
        
        Activity.Current?.SetStatus(ActivityStatusCode.Ok);
        return forecast;
    })
    .WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}