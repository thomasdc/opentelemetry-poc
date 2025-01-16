namespace OpenTelemetryPoc;

public class SomeRecurringJob(ICodexApi codexApi, ILogger<SomeRecurringJob> logger)
{
    public async Task Execute()
    {
        await Task.Delay(123);
        await codexApi.GetThema(1000142);
        logger.LogInformation("Hello from {JobName}", nameof(SomeRecurringJob));
        await Task.Delay(456);
        await codexApi.GetThema(1000142);
    }
}
